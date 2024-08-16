using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

#pragma warning disable CS8600
#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public class CampaignControllerM
    {
        private static readonly List<Ship> _TempShipList = new List<Ship>();
        private static readonly List<Ship> _TempShipListMB = new List<Ship>();
        private static readonly Dictionary<ShipType, float> _TempShipTypeToWeight = new Dictionary<ShipType, float>();
        private static readonly Dictionary<ShipType, float> _TempShipTypeToWeightMB = new Dictionary<ShipType, float>();
        private static readonly Dictionary<ShipType, float> _TempShipTypeToRemaining = new Dictionary<ShipType, float>();
        private static void ClearScrapData()
        {
            _TempShipList.Clear();
            _TempShipListMB.Clear();
            _TempShipTypeToWeight.Clear();
            _TempShipTypeToWeightMB.Clear();
            _TempShipTypeToRemaining.Clear();
        }
        public static void HandleScrapping(CampaignController _this, Player player)
        {
            float totalTonnage = 0f;
            float totalTonnageMB = 0f;

            foreach (var s in player.GetFleetAll())
            {
                if (s.isScrapped || s.isSunk || s.isBuilding || s.isRefit)
                    continue;

                float weight = s.Weight();
                if (s.isMothballed || s.isLowCrew)
                {
                    _TempShipListMB.Add(s);
                    _TempShipTypeToWeightMB.ChangeValueFor(s.shipType, weight);
                    totalTonnageMB += weight;
                }
                else
                {
                    _TempShipList.Add(s);
                    _TempShipTypeToWeight.ChangeValueFor(s.shipType, weight);
                    totalTonnage += weight;
                }
            }
            
            float maxTonnageParam = MonoBehaviourExt.Param("min_fleet_tonnage_for_scrap", 1f);
            if (totalTonnage + totalTonnageMB < maxTonnageParam)
            {
                ClearScrapData();
                return;
            }

            // We will iterate backwards so we sort so newest ships are first.
            _TempShipList.Sort((a, b) => a.dateFinished.CompareTo(b.dateFinished));
            _TempShipListMB.Sort((a, b) => a.dateFinished.CompareTo(b.dateFinished));

            bool strictAge = MonoBehaviourExt.Param("taf_scrap_useOnlyShipAge", 0) > 0;

            float capTerm = Mathf.Pow(player.ShipbuildingCapacity(), MonoBehaviourExt.Param("taf_scrap_capacityExponent", 1f));
            float hystMult = MonoBehaviourExt.Param("taf_scrap_hysteresisMult", 1.05f);

            // Scrap mothballed first
            ScrapLoop(_this, _TempShipTypeToWeightMB, _TempShipListMB,
                MonoBehaviourExt.Param("taf_scrap_scrapTargetBaseMothball", float.MaxValue) + MonoBehaviourExt.Param("taf_scrap_capacityCoeffMothball", 0f) * capTerm,
                totalTonnageMB, strictAge, hystMult);

            // Then scrap main fleet if needed
            ScrapLoop(_this, _TempShipTypeToWeight, _TempShipList,
                MonoBehaviourExt.Param("taf_scrap_scrapTargetBaseMain", float.MaxValue) + MonoBehaviourExt.Param("taf_scrap_capacityCoeffMain", 0f) * capTerm,
                totalTonnage, strictAge, hystMult);

            // Finally, scrap everything down to target, taking mothballed first
            for (int i = 0; i < _TempShipListMB.Count; ++i)
                _TempShipList.Add(_TempShipListMB[i]);
            _TempShipTypeToWeight.Clear();
            totalTonnage = 0f;
            foreach (var s in _TempShipList)
            {
                float weight = s.Weight();
                totalTonnage += weight;
                _TempShipTypeToWeight.ChangeValueFor(s.shipType, weight);
            }
            ScrapLoop(_this, _TempShipTypeToWeight, _TempShipList,
                MonoBehaviourExt.Param("taf_scrap_scrapTargetBaseTotal", float.MaxValue) + MonoBehaviourExt.Param("taf_scrap_capacityCoeffTotal", 0f) * capTerm,
                totalTonnage, strictAge, hystMult);

            ClearScrapData();
        }

        private static void ScrapLoop(CampaignController _this, Dictionary<ShipType, float> tngDict, List<Ship> ships, float targetTonnage, float totalTonnage, bool strictAge, float hystMult)
        {
            if (totalTonnage <= targetTonnage * hystMult)
                return;

            bool didScrap = false;
            float tonnageDelta = totalTonnage - targetTonnage;
            float tngLeft = tonnageDelta;
            float tMult = 1f / totalTonnage * tonnageDelta;
            float maxScrapTngMult = MonoBehaviourExt.Param("taf_scrap_multToScrapRequiredVsShipTng", 2f);

            foreach (var kvp in tngDict)
                _TempShipTypeToRemaining[kvp.Key] = kvp.Value * tMult;
            do
            {
                for (int i = ships.Count; i-- > 0 && tngLeft > 0;)
                {
                    var s = ships[i];
                    float testTng = strictAge ? tngLeft : _TempShipTypeToRemaining.GetValueOrDefault(s.shipType);
                    float sWeight = s.Weight();
                    if (sWeight < testTng * maxScrapTngMult)
                    {
                        ships.RemoveAt(i);
                        tngLeft -= sWeight;
                        _TempShipTypeToRemaining.ChangeValueFor(s.shipType, -sWeight);
                        _this.ScrapShip(s, true);
                        didScrap = true;
                    }
                }
                // If we failed to find anything with tight constraints, try
                // with the looser constraint (ignore ship type)
                if (!strictAge && !didScrap)
                {
                    strictAge = true;
                    didScrap = true;
                }
            } while (didScrap && tngLeft > 0);

            _TempShipTypeToRemaining.Clear();
        }

        public static List<CampaignController.TaskForce> GetTaskForceInsideRadius(CampaignController _this, Vector3 coord, float radiusKm, CampaignController.TaskForce except, Func<CampaignController.TaskForce, bool> predicate)
        {
            List<CampaignController.TaskForce> ret = null;
            foreach (var tf in _this.CampaignData.TaskForces)
            {
                if (predicate != null && !predicate(tf))
                    continue;
                if (tf == except)
                    continue;

                float zone = tf.GetZoneRadius(false);
                float distSqr = (zone + radiusKm) * 0.005f;
                distSqr *= distSqr;
                if ((coord - tf.WorldPos).sqrMagnitude > distSqr)
                    continue;

                if (ret == null)
                    ret = new List<CampaignController.TaskForce>();
                ret.Add(tf);
            }

            return ret;
        }

        public static Ship GetSharedDesign(CampaignController _this, Player player, ShipType shipType, int year, bool checkTech = true, bool isEarlySavedShip = false)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"Getting shared design for {shipType.name} of {player.data.name}");

            if (!G.GameData.sharedDesignsPerNation.TryGetValue(player.data.name, out var designs))
                return null;

            List<Ship.Store> newerShips = new List<Ship.Store>();
            List<Ship.Store> olderShips = new List<Ship.Store>();
            int oldestNew = int.MaxValue;
            int newestOld = int.MinValue;
            List<float> techCoverage = new List<float>();
            int maxYearUp = Mathf.RoundToInt(MonoBehaviourExt.Param("taf_shareddesign_maxYearsIntoFuture", 10f));
            int maxYearDpwnForSplit = Mathf.RoundToInt(MonoBehaviourExt.Param("taf_shareddesign_yearsInPastForSplit", 5f));
            foreach (var tuple in designs)
            {
                var store = tuple.Item1;
                if (store.shipType != shipType.name)
                    continue;

                if (store.YearCreated > year + maxYearUp)
                    continue;

                if (store.YearCreated < year - maxYearDpwnForSplit)
                {
                    olderShips.Add(store);
                    if (store.YearCreated > newestOld)
                        newestOld = store.YearCreated;
                }
                else
                {
                    newerShips.Add(store);
                    techCoverage.Add(0f);
                    if (store.YearCreated < oldestNew)
                        oldestNew = store.YearCreated;
                }
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Found {newerShips.Count} / {olderShips.Count} ships");
            if (newerShips.Count == 0 && olderShips.Count == 0)
                return null;

            if (newerShips.Count == 0)
                oldestNew = newestOld - 5;

            // If some ships are borderline, grab them too.
            int yearBorder = Mathf.RoundToInt(MonoBehaviourExt.Param("taf_shareddesign_yearClosenessAroundSplit", 3f));
            if (newestOld + yearBorder > oldestNew)
            {
                for (int i = olderShips.Count - 1; i >= 0; --i)
                {
                    if (olderShips[i].YearCreated + yearBorder > oldestNew)
                    {
                        newerShips.Add(olderShips[i]);
                        techCoverage.Add(0f);
                        olderShips.RemoveAt(i);
                    }
                }
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Counts now {newerShips.Count} / {olderShips.Count} ships");

            for (int i = 0; i < newerShips.Count; ++i)
            {
                var ship = Ship.Create(null, null, false, false, false);
                var guid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
                if (!ship.FromStore(newerShips[i], guid, null, null, false))
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Couldn't load {newerShips[i].vesselName} ({newerShips[i].hullName}, {newerShips[i].YearCreated})");
                    ship.Erase();
                    continue;
                }

                ship.SetActive(false);
                if (!isEarlySavedShip)
                {
                    if (PlayerController.Instance == null)
                    {
                        ship.Erase();
                        continue;
                    }
                    if (!PlayerController.Instance.CanBuildShipsFromDesign(ship, out _))
                    {
                        ship.Erase();
                        continue;
                    }
                }
                float techVal;
                if (checkTech)
                {
                    techVal = TechMatchRatio(ship, player);
                }
                else
                {
                    // until checking further whether tech will be reliable, don't trust tech commonality in this case
                    float yearDelta = Mathf.Abs(year - newerShips[i].YearCreated);
                    yearDelta += 1f;
                    yearDelta *= yearDelta;
                    techVal = 1f / (1f + yearDelta * 0.018f);
                }
                //Melon<TweaksAndFixes>.Logger.Msg($"Tech Val for {newerShips[i].vesselName} ({newerShips[i].hullName}, {newerShips[i].YearCreated}) is {techVal:F3}");
                if (techVal < 0f)
                {
                    ship.Erase();
                    continue;
                }

                techCoverage[i] = techVal;

                ship.Erase();
            }
            List<int> indices = new List<int>();
            List<int> indicesMed = new List<int>();
            List<int> indicesLow = new List<int>();
            float bestVal = MonoBehaviourExt.Param("taf_shareddesign_bestTechValue", 0.9f);
            float okVal = MonoBehaviourExt.Param("taf_shareddesign_okTechValue", 0.75f);
            float minVal = MonoBehaviourExt.Param("taf_shareddesign_minTechValue", 0.5f);
            for (int i = 0; i < techCoverage.Count; ++i)
                if (techCoverage[i] > bestVal)
                    indices.Add(i);
                else if (techCoverage[i] > okVal)
                    indicesMed.Add(i);
                else if (techCoverage[i] > minVal)
                    indicesLow.Add(i);

            if (indices.Count == 0)
                indices = indicesMed;

            if (indices.Count == 0)
                indices = indicesLow;

            if (indices.Count == 0)
                return null;

            //Melon<TweaksAndFixes>.Logger.Msg($"Choosing from {indices.Count} choices");
            int idx = indices[ModUtils.Range(0, indices.Count - 1)];
            var shipRet = Ship.Create(null, null, false, false, false);
            var guidRet = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
            if (!shipRet.FromStore(newerShips[idx], guidRet, null, null, false))
            {
                Melon<TweaksAndFixes>.Logger.Error($"Couldn't load {newerShips[idx].vesselName} ({newerShips[idx].hullName}, {newerShips[idx].YearCreated})");
                shipRet.Erase();
                return null;
            }

            shipRet.SetActive(false);
            return shipRet;
        }

        private enum TechRelevance
        {
            None,
            OlderComponent,
            Improvement,
            Required,

            COUNT
        }

        private static TechRelevance GetTechRelevance(Ship ship, TechnologyData tech, bool isOnShip, HashSet<CompType> availableTypes, int torpedoTubes)
        {
            if (tech.componentx != null)
            {
                if (!ship.IsComponentTypeAvailable(tech.componentx.typex))
                    return TechRelevance.None;

                // Assume if the player hasn't selected any component for this type, they don't want
                // any component, so newer options aren't relevant. If they didn't have any choice,
                // then it counts as an improvement (note that it will only be available
                // as a type 
                if (!ship.components.TryGetValue(tech.componentx.typex, out var comp))
                    return availableTypes.Contains(tech.componentx.typex) ? TechRelevance.None : TechRelevance.Improvement;

                if (comp == tech.componentx)
                    return TechRelevance.Required;

                
                return comp.tech.year < tech.componentx.tech.year ? TechRelevance.Improvement : TechRelevance.OlderComponent;
            }

            TechRelevance bestRel = TechRelevance.None;

            foreach (var tc in ship.shipGunCaliber)
            {
                int calIn = Mathf.RoundToInt(tc.turretPartData.caliber * (1f / 25.4f));
                int grade = ship.TechGunGrade(tc.turretPartData);
                for (int g = 1; g < Config.MaxGunGrade + 1; ++g)
                {
                    var gTech = Database.GetGunTech(calIn, g);
                    if (gTech == tech.name)
                    {
                        if (g <= grade)
                            return TechRelevance.Required;
                        else if (!isOnShip)
                            bestRel = Max(bestRel, TechRelevance.Improvement);
                        
                        break;
                    }

                }
            }

            var partsUnlocked = Database.GetPartNamesForTech(tech);
            if (partsUnlocked != null)
            {
                    foreach (var part in ship.parts)
                        if (partsUnlocked.Contains(part.data.name))
                            return TechRelevance.Required;
                // FIXME do we care about better towers? Or funnels?
                // For now, let's say no.
                // (So we skip it isOnShip extra handling.)
            }

            foreach (var kvp in tech.effects)
            {
                switch (kvp.Key)
                {
                    case "start": break;
                    // handled above.
                    case "unlockparts": break;
                    case "unlock": break;
                    case "gun": break;
                    case "tonnage": break; // handled in main method

                    case "torp_mark":
                        if (torpedoTubes != -1)
                            if (isOnShip)
                                return TechRelevance.Required;

                            bestRel = Max(bestRel, TechRelevance.Improvement);
                        break;
                    case "torpedo_tubes":
                        if (torpedoTubes == -1)
                            break;

                        int.TryParse(kvp.Value[0][0], out var tubes);
                        if (torpedoTubes >= tubes)
                            return TechRelevance.Required;
                        // FIXME In the case where the tech allows more tubes than used
                        // it's not _necessarily_ an improvement, so do nothing.
                        break;

                    // FIXME ignore DC or Mines maybe?

                    default:
                        if (isOnShip)
                            return TechRelevance.Required;
                        bestRel = Max(bestRel, TechRelevance.Improvement);
                        break;
                }
            }

            return bestRel;
        }

        private static TechRelevance Max(TechRelevance a, TechRelevance b) => a > b ? a : b;


        private static readonly HashSet<TechnologyData> _TechsPlayer = new HashSet<TechnologyData>();
        private static readonly HashSet<TechnologyData> _TechsShip = new HashSet<TechnologyData>();
        private static readonly HashSet<CompType> _CompTypes = new HashSet<CompType>();
        private static readonly int[] _TechRelevanceCountsPlayer = new int[(int)TechRelevance.COUNT];
        private static readonly int[] _TechRelevanceCountsShip = new int[(int)TechRelevance.COUNT];

        public static float TechMatchRatio(Ship design, Player player)
        {
            _TechsPlayer.Clear();
            _TechsShip.Clear();
            _CompTypes.Clear();
            for (int i = (int)TechRelevance.COUNT; i-- > 0;)
            {
                _TechRelevanceCountsPlayer[i] = 0;
                _TechRelevanceCountsShip[i] = 0;
            }

            int torpedoTubes = -1;
            foreach (var p in design.parts)
            {
                var bar = p.data.name.Contains("x0") ? 0 : p.data.barrels;
                if (p.data.isTorpedo && torpedoTubes < bar)
                    torpedoTubes = bar;
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Tech ratio: {design.vesselName} ({design.hull.name}: torps {torpedoTubes}");
            // Kinda slow, but eh.
            foreach (var c in G.GameData.components.Values)
                if (design.IsComponentAvailable(c))
                    _CompTypes.Add(c.typex);

            foreach (var tech in player.technologies)
            {
                if (tech.progress == 100f || tech.IsEndTechResearched && tech.Index > 0)
                {
                    _TechsPlayer.Add(tech.data);
                    // Don't bother counting it if the ship already uses it
                    if (design.techs.Contains(tech.data))
                        continue;
                    var rel = GetTechRelevance(design, tech.data, false, _CompTypes, torpedoTubes);
                    ++_TechRelevanceCountsPlayer[(int)rel];
                }
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Tech ratio: {design.vesselName} ({design.hull.name}: Found {_TechsPlayer.Count} player techs, {design.techs.Count} ship techs");

            foreach (var tech in design.techs)
            {
                var rel = GetTechRelevance(design, tech, true, _CompTypes, torpedoTubes);
                //Melon<TweaksAndFixes>.Logger.Msg($"Tech ratio: {design.vesselName} ({design.hull.name}: tech {tech.name} has relevance {rel}");
                ++_TechRelevanceCountsShip[(int)rel];
                if (rel == TechRelevance.Required && !_TechsPlayer.Contains(tech))
                    return -1f;
            }

            // We know we don't lack any required techs. Let's see how closely we match.
            // FIXME ignore older components, and ignore mismatch in requireds (which should never happen).
            // Also completely ignore any ship improvement techs that aren't relevant to the ship.
            return _TechRelevanceCountsShip[(int)TechRelevance.Required] / (float)(_TechRelevanceCountsShip[(int)TechRelevance.Required] + _TechRelevanceCountsPlayer[(int)TechRelevance.Improvement]);
        }
    }
}
