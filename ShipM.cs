//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    public static class ShipM
    {
        public static void SetShipBeamDraughtFineness(Ship ship)
        {
            // First, set L/B from B/T
            float desiredBdivT = ShipStats.DefaultBdivT + ModUtils.DistributedRange(0.2f);
            float desiredLdivB = ShipStats.GetDesiredLdivB(ship, desiredBdivT);
            desiredLdivB *= 1f + ModUtils.DistributedRange(0.025f);

            float CpOffset = ModUtils.DistributedRange(0.02f, 3);
            //Melon<UADRealismMod>.Logger.Msg($"Iterating to find Cp for {(ship.speedMax / ShipStats.KnotsToMS)}kn. L/B {desiredLdivB:F2}, B/T {desiredBdivT:F2}, Cp offset {CpOffset:F3}");

            var bestSec = ShipStats.GetDesiredSections(ship, desiredLdivB, desiredBdivT, out var finalBmPct, out var finalDrPct, out _, CpOffset);

            ship.SetBeam(finalBmPct, false);
            ship.SetDraught(finalDrPct, false);

            float t = Mathf.InverseLerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, bestSec);
            ship.SetFineness(Mathf.Lerp(Patch_Ui._MinFineness, Patch_Ui._MaxFineness, 1f - t));

            // Also set freeboard
            ship.SetFreeboard(Mathf.Lerp(Patch_Ui._MinFreeboard, Patch_Ui._MaxFreeboard, ModUtils.DistributedRange(0.5f) + 0.5f));

            //Melon<UADRealismMod>.Logger.Msg($"Setting sizeZ={ship.GetFineness():F1} from {t:F3} from {bestSec} in {ship.hull.data.sectionsMin}-{ship.hull.data.sectionsMax}");
            if (ship.modelState == Ship.ModelState.Constructor || ship.modelState == Ship.ModelState.Battle)
                ship.RefreshHull(false);
        }

        private static float[] turretBarrelCountWeightMults = new float[5];
        private static float GetTurretBaseWeight(Ship ship, PartData data)
        {
            turretBarrelCountWeightMults[1] = 1f;
            turretBarrelCountWeightMults[1] = G.GameData.Param("w_turret_barrels_2", 1.8f);
            turretBarrelCountWeightMults[2] = G.GameData.Param("w_turret_barrels_3", 2.48f);
            turretBarrelCountWeightMults[3] = G.GameData.Param("w_turret_barrels_4", 3.1f);
            turretBarrelCountWeightMults[4] = G.GameData.Param("w_turret_barrels_5", 3.6f);

            var gunData = G.GameData.GunData(data);
            float baseWeight = gunData.BaseWeight(ship, data);
            float techWeightMod = ship.TechWeightMod(data);
            // This is just used for cost
            //var tc = Patch_GunData.FindMatchingTurretCaliber(ship, data);
            //float minLengthParam, techLengthLimit;
            //if (data.GetCaliberInch() > 2f)
            //{
            //    minLengthParam = G.GameData.Param("min_gun_length_mod", -20f);
            //    techLengthLimit = ship.TechMax("tech_gun_length_limit");
            //}
            //else
            //{
            //    minLengthParam = G.GameData.Param("min_casemate_length_mod", -10f);
            //    techLengthLimit = isCasemate ? ship.TechMax("tech_gun_length_limit_casemates") : ship.TechMax("tech_gun_length_limit_small");
            //}

            float barrelMult = turretBarrelCountWeightMults[Util.Clamp(data.barrels - 1, 0, 4)];
            float casemateMult = data.mounts.Contains("casemate") ? MonoBehaviourExt.Param("w_turret_casemate_mod", 0.75f) : 1f;
            float turretWeight = baseWeight * techWeightMod * barrelMult * casemateMult;
            return turretWeight;
        }

        private static float BarbetteWeight(Ship ship, Part part, PartData data)
        {
            Ship.TurretArmor armorData = null;
            bool isCasemate = Ship.IsCasemateGun(data);
            foreach (var ta in ship.shipTurretArmor)
            {
                if (ta.turretPartData.GetCaliber() == data.GetCaliber() && ta.isCasemateGun == isCasemate)
                {
                    armorData = ta;
                    break;
                }
            }

            if (armorData == null)
                return 0f;



            float thickLerp = Mathf.Lerp(1.0f, 3.0f, (armorData.barbetteArmor / 25.4f) / 15.0f);
            float weightParam = MonoBehaviourExt.Param("w_armor_barbette_turret", 0.029999999f);
            float tech = ship.TechR("armor");
            float weight = armorData.barbetteArmor * thickLerp / 25.4f * weightParam * GetTurretBaseWeight(ship, data) * tech;
            return weight;
        }

        private static Dictionary<Ship.A, float> _AdjustPriorityArmorReduce = GenerateAdjustArmorPriorities(false);
        private static Dictionary<Ship.A, float> _AdjustPriorityArmorIncrease = GenerateAdjustArmorPriorities(true);
        private static Dictionary<AdjustHullStatsItem, float> _AdjustHullStatsOptions = new Dictionary<AdjustHullStatsItem, float>();
        private static System.Array _OpRangeValues = null;
        internal enum AdjustHullStatsItem
        {
            Speed,
            Range,
            Quarters,
            Armor,
            Empty
        }

        private static Dictionary<Ship.A, float> GenerateAdjustArmorPriorities(bool increase)
        {
            var armorPriority = new Dictionary<Ship.A, float>();
            if (increase)
            {
                armorPriority.Add(Ship.A.Belt, 31);
                armorPriority.Add(Ship.A.BeltBow, 18);
                armorPriority.Add(Ship.A.BeltStern, 17);
                armorPriority.Add(Ship.A.Deck, 18);
                armorPriority.Add(Ship.A.DeckBow, 16);
                armorPriority.Add(Ship.A.DeckStern, 15);
                armorPriority.Add(Ship.A.ConningTower, 12);
                armorPriority.Add(Ship.A.Superstructure, 12);
                armorPriority.Add(Ship.A.TurretTop, 25);
                armorPriority.Add(Ship.A.TurretSide, 25);
                armorPriority.Add(Ship.A.Barbette, 25);
                armorPriority.Add(Ship.A.InnerBelt_1st, 25);
                armorPriority.Add(Ship.A.InnerBelt_2nd, 24);
                armorPriority.Add(Ship.A.InnerBelt_3rd, 23);
                armorPriority.Add(Ship.A.InnerDeck_1st, 25);
                armorPriority.Add(Ship.A.InnerDeck_2nd, 24);
                armorPriority.Add(Ship.A.InnerDeck_3rd, 23);
            }
            else
            {
                armorPriority.Add(Ship.A.Belt, 4);
                armorPriority.Add(Ship.A.BeltBow, 10);
                armorPriority.Add(Ship.A.BeltStern, 12);
                armorPriority.Add(Ship.A.Deck, 7);
                armorPriority.Add(Ship.A.DeckBow, 10);
                armorPriority.Add(Ship.A.DeckStern, 12);
                armorPriority.Add(Ship.A.ConningTower, 10);
                armorPriority.Add(Ship.A.Superstructure, 10);
                armorPriority.Add(Ship.A.TurretTop, 10);
                armorPriority.Add(Ship.A.TurretSide, 10);
                armorPriority.Add(Ship.A.Barbette, 10);
                armorPriority.Add(Ship.A.InnerBelt_1st, 4);
                armorPriority.Add(Ship.A.InnerBelt_2nd, 4);
                armorPriority.Add(Ship.A.InnerBelt_3rd, 4);
                armorPriority.Add(Ship.A.InnerDeck_1st, 5);
                armorPriority.Add(Ship.A.InnerDeck_2nd, 5);
                armorPriority.Add(Ship.A.InnerDeck_3rd, 5);
            }

            return armorPriority;
        }

        internal static void AdjustHullStats(Ship ship, int delta, float targetWeight, Func<bool> stopCondition, bool allowEditSpeed = true, bool allowEditArmor = true, bool allowEditCrewQuarters = true, bool allowEditRange = true, System.Random rnd = null, float limitArmor = -1, float limitSpeed = 0f)
        {
            float year = ship.GetYear(ship);
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> oldArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            bool isCL = ship.shipType.name == "cl";
            bool isLightCraft = ship.shipType.name == "tb" || ship.shipType.name == "dd";
            bool cldd = isCL || isLightCraft;

            VesselEntity.OpRange minOpRange = VesselEntity.OpRange.VeryLow;
            if (ship.shipType.paramx.TryGetValue("range_min", out var minRange) && minRange.Count > 0)
            {
                if (_OpRangeValues == null)
                    _OpRangeValues = Enum.GetValues(typeof(VesselEntity.OpRange));

                foreach (var e in _OpRangeValues)
                {
                    var eStr = Util.ToStringShort(e.ToString()).ToLower();
                    if (eStr == minRange[0].ToLower())
                    {
                        minOpRange = (VesselEntity.OpRange)e;
                        break;
                    }
                }
            }

            var armorMin = ship.shipType.armorMin;
            if (armorMin <= 0)
                armorMin = ship.shipType.armor;
            float armorInches;
            // All ships are expected to have a hint
            if (ship.shipType.paramx.TryGetValue("armor_min_hint", out var minArmorParam) && minArmorParam.Count > 0 && float.TryParse(minArmorParam[0], out var armorParam))
            {
                armorInches = armorMin * armorParam;
            }
            else if (ship.shipType.name == "dd")
            {
                float armorVal = ship.shipType.armor;
                float randMult = ModUtils.Range(1f, 1.3f, rnd);
                float yearMult = Util.Remap(year, 1890f, 1940f, 1f, 0.85f, true);

                armorInches = armorVal * randMult * yearMult;
            }
            else
            {
                armorInches = armorMin * ModUtils.Range(1f, 1.2f, rnd);
            }
            var armorMinHint = Ship.GenerateArmor(armorInches * 25.4f, ship);

            float hullSpeedMS = ship.hull.data.speedLimiter * ShipStats.KnotsToMS;

            float oldSpeed = ship.speedMax;
            Ship.CrewQuarters oldQuarters = ship.CurrentCrewQuarters;
            foreach (var kvp in ship.armor)
                oldArmor[kvp.Key] = kvp.Value;
            var oldRange = ship.opRange < minOpRange ? minOpRange : ship.opRange;

            Melon<UADRealismMod>.Logger.Msg($"Adjust. Old values: {oldSpeed:F1}/{oldQuarters}/{oldRange}. Armor {ModUtils.ArmorString(ship.armor)}");

            float minSpeed;
            if (limitSpeed > 0f)
            {
                minSpeed = limitSpeed;
            }
            else
            {
                float mult;
                if (isLightCraft)
                    mult = 0.92f;
                else if (isCL)
                    mult = 0.86f;
                else
                    mult = 0.79f;
                minSpeed = hullSpeedMS * Util.Remap(year, 1890f, 1940f, 0.9f, 0.8f, true) * mult;
            }
            float maxMult = cldd ? 1.05f : 1.1f;
            float maxSpeed = hullSpeedMS * maxMult * Util.Remap(year, 1890f, 1940f, 1.1f, 1.06f, true);

            float armorLimit = limitArmor > 0f ? limitArmor : ship.shipType.armorMax * 3f * 25.4f;

            bool canMakeTarget;
            if (delta < 0)
            {

                ship.SetSpeedMax(minSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Cramped;
                ship.SetOpRange(minOpRange);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Max(armorMinHint[kvp.Key], ship.MinArmorForZone(kvp.Key));

                ship.SetArmor(newArmor);
                Melon<UADRealismMod>.Logger.Msg($"Min values: {ship.speedMax:F1}/{ship.CurrentCrewQuarters}/{ship.opRange}. Armor {ModUtils.ArmorString(ship.armor)}.\n{ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");

                canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
                if (!canMakeTarget)
                {
                    foreach (var kvp in ship.armor)
                        newArmor[kvp.Key] = ship.MinArmorForZone(kvp.Key);

                    ship.SetArmor(newArmor);
                    Melon<UADRealismMod>.Logger.Msg($"Trying again. Min values: {ship.speedMax:F1}/{ship.CurrentCrewQuarters}/{ship.opRange}. Armor {ModUtils.ArmorString(ship.armor)}.\n{ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");
                    canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
                }
            }
            else
            {
                ship.SetSpeedMax(maxSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Spacious;
                ship.SetOpRange(VesselEntity.OpRange.VeryHigh);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Min(limitArmor, ship.MaxArmorForZone(kvp.Key));

                ship.SetArmor(newArmor);

                canMakeTarget = targetWeight * 0.997f <= ship.Weight() / ship.Tonnage();
            }

            if (!canMakeTarget)
                return;

            minSpeed = Mathf.Max(minSpeed, ship.shipType.speedMin * ShipStats.KnotsToMS);
            maxSpeed = Mathf.Min(maxSpeed, ship.shipType.speedMax * ShipStats.KnotsToMS);
            Melon<UADRealismMod>.Logger.Msg($"Min speed: {minSpeed:F1}, max {maxSpeed:F1}");
            oldSpeed = Ship.RoundSpeedToStep(oldSpeed);

            // Reset
            ship.SetSpeedMax(oldSpeed);
            ship.CurrentCrewQuarters = oldQuarters;
            ship.SetArmor(oldArmor);
            ship.SetOpRange(oldRange);

            foreach (var kvp in ship.armor)
                newArmor[kvp.Key] = kvp.Value;

            if (stopCondition())
                return;

            float speedStep = G.GameData.Param("speed_step", 0.5f);
            Dictionary<Ship.A, float> armorPriority = delta > 0 ? _AdjustPriorityArmorIncrease : _AdjustPriorityArmorReduce;

            for (int j = 0; j < 699; ++j)
            {
                float step = speedStep * delta * ShipStats.KnotsToMS;
                float newSpeed = step + ship.speedMax;
                newSpeed = Mathf.Clamp(newSpeed, minSpeed, maxSpeed);
                newSpeed = Ship.RoundSpeedToStep(newSpeed);
                Ship.CrewQuarters newQuarters = (Ship.CrewQuarters)Mathf.Clamp((int)ship.CurrentCrewQuarters + delta, (int)Ship.CrewQuarters.Cramped, (int)Ship.CrewQuarters.Spacious);
                VesselEntity.OpRange newOpRange = (VesselEntity.OpRange)Mathf.Clamp((int)ship.opRange + delta, (int)minOpRange, (int)VesselEntity.OpRange.VeryHigh);

                var randomA = ModUtils.RandomByWeights(armorPriority);
                float minHint = armorMinHint == null ? ship.shipType.armorMin : armorMinHint[randomA];
                float minArmor = Mathf.Max(minHint, ship.MinArmorForZone(randomA));
                float maxArmor = Mathf.Min(armorLimit, ship.MaxArmorForZone(randomA));
                float newArmorLevel = Mathf.Clamp((delta * G.settings.armorStep) * 25.4f + ship.armor[randomA], minArmor, maxArmor);
                if (delta < 0)
                    newArmorLevel = Mathf.Floor(newArmorLevel);
                else
                    newArmorLevel = Mathf.Ceil(newArmorLevel);
                newArmor[randomA] = newArmorLevel;

                _AdjustHullStatsOptions.Clear();
                if (allowEditSpeed && newSpeed != ship.speedMax)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Speed, delta > 0 ? 350f : 690f);
                if (allowEditCrewQuarters && newQuarters != ship.CurrentCrewQuarters)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Quarters, 150f);
                if (allowEditArmor && !Util.DictionaryEquals(ship.armor, newArmor))
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Armor, delta > 0 ? 850f : 450f);
                if (allowEditRange && newOpRange != ship.opRange)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Range, delta > 0 ? 75f : 400f);

                string newValues = "Potentials:";
                foreach (var kvp in _AdjustHullStatsOptions)
                {
                    string val = null;
                    switch (kvp.Key)
                    {
                        case AdjustHullStatsItem.Armor:
                            val = $" A:{ship.armor[randomA]:F1}->{newArmor[randomA]:F1}";
                            break;
                        case AdjustHullStatsItem.Speed:
                            val = $" S:{ship.speedMax:F1}->{newSpeed:F1}";
                            break;
                        case AdjustHullStatsItem.Range:
                            val = $" R:{ship.opRange}->{newOpRange}";
                            break;
                        default:
                            val = $" C:{ship.CurrentCrewQuarters}->{newQuarters}";
                            break;
                    }
                    newValues += val;
                }
                Melon<UADRealismMod>.Logger.Msg(newValues);

                if (_AdjustHullStatsOptions.Count == 0)
                    return;


                // We're going to replace entries we pull and use with "empty"
                // so that we don't screw with our weights. (If we just removed items,
                // like stock, then quarters and range would be overrepresented.)
                AdjustHullStatsItem thingToChange = AdjustHullStatsItem.Empty;
                while ((thingToChange = ModUtils.RandomByWeights(_AdjustHullStatsOptions, rnd)) != AdjustHullStatsItem.Empty)
                {
                    float weight = _AdjustHullStatsOptions[thingToChange];
                    _AdjustHullStatsOptions.Remove(thingToChange);
                    if (_AdjustHullStatsOptions.TryGetValue(AdjustHullStatsItem.Empty, out var unusedWeight))
                        weight += unusedWeight;
                    _AdjustHullStatsOptions[AdjustHullStatsItem.Empty] = weight;
                    switch (thingToChange)
                    {
                        case AdjustHullStatsItem.Speed:
                            ship.SetSpeedMax(newSpeed);
                            Melon<UADRealismMod>.Logger.Msg($"Picked speed");
                            break;
                        case AdjustHullStatsItem.Quarters:
                            ship.CurrentCrewQuarters = newQuarters;
                            Melon<UADRealismMod>.Logger.Msg($"Picked quarters");
                            break;
                        case AdjustHullStatsItem.Range:
                            ship.SetOpRange(newOpRange);
                            Melon<UADRealismMod>.Logger.Msg($"Picked range");
                            break;
                        default:
                            ship.SetArmor(newArmor);
                            Melon<UADRealismMod>.Logger.Msg($"Picked armor");
                            break;
                    }
                    Melon<UADRealismMod>.Logger.Msg($"Checking: {ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");
                    if (stopCondition())
                        return;

                    // We have to manually reset this, since
                    // we don't want to freshly clone all of ship.armor
                    // every time.
                    newArmor[randomA] = ship.armor[randomA];
                }
            }
        }

        public static float GetEngineMatsWeightCalculateValue(Ship ship)
        {
            var hp = ship.EnginePower();
            float techHP = ship.TechMax("hp");
            float techEngine = ship.TechR("engine");
            float techBoiler = ship.TechR("boiler");
            float weight = (0.45f * techBoiler + 0.55f * techEngine) * (hp / techHP);
            if (!GameManager.IsCampaign)
                return weight;

            foreach (var flaw in Ship.flawsDefectStats)
            {
                if (ship.IsThisShipIsFlawsDefects("engines", flaw))
                {
                    ship.CStats();
                    if (!G.GameData.stats.TryGetValue(flaw.ToString(), out var statData))
                        continue;
                    if (!ship.stats.TryGetValue(statData, out var statValue))
                        continue;
                    weight += statValue.basic * 0.01f * weight;
                }
            }

            return weight;
        }

        public static Il2CppSystem.Collections.Generic.List<Ship.MatInfo> PartMats(Ship ship, PartData part, bool calcCosts)
        {
            Il2CppSystem.Collections.Generic.List<Ship.MatInfo> mats = new Il2CppSystem.Collections.Generic.List<Ship.MatInfo>();
            if (part.isHull)
            {
                var stats = ShipStats.GetScaledStats(ship);
                float year = ShipM.GetYear(part);

                //float hullWeightMod = MonoBehaviourExt.Param("w_hull_weight_modifier", 0.7f);
                //float hullWeightRatio = part.hullWeightRatio;
                float tonnage = ship.Tonnage();
                float hullTechMult = ship.TechR("hull");
                //float citadelLengthWeight = MonoBehaviourExt.Param("w_citadel_length", 0.001f);
                float citadelLength = ship.GetDynamicCitadelMaxZ(false, false) - ship.GetDynamicCitadelMinZ(false, false);
                float citPct = citadelLength / stats.Lwl;

                float scantlingStrength = 1f;
                const float scantlingLightEarly = 0.75f;
                const float scantlingLightLate = 0.85f;
                const float scantlingCruiser = 0.95f;
                const float scantlingCL = 0.9f;
                const float scantlingACR = 1f;
                const float scantlingBB = 1.1f;
                bool isTBDD = false;
                switch (ship.shipType.name)
                {
                    case "dd":
                    case "tb":
                        scantlingStrength = Util.Remap(year, 1890f, 1920f, scantlingLightEarly, scantlingLightLate, true);
                        isTBDD = true;
                        break;
                    case "bb":
                        scantlingStrength = scantlingBB;
                        break;
                    case "cl":
                        scantlingStrength = part.nameUi.Contains("Semi-") ? scantlingCruiser : Util.Remap(year, 1890f, 1920f, scantlingCL, scantlingCruiser, true);
                        break;
                    case "ca":
                        scantlingStrength = Util.Remap(year, 1910f, 1925f, scantlingACR, scantlingCruiser, true);
                        break;
                    case "bc":
                        scantlingStrength = Util.Remap(year, 1900f, 1930f, scantlingACR, scantlingBB, true);
                        break;
                    default:
                        scantlingStrength = 1f;
                        break;
                }

                float lbd = stats.Lwl * stats.B * stats.T;
                float steelweight = lbd * (0.21f - 0.026f * Mathf.Log10(lbd)) * (1f + 0.025f * (stats.Lwl / stats.T - 12)) * (1f + (2f / 3f) * (stats.Cb - 0.7f));
                steelweight *= 0.9f + (1f + ship.GetFreeboard() * 0.01f) * 0.1f;
                steelweight *= 1f + citPct * 0.1f;
                float actualTech = Util.Remap(hullTechMult, 0.58f, 1f, 1.55f, 2f);
                steelweight *= actualTech;

                Ship.MatInfo mat = new Ship.MatInfo();
                mat.name = "hull";
                mat.mat = Ship.Mat.Hull;
                mat.weight = steelweight;
                mat.costMod = ship.TechCostMod(part) * Mathf.Lerp(1.55f, 12.5f, ship.Tonnage() / 150000f) * 0.0675f; // stock
                mats.Add(mat);


                // engine
                float engineWeight = GetEngineMatsWeightCalculateValue(ship);
                if (isTBDD)
                    engineWeight *= 0.8f;

                mat = new Ship.MatInfo();
                mat.name = "engines";
                mat.mat = Ship.Mat.Engine;
                mat.weight = engineWeight;
                mat.costMod = ship.TechR("engine_c") * Mathf.Lerp(1f, 12.5f, ship.Tonnage() / 150000f); // stock
                mats.Add(mat);

                // Armor
                float beltHeightEstimate = Mathf.Sqrt(stats.B) * 1.2f * (1f + ship.GetFreeboard() * 0.01f * 0.15f);


    //            armor_k__BackingField = this->fields._armor_k__BackingField;
    //            if (!armor_k__BackingField)
    //                goto LABEL_400;
    //            beltInchesDiv18 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                        (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)armor_k__BackingField,
    //                                        2,
    //                                        Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                    / 25.4)
    //                            / 18.0;
    //            beltThickLerp = UnityEngine_Mathf__Lerp(2.3, 6.0, beltInchesDiv18, 0i64);
    //            armor_k__BackingField1 = this->fields._armor_k__BackingField;
    //            if (!armor_k__BackingField1)
    //                goto LABEL_400;
    //            beltAmount = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                           (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)armor_k__BackingField1,
    //                           2,
    //                           Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            armorBeltWeight = MonoBehaviourExt__Param(LIT_w_armor_belt, 0.0074906368, 0i64);
    //            tonnage_4 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                        this,
    //                        this->klass->vtable._24_Tonnage.method);
    //            tonnagePow = Util__Pow_0(tonnage_4, 0.89999998);
    //            CitadelRatio = Ship__GetCitadelRatio(this, 0i64);
    //            techArmor = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                         this,
    //                         LIT_armor,
    //                         this->klass->vtable._29_TechR.method);
    //            techArmor_1 = *(float*)&techArmor;
    //            techBelt = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                       this,
    //                       LIT_belt,
    //                       this->klass->vtable._29_TechR.method);
    //            shipType_k__BackingField = this->fields._shipType_k__BackingField;
    //            if (!shipType_k__BackingField)
    //                goto LABEL_400;
    //            v213 = System_String__op_Equality(shipType_k__BackingField->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v214 = (float)((float)((float)((float)((float)((float)(beltAmount * beltThickLerp) / 25.4) * armorBeltWeight)
    //                                         * tonnagePow)
    //                                 * CitadelRatio)
    //                         * techArmor_1)
    //                 * techBelt;
    //            v215 = (System_String_o*)LIT_armor_belt;
    //            if (v213)
    //                goto LABEL_186;
    //            v216 = this->fields._shipType_k__BackingField;
    //            if (!v216)
    //                goto LABEL_400;
    //            if (System_String__op_Equality(v216->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //            {
    //            LABEL_186:
    //                v218 = 2.4000001;
    //                v219 = 2.4000001;
    //            }
    //            else
    //            {
    //                v217 = this->fields._shipType_k__BackingField;
    //                if (!v217)
    //                    goto LABEL_400;
    //                v218 = 2.4000001;
    //                v219 = System_String__op_Equality(v217->fields.name, *(System_String_o**)&LIT_ca, 0i64) ? 1.7 : 1.0;
    //            }
    //            v220 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v221 = UnityEngine_Mathf__Lerp(1.75, 0.80000001, *(float*)&v220 / 17500.0, 0i64);
    //            v222 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v223 = (float)((float)(v214 * v219) * v221)
    //                 * UnityEngine_Mathf__Lerp(0.80000001, 0.64999998, (float)(*(float*)&v222 + 17500.0) / 125000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v215,
    //              v223,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v224 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            v225 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_armor_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v226 = *(float*)&v225;
    //            v227 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_belt_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v228 = *(float*)&v227;
    //            v229 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v230 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v229 / 200000.0, 0i64);
    //            if (!v224)
    //                goto LABEL_400;
    //            v224->fields.costMod = (float)(v228 * v226) * v230;
    //            v231 = this->fields._armor_k__BackingField;
    //            if (!v231)
    //                goto LABEL_400;
    //            v232 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v231,
    //                             3,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 * 0.25;
    //            v233 = UnityEngine_Mathf__Lerp(2.3, 6.0, v232, 0i64);
    //            v234 = this->fields._armor_k__BackingField;
    //            if (!v234)
    //                goto LABEL_400;
    //            Item = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v234,
    //                     3,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            sourcef = MonoBehaviourExt__Param(LIT_w_armor_belt_extended, 0.0032467532, 0i64);
    //            v236 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v614 = Util__Pow_0(v236, 0.89999998);
    //            BowRatio = Ship__GetBowRatio(this, 0i64);
    //            v623 = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_armor,
    //                    this->klass->vtable._29_TechR.method);
    //            value = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_belt,
    //                    this->klass->vtable._29_TechR.method);
    //            v237 = this->fields._shipType_k__BackingField;
    //            if (!v237)
    //                goto LABEL_400;
    //            v238 = System_String__op_Equality(v237->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v239 = (float)((float)((float)((float)((float)((float)(Item * v233) / 25.4) * sourcef) * v614) * BowRatio) * v623)
    //                 * value;
    //            v240 = (System_String_o*)LIT_armor_belt_bow;
    //            if (v238)
    //                goto LABEL_195;
    //            v241 = this->fields._shipType_k__BackingField;
    //            if (!v241)
    //                goto LABEL_400;
    //            if (System_String__op_Equality(v241->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //            LABEL_195:
    //            v242 = 2.4000001;
    //            else
    //                v242 = 1.0;
    //            v243 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v244 = (float)(v239 * v242) * UnityEngine_Mathf__Lerp(1.25, 0.75, *(float*)&v243 / 75000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v240,
    //              v244,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v245 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            sourceg = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                      this,
    //                      LIT_armor_c,
    //                      this->klass->vtable._29_TechR.method);
    //            v246 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_belt_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v247 = *(float*)&v246;
    //            v248 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v249 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v248 / 200000.0, 0i64);
    //            if (!v245)
    //                goto LABEL_400;
    //            v245->fields.costMod = (float)(v247 * sourceg) * v249;
    //            v250 = this->fields._armor_k__BackingField;
    //            if (!v250)
    //                goto LABEL_400;
    //            v251 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v250,
    //                             4,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 * 0.25;
    //            v252 = UnityEngine_Mathf__Lerp(2.3, 6.0, v251, 0i64);
    //            v253 = this->fields._armor_k__BackingField;
    //            if (!v253)
    //                goto LABEL_400;
    //            v254 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v253,
    //                     4,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            sourceh = MonoBehaviourExt__Param(LIT_w_armor_belt_extended, 0.0032467532, 0i64);
    //            v255 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            valuea = Util__Pow_0(v255, 0.89999998);
    //            SternRatio = Ship__GetSternRatio(this, 0i64);
    //            v618 = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_armor,
    //                    this->klass->vtable._29_TechR.method);
    //            v615 = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_belt,
    //                   this->klass->vtable._29_TechR.method);
    //            v256 = this->fields._shipType_k__BackingField;
    //            if (!v256)
    //                goto LABEL_400;
    //            v257 = System_String__op_Equality(v256->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v258 = (float)((float)((float)((float)((float)((float)(v254 * v252) / 25.4) * sourceh) * valuea) * SternRatio) * v618)
    //                 * v615;
    //            v259 = (System_String_o*)LIT_armor_belt_stern;
    //            if (v257)
    //                goto LABEL_204;
    //            v260 = this->fields._shipType_k__BackingField;
    //            if (!v260)
    //                goto LABEL_400;
    //            if (System_String__op_Equality(v260->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //        LABEL_204:
    //            v261 = 2.4000001;
    //            else
    //                v261 = 1.0;
    //            v262 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v263 = (float)(v258 * v261) * UnityEngine_Mathf__Lerp(1.25, 0.75, *(float*)&v262 / 75000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v259,
    //              v263,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v264 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            v265 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_armor_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v266 = *(float*)&v265;
    //            v267 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_belt_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v268 = *(float*)&v267;
    //            v269 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v270 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v269 / 200000.0, 0i64);
    //            if (!v264)
    //                goto LABEL_400;
    //            v264->fields.costMod = (float)(v268 * v266) * v270;
    //            v271 = this->fields._armor_k__BackingField;
    //            if (!v271)
    //                goto LABEL_400;
    //            v272 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v271,
    //                             5,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 / 21.0;
    //            v273 = UnityEngine_Mathf__Lerp(3.3, 6.0, v272, 0i64);
    //            v274 = this->fields._armor_k__BackingField;
    //            if (!v274)
    //                goto LABEL_400;
    //            v275 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v274,
    //                     5,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            v276 = MonoBehaviourExt__Param(LIT_w_armor_deck, 0.011235955, 0i64);
    //            v277 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            sourcei = Util__Pow_0(v277, 0.89999998);
    //            valueb = Ship__GetCitadelRatio(this, 0i64);
    //            v625 = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_armor,
    //                    this->klass->vtable._29_TechR.method);
    //            v619 = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck,
    //                   this->klass->vtable._29_TechR.method);
    //            v278 = this->fields._shipType_k__BackingField;
    //            if (!v278)
    //                goto LABEL_400;
    //            v279 = System_String__op_Equality(v278->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v280 = (float)((float)((float)((float)((float)((float)(v275 * v273) / 25.4) * v276) * sourcei) * valueb) * v625)
    //                 * v619;
    //            v281 = (System_String_o*)LIT_armor_deck;
    //            if (v279)
    //                goto LABEL_216;
    //            v282 = this->fields._shipType_k__BackingField;
    //            if (!v282)
    //                goto LABEL_400;
    //            if (System_String__op_Equality(v282->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //            {
    //            LABEL_216:
    //                v284 = 2.4000001;
    //            }
    //            else
    //            {
    //                v283 = this->fields._shipType_k__BackingField;
    //                if (!v283)
    //                    goto LABEL_400;
    //                v284 = System_String__op_Equality(v283->fields.name, *(System_String_o**)&LIT_ca, 0i64) ? 1.5 : 1.0;
    //            }
    //            v285 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v286 = UnityEngine_Mathf__Lerp(1.75, 0.80000001, *(float*)&v285 / 17500.0, 0i64);
    //            v287 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v288 = (float)((float)(v284 * v280) * v286)
    //                 * UnityEngine_Mathf__Lerp(0.80000001, 0.64999998, (float)(*(float*)&v287 + 17500.0) / 125000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v281,
    //              v288,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v289 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            v290 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_armor_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v291 = *(float*)&v290;
    //            v292 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v293 = *(float*)&v292;
    //            v294 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v295 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v294 / 200000.0, 0i64);
    //            if (!v289)
    //                goto LABEL_400;
    //            v289->fields.costMod = (float)(v293 * v291) * v295;
    //            v296 = this->fields._armor_k__BackingField;
    //            if (!v296)
    //                goto LABEL_400;
    //            v297 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v296,
    //                             6,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 * 0.25;
    //            v298 = UnityEngine_Mathf__Lerp(2.3, 6.0, v297, 0i64);
    //            v299 = this->fields._armor_k__BackingField;
    //            if (!v299)
    //                goto LABEL_400;
    //            v300 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v299,
    //                     6,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            sourcej = MonoBehaviourExt__Param(LIT_w_armor_deck_extended, 0.0048076925, 0i64);
    //            v301 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            valuec = Util__Pow_0(v301, 0.89999998);
    //            v626 = Ship__GetBowRatio(this, 0i64);
    //            v620 = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_armor,
    //                    this->klass->vtable._29_TechR.method);
    //            v616 = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck,
    //                   this->klass->vtable._29_TechR.method);
    //            v302 = this->fields._shipType_k__BackingField;
    //            if (!v302)
    //                goto LABEL_400;
    //            v303 = System_String__op_Equality(v302->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v304 = (float)((float)((float)((float)((float)((float)(v300 * v298) / 25.4) * sourcej) * valuec) * v626) * v620)
    //                 * v616;
    //            v305 = (System_String_o*)LIT_armor_deck_bow;
    //            if (v303)
    //                goto LABEL_225;
    //            v306 = this->fields._shipType_k__BackingField;
    //            if (!v306)
    //                goto LABEL_400;
    //            if (System_String__op_Equality(v306->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //            LABEL_225:
    //            v307 = 2.4000001;
    //            else
    //                v307 = 1.0;
    //            v308 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v309 = (float)(v307 * v304) * UnityEngine_Mathf__Lerp(1.25, 0.75, *(float*)&v308 / 75000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v305,
    //              v309,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v310 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            sourcek = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                      this,
    //                      LIT_armor_c,
    //                      this->klass->vtable._29_TechR.method);
    //            v311 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v312 = *(float*)&v311;
    //            v313 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v314 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v313 / 200000.0, 0i64);
    //            if (!v310)
    //                goto LABEL_400;
    //            v310->fields.costMod = (float)(v312 * sourcek) * v314;
    //            v315 = this->fields._armor_k__BackingField;
    //            if (!v315)
    //                goto LABEL_400;
    //            v316 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v315,
    //                             7,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 * 0.25;
    //            v317 = UnityEngine_Mathf__Lerp(2.3, 6.0, v316, 0i64);
    //            v318 = this->fields._armor_k__BackingField;
    //            if (!v318)
    //                goto LABEL_400;
    //            v319 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v318,
    //                     7,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            v320 = MonoBehaviourExt__Param(LIT_w_armor_deck_extended, 0.0048076925, 0i64);
    //            v321 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v322 = Util__Pow_0(v321, 0.89999998);
    //            sourcel = Ship__GetSternRatio(this, 0i64);
    //            valued = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                      this,
    //                      LIT_armor,
    //                      this->klass->vtable._29_TechR.method);
    //            v627 = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck,
    //                   this->klass->vtable._29_TechR.method);
    //            v323 = this->fields._shipType_k__BackingField;
    //            if (!v323)
    //                goto LABEL_400;
    //            v324 = System_String__op_Equality(v323->fields.name, *(System_String_o**)&LIT_dd, 0i64);
    //            v325 = (float)((float)((float)((float)((float)((float)(v319 * v317) / 25.4) * v320) * v322) * sourcel) * valued)
    //                 * v627;
    //            v326 = (System_String_o*)LIT_armor_deck_stern;
    //            if (!v324)
    //            {
    //                v327 = this->fields._shipType_k__BackingField;
    //                if (!v327)
    //                    goto LABEL_400;
    //                if (!System_String__op_Equality(v327->fields.name, *(System_String_o**)&LIT_cl, 0i64))
    //                    v218 = 1.0;
    //            }
    //            v328 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v329 = (float)(v218 * v325) * UnityEngine_Mathf__Lerp(1.25, 0.75, *(float*)&v328 / 75000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              v326,
    //              v329,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v330 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            v331 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_armor_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v332 = *(float*)&v331;
    //            v333 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_deck_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v334 = *(float*)&v333;
    //            v335 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v336 = UnityEngine_Mathf__Lerp(5.0, 11.0, *(float*)&v335 / 200000.0, 0i64);
    //            if (!v330)
    //                goto LABEL_400;
    //            v330->fields.costMod = (float)(v334 * v332) * v336;
    //            CitadelArmor = Ship__GetCitadelArmor(this, 0i64);
    //            v338 = CitadelArmor;
    //            if (CitadelArmor)
    //            {
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       CitadelArmor,
    //                       16,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v339 = this->fields._armor_k__BackingField;
    //                    if (!v339)
    //                        goto LABEL_400;
    //                    v340 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v339,
    //                                     16,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourcem = UnityEngine_Mathf__Lerp(1.0, 4.0, v340, 0i64);
    //                    v341 = this->fields._armor_k__BackingField;
    //                    if (!v341)
    //                        goto LABEL_400;
    //                    v342 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v341,
    //                             16,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v343 = MonoBehaviourExt__Param(LIT_w_1st_deck, 0.00039999999, 0i64);
    //                    v344 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v345 = *(float*)&v344;
    //                    v346 = Ship__GetCitadelRatio(this, 0i64);
    //                    v347 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v348 = *(float*)&v347;
    //                    v349 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck,
    //                           this->klass->vtable._29_TechR.method);
    //                    v350 = *(float*)&v349;
    //                    v351 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v352 = (float)((float)((float)((float)((float)(v343 * (float)((float)(v342 * sourcem) / 25.4)) * v345) * v346)
    //                                         * v348)
    //                                 * v350)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v351 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_deck_1st,
    //                      v352,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v353 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v354 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v355 = *(float*)&v354;
    //                    v356 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v357 = *(float*)&v356;
    //                    v358 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v359 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v358 / 200000.0, 0i64);
    //                    if (!v353)
    //                        goto LABEL_400;
    //                    v353->fields.costMod = (float)(v357 * v355) * v359;
    //                }
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       v338,
    //                       17,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v360 = this->fields._armor_k__BackingField;
    //                    if (!v360)
    //                        goto LABEL_400;
    //                    v361 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v360,
    //                                     17,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourcen = UnityEngine_Mathf__Lerp(1.0, 4.5, v361, 0i64);
    //                    v362 = this->fields._armor_k__BackingField;
    //                    if (!v362)
    //                        goto LABEL_400;
    //                    v363 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v362,
    //                             17,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v364 = MonoBehaviourExt__Param(LIT_w_2nd_deck, 0.00030000001, 0i64);
    //                    v365 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v366 = *(float*)&v365;
    //                    v367 = Ship__GetCitadelRatio(this, 0i64);
    //                    v368 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v369 = *(float*)&v368;
    //                    v370 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck,
    //                           this->klass->vtable._29_TechR.method);
    //                    v371 = *(float*)&v370;
    //                    v372 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v373 = (float)((float)((float)((float)((float)(v364 * (float)((float)(v363 * sourcen) / 25.4)) * v366) * v367)
    //                                         * v369)
    //                                 * v371)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v372 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_deck_2nd,
    //                      v373,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v374 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v375 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v376 = *(float*)&v375;
    //                    v377 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v378 = *(float*)&v377;
    //                    v379 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v380 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v379 / 200000.0, 0i64);
    //                    if (!v374)
    //                        goto LABEL_400;
    //                    v374->fields.costMod = (float)(v378 * v376) * v380;
    //                }
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       v338,
    //                       18,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v381 = this->fields._armor_k__BackingField;
    //                    if (!v381)
    //                        goto LABEL_400;
    //                    v382 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v381,
    //                                     18,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourceo = UnityEngine_Mathf__Lerp(1.0, 5.0, v382, 0i64);
    //                    v383 = this->fields._armor_k__BackingField;
    //                    if (!v383)
    //                        goto LABEL_400;
    //                    v384 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v383,
    //                             18,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v385 = MonoBehaviourExt__Param(LIT_w_3rd_deck, 0.00019999999, 0i64);
    //                    v386 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v387 = *(float*)&v386;
    //                    v388 = Ship__GetCitadelRatio(this, 0i64);
    //                    v389 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v390 = *(float*)&v389;
    //                    v391 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck,
    //                           this->klass->vtable._29_TechR.method);
    //                    v392 = *(float*)&v391;
    //                    v393 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v394 = (float)((float)((float)((float)((float)(v385 * (float)((float)(v384 * sourceo) / 25.4)) * v387) * v388)
    //                                         * v390)
    //                                 * v392)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v393 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_deck_3rd,
    //                      v394,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v395 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v396 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v397 = *(float*)&v396;
    //                    v398 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_deck_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v399 = *(float*)&v398;
    //                    v400 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v401 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v400 / 200000.0, 0i64);
    //                    if (!v395)
    //                        goto LABEL_400;
    //                    v395->fields.costMod = (float)(v399 * v397) * v401;
    //                }
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       v338,
    //                       13,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v402 = this->fields._armor_k__BackingField;
    //                    if (!v402)
    //                        goto LABEL_400;
    //                    v403 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v402,
    //                                     13,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourcep = UnityEngine_Mathf__Lerp(1.0, 4.0, v403, 0i64);
    //                    v404 = this->fields._armor_k__BackingField;
    //                    if (!v404)
    //                        goto LABEL_400;
    //                    v405 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v404,
    //                             13,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v406 = MonoBehaviourExt__Param(LIT_w_1st_belt, 0.00030000001, 0i64);
    //                    v407 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v408 = *(float*)&v407;
    //                    v409 = Ship__GetCitadelRatio(this, 0i64);
    //                    v410 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v411 = *(float*)&v410;
    //                    v412 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt,
    //                           this->klass->vtable._29_TechR.method);
    //                    v413 = *(float*)&v412;
    //                    v414 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v415 = (float)((float)((float)((float)((float)(v406 * (float)((float)(v405 * sourcep) / 25.4)) * v408) * v409)
    //                                         * v411)
    //                                 * v413)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v414 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_belt_1st,
    //                      v415,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v416 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v417 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v418 = *(float*)&v417;
    //                    v419 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v420 = *(float*)&v419;
    //                    v421 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v422 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v421 / 200000.0, 0i64);
    //                    if (!v416)
    //                        goto LABEL_400;
    //                    v416->fields.costMod = (float)(v420 * v418) * v422;
    //                }
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       v338,
    //                       14,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v423 = this->fields._armor_k__BackingField;
    //                    if (!v423)
    //                        goto LABEL_400;
    //                    v424 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v423,
    //                                     14,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourceq = UnityEngine_Mathf__Lerp(1.0, 4.5, v424, 0i64);
    //                    v425 = this->fields._armor_k__BackingField;
    //                    if (!v425)
    //                        goto LABEL_400;
    //                    v426 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v425,
    //                             14,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v427 = MonoBehaviourExt__Param(LIT_w_2nd_belt, 0.00019999999, 0i64);
    //                    v428 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v429 = *(float*)&v428;
    //                    v430 = Ship__GetCitadelRatio(this, 0i64);
    //                    v431 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v432 = *(float*)&v431;
    //                    v433 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt,
    //                           this->klass->vtable._29_TechR.method);
    //                    v434 = *(float*)&v433;
    //                    v435 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v436 = (float)((float)((float)((float)((float)(v427 * (float)((float)(v426 * sourceq) / 25.4)) * v429) * v430)
    //                                         * v432)
    //                                 * v434)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v435 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_belt_2nd,
    //                      v436,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v437 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v438 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v439 = *(float*)&v438;
    //                    v440 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v441 = *(float*)&v440;
    //                    v442 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v443 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v442 / 200000.0, 0i64);
    //                    if (!v437)
    //                        goto LABEL_400;
    //                    v437->fields.costMod = (float)(v441 * v439) * v443;
    //                }
    //                if (System_Collections_Generic_HashSet_Ship_A___Contains(
    //                       v338,
    //                       15,
    //                       Method_System_Collections_Generic_HashSet_Ship_A__Contains__))
    //                {
    //                    v444 = this->fields._armor_k__BackingField;
    //                    if (!v444)
    //                        goto LABEL_400;
    //                    v445 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v444,
    //                                     15,
    //                                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                                 / 25.4)
    //                         / 7.0;
    //                    sourcer = UnityEngine_Mathf__Lerp(1.0, 5.0, v445, 0i64);
    //                    v446 = this->fields._armor_k__BackingField;
    //                    if (!v446)
    //                        goto LABEL_400;
    //                    v447 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v446,
    //                             15,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                    v448 = MonoBehaviourExt__Param(LIT_w_3rd_belt, 0.000099999997, 0i64);
    //                    v449 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v450 = *(float*)&v449;
    //                    v451 = Ship__GetCitadelRatio(this, 0i64);
    //                    v452 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                            this,
    //                            LIT_armor,
    //                            this->klass->vtable._29_TechR.method);
    //                    v453 = *(float*)&v452;
    //                    v454 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt,
    //                           this->klass->vtable._29_TechR.method);
    //                    v455 = *(float*)&v454;
    //                    v456 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                           this,
    //                           this->klass->vtable._24_Tonnage.method);
    //                    v457 = (float)((float)((float)((float)((float)(v448 * (float)((float)(v447 * sourcer) / 25.4)) * v450) * v451)
    //                                         * v453)
    //                                 * v455)
    //                         * UnityEngine_Mathf__Lerp(20.0, 1.0, *(float*)&v456 / 5000.0, 0i64);
    //                    System_Action_string__float__Ship_Mat___Invoke(
    //                      addMat_name_wegiht_mat,
    //                      *(System_String_o**)&LIT_armor_inner_belt_3rd,
    //                      v457,
    //                      4,
    //                      Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                    v458 = System_Linq_Enumerable__Last_Ship_MatInfo_(*p_fields, Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //                    v459 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_armor_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v460 = *(float*)&v459;
    //                    v461 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_belt_c,
    //                           this->klass->vtable._29_TechR.method);
    //                    v462 = *(float*)&v461;
    //                    *(float*)&v461 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                                     this,
    //                                     this->klass->vtable._24_Tonnage.method);
    //                    v463 = UnityEngine_Mathf__Lerp(5.0, 12.0, *(float*)&v461 / 200000.0, 0i64);
    //                    if (!v458)
    //                        goto LABEL_400;
    //                    v458->fields.costMod = (float)(v462 * v460) * v463;
    //                }
    //            }
    //            v464 = -1.0;
    //            parts_k__BackingField = this->fields._parts_k__BackingField;
    //            if (!parts_k__BackingField)
    //                goto LABEL_400;
    //            System_Collections_Generic_List_Ship_TurretCaliber_Store___GetEnumerator(
    //              &v636,
    //              (System_Collections_Generic_List_Ship_TurretCaliber_Store__o*)parts_k__BackingField,
    //              (const MethodInfo_1A92F80*)Method_System_Collections_Generic_List_Part__GetEnumerator__);
    //            *(_OWORD*)&v632.klass = *(_OWORD*)&v636.fields.list;
    //            *(_QWORD*)&v632.fields.__1__state = v636.fields.current;
    //            while (System_Collections_Generic_List_Enumerator_Ship_TurretCaliber_Store___MoveNext(
    //                      (System_Collections_Generic_List_Enumerator_T__o*)&v632,
    //                      (const MethodInfo_F475D0*)Method_System_Collections_Generic_List_Enumerator_Part__MoveNext__) )
    //{
    //                if (!*(_QWORD*)&v632.fields.__1__state)
    //                    ThrowException();
    //                v466 = *(PartData_o**)(*(_QWORD*)&v632.fields.__1__state + 56i64);
    //                if (v466 && v466->fields._isTowerMain_k__BackingField)
    //                {
    //                    weight = v466->fields.weight;
    //                    v464 = Ship__TechWeightMod(this, v466, 0i64) * weight;
    //                }
    //            }
    //            UnrealByte_EasyJira_JiraConnect__GetTexture_d__12__System_IDisposable_Dispose(
    //              &v632,
    //              Method_System_Collections_Generic_List_Enumerator_Part__Dispose__);
    //            v468 = (System_String_o*)LIT_armor_conning_tower;
    //            klass = v632.fields.url[22].klass;
    //            if (v464 < 0.0)
    //            {
    //                if (!klass)
    //                    goto LABEL_400;
    //                v477 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                 (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)klass,
    //                                 8,
    //                                 Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                             / 25.4)
    //                     / 35.0;
    //                v478 = UnityEngine_Mathf__Lerp(0.5, 3.0, v477, 0i64);
    //                v479 = this->fields._armor_k__BackingField;
    //                if (!v479)
    //                    goto LABEL_400;
    //                v480 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                         (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v479,
    //                         8,
    //                         Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                v476 = 0.44999999;
    //                v481 = (float)((float)((float)(v480 * v478) / 25.4) * 0.44999999)
    //                     * ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                        this,
    //                        LIT_armor,
    //                        this->klass->vtable._29_TechR.method);
    //                System_Action_string__float__Ship_Mat___Invoke(
    //                  v633,
    //                  v468,
    //                  v481,
    //                  4,
    //                  Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            }
    //            else
    //            {
    //                if (!klass)
    //                    goto LABEL_400;
    //                v470 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                                 (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)klass,
    //                                 8,
    //                                 Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                             / 25.4)
    //                     / 35.0;
    //                v471 = UnityEngine_Mathf__Lerp(0.5, 3.0, v470, 0i64);
    //                v472 = this->fields._armor_k__BackingField;
    //                if (!v472)
    //                    goto LABEL_400;
    //                v473 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                         (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v472,
    //                         8,
    //                         Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //                v474 = MonoBehaviourExt__Param(LIT_w_conning_tower, 0.050000001, 0i64);
    //                v475 = (float)((float)(v474 * (float)((float)(v473 * v471) / 25.4)) * v464)
    //                     * ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                        this,
    //                        LIT_armor,
    //                        this->klass->vtable._29_TechR.method);
    //                System_Action_string__float__Ship_Mat___Invoke(
    //                  v633,
    //                  v468,
    //                  v475,
    //                  4,
    //                  Method_System_Action_string__float__Ship_Mat__Invoke__);
    //                v476 = 0.44999999;
    //            }
    //            v482 = System_Linq_Enumerable__Last_Ship_MatInfo_(
    //                     (System_Collections_Generic_IEnumerable_TSource__o*)helper->fields.mats,
    //                     Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            v483 = ((double(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                   this,
    //                   LIT_armor_c,
    //                   this->klass->vtable._29_TechR.method);
    //            v484 = *(float*)&v483;
    //            *(float*)&v483 = ((float(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                             this,
    //                             this->klass->vtable._24_Tonnage.method);
    //            v485 = UnityEngine_Mathf__Lerp(2.0, 12.0, *(float*)&v483 / 100000.0, 0i64);
    //            if (!v482)
    //                goto LABEL_400;
    //            v482->fields.costMod = v485 * v484;
    //            v486 = -1.0;
    //            v487 = this->fields._parts_k__BackingField;
    //            if (!v487)
    //                goto LABEL_400;
    //            System_Collections_Generic_List_Ship_TurretCaliber_Store___GetEnumerator(
    //              &v637,
    //              (System_Collections_Generic_List_Ship_TurretCaliber_Store__o*)v487,
    //              (const MethodInfo_1A92F80*)Method_System_Collections_Generic_List_Part__GetEnumerator__);
    //            *(_OWORD*)&v632.klass = *(_OWORD*)&v637.fields.list;
    //            *(_QWORD*)&v632.fields.__1__state = v637.fields.current;
    //            while (System_Collections_Generic_List_Enumerator_Ship_TurretCaliber_Store___MoveNext(
    //                      (System_Collections_Generic_List_Enumerator_T__o*)&v632,
    //                      (const MethodInfo_F475D0*)Method_System_Collections_Generic_List_Enumerator_Part__MoveNext__) )
    //{
    //                if (!*(_QWORD*)&v632.fields.__1__state)
    //                    ThrowException();
    //                v488 = *(PartData_o**)(*(_QWORD*)&v632.fields.__1__state + 56i64);
    //                if (v488 && !v488->fields._isTowerMain_k__BackingField && v488->fields._isTowerAny_k__BackingField)
    //                {
    //                    v489 = v488->fields.weight;
    //                    v486 = v489 * Ship__TechWeightMod(this, v488, 0i64);
    //                }
    //            }
    //            UnrealByte_EasyJira_JiraConnect__GetTexture_d__12__System_IDisposable_Dispose(
    //              &v632,
    //              Method_System_Collections_Generic_List_Enumerator_Part__Dispose__);
    //            if (v464 < 0.0)
    //                v464 = 0.44999999;
    //            if (v486 >= 0.0)
    //                v476 = v486;
    //            v490 = this->fields._armor_k__BackingField;
    //            if (!v490)
    //                goto LABEL_400;
    //            v491 = (float)(System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                             (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v490,
    //                             9,
    //                             Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__)
    //                         / 25.4)
    //                 / 7.0;
    //            sources = UnityEngine_Mathf__Lerp(0.66000003, 2.0, v491, 0i64);
    //            v492 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v493 = UnityEngine_Mathf__Lerp(1.45, 0.40000001, *(float*)&v492 / 100000.0, 0i64);
    //            v494 = this->fields._armor_k__BackingField;
    //            if (!v494)
    //                goto LABEL_400;
    //            v495 = System_Collections_Generic_Dictionary_Ship_Mat__float___get_Item(
    //                     (System_Collections_Generic_Dictionary_Ship_Mat__float__o*)v494,
    //                     9,
    //                     Method_System_Collections_Generic_Dictionary_Ship_A__float__get_Item__);
    //            v496 = MonoBehaviourExt__Param(LIT_w_superstructure, 0.5, 0i64);
    //            v497 = ((float(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_armor,
    //                    this->klass->vtable._29_TechR.method);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              *(System_String_o**)&LIT_armor_superstructure,
    //              (float)((float)(v476 + v464) * (float)(v496 * (float)((float)((float)(v493 * sources) * v495) / 25.4))) * v497,
    //              4,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            v498 = System_Linq_Enumerable__Last_Ship_MatInfo_(
    //                     (System_Collections_Generic_IEnumerable_TSource__o*)helper->fields.mats,
    //                     Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            ((void(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //            this,
    //            LIT_armor_c,
    //            this->klass->vtable._29_TechR.method);
    //            if (!v498)
    //                goto LABEL_400;
    //            v498->fields.costMod = v497;
    //            v499 = MonoBehaviourExt__Param(LIT_w_antitorpedo, 0.05882353, 0i64);
    //            v500 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v501 = *(float*)&v500;
    //            v502 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_hull,
    //                    this->klass->vtable._29_TechR.method);
    //            v503 = (float)((float)(v501 * v499) * *(float*)&v502)
    //                 * (float)(((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                           this,
    //                           LIT_anti_torp_weight,
    //                           this->klass->vtable._29_TechR.method)
    //                       - 1.0);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              *(System_String_o**)&LIT_antitorpedo,
    //              v503,
    //              8,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            armorConningTowerMat = System_Linq_Enumerable__Last_Ship_MatInfo_(
    //                                     (System_Collections_Generic_IEnumerable_TSource__o*)helper->fields.mats,
    //                                     Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            tonnage_5 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                        this,
    //                        this->klass->vtable._24_Tonnage.method);
    //            tonnageLerp_1 = UnityEngine_Mathf__Lerp(1.0, 3.0, *(float*)&tonnage_5 / 100000.0, 0i64);
    //            if (!armorConningTowerMat)
    //                goto LABEL_400;
    //            armorConningTowerMat->fields.costMod = tonnageLerp_1;
    //            if ((Util_TypeInfo->_2.bitflags2 & 2) != 0 && !Util_TypeInfo->_2.cctor_finished)
    //                il2cpp_runtime_class_init(Util_TypeInfo);
    //            opRangeEnumValues = (System_Collections_IEnumerable_o*)Util__IterateEnum_VesselEntity_OpRange_(Method_Util_IterateEnum_VesselEntity_OpRange___);
    //            opRangesAsInts = (System_Collections_Generic_IEnumerable_TSource__o*)System_Linq_Enumerable__Cast_int_(
    //                                                                                    opRangeEnumValues,
    //                                                                                    Method_System_Linq_Enumerable_Cast_int___);
    //            opRange_k__BackingField = this->fields._opRange_k__BackingField;
    //            firstOpRange = System_Linq_Enumerable__First_int_(opRangesAsInts, Method_System_Linq_Enumerable_First_int___);
    //            LODWORD(opRangesAsInts) = System_Linq_Enumerable__Last_int_(
    //                                        opRangesAsInts,
    //                                        Method_System_Linq_Enumerable_Last_int___);
    //            wgtMinOp = MonoBehaviourExt__Param(LIT_w_op_range_min, 3.0, 0i64);
    //            wgtMaxOp = MonoBehaviourExt__Param(LIT_w_op_range_max, 15.0, 0i64);
    //            multFromOpRange = Util__Remap(
    //                                (float)opRange_k__BackingField,
    //                                (float)firstOpRange,
    //                                (float)(int)opRangesAsInts,
    //                                wgtMinOp,
    //                                wgtMaxOp,
    //                                0,
    //                                0i64);
    //            tonnage_6 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                        this,
    //                        this->klass->vtable._24_Tonnage.method);
    //            tonnage_7 = *(float*)&tonnage_6;
    //            techFuel = ((float(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //                       this,
    //                       LIT_fuel,
    //                       this->klass->vtable._29_TechR.method);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              *(System_String_o**)&LIT_op_range,
    //              (float)((float)(multFromOpRange / 100.0) * tonnage_7) * techFuel,
    //              9,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            matOpRange = System_Linq_Enumerable__Last_Ship_MatInfo_(
    //                           (System_Collections_Generic_IEnumerable_TSource__o*)object[1].klass,
    //                           Method_System_Linq_Enumerable_Last_Ship_MatInfo___);
    //            ((void(__fastcall *)(Ship_o *, __int64, const MethodInfo*))this->klass->vtable._29_TechR.methodPtr)(
    //            this,
    //            LIT_fuel_c,
    //            this->klass->vtable._29_TechR.method);
    //            if (!matOpRange)
    //                goto LABEL_400;
    //            matOpRange->fields.costMod = techFuel;
    //            v518 = (System_Collections_IEnumerable_o*)Util__IterateEnum_VesselEntity_OpRange_(Method_Util_IterateEnum_Ship_Survivability___);
    //            v519 = (System_Collections_Generic_IEnumerable_TSource__o*)System_Linq_Enumerable__Cast_int_(
    //                                                                          v518,
    //                                                                          Method_System_Linq_Enumerable_Cast_int___);
    //            survivability_k__BackingField = this->fields._survivability_k__BackingField;
    //            v521 = System_Linq_Enumerable__First_int_(v519, Method_System_Linq_Enumerable_First_int___);
    //            LODWORD(v519) = System_Linq_Enumerable__Last_int_(v519, Method_System_Linq_Enumerable_Last_int___);
    //            survMinMult = MonoBehaviourExt__Param(LIT_w_survivability_min, 0.0, 0i64);
    //            survMaxMult = MonoBehaviourExt__Param(LIT_w_survivability_max, 10.0, 0i64);
    //            survMult = Util__Remap(
    //                         (float)survivability_k__BackingField,
    //                         (float)v521,
    //                         (float)(int)v519,
    //                         survMinMult,
    //                         survMaxMult,
    //                         0,
    //                         0i64);
    //            v525 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v526 = *(float*)&v525;
    //            v527 = ((double(__fastcall *)(_QWORD, _QWORD, _QWORD))this->klass->vtable._29_TechR.methodPtr)(
    //                    this,
    //                    LIT_hull,
    //                    this->klass->vtable._29_TechR.method);
    //            v528 = *(float*)&v527;
    //            v529 = ((double(__fastcall *)(Ship_o *, const MethodInfo*))this->klass->vtable._24_Tonnage.methodPtr)(
    //                   this,
    //                   this->klass->vtable._24_Tonnage.method);
    //            v530 = (float)((float)((float)(survMult / 100.0) * v526) * v528)
    //                 * UnityEngine_Mathf__Lerp(0.66000003, 1.33, *(float*)&v529 / 90000.0, 0i64);
    //            System_Action_string__float__Ship_Mat___Invoke(
    //              addMat_name_wegiht_mat,
    //              *(System_String_o**)&LIT_survivability,
    //              v530,
    //              3,
    //              Method_System_Action_string__float__Ship_Mat__Invoke__);
    //            goto LABEL_302;
            }

            return mats;
        }

        public static bool IsPartAllowedNoTech(PartData hull, ShipType sType, PartData part)
        {
            bool failsTest = false;
            foreach (var needSet in part.needTags)
            {
                bool noOverlap = true;
                foreach (var s in needSet)
                {
                    if (hull.paramx.ContainsKey(s))
                    {
                        noOverlap = false;
                        break;
                    }
                }
                if (noOverlap)
                {
                    failsTest = true;
                    break;
                }
            }
            if (failsTest)
                return false;

            foreach (var excludeSet in part.excludeTags)
            {
                bool noOverlap = true;
                foreach (var s in excludeSet)
                {
                    if (hull.paramx.ContainsKey(s))
                    {
                        noOverlap = false;
                        break;
                    }
                }
                if (!noOverlap)
                {
                    failsTest = true;
                    break;
                }
            }

            if (failsTest)
                return false;

            if (part.isGun)
            {
                var calInch = part.GetCaliberInch();
                if ((calInch < sType.mainFrom || sType.mainTo < calInch) && (calInch < sType.secFrom || sType.secTo < calInch))
                    return false;

                if (hull.maxAllowedCaliber > 0 && calInch > hull.maxAllowedCaliber)
                    return false;

                if (hull.paramx.ContainsKey("unique_guns"))
                {
                    return part.paramx.ContainsKey("unique");
                }
                else
                {
                    // Ignore max caliber for secondary guns from tech
                    // Ignore barrel limits from tech
                    return true;
                }
            }

            // No need to check torpedo, since all paths lead to true.
            //if (part.isTorpedo)
            //{
            //    if (part.paramx.ContainsKey("sub_torpedo"))
            //        return true;
            //    if (part.paramx.ContainsKey("deck_torpedo"))
            //        return true;

            //    // Ignore barrel limit by tech
            //}

            return true;
        }

        public static float GetYear(PartData hull)
        {
            foreach (var tech in G.GameData.technologies)
            {
                if (!tech.Value.effects.TryGetValue("unlock", out var eff))
                    continue;

                foreach (var lst in eff)
                {
                    foreach (var item in lst)
                    {
                        if (item == hull.name)
                        {
                            return tech.value.year;
                        }
                    }
                }
            }

            return -1f;
        }

        public static float GetFreeboard(this Ship ship)
            => ship.armor.ArmorValue(Ship.A.None);

        public static void SetFreeboard(this Ship ship, float freeboard)
        {
            ship.armor[Ship.A.None] = freeboard;
        }

        public static float GetFineness(this Ship ship)
            => ship.armor.ArmorValue(Ship.A.Invalid);

        public static void SetFineness(this Ship ship, float fineness)
        {
            ship.armor[Ship.A.Invalid] = fineness;
        }
    }    
}
