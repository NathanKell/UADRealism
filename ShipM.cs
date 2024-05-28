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
            float desiredBdivT = ShipStats.DefaultBdivT + ModUtils.DistributedRange(0.3f);
            float desiredLdivB = ShipStats.GetDesiredLdivB(ship, desiredBdivT);
            desiredLdivB *= 1f + ModUtils.DistributedRange(0.025f);

            float CpOffset = ModUtils.DistributedRange(0.02f, 3);
            //Melon<UADRealismMod>.Logger.Msg($"Iterating to find Cp for {(ship.speedMax / ShipStats.KnotsToMS)}kn. L/B {desiredLdivB:F2}, B/T {desiredBdivT:F2}, Cp offset {CpOffset:F3}");

            var bestSec = ShipStats.GetDesiredSections(ship, desiredLdivB, desiredBdivT, out var finalBmPct, out var finalDrPct, out _, CpOffset);

            ship.SetBeam(finalBmPct, false);
            ship.SetDraught(finalDrPct, false);

            float t = Mathf.InverseLerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, bestSec);
            ship.ModData().SetFineness(Mathf.Lerp(ShipData._MinFineness, ShipData._MaxFineness, 1f - t));

            // Also set freeboard. Center it on 0.
            float fbVal = ModUtils.DistributedRange(1f);
            if (fbVal < 0f)
                fbVal = Mathf.Lerp(0f, ShipData._MinFreeboard, -fbVal);
            else
                fbVal = Mathf.Lerp(0f, ShipData._MaxFreeboard, fbVal);
            ship.ModData().SetFreeboard(fbVal);

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

        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorReduce = GenerateAdjustArmorPriorities(false);
        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorIncrease = GenerateAdjustArmorPriorities(true);
        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorLocal = new Dictionary<Ship.A, float>();
        private static readonly Dictionary<AdjustHullStatsItem, float> _AdjustHullStatsOptions = new Dictionary<AdjustHullStatsItem, float>();
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

        public static float RoundSpeedToStep(float speed)
        {
            float step = MonoBehaviourExt.Param("speed_step", 0.1f);
            return Mathf.RoundToInt(speed / step * (1f / ShipStats.KnotsToMS)) * ShipStats.KnotsToMS * step;
        }

        public static void AdjustHullStats(Ship ship, int delta, float targetWeight, Func<bool> stopCondition, bool allowEditSpeed = true, bool allowEditArmor = true, bool allowEditCrewQuarters = true, bool allowEditRange = true, System.Random rnd = null, float limitArmor = -1, float limitSpeed = 0f)
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

            //Melon<UADRealismMod>.Logger.Msg($"Adjust. Old values: {oldSpeed:F1}/{oldQuarters}/{oldRange}. Armor {ModUtils.ArmorString(ship.armor)}");

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
                //Melon<UADRealismMod>.Logger.Msg($"Min values: {ship.speedMax:F1}/{ship.CurrentCrewQuarters}/{ship.opRange}. Armor {ModUtils.ArmorString(ship.armor)}.\n{ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");

                canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
                if (!canMakeTarget)
                {
                    foreach (var kvp in ship.armor)
                        newArmor[kvp.Key] = ship.MinArmorForZone(kvp.Key);

                    ship.SetArmor(newArmor);
                    //Melon<UADRealismMod>.Logger.Msg($"Trying again. Min values: {ship.speedMax:F1}/{ship.CurrentCrewQuarters}/{ship.opRange}. Armor {ModUtils.ArmorString(ship.armor)}.\n{ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");
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
            //Melon<UADRealismMod>.Logger.Msg($"Min speed: {minSpeed:F1}, max {maxSpeed:F1}");
            oldSpeed = RoundSpeedToStep(oldSpeed);

            // Reset
            ship.SetSpeedMax(oldSpeed);
            ship.CurrentCrewQuarters = oldQuarters;
            ship.SetOpRange(oldRange);
            ship.SetArmor(oldArmor);

            if (stopCondition())
                return;

            Dictionary<Ship.A, float> armorPriority = delta > 0 ? _AdjustPriorityArmorIncrease : _AdjustPriorityArmorReduce;
            float speedStep = MonoBehaviourExt.Param("speed_step", 0.5f) * delta * ShipStats.KnotsToMS;

            for (int j = 0; j < 699; ++j)
            {
                // Copy armor over (since it might have changed)
                newArmor.Clear();
                // Recreate, since we might have touched citadel armor
                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = kvp.Value;

                float newSpeed = speedStep + ship.speedMax;
                newSpeed = Mathf.Clamp(newSpeed, minSpeed, maxSpeed);
                newSpeed = RoundSpeedToStep(newSpeed);
                Ship.CrewQuarters newQuarters = (Ship.CrewQuarters)Mathf.Clamp((int)ship.CurrentCrewQuarters + delta, (int)Ship.CrewQuarters.Cramped, (int)Ship.CrewQuarters.Spacious);
                VesselEntity.OpRange newOpRange = (VesselEntity.OpRange)Mathf.Clamp((int)ship.opRange + delta, (int)minOpRange, (int)VesselEntity.OpRange.VeryHigh);

                // We don't have to do all this work if we're not allowed to change armor anyway
                bool armorFound = false;
                if (allowEditArmor)
                {
                    // We need to find a valid armor zone. Note
                    // due to citadel armor weirdness, we have
                    // to do this fresh each time.
                    foreach (var kvp in armorPriority)
                    {
                        float maxZone = ship.MaxArmorForZone(kvp.Key);
                        if (maxZone > 0f)
                        {
                            armorMinHint.TryGetValue(kvp.Key, out var minHint);
                            float minArmor = Mathf.Max(minHint, ship.MinArmorForZone(kvp.Key));
                            float maxArmor = Mathf.Min(armorLimit, maxZone);
                            float oldLevel = ship.armor.ContainsKey(kvp.Key) ? ship.armor[kvp.Key] : 0f; // trygetvalue is grumpy in IL2Cpp
                            if ((delta > 0) ? (oldLevel < maxArmor) : (oldLevel > minArmor))
                                _AdjustPriorityArmorLocal.Add(kvp.Key, kvp.Value);
                        }
                    }
                    if(_AdjustPriorityArmorLocal.Count > 0)
                    {
                        var randomA = ModUtils.RandomByWeights(_AdjustPriorityArmorLocal);
                        armorMinHint.TryGetValue(randomA, out var minHint);
                        float minArmor = Mathf.Max(minHint, ship.MinArmorForZone(randomA));
                        float maxArmor = Mathf.Min(armorLimit, ship.MaxArmorForZone(randomA));
                        float oldLevel = ship.armor.ContainsKey(randomA) ? ship.armor[randomA] : 0f; // trygetvalue is grumpy in IL2Cpp
                        float newArmorLevel = delta * 2.54f + oldLevel;
                        if (delta < 0)
                            newArmorLevel = Mathf.Floor(newArmorLevel);
                        else
                            newArmorLevel = Mathf.Ceil(newArmorLevel);
                        newArmorLevel = Mathf.Clamp(newArmorLevel, minArmor, maxArmor);

                        // by definition this check should be true because
                        // we shouldn't have added the zone otherwise. But just in case.
                        if (newArmorLevel != oldLevel)
                        {
                            armorFound = true;
                            newArmor[randomA] = newArmorLevel;
                        }
                        _AdjustPriorityArmorLocal.Clear();
                    }
                }

                _AdjustHullStatsOptions.Clear();
                if (allowEditSpeed && newSpeed != ship.speedMax)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Speed, delta > 0 ? 350f : 690f);
                if (allowEditCrewQuarters && newQuarters != ship.CurrentCrewQuarters)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Quarters, 150f);
                if (armorFound)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Armor, delta > 0 ? 850f : 450f);
                if (allowEditRange && newOpRange != ship.opRange)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Range, delta > 0 ? 75f : 400f);

                //string newValues = "Potentials:";
                //foreach (var kvp in _AdjustHullStatsOptions)
                //{
                //    string val = null;
                //    switch (kvp.Key)
                //    {
                //        case AdjustHullStatsItem.Armor:
                //            val = $" A:{ship.armor[randomA]:F1}->{newArmor[randomA]:F1}";
                //            break;
                //        case AdjustHullStatsItem.Speed:
                //            val = $" S:{ship.speedMax:F1}->{newSpeed:F1}";
                //            break;
                //        case AdjustHullStatsItem.Range:
                //            val = $" R:{ship.opRange}->{newOpRange}";
                //            break;
                //        default:
                //            val = $" C:{ship.CurrentCrewQuarters}->{newQuarters}";
                //            break;
                //    }
                //    newValues += val;
                //}
                //Melon<UADRealismMod>.Logger.Msg(newValues);

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
                            //Melon<UADRealismMod>.Logger.Msg($"Picked speed");
                            break;
                        case AdjustHullStatsItem.Quarters:
                            ship.CurrentCrewQuarters = newQuarters;
                            //Melon<UADRealismMod>.Logger.Msg($"Picked quarters");
                            break;
                        case AdjustHullStatsItem.Range:
                            ship.SetOpRange(newOpRange);
                            //Melon<UADRealismMod>.Logger.Msg($"Picked range");
                            break;
                        default:
                            ship.SetArmor(newArmor);
                            //Melon<UADRealismMod>.Logger.Msg($"Picked armor");
                            break;
                    }
                    //Melon<UADRealismMod>.Logger.Msg($"Checking: {ship.Weight():F0}/{ship.Tonnage():F0}={(ship.Weight() / ship.Tonnage()):F2} vs {targetWeight:F2}");
                    if (stopCondition())
                        return;
                }
            }
        }

        public static float GetEngineMatsWeightCalculateValue(Ship ship, bool flaws)
        {
            var hp = ship.EnginePower();
            float techHP = ship.TechMax("hp");
            float techEngine = ship.TechR("engine");
            float techBoiler = ship.TechR("boiler");
            float weight = (0.45f * techBoiler + 0.55f * techEngine) * (hp / techHP);
            if (!flaws || !GameManager.IsCampaign || !Ship.IsFlawsActive(ship))
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
                float year = GetYear(part);

                float tonnage = ship.Tonnage();
                float hullTechMult = ship.TechR("hull");
                float citadelLength = ship.GetDynamicCitadelMaxZ(false, false) - ship.GetDynamicCitadelMinZ(false, false);
                float citPct = citadelLength / stats.Lwl;

                float scantlingStrength;
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
                steelweight *= 0.9f + (1f + ship.ModData().Freeboard * 0.02f) * 0.1f;
                steelweight *= 1f + citPct * 0.1f;
                float modifiedHullTechMult = Util.Remap(hullTechMult, 0.58f, 1f, 0.675f, 1f);
                //Melon<UADRealismMod>.Logger.Msg($"PartMats: Scantlings {scantlingStrength:F2}, tech {modifiedHullTechMult:F3}. Cit len {citadelLength:F1} / {stats.Lwl:F1}");
                steelweight *= 1.65f * modifiedHullTechMult;
                steelweight *= scantlingStrength;

                Ship.MatInfo mat = new Ship.MatInfo();
                mat.name = "hull";
                mat.mat = Ship.Mat.Hull;
                mat.weight = steelweight;
                mat.costMod = ship.TechCostMod(part) * Mathf.Lerp(1.55f, 12.5f, tonnage / 150000f) * 0.0675f; // stock
                mats.Add(mat);


                // engine
                float engineWeight = GetEngineMatsWeightCalculateValue(ship, false);
                if (isTBDD)
                    engineWeight *= 0.8f;

                mat = new Ship.MatInfo();
                mat.name = "engines";
                mat.mat = Ship.Mat.Engine;
                mat.weight = engineWeight;
                mat.costMod = ship.TechR("engine_c") * Mathf.Lerp(1f, 12.5f, tonnage / 150000f); // stock
                mats.Add(mat);

                // Armor
                // Note: height estimate includes upper belt, for now. On AON ships that's obviously correct
                // since there was one armor belt.
                float beltHeightEstimate = Mathf.Sqrt(stats.B) * (1f + ship.ModData().Freeboard * 0.01f * 0.25f);
                float a = stats.Lwl * 0.5f;
                float b = stats.B * 0.5f;
                float ab = a + b;
                float ab2 = ab * ab;
                float h = (a - b) * (a - b) / ab2;
                float halfCirc = Mathf.PI * ab * (1 + 3 * h / (10 + Mathf.Sqrt(4 - 3 * h))) * 0.5f;
                float cornerCirc = stats.Lwl + stats.B;
                halfCirc = 0.8f * halfCirc + 0.2f * cornerCirc;
                const float ellipseCwp = Mathf.PI / 4f;
                float shipSideLength = stats.Cwp > ellipseCwp ? Util.Remap(stats.Cwp, ellipseCwp, 1f, halfCirc, cornerCirc)
                    : Util.Remap(stats.Cwp, 0.5f, ellipseCwp, Mathf.Sqrt(a*a + b*b) * 2f, halfCirc, true);
                float armorCitadelLength = citadelLength * shipSideLength / stats.Lwl;
                armorCitadelLength = Mathf.Clamp(armorCitadelLength, 0.2f * shipSideLength, 0.8f * shipSideLength); // sanity
                float armorExtLength = shipSideLength - armorCitadelLength;
                float bowRatio = ship.hullPartMaxZ - ship.GetDynamicCitadelMaxZ();
                if (bowRatio < 0.1f)
                    bowRatio = 0.1f;
                bowRatio = bowRatio / (bowRatio + Mathf.Abs(ship.hullPartMinZ - ship.GetDynamicCitadelMinZ()));
                const float armorDensity = 7.86f; // grams / cubic centimeter or tonnes / cubic meter

                float techArmor = ship.TechR("armor");
                float techBelt = ship.TechR("belt");
                float techDeck = ship.TechR("deck");
                techArmor = Util.Remap(techArmor, 0.75f, 1.25f, 0.96f, 1.06f);
                techBelt = Util.Remap(techBelt, 0.75f, 1.25f, 0.96f, 1.06f);
                techDeck = Util.Remap(techDeck, 0.75f, 1.25f, 0.96f, 1.06f);
                float armorC = ship.TechR("armor_c") * 0.75f; // STS rather than face-hardened
                float beltC = ship.TechR("belt_c") * (1f / 0.75f); // back to face-harended
                float deckC = ship.TechR("deck_c");

                float armorBaseCost = Mathf.Lerp(5f, 11f, tonnage / 200000f) * armorC;

                float weightFaceHard = techArmor * techBelt * armorDensity * 0.001f;
                float costFaceHard = armorBaseCost * beltC;
                float weightSTS = techArmor * techDeck * armorDensity * 0.001f;
                float costSTS = armorBaseCost * deckC;

                // Belt
                mat = new Ship.MatInfo();
                mat.name = "armor_belt";
                mat.mat = Ship.Mat.Armor;
                float mainThick = ship.armor.ArmorValue(Ship.A.Belt);
                mainThick = (0.75f * mainThick + 0.25f * mainThick * 0.5f); // assume half is normal thickness
                // and half tapers below the waterline to half thickness

                mat.weight = weightFaceHard * armorCitadelLength * beltHeightEstimate * 2f * mainThick;
                mat.costMod = costFaceHard;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_belt_bow";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightFaceHard * armorExtLength * bowRatio * beltHeightEstimate * 2f * ship.armor.ArmorValue(Ship.A.BeltBow);
                mat.costMod = costFaceHard;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_belt_stern";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightFaceHard * armorExtLength * (1f - bowRatio) * beltHeightEstimate * 2f * ship.armor.ArmorValue(Ship.A.BeltStern);
                mat.costMod = costFaceHard;
                mats.Add(mat);

                //// Deck
                float Awp = stats.Lwl * stats.B * stats.Cwp;
                float extAreaRatio = 1f - citPct * Mathf.Sqrt(citPct);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * (1f - extAreaRatio) * Awp * ship.armor.ArmorValue(Ship.A.Deck);
                mat.costMod = costSTS;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck_bow";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * extAreaRatio * bowRatio * Awp * ship.armor.ArmorValue(Ship.A.DeckBow);
                mat.costMod = costSTS;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck_stern";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * extAreaRatio * (1f - bowRatio) * Awp * ship.armor.ArmorValue(Ship.A.DeckStern);
                mat.costMod = costSTS;
                mats.Add(mat);

                //Melon<UADRealismMod>.Logger.Msg($"Armor: C: {ship.GetDynamicCitadelMinZ():F1}-{ship.GetDynamicCitadelMaxZ():F1} vs {ship.hullPartMinZ:F1}-{ship.hullPartMaxZ:F1}. Bow {bowRatio:F3}. Belt: {armorCitadelLength:F1}x{beltHeightEstimate:F1}m. QC {halfCirc:F1}, QSL {shipSideLength:F1}, "
                //    + $"Deck: Awp {Awp:F1}. Ratio {extAreaRatio:F3}");

                var citArmor = ship.GetCitadelArmor();
                if (citArmor != null)
                {
                    float beltMult = weightFaceHard * armorCitadelLength * beltHeightEstimate * 2f;
                    float deckMult = weightSTS * (1f - extAreaRatio) * Awp;
                    foreach (var area in citArmor)
                    {
                        mat = new Ship.MatInfo();
                        switch (area)
                        {
                            case Ship.A.InnerBelt_1st: mat.name = "armor_inner_belt_1st"; break;
                            case Ship.A.InnerBelt_2nd: mat.name = "armor_inner_belt_2nd"; break;
                            case Ship.A.InnerBelt_3rd: mat.name = "armor_inner_belt_3rd"; break;
                            case Ship.A.InnerDeck_1st: mat.name = "armor_inner_deck_1st"; break;
                            case Ship.A.InnerDeck_2nd: mat.name = "armor_inner_deck_2nd"; break;
                            default:
                            case Ship.A.InnerDeck_3rd: mat.name = "armor_inner_deck_3rd"; break;
                        }
                        mat.mat = Ship.Mat.Armor;
                        if (area > Ship.A.InnerBelt_3rd)
                        {
                            mat.weight = deckMult * ship.armor.ArmorValue(area);
                            mat.costMod = costSTS;
                        }
                        else
                        {
                            mat.weight = beltMult * ship.armor.ArmorValue(area);
                            mat.costMod = costFaceHard;
                        }
                        mats.Add(mat);
                    }
                }

                // We'll estimate area as the 2/3rds power of weight.
                float ctAreaEstimate = 0f;
                float superstructureAreaEstimate = 0f;
                foreach (var p in ship.parts)
                {
                    if (p.data.isTowerMain)
                    {
                        ctAreaEstimate = Mathf.Pow(p.data.weight * ship.TechWeightMod(p.data), 2f / 3f);
                    }
                    // Unlike stock, we count uptakes as superstructure
                    else if (p.data.isTowerAny || p.data.isFunnel)
                    {
                        float areaEst = Mathf.Pow(p.data.weight * ship.TechWeightMod(p.data), 2f / 3f);
                        // But uptakes aren't the whole funnel
                        if (p.data.isFunnel)
                            areaEst *= 0.25f;

                        superstructureAreaEstimate += areaEst;
                    }
                }

                mat = new Ship.MatInfo();
                mat.name = "armor_conning_tower";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightFaceHard * ctAreaEstimate * ship.armor.ArmorValue(Ship.A.ConningTower);
                mat.costMod = costFaceHard;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_superstructure";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * superstructureAreaEstimate * ship.armor.ArmorValue(Ship.A.Superstructure);
                mat.costMod = costSTS;
                mats.Add(mat);

                // Torpedo Defense System
                float weightMultTDS = ship.TechR("anti_torp_weight") - 1f;
                // total guesswork here
                float depthTDS = stats.T * 0.75f;
                float volumeTDS = citadelLength * stats.B * 0.2f * depthTDS; // assume 10% on each side
                float areaTDS = citadelLength * 2f * depthTDS;
                const float maxTDSDensity = 0.2f;
                const float maxTDSThickMult = 0.06f * 0.0254f;
                float weightTDS = (volumeTDS * maxTDSDensity * modifiedHullTechMult + maxTDSThickMult * Mathf.Sqrt(tonnage) * areaTDS * weightSTS) * weightMultTDS;
                mat = new Ship.MatInfo();
                mat.name = "antitorpedo";
                mat.mat = Ship.Mat.AntiTorp;
                mat.weight = weightTDS;
                mat.costMod = 0.6f * Mathf.Lerp(1f, 3f, tonnage / 100000f) + 0.4f * costSTS; // stock, but use STS cost too
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "op_range";
                mat.mat = Ship.Mat.Fuel;
                mat.weight = tonnage * ShipStats.OpRangeToPct(ship.opRange);
                mat.costMod = ship.TechR("fuel_c");
                mats.Add(mat);

                // Handle survivability mostly like stock
                float survMinMult = MonoBehaviourExt.Param("w_survivability_min", 0f);
                float survMaxMult = MonoBehaviourExt.Param("w_survivability_max", 10f);
                float survWeight = modifiedHullTechMult * tonnage * scantlingStrength
                    * 0.01f * Util.Remap((int)ship.survivability, (int)Ship.Survivability.VeryLow, (int)Ship.Survivability.VeryHigh, survMinMult, survMaxMult);
                mat = new Ship.MatInfo();
                mat.name = "survivability";
                mat.mat = Ship.Mat.Surv;
                mat.weight = survWeight;
                mat.costMod = 1f;
                mats.Add(mat);
            }

            foreach (var mat in mats)
                mat.part = part;

            if (Ship.IsFlawsActive(ship))
            {
                foreach (var mat in mats)
                {
                    for (int i = 0; i < Ship.flawsDefectStats.Length; ++i)
                    {
                        Ship.FlawsStats flaw = Ship.flawsDefectStats[i];
                        if (ship.IsThisShipIsFlawsDefects(mat.name, flaw))
                        {
                            float curWeight = mat.weight;
                            ship.CStats();
                            if (!G.GameData.stats.TryGetValue(flaw.ToString(), out var sData))
                                continue;
                            if (!ship.stats.TryGetValue(sData, out var sValue))
                                continue;
                            mat.weight = curWeight + sValue.basic * 0.01f * curWeight;
                        }
                    }
                }
            }

            if (calcCosts)
            {
                if (Ship.matCostsCache == null)
                {
                    Ship.matCostsCache = new Il2CppSystem.Collections.Generic.Dictionary<Ship.Mat, float>();

                    float cSteel = MonoBehaviourExt.Param("price_steel", 53f);
                    float cNickel = MonoBehaviourExt.Param("price_nickel", 1401f);
                    float cChrome = MonoBehaviourExt.Param("price_chrome", 765f);
                    float cMolyb = MonoBehaviourExt.Param("price_molybdenum", 9800f);
                    float cCopper = MonoBehaviourExt.Param("price_copper", 583f);
                    float cHull = MonoBehaviourExt.Param("price_hull", cSteel);
                    float cSurv = MonoBehaviourExt.Param("price_surv", cSteel);
                    float cArmor = MonoBehaviourExt.Param("price_armor", cSteel * 0.94f + cNickel * 0.04f + cChrome * 0.02f);
                    float cTurret = MonoBehaviourExt.Param("price_turret", cSteel);
                    float cBarrel = MonoBehaviourExt.Param("price_barrel", cSteel * 0.935f + cNickel * 0.04f + cChrome * 0.015f + cMolyb * 0.01f);
                    float cEngine = MonoBehaviourExt.Param("price_engine", cSteel * 0.9f + cCopper * 0.1f);
                    float cAntiTorp = MonoBehaviourExt.Param("price_anti_torp", cSteel);
                    float cFuel = MonoBehaviourExt.Param("price_fuel", 0f);
                    float cAmmo = MonoBehaviourExt.Param("price_ammo", 0f);
                    float cTorp = MonoBehaviourExt.Param("price_torpedoes", 0f);

                    Ship.matCostsCache.Add(Ship.Mat.Raw, -1f);
                    Ship.matCostsCache.Add(Ship.Mat.Steel, cSteel);
                    Ship.matCostsCache.Add(Ship.Mat.Hull, cHull);
                    Ship.matCostsCache.Add(Ship.Mat.Surv, cSurv);
                    Ship.matCostsCache.Add(Ship.Mat.Armor, cArmor);
                    Ship.matCostsCache.Add(Ship.Mat.Turret, cTurret);
                    Ship.matCostsCache.Add(Ship.Mat.Barrel, cBarrel);
                    Ship.matCostsCache.Add(Ship.Mat.Engine, cEngine);
                    Ship.matCostsCache.Add(Ship.Mat.AntiTorp, cAntiTorp);
                    Ship.matCostsCache.Add(Ship.Mat.Fuel, cFuel);
                    Ship.matCostsCache.Add(Ship.Mat.Ammo, cAmmo);
                    Ship.matCostsCache.Add(Ship.Mat.Torpedo, cTorp);
                }

                foreach (var mat in mats)
                {
                    if (!Ship.matCostsCache.TryGetValue(mat.mat, out var cost))
                        continue;

                    mat.cost = cost * mat.weight * mat.costMod;
                    if (!part.isHull)
                        mat.cost *= part.costMod;

                    if (Ship.IsFlawsActive(ship))
                    {
                        for (int i = 0; i < Ship.flawsDefectStats.Length; ++i)
                        {
                            Ship.FlawsStats flaw = Ship.flawsDefectStats[i];
                            if (ship.IsThisShipIsFlawsDefects(mat.name, flaw))
                            {
                                float oldCost = mat.cost;
                                ship.CStats();
                                if (!G.GameData.stats.TryGetValue(flaw.ToString(), out var sData))
                                    continue;
                                if (!ship.stats.TryGetValue(sData, out var sValue))
                                    continue;
                                // This takes it back to what the weight would have been
                                mat.cost = oldCost / (1 + sValue.basic * 0.01f);
                            }
                        }
                    }
                }
            }
            //foreach (var mat in mats)
            //{
            //    Melon<UADRealismMod>.Logger.Msg($"Mat: {mat.name}, type {mat.mat}, {mat.weight:N1}t, ${mat.cost:N0} from {mat.costMod:F3}");
            //}

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

        public static ShipData ModData(this Ship ship)
        {
            var sd = ship.gameObject.GetComponent<ShipData>();
            if (sd == null)
                sd = ship.gameObject.AddComponent<ShipData>();

            return sd;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class ShipData : MonoBehaviour
    {
        public const float _MinFineness = 0f;
        public const float _MaxFineness = 100f;
        public const float _MinFreeboard = -30f;
        public const float _MaxFreeboard = 45f;

        public ShipData(IntPtr ptr) : base(ptr) { }

        private float _freeboard = 0f;
        public float Freeboard => _freeboard;
        private float _fineness = _MinFineness + (_MaxFineness - _MinFineness) * 0.5f;
        public float Fineness => _fineness;
        private bool _ignoreNextPartYChange = false;
        public bool IgnoreNextPartYChange => _ignoreNextPartYChange;
        private Ship _ship = null;

        public void SetFreeboard(float fb) => _freeboard = fb;
        public void SetFineness(float fn) => _fineness = fn;
        public void SetIgnoreNextPartYChange(bool val) => _ignoreNextPartYChange = val;

        public int SectionsFromFineness()
        {
            return Mathf.RoundToInt(Mathf.Lerp(_ship.hull.data.sectionsMin, _ship.hull.data.sectionsMax, 1f - _fineness * 0.01f));
        }

        public void ToStore(Ship.Store store)
        {
            store.hullPartSizeZ = _fineness;
            store.hullPartSizeY = _freeboard;
        }

        public void FromStore(Ship.Store store)
        {
            _fineness = store.hullPartSizeZ;
            _freeboard = store.hullPartSizeY;

            store.hullPartSizeZ = 0f;
            store.hullPartSizeY = 0f;
            store.hullPartMaxZ = 0f;
            store.hullPartMinZ = 0f;

            // Let's also set beam and draught so when ChangeHull
            // calls SetTonnage which calls RefreshHull (since it
            // calls SetBeam/Draught without model updating) the
            // hull will be correct _before_ FromStore adds parts.
            _ship.beam = store.beam;
            _ship.draught = store.draught;
        }

        private void Awake()
        {
            _ship = gameObject.GetComponent<Ship>();
        }
    }
}
