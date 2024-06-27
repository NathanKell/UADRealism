using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public class CampaignControllerM
    {
        public static Ship GetSharedDesign(CampaignController _this, Player player, ShipType shipType, int year, bool checkTech = true, bool isEarlySavedShip = false)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"Getting shared design for {shipType.name} of {player.data.name}");

            if (!G.GameData.sharedDesignsPerNation.TryGetValue(player.data.name, out var designs))
                return null;

            List<Ship.Store> newerShips = new List<Ship.Store>();
            List<Ship.Store> olderShips = new List<Ship.Store>();
            int oldestNew = int.MaxValue;
            int newestOld = int.MinValue;
            List<float> techCoverage = new List<float>();
            foreach (var tuple in designs)
            {
                var store = tuple.Item1;
                if (store.shipType != shipType.name)
                    continue;

                if (store.YearCreated > year + 10)
                    continue;

                if (store.YearCreated < year - 5)
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
            Melon<TweaksAndFixes>.Logger.Msg($"Found {newerShips.Count} / {olderShips.Count} ships");
            if (newerShips.Count == 0 && olderShips.Count == 0)
                return null;

            // If some ships are borderline, grab them too.
            if (newestOld + 3 > oldestNew)
            {
                for (int i = olderShips.Count - 1; i >= 0; --i)
                {
                    if (olderShips[i].YearCreated + 3 > oldestNew)
                    {
                        newerShips.Add(olderShips[i]);
                        techCoverage.Add(0f);
                        olderShips.RemoveAt(i);
                    }
                }
            }
            Melon<TweaksAndFixes>.Logger.Msg($"Counts now {newerShips.Count} / {olderShips.Count} ships");

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
                        continue;
                    if (!PlayerController.Instance.CanBuildShipsFromDesign(ship, out _))
                        continue;
                }
                float techVal = TechMatchRatio(ship, player);
                Melon<TweaksAndFixes>.Logger.Error($"Tech Val for {newerShips[i].vesselName} ({newerShips[i].hullName}, {newerShips[i].YearCreated}) is {techVal:F3}");
                if (checkTech && techVal < 0f)
                    continue;
                techCoverage[i] = techVal;

                ship.Erase();
            }
            List<int> indices = new List<int>();
            List<int> indicesLow = new List<int>();
            for (int i = 0; i < techCoverage.Count; ++i)
                if (techCoverage[i] > 0.9f)
                    indices.Add(i);
                else if (techCoverage[i] > 0.75f)
                    indicesLow.Add(i);

            if (indices.Count == 0)
                indices = indicesLow;

            if (indices.Count == 0)
                return null;

            Melon<TweaksAndFixes>.Logger.Error($"Choosing from {indices.Count} choices");
            int idx = indices[ModUtils.Range(0, indices.Count - 1)];
            var shipRet = Ship.Create(null, null, false, false, false);
            var guidRet = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
            if (!shipRet.FromStore(newerShips[idx], guidRet, null, null, false))
            {
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
                for (int g = 1; g < 6; ++g)
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

            foreach (var tech in design.techs)
            {
                var rel = GetTechRelevance(design, tech, true, _CompTypes, torpedoTubes);
                ++_TechRelevanceCountsPlayer[(int)rel];
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
