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
using TweaksAndFixes;

#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

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

            var bestSec = ShipStats.GetDesiredSections(ship, desiredLdivB, desiredBdivT, out var finalBmPct, out var finalDrPct, out _, CpOffset);

            ship.SetBeam(finalBmPct, false);
            ship.SetDraught(finalDrPct, false);

            float t = Mathf.InverseLerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, bestSec);
            ship.ModData().SetFineness(Mathf.Lerp(ShipData._MinFineness, ShipData._MaxFineness, 1f - t));

            // Also set freeboard. Center it on 0.
            float fbVal = ModUtils.DistributedRange(1f, 3);
            if (fbVal < 0f)
                fbVal = Mathf.Lerp(0f, ShipData._MinFreeboard, -fbVal);
            else
                fbVal = Mathf.Lerp(0f, ShipData._MaxFreeboard, fbVal);
            ship.ModData().SetFreeboard(fbVal);

            if (ship.modelState == Ship.ModelState.Constructor || ship.modelState == Ship.ModelState.Battle)
                ship.RefreshHull(false);
        }

        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorReduce = GenerateAdjustArmorPriorities(false);
        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorIncrease = GenerateAdjustArmorPriorities(true);
        private static readonly Dictionary<Ship.A, float> _AdjustPriorityArmorLocal = new Dictionary<Ship.A, float>();
        private static readonly Dictionary<AdjustHullStatsItem, float> _AdjustHullStatsOptions = new Dictionary<AdjustHullStatsItem, float>();
        private static string[] _OpRangeValues = null;
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

        public static void AdjustHullStats(Ship ship, int delta, float targetWeight, Func<bool> stopCondition, Il2CppSystem.Func<bool> nativeStop,
            bool allowEditSpeed = true, bool allowEditArmor = true, bool allowEditCrewQuarters = true, bool allowEditRange = true, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null, float limitArmor = -1, float limitSpeed = 0f)
        {
            float year = ship.GetYear(ship);
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> oldArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            bool isCL = ship.shipType.name == "cl";
            bool isLightCraft = ship.shipType.name == "tb" || ship.shipType.name == "dd";
            bool cldd = isCL || isLightCraft;

            System.Func<bool> stopFunc;
            if (nativeStop != null)
                stopFunc = new Func<bool>(() =>
                {
                    return nativeStop.Invoke();
                });
            else if (stopCondition != null)
                stopFunc = stopCondition;
            else if (delta > 0)
                stopFunc = new Func<bool>(() =>
                {
                    return ship.Weight() / ship.Tonnage() >= targetWeight;
                });
            else
                stopFunc = new Func<bool>(() =>
                {
                    return ship.Weight() / ship.Tonnage() <= targetWeight;
                });

            VesselEntity.OpRange minOpRange = VesselEntity.OpRange.Low;
            if (ship.shipType.paramx.TryGetValue("range_min", out var minRange) && minRange.Count > 0)
            {
                if (_OpRangeValues == null)
                {
                    var vals = Enum.GetValues(typeof(VesselEntity.OpRange));
                    _OpRangeValues = new string[vals.Length];
                    for (int i = vals.Length; i-- > 0;)
                        _OpRangeValues[i] = Util.ToStringShort(vals.GetValue(i).ToString()).ToLower();
                }

                for (int i = 0; i < _OpRangeValues.Length; ++i)
                {
                    if (_OpRangeValues[i] == minRange[0].ToLower())
                    {
                        minOpRange = (VesselEntity.OpRange)i;
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
                armorInches = ship.shipType.armor * armorParam;
            }
            else
            {
                armorInches = armorMin * ModUtils.Range(1f, 1.2f, rnd, nativeRnd);
            }
            var armorMinHint = Ship.GenerateArmor(armorInches * 25.4f, ship);
            // TB/DD only armor CT and turrets
            if (isLightCraft)
            {
                foreach (var key in _AdjustPriorityArmorReduce.Keys)
                {
                    switch (key)
                    {
                        case Ship.A.ConningTower:
                        case Ship.A.Barbette:
                        case Ship.A.TurretSide:
                        case Ship.A.TurretTop:
                            continue;
                    }
                    armorMinHint[key] = 0f;
                }
            }

            float hullSpeedMS = ship.hull.data.speedLimiter * ShipStats.KnotsToMS;

            float oldSpeed = ship.speedMax;
            Ship.CrewQuarters oldQuarters = ship.CurrentCrewQuarters;
            var oldRange = ship.opRange < minOpRange ? minOpRange : ship.opRange;
            foreach (var kvp in ship.armor)
                oldArmor[kvp.Key] = kvp.Value;

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

            minSpeed = Mathf.Max(minSpeed, ship.shipType.speedMin * ShipStats.KnotsToMS);
            maxSpeed = Mathf.Min(maxSpeed, ship.shipType.speedMax * ShipStats.KnotsToMS);

            float armorLimit = limitArmor > 0f ? limitArmor : ship.shipType.armorMax * 3f * 25.4f;

            bool canMakeTarget;
            if (delta < 0)
            {

                ship.SetSpeedMax(minSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Cramped;
                ship.SetOpRange(minOpRange);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Max(armorMinHint[kvp.Key], MinArmorForZone(ship, kvp.Key));

                ship.SetArmor(newArmor);

                canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
                if (!canMakeTarget)
                {
                    foreach (var kvp in ship.armor)
                        newArmor[kvp.Key] = MinArmorForZone(ship, kvp.Key);

                    ship.SetArmor(newArmor);
                    canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
                }
            }
            else
            {
                ship.SetSpeedMax(maxSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Spacious;
                ship.SetOpRange(VesselEntity.OpRange.VeryHigh);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Min(armorLimit, MaxArmorForZone(ship, kvp.Key, null));
                foreach (var key in ship.GetCitadelArmor())
                    newArmor[key] = Mathf.Min(armorLimit, MaxArmorForZone(ship, key, null));

                ship.SetArmor(newArmor);

                canMakeTarget = targetWeight * 0.997f <= ship.Weight() / ship.Tonnage();
            }

            if (!canMakeTarget)
                return;

            oldSpeed = RoundSpeedToStep(oldSpeed);

            // Reset
            ship.SetSpeedMax(oldSpeed);
            ship.CurrentCrewQuarters = oldQuarters;
            ship.SetOpRange(oldRange);
            ship.SetArmor(oldArmor);

            Dictionary<Ship.A, float> armorPriority = delta > 0 ? _AdjustPriorityArmorIncrease : _AdjustPriorityArmorReduce;
            float speedStep = MonoBehaviourExt.Param("speed_step", 0.5f) * delta * ShipStats.KnotsToMS;

            for (int j = 0; j < 699; ++j)
            {
                if (stopFunc())
                    return;

                // Recreate, since we might have touched citadel armor
                newArmor.Clear();
                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = kvp.Value;
                oldArmor.Clear();
                foreach (var kvp in ship.armor)
                    oldArmor[kvp.Key] = kvp.Value;

                oldSpeed = ship.speedMax;
                oldQuarters = ship.CurrentCrewQuarters;
                oldRange = ship.opRange;

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
                        float maxZone = MaxArmorForZone(ship, kvp.Key, null);
                        if (maxZone > 0f)
                        {
                            armorMinHint.TryGetValue(kvp.Key, out var minHint);
                            float minArmor = Mathf.Max(minHint, MinArmorForZone(ship, kvp.Key));
                            float maxArmor = Mathf.Min(armorLimit, maxZone);
                            float oldLevel = ship.armor.ContainsKey(kvp.Key) ? ship.armor[kvp.Key] : 0f;
                            if ((delta > 0) ? (oldLevel < maxArmor) : (oldLevel > minArmor))
                                _AdjustPriorityArmorLocal.Add(kvp.Key, kvp.Value);
                        }
                    }
                    if (_AdjustPriorityArmorLocal.Count > 0)
                    {
                        var randomA = ModUtils.RandomByWeights(_AdjustPriorityArmorLocal);
                        armorMinHint.TryGetValue(randomA, out var minHint);
                        float minArmor = Mathf.Max(minHint, MinArmorForZone(ship, randomA));
                        float maxArmor = Mathf.Min(armorLimit, MaxArmorForZone(ship, randomA, null));
                        float oldLevel = ship.armor.ContainsKey(randomA) ? ship.armor[randomA] : 0f;
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
                if (allowEditSpeed && newSpeed != oldSpeed)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Speed, delta > 0 ? 350f : 690f);
                if (allowEditCrewQuarters && newQuarters != oldQuarters)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Quarters, 150f);
                if (armorFound)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Armor, delta > 0 ? 850f : 450f);
                if (allowEditRange && newOpRange != oldRange)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Range, delta > 0 ? 75f : 400f);

                if (_AdjustHullStatsOptions.Count == 0)
                    return;

                // We're going to replace entries we pull and use with "empty"
                // so that we don't screw with our weights. (If we just removed items,
                // like stock, then quarters and range would be overrepresented.)
                AdjustHullStatsItem thingToChange = AdjustHullStatsItem.Empty;
                while ((thingToChange = ModUtils.RandomByWeights(_AdjustHullStatsOptions, rnd, nativeRnd)) != AdjustHullStatsItem.Empty)
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
                            break;
                        case AdjustHullStatsItem.Quarters:
                            ship.CurrentCrewQuarters = newQuarters;
                            break;
                        case AdjustHullStatsItem.Range:
                            ship.SetOpRange(newOpRange);
                            break;
                        default:
                            ship.SetArmor(newArmor);
                            break;
                    }

                    if (ship.Weight() > ship.Tonnage() || (GameManager.IsMission && ship.player.isMain && BattleManager.Instance != null && BattleManager.Instance.MissionMainShip == ship && ship.Cost() > ship.player.cash))
                    {
                        switch (thingToChange)
                        {
                            case AdjustHullStatsItem.Speed:
                                ship.SetSpeedMax(oldSpeed);
                                break;
                            case AdjustHullStatsItem.Quarters:
                                ship.CurrentCrewQuarters = oldQuarters;
                                break;
                            case AdjustHullStatsItem.Range:
                                ship.SetOpRange(oldRange);
                                break;
                            default:
                                ship.SetArmor(oldArmor);
                                break;
                        }
                        return;
                    }

                    if (stopFunc())
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
            // NOTE this is the design year, not the hull's unlock year. This is because we presume
            // the machinery will be current (and the player might have selected an engine type
            // unlocked later than the hull anyway!)
            weight *= ShipStats.GetMachineryWeightMult(ship.shipType.name, ship.GetYear(ship), ship.hull.data) * ship.ModData().machineryMult;
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

        private const float _TurretLengthMult = 1.55f;
        private const float _TurretCalBigExp = 0.65f;
        private const float _TurretCalSmallExp = 1.5f;
        // Normalized to Iowa 16in/50
        private static readonly float _TurretCalBigMult = 15.44f / (Mathf.Pow(16f * 25.4f, _TurretCalBigExp) * _TurretLengthMult * TurretBarrelsLengthHeightMult(3f));
        private static readonly float _TurretCalSmallMult = Mathf.Pow(6f * 25.4f, _TurretCalBigExp) * _TurretCalBigMult / Mathf.Pow(6f * 25.4f, _TurretCalSmallExp);
        private const float _TurretBarrelsWidthPow = 0.4f;
        private static readonly float _TurretCalBarrelsWidthOffset = 1f - Mathf.Pow(2f, _TurretBarrelsWidthPow);

        public static float TurretBaseWidth(float cal, float year)
            => Mathf.Lerp(Mathf.Pow(cal, _TurretCalSmallExp) * _TurretCalSmallMult * Util.Remap(year, 1900f, 1930f, 0.5f, 1f, true),
                Mathf.Pow(cal, _TurretCalBigExp) * _TurretCalBigMult, Mathf.InverseLerp(6f * 25.4f, 7f * 25.4f, cal));

        public static float TurretBaseHeight(float cal)
        {
            float height = Mathf.Pow(cal, 0.35f) * 0.12f + 1.8f;
            if (cal < 5f * 25.4f)
                height *= cal * (1f / (5f * 25.4f));
            return height;
        }

        public static float TurretBarrelsWidthMult(float barrelsF)
            => Mathf.Pow(barrelsF, _TurretBarrelsWidthPow) + _TurretCalBarrelsWidthOffset;

        public static float TurretBarrelsLengthHeightMult(float barrelsF)
            => Mathf.Pow(barrelsF, 0.125f);

        public static float ArmorBeltHeight(Ship ship)
        {
            // TODO: Use scaledStats.B instead of size? Would avoid bulges.
            // Does make it somewhat slower though.
            return Mathf.Sqrt(ship.hullSize.size.x) * (1f + ship.ModData().Freeboard * 0.01f * 0.25f);
            // ALSO: eventually separate main belt, lower & upper casemate, etc.
        }

        public static float BarbetteWeight(Ship ship, Part p, float beltHeight, float barbetteWidth, float weightFaceHard, Ship.TurretArmor ta)
        {
            float barbHeight = ship.hull.transform.InverseTransformPoint(p.transform.position).z - beltHeight * 0.5f; // assume half above waterline
            if (barbHeight <= 0f)
                return 0f;
            float barbArea = barbHeight * Mathf.PI * barbetteWidth * (0.5f + 0.25f * 0.75f + 0.25f * 0.5f);
            return barbArea * weightFaceHard * ta.barbetteArmor;
        }

        //private struct BarbetteData
        //{
        //    public float _width;
        //    public float _turretWeight;
        //    public float _armor;
        //}

        public struct ArmorData
        {
            public float _weightFaceHard;
            public float _costFaceHard;
            public float _weightSTS;
            public float _costSTS;

            public ArmorData(Ship ship)
            {
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

                float armorBaseCost = Mathf.Lerp(5f, 11f, ship.Tonnage() / 200000f) * armorC;

                _weightFaceHard = techArmor * techBelt * armorDensity * 0.001f;
                _costFaceHard = armorBaseCost * beltC;
                _weightSTS = techArmor * techDeck * armorDensity * 0.001f;
                _costSTS = armorBaseCost * deckC;
            }
        }

        public static Il2CppSystem.Collections.Generic.List<Ship.MatInfo> PartMats(Ship ship, PartData data)
        {
            Il2CppSystem.Collections.Generic.List<Ship.MatInfo> mats = new Il2CppSystem.Collections.Generic.List<Ship.MatInfo>();

            // Armor vars are used in multiple cases so we have to declare here.
            float weightFaceHard = 0f, costFaceHard = 0f, weightSTS = 0f, costSTS = 0f;

            if (data.isHull || data.isGun)
            {
                var armorData = new ArmorData(ship);
                weightFaceHard = armorData._weightFaceHard;
                weightSTS = armorData._weightSTS;
                costFaceHard = armorData._costFaceHard;
                costSTS = armorData._costSTS;
            }

            if (data.isHull)
            {
                var stats = ShipStats.GetScaledStats(ship);
                float year = Database.GetYear(data);

                float tonnage = ship.Tonnage();
                float hullTechMult = ship.TechR("hull");
                float citadelLength = ship.GetDynamicCitadelMaxZ(false, false) - ship.GetDynamicCitadelMinZ(false, false);
                float citPct = citadelLength / stats.Lwl;

                float scantlingStrength = ship.ModData().scantlingStrength;

                var statsNormal = ShipStats.GetLoadingStats(ship, HullLoadState.Normal);
                float lbd = statsNormal.Lwl * statsNormal.B * statsNormal.T;
                float steelweight = lbd * (0.21f - 0.026f * Mathf.Log10(lbd)) * (1f + 0.025f * (statsNormal.Lwl / statsNormal.T - 12)) * (1f + (2f / 3f) * (statsNormal.Cb - 0.7f));
                steelweight *= 0.9f + (1f + ship.ModData().Freeboard * 0.02f) * 0.1f;
                steelweight *= 1f + citPct * 0.1f;
                steelweight *= 1.65f * hullTechMult;
                steelweight *= scantlingStrength;

                Ship.MatInfo mat = new Ship.MatInfo();
                mat.name = "hull";
                mat.mat = Ship.Mat.Hull;
                mat.weight = steelweight;
                mat.costMod = ship.TechCostMod(data) * Mathf.Lerp(1.55f, 12.5f, tonnage / 150000f) * 0.0675f; // stock
                mat.cost = 0f;
                mats.Add(mat);


                // engine
                float engineWeight = GetEngineMatsWeightCalculateValue(ship, false);

                mat = new Ship.MatInfo();
                mat.name = "engines";
                mat.mat = Ship.Mat.Engine;
                mat.weight = engineWeight;
                mat.costMod = ship.TechR("engine_c") * Mathf.Lerp(1f, 12.5f, tonnage / 150000f); // stock
                mat.cost = 0f;
                mats.Add(mat);

                // Armor
                // Note: height estimate includes upper belt, for now. On AON ships that's obviously correct
                // since there was one armor belt.
                float beltHeightEstimate = ArmorBeltHeight(ship);
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
                    : Util.Remap(stats.Cwp, 0.5f, ellipseCwp, Mathf.Sqrt(a * a + b * b) * 2f, halfCirc, true);
                float armorCitadelLength = citadelLength * shipSideLength / stats.Lwl;
                armorCitadelLength = Mathf.Clamp(armorCitadelLength, 0.2f * shipSideLength, 0.8f * shipSideLength); // sanity
                float armorExtLength = shipSideLength - armorCitadelLength;
                float bowRatio = ship.hullPartMaxZ - ship.GetDynamicCitadelMaxZ();
                if (bowRatio < 0.1f)
                    bowRatio = 0.1f;
                bowRatio = bowRatio / (bowRatio + Mathf.Abs(ship.hullPartMinZ - ship.GetDynamicCitadelMinZ()));

                // Belt
                mat = new Ship.MatInfo();
                mat.name = "armor_belt";
                mat.mat = Ship.Mat.Armor;
                float mainThick = ship.armor.ArmorValue(Ship.A.Belt);
                mainThick = (0.75f * mainThick + 0.25f * mainThick * 0.5f); // assume half is normal thickness
                // and half tapers below the waterline to half thickness

                mat.weight = weightFaceHard * armorCitadelLength * beltHeightEstimate * 2f * mainThick;
                mat.costMod = costFaceHard;
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_belt_bow";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightFaceHard * armorExtLength * bowRatio * beltHeightEstimate * 2f * ship.armor.ArmorValue(Ship.A.BeltBow);
                mat.costMod = costFaceHard;
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_belt_stern";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightFaceHard * armorExtLength * (1f - bowRatio) * beltHeightEstimate * 2f * ship.armor.ArmorValue(Ship.A.BeltStern);
                mat.costMod = costFaceHard;
                mat.cost = 0f;
                mats.Add(mat);

                //// Deck
                float Awp = stats.Lwl * stats.B * stats.Cwp;
                float extAreaRatio = 1f - citPct * Mathf.Sqrt(citPct);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * (1f - extAreaRatio) * Awp * ship.armor.ArmorValue(Ship.A.Deck);
                mat.costMod = costSTS;
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck_bow";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * extAreaRatio * bowRatio * Awp * ship.armor.ArmorValue(Ship.A.DeckBow);
                mat.costMod = costSTS;
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_deck_stern";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * extAreaRatio * (1f - bowRatio) * Awp * ship.armor.ArmorValue(Ship.A.DeckStern);
                mat.costMod = costSTS;
                mat.cost = 0f;
                mats.Add(mat);

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
                        mat.cost = 0f;
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
                        ctAreaEstimate = Mathf.Pow(p.data.weight, 2f / 3f);
                    }
                    // Unlike stock, we count uptakes as superstructure
                    else if (p.data.isTowerAny || p.data.isFunnel)
                    {
                        float areaEst = Mathf.Pow(p.data.weight, 2f / 3f);
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
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "armor_superstructure";
                mat.mat = Ship.Mat.Armor;
                mat.weight = weightSTS * superstructureAreaEstimate * ship.armor.ArmorValue(Ship.A.Superstructure);
                mat.costMod = costSTS;
                mat.cost = 0f;
                mats.Add(mat);

                // Torpedo Defense System
                float weightMultTDS = Math.Max(0f, ship.TechR("anti_torp_weight") - 1f);
                // total guesswork here
                float depthTDS = statsNormal.T * 0.75f;
                float volumeTDS = citadelLength * statsNormal.B * 0.2f * depthTDS; // assume 10% on each side
                float areaTDS = citadelLength * 2f * depthTDS;
                const float maxTDSDensity = 0.2f;
                const float maxTDSThickMult = 0.06f * 0.0254f;
                // FIXME maybe shouldn't increase TDS weight based on components that increase hull weight, just techs?
                float weightTDS = (volumeTDS * maxTDSDensity * hullTechMult + maxTDSThickMult * Mathf.Sqrt(tonnage) * areaTDS * weightSTS) * weightMultTDS;
                mat = new Ship.MatInfo();
                mat.name = "antitorpedo";
                mat.mat = Ship.Mat.AntiTorp;
                mat.weight = weightTDS;
                mat.costMod = 0.6f * Mathf.Lerp(1f, 3f, tonnage / 100000f) + 0.4f * costSTS; // stock, but use STS cost too
                mat.cost = 0f;
                mats.Add(mat);

                mat = new Ship.MatInfo();
                mat.name = "op_range";
                mat.mat = Ship.Mat.Fuel;
                mat.weight = tonnage * ShipStats.OpRangeToPct(ship.opRange, year, ship.shipType.name) * 0.01f;
                mat.costMod = ship.TechR("fuel_c");
                mat.cost = 0f;
                mats.Add(mat);

                // Handle survivability mostly like stock
                float survMinMult = MonoBehaviourExt.Param("w_survivability_min", 0f);
                float survMaxMult = MonoBehaviourExt.Param("w_survivability_max", 10f);
                float survWeight = hullTechMult * tonnage * scantlingStrength
                    * 0.01f * Util.Remap((int)ship.survivability, (int)Ship.Survivability.VeryLow, (int)Ship.Survivability.VeryHigh, survMinMult, survMaxMult);
                mat = new Ship.MatInfo();
                mat.name = "survivability";
                mat.mat = Ship.Mat.Surv;
                mat.weight = survWeight;
                mat.costMod = 1f;
                mat.cost = 0f;
                mats.Add(mat);
            }
            else if (data.isGun)
            {
                ship.CheckCaliberOnShip();
                var gunDataM = new GunDataM(data, ship, true);
                bool isCasemate = gunDataM.isCasemate;
                float baseWeight = gunDataM.BaseWeight();
                float techWeightMod = ship.TechWeightMod(data);

                float barrelMult = GunDataM.GetTurretBarrelWeightMult(data);
                float turretWeight = baseWeight * techWeightMod * barrelMult;
                if (gunDataM.isCasemate)
                    turretWeight *= MonoBehaviourExt.Param("w_turret_casemate_mod", 0.75f); // 0.5 in NAR

                float costMod = ship.TechCostMod(data) + gunDataM.RemapLength(MonoBehaviourExt.Param("gun_length_extra_cost_min", -0.2f), MonoBehaviourExt.Param("gun_length_extra_cost_max", 0.3f), 0f);

                var mat = new Ship.MatInfo();
                mat.name = "turret";
                mat.mat = Ship.Mat.Turret;
                mat.weight = turretWeight;
                mat.costMod = costMod;
                mat.cost = 0f;
                mats.Add(mat);

                int gunGrade = ship.TechGunGrade(data, true);
                float barrelWeight = gunDataM.BarrelWeight(gunGrade);
                float barrelsF = (float)data.barrels;
                mat = new Ship.MatInfo();
                mat.name = "turret_barrels";
                mat.weight = barrelsF * barrelWeight;
                mat.mat = Ship.Mat.Barrel;
                mat.costMod = costMod;
                mat.cost = 0f;
                mats.Add(mat);

                var ta = TweaksAndFixes.ShipM.FindMatchingTurretArmor(ship, data);
                if (ta != null)
                {
                    float aTop, aFace, aSides;
                    string sideMat;
                    string topMat;
                    float barrelWidthMult;
                    int calInch = Mathf.RoundToInt(gunDataM.calInch);
                    float cal = gunDataM.caliber;
                    float gunYear = Database.GetGunYear(calInch, gunGrade);
                    float barbetteWidth = TurretBaseWidth(cal, gunYear);
                    if (isCasemate)
                    {
                        aFace = 360f * cal * 0.001f; // i.e. faceplate 33x wide, 11x tall the caliber
                        aTop = aFace * 2.5f;
                        aSides = 0f;
                        sideMat = "casemate_gun_side_armor_weight_threshold";
                        topMat = "casemate_gun_top_armor_weight_threshold";
                        barrelWidthMult = 1f;
                    }
                    else
                    {
                        // hacky guesstimate of turret top and turret sides
                        bool isSmall = cal < 25.4f * 7f;
                        bool isCyl = !isSmall && gunYear < 1900;
                        barrelWidthMult = TurretBarrelsWidthMult(barrelsF);
                        bool isMain = ship.IsMainCal(data);
                        if (isMain)
                        {
                            sideMat = "armor_turret";
                            topMat = "armor_turret_top";
                        }
                        else
                        {
                            sideMat = "small_gun_side_armor_weight_threshold";
                            topMat = "small_gun_top_armor_weight_threshold";
                        }
                        if (isCyl)
                        {
                            float radius = 0.25f + cal * 0.013f * barrelWidthMult;
                            aTop = Mathf.PI * radius * radius;
                            float sidesArea = Mathf.PI * radius * 2f * (cal * 0.012f);
                            aFace = 0.25f * sidesArea;
                            aSides = sidesArea - aFace;
                            barbetteWidth *= barrelWidthMult;
                        }
                        else
                        {
                            float barrelScale = TurretBarrelsLengthHeightMult(barrelsF);
                            float length = barrelScale * 1.55f * barbetteWidth;
                            float height = barrelScale * TurretBaseHeight(cal);
                            barbetteWidth *= barrelWidthMult;

                            float frontSlopeMult = Util.Remap(gunYear, 1895f, 1925f, 1f, 1.25f, true);

                            aTop = barbetteWidth * length;
                            aFace = frontSlopeMult * barbetteWidth * height;
                            aSides = barbetteWidth * height + length * 2f * height;
                            aSides *= GetSideArmorRatio(cal, gunYear); // small guns have only face shields, and some guns have open backs.
                        }
                    }

                    aFace *= (1f - barrelsF * 0.1f) / barrelWidthMult; // gunports

                    // The game makes a distinction here between small guns and big guns
                    // (i.e. ACR/BB/BC grade and smaller). We don't, because armor is armor.

                    mat = new Ship.MatInfo();
                    mat.name = sideMat;
                    mat.mat = Ship.Mat.Armor;
                    mat.weight = (aFace * weightFaceHard + aSides * weightSTS) * ta.sideTurretArmor;
                    mat.costMod = (aFace * costFaceHard + aSides * costSTS) / (aFace + aSides);
                    mat.cost = 0f;
                    mats.Add(mat);

                    mat = new Ship.MatInfo();
                    mat.name = topMat;
                    mat.mat = Ship.Mat.Armor;
                    mat.weight = aTop * weightSTS * ta.topTurretArmor;
                    mat.costMod = costSTS;
                    mat.cost = 0f;
                    mats.Add(mat);

                    float totalBarbWeight = 0f;
                    int totalParts = 0;
                    float beltHeight = ArmorBeltHeight(ship);
                    foreach (var p in ship.parts)
                    {
                        if (p.data != data)
                            continue;

                        ++totalParts;
                        totalBarbWeight += BarbetteWeight(ship, p, beltHeight, barbetteWidth, weightFaceHard, ta);
                    }
                    mat = new Ship.MatInfo();
                    mat.name = "armor_turret_barbette";
                    mat.mat = Ship.Mat.Armor;
                    mat.weight = totalBarbWeight / (float)totalParts;
                    mat.costMod = costFaceHard;
                    mat.cost = 0f;
                    mats.Add(mat);
                }

                float ammoAmount = gunDataM.gunData.ammo * ship.TechA("ammo_amount");
                mat = new Ship.MatInfo();
                mat.name = "ammo";
                mat.mat = Ship.Mat.Ammo;
                mat.weight = ammoAmount * Part.AvgShellWeight(gunDataM.gunData, data, ship) * 0.001f;
                mat.costMod = ship.ShellCost(data);
                mat.cost = 0f;
                mats.Add(mat);
            }
            //else if (data.isBarbette)
            //{
            //    List<Part> barbettePartsOfData = new List<Part>();
            //    foreach (var p in ship.parts)
            //        if (p.data == data)
            //            barbettePartsOfData.Add(p);

            //    // failure case
            //    if (barbettePartsOfData.Count == 0)
            //    {
            //        var mat = new Ship.MatInfo();
            //        mat.name = "barbette";
            //        mat.mat = Ship.Mat.Hull;
            //        mat.weight = ship.TechWeightMod(data) * data.weight;
            //        mat.cost = ship.TechCostMod(data) * data.cost;
            //        mats.Add(mat);
            //    }
            //    else
            //    {
            //        Dictionary<PartData, BarbetteData> gunTurretWeights = new Dictionary<PartData, BarbetteData>();
            //        float totalWeight = 0f;
            //        float totalCost = 0f;
            //        foreach (var b in barbettePartsOfData)
            //        {
            //            var mounts = b.GetComponentsInChildren<Mount>();
            //            float barbetteZ = ship.hull.transform.InverseTransformPoint(b.transform.position).z;
            //            foreach (var m in mounts)
            //            {
            //                if (m == null)
            //                    continue;
            //                if (!m.barbette)
            //                    continue;
            //                if (m.employedPart == null || !m.employedPart.data.isGun)
            //                    continue;

            //                if (!gunTurretWeights.TryGetValue(m.employedPart.data, out var bData))
            //                {
            //                    float tWeight = 0f;
            //                    var gMats = ship.PartMats(m.employedPart.data, true);
            //                    foreach (var mt in gMats)
            //                    {
            //                        if (mt.name == "ammo" || mt.name == "armor_turret_barbette")
            //                            continue;
            //                        tWeight += mt.weight;
            //                    }
            //                    float cal = m.employedPart.data.caliber;
            //                    int gunYear = Database.GetGunYear(Mathf.RoundToInt(cal * (1f / 25.4f)), ship.TechGunGrade(m.employedPart.data));
            //                    var tc = TweaksAndFixes.ShipM.FindMatchingTurretCaliber(ship, m.employedPart.data);
            //                    if (tc != null)
            //                        cal += tc.diameter;
            //                    float barbetteWidth = TurretBaseWidth(cal, gunYear) * TurretBarrelsWidthMult(m.employedPart.data.barrels);
            //                    var ga = ship.GetGunArmor(m.employedPart);
            //                    bData = new BarbetteData()
            //                    {
            //                        _width = barbetteWidth,
            //                        _turretWeight = tWeight,
            //                        _armor = ga.barbetteArmor
            //                    };
            //                    gunTurretWeights.Add(m.employedPart.data, bData);
            //                }
            //                float barbHeight = Math.Max(0f, ship.hull.transform.InverseTransformPoint(m.employedPart.transform.position).z - barbetteZ);
            //                float structWeight = (1f + barbHeight / bData._width * 0.5f) * bData._turretWeight * 0.05f;
            //                totalWeight += structWeight;
            //                totalCost += b.data.cost + structWeight * 100f;
            //                // Don't add armor here, we do it all on the guns.
            //            }
            //        }
            //        float recip = 1f / barbettePartsOfData.Count;
            //        var mat = new Ship.MatInfo();
            //        mat.name = "barbette";
            //        mat.mat = Ship.Mat.Hull;
            //        mat.weight = totalWeight * recip * ship.TechWeightMod(data);
            //        mat.cost = totalCost * recip * ship.TechCostMod(data);
            //        mats.Add(mat);
            //    }
            //}
            else if (data.isTorpedo)
            {
                var torpData = G.GameData.TorpedosData(data);
                var torpMult = G.GameData.GetTorpedosDataValue(data, 0f, ship);
                var mat = new Ship.MatInfo();
                mat.name = "torpedo";
                mat.weight = torpData.baseTorpWeight * torpMult * ship.TechWeightMod(data);
                mat.mat = Ship.Mat.Torpedo;
                mat.costMod = torpData.baseTorpCost * torpMult * ship.TechCostMod(data);
                mat.cost = 0f;
                mats.Add(mat);
            }
            else
            {
                var mat = new Ship.MatInfo();
                mat.name = "part";
                mat.weight = data.weight * ship.TechWeightMod(data);
                mat.mat = Ship.Mat.Raw;
                mat.costMod = 1f;
                mat.cost = data.cost * ship.TechCostMod(data);
                mats.Add(mat);
            }

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

                            float flawMult = 1f + sValue.basic * 0.01f;
                            mat.weight = curWeight * flawMult;
                            // Normalize cost to unflawed
                            mat.costMod /= flawMult;
                            // Increase cost slightly regardless of direction of flaw
                            mat.costMod *= Mathf.Abs(flawMult - 1f) * 0.25f + 1f;
                        }
                    }
                }
            }
            // We'll always calc costs
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
                mat.part = data;

                if (!Ship.matCostsCache.TryGetValue(mat.mat, out var cost))
                    continue;
                if (mat.cost == 0f)
                    mat.cost = cost * mat.weight * mat.costMod;
                if (!data.isHull && mat.mat != Ship.Mat.Armor)
                    mat.cost *= data.costMod;
            }
            //Melon<UADRealismMod>.Logger.Msg($"PartMats: Part {data.name}");
            //foreach (var mat in mats)
            //{
            //    Melon<UADRealismMod>.Logger.Msg($"Mat: {mat.name}, type {mat.mat}, {mat.weight:N1}t, ${mat.cost:N0} from {mat.costMod:F3}");
            //}

            return mats;
        }

        public static float GetSideArmorRatio(float gunCaliber, float gunYear)
        {
            if (gunCaliber >= 6f * 25.4f)
                return 1f;

            if (gunCaliber < 4f * 25.4f)
                return 0f;

            return Util.Remap(gunCaliber, 4f * 25.4f, 6f * 25.4f, Util.Remap(gunYear, 1910f, 1940f, 0.125f, 1f, true), Util.Remap(gunYear, 1890f, 1920f, 0.25f, 1f, true));
        }

        public static float MaxArmorForZone(Ship ship, Ship.A armorA, PartData gunPartData)
        {
            float thickness = 0f;
            switch (armorA)
            {
                case Ship.A.ConningTower:
                    foreach (var part in ship.parts)
                    {
                        if (part.data.isTowerMain)
                        {
                            var wConning = MonoBehaviourExt.Param("w_conning_tower_threshold", 0.3f);
                            var conningPct = MonoBehaviourExt.Param("conning_tower_armor_percent", 0.05f);
                            thickness = Mathf.Clamp(ship.TechWeightMod(part.data) * part.data.weight * wConning * conningPct, 0f, ship.shipType.armorMax * 1.5f) * 25.4f;
                            break;
                        }
                    }
                    break;

                case Ship.A.TurretTop:
                case Ship.A.TurretSide:
                case Ship.A.Barbette:
                    if (gunPartData == null)
                        break;
                    var gdm = new GunDataM(gunPartData, ship, false);
                    if (gdm.calInch < 4f)
                    {
                        if (armorA != Ship.A.TurretSide)
                            return 0f;

                        thickness = 25.4f;
                        break;
                    }
                    float mult = armorA == Ship.A.TurretTop ? 0.75f : 1.5f;
                    thickness = mult * gdm.caliber;
                    if (gdm.isCasemate)
                    {
                        thickness *= 0.8f;
                        if (armorA != Ship.A.Barbette)
                            thickness = ship.shipType.armorMax == -1f ? float.PositiveInfinity : Math.Max(ship.shipType.armorMax * 25.4f, thickness);
                    }

                    if (G.settings.armorStep * 2f >= thickness)
                        return 0f;
                    break;

                case Ship.A.InnerBelt_1st:
                    if (ship.armor.TryGetValue(Ship.A.Belt, out var beltThick))
                        return beltThick * 0.5f;
                    break;

                case Ship.A.InnerBelt_2nd:
                    if (ship.armor.TryGetValue(Ship.A.InnerBelt_1st, out var beltThick1))
                        return beltThick1 * 0.8f;
                    break;

                case Ship.A.InnerBelt_3rd:
                    if (ship.armor.TryGetValue(Ship.A.InnerBelt_2nd, out var beltThick2))
                        return beltThick2 * 0.8f;
                    break;

                case Ship.A.InnerDeck_1st:
                    if (ship.armor.TryGetValue(Ship.A.Deck, out var deckThick))
                        return deckThick * 0.6f;
                    break;

                case Ship.A.InnerDeck_2nd:
                    if (ship.armor.TryGetValue(Ship.A.InnerDeck_1st, out var deckThick1))
                        return deckThick1 * 0.8f;
                    break;

                case Ship.A.InnerDeck_3rd:
                    if (ship.armor.TryGetValue(Ship.A.InnerDeck_2nd, out var deckThick2))
                        return deckThick2 * 0.8f;
                    break;
            }

            thickness = G.settings.RoundToArmorStep(thickness);
            if (thickness != 0f)
                return thickness;

            if (ship.shipType.armorMax == -1f)
                return float.PositiveInfinity;
            else
                return ship.StatEffect("armor_max_mod", false) * ship.shipType.armorMax * 25.4f;
        }

        public static float MinArmorForZone(Ship ship, Ship.A armor)
        {
            if (armor != Ship.A.Belt)
                return 0f;

            return ship.shipType.armorMin * 25.4f;
        }

        public static Il2CppSystem.Collections.Generic.List<Ship.MatInfo> GetCachedPartMats(Ship ship, PartData data)
        {
            if (ship.matsCache == null)
            {
                ship.matsCache = new Il2CppSystem.Collections.Generic.Dictionary<Part, Il2CppSystem.Collections.Generic.List<Ship.MatInfo>>();
                return null;
            }

            foreach (var part in ship.matsCache.Keys)
                if (part.data == data)
                    return ship.matsCache[part];

            return null;
        }

        public static void CachePartMats(Ship ship, PartData data, Il2CppSystem.Collections.Generic.List<Ship.MatInfo> mats)
        {
            if (ship.matsCache == null)
                return;

            if (data == ship.hull.data)
            {
                ship.matsCache[ship.hull] = mats;
                return;
            }

            foreach (var part in ship.parts)
            {
                if (part.data == data)
                    ship.matsCache[part] = mats;
            }
        }

        private static List<Part> _Parts = new List<Part>();

        public static void ClearMatCache(Ship ship, PartData data)
        {
            if (ship.matsCache == null)
                return;

            foreach (var p in ship.matsCache.Keys)
                if (p.data == data)
                    _Parts.Add(p);
            foreach (var p in _Parts)
                ship.matsCache.Remove(p);
            _Parts.Clear();

            ship.weightsValid = false;
        }

        public static void ClearMatCache(Ship ship, Part part)
        {
            if (ship.matsCache == null)
                return;

            ship.matsCache.Remove(part);
            ship.weightsValid = false;
        }

        public static void ClearMatCache(Ship ship)
        {
            if (ship.matsCache == null)
                return;

            ship.matsCache.Clear();
            ship.weightsValid = false;
        }

        private static readonly char[] _OpSplitChars = new char[] { '[', ']' };
        public enum RPOperation
        {
            Tag = 0,
            NotTag,
            Zero,
            NotZero,
            Design,
        }
        public static List<KeyValuePair<RPOperation, string>> CheckOperationsGetArgs(List<string> args)
        {
            List<KeyValuePair<RPOperation, string>> kvps = new List<KeyValuePair<RPOperation, string>>();
            if (args == null)
                return kvps;

            foreach (var arg in args)
            {
                var split = arg.Split(_OpSplitChars, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length != 2)
                    continue;
                var key = split[0];
                RPOperation op;
                switch(split[0])
                {
                    case "tag": op = RPOperation.Tag; break;
                    case "!tag": op = RPOperation.NotTag; break;
                    case "zero": op = RPOperation.Zero; break;
                    case "!zero": op = RPOperation.NotZero; break;
                    case "design": op = RPOperation.Design; break;
                    default: continue;
                }
                kvps.Add(new KeyValuePair<RPOperation, string>(op, split[1]));
            }
            return kvps;
        }

        public static bool CheckOperationsProcess(PartData hull, RPOperation op, string arg, Ship ship = null, string design = null)
        {
            switch (op)
            {
                case RPOperation.Zero:
                    return ship == null || ship.TechVar(arg) == 0f;
                case RPOperation.NotZero:
                    return ship == null || ship.TechVar(arg) != 0f;
                case RPOperation.Tag:
                    return hull.paramx.ContainsKey(arg);
                case RPOperation.NotTag:
                    return !hull.paramx.ContainsKey(arg);
                case RPOperation.Design:
                    return arg == design;
                default:
                    return true;
            }
        }

        public static bool CheckOperations(PartData hull, RandPart rp, out string vars)
        {
            var varsOr = string.Empty;
            var varsAnd = string.Empty;
            rp.paramx.TryGetValue("or", out var ors);
            var orOps = CheckOperationsGetArgs(ors.ToManaged());
            rp.paramx.TryGetValue("and", out var ands);
            var andOps = CheckOperationsGetArgs(ands.ToManaged());

            foreach (var op in orOps)
            {
                if (op.Key > RPOperation.NotTag)
                {
                    if (varsOr == string.Empty)
                        varsOr = "Or vars:";
                    if (op.Key == RPOperation.Zero)
                        varsOr += " " + op.Value + "=0";
                    else
                        varsOr += " " + op.Value + "!=0";
                }
            }
            foreach (var op in andOps)
            {
                if (op.Key > RPOperation.NotTag)
                {
                    if (varsAnd == string.Empty)
                        varsAnd = "And vars:";
                    if (op.Key == RPOperation.Zero)
                        varsAnd += " " + op.Value + "=0";
                    else
                        varsAnd += " " + op.Value + "!=0";
                }
            }
            vars = varsOr + ((varsOr != string.Empty && varsAnd != string.Empty) ? " / " : string.Empty) + varsAnd;

            bool ok = orOps.Count == 0;
            foreach (var op in orOps)
            {
                if (CheckOperationsProcess(hull, op.Key, op.Value))
                {
                    ok = true;
                    break;
                }
            }
            if (!ok || andOps.Count == 0)
                return ok;

            foreach (var op in andOps)
            {
                if (!CheckOperationsProcess(hull, op.Key, op.Value))
                {
                    ok = false;
                    break;
                }
            }
            return ok;
        }
    }
}