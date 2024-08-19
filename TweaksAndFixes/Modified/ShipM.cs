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
using Il2CppSystem.Linq;
using Il2CppDigitalRuby.PyroParticles;

#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class ShipM
    {
        public static Ship.TurretArmor FindMatchingTurretArmor(Ship ship, PartData data)
            => FindMatchingTurretArmor(ship, data.caliber, Ship.IsCasemateGun(data));

        public static Ship.TurretArmor FindMatchingTurretArmor(Ship ship, float caliber, bool isCasemate)
        {
            foreach (var ta in ship.shipTurretArmor)
                if (ta.turretPartData.caliber == caliber && ta.isCasemateGun == isCasemate)
                    return ta;

            return null;
        }

        public static void CloneFrom(this Ship.TurretArmor dest, Ship.TurretArmor src)
        {
            dest.barbetteArmor = src.barbetteArmor;
            dest.isCasemateGun = src.isCasemateGun;
            dest.sideTurretArmor = src.sideTurretArmor;
            dest.topTurretArmor = src.topTurretArmor;
        }

        public static Ship.TurretCaliber FindMatchingTurretCaliber(Ship ship, PartData data)
            => FindMatchingTurretCaliber(ship, data.caliber, Ship.IsCasemateGun(data));

        public static Ship.TurretCaliber FindMatchingTurretCaliber(Ship ship, float caliber, bool isCasemate)
        {
            foreach (var tc in ship.shipGunCaliber)
                if (tc.turretPartData.caliber == caliber && isCasemate == tc.isCasemateGun)
                    return tc;

            return null;
        }

        public static void CloneFrom(this Ship.TurretCaliber dest, Ship.TurretCaliber src)
        {
            dest.diameter = src.diameter;
            dest.isCasemateGun = src.isCasemateGun;
            dest.length = src.length;
        }

        public static bool ExistsMount(Ship ship, PartData part, Il2CppSystem.Collections.Generic.List<string> demandMounts = null, Il2CppSystem.Collections.Generic.List<string> excludeMounts = null, bool allowUsed = true)
        {
            foreach (var m in ship.mounts)
                if ((allowUsed || m.employedPart == null) && m.Fits(part, demandMounts, excludeMounts))
                    return true;

            return false;
        }

        private static float ArmorFunc(Ship shipHint, float ratio, float max, bool isLight)
        {
            float minArmor = isLight ? 0.05f : 0.75f;
            float unrounded = UnityEngine.Random.Range(minArmor, 1.1f) * max * ratio * (1f / 11f);
            return G.settings.RoundToArmorStep(unrounded);
        }

        private static void SetArmor(Ship shipHint, Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> dict, Ship.A area, float value)
        {
            dict[area] = Mathf.Clamp(value, shipHint.MinArmorForZone(area), shipHint.MaxArmorForZone(area));
        }

        private static float GenerateCitadelArmorPreFunc(Ship.A citadelArmorsLayer, float year)
        {
            float from, to;
            switch (citadelArmorsLayer)
            {
                case Ship.A.InnerBelt_1st:
                    from = 3f;
                    to = 9f;
                    break;
                case Ship.A.InnerBelt_2nd:
                    from = 2f;
                    to = 7f;
                    break;
                case Ship.A.InnerBelt_3rd:
                case Ship.A.InnerDeck_1st:
                    from = 1.5f;
                    to = 5f;
                    break;
                case Ship.A.InnerDeck_2nd:
                    from = 1.25f;
                    to = 4f;
                    break;
                case Ship.A.InnerDeck_3rd:
                    from = 1.2f;
                    to = 3f;
                    break;
                default:
                    return 0f;
            }

            return Util.Remap(year, 1890f, 1940f, from, to, true);
        }

        public static Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> GenerateArmor(float armorMaximal, Ship shipHint)
        {
            float year;

            if (GameManager.IsMission && BattleManager.Instance.CurrentAcademyMission != null)
            {
                if (shipHint.player.isMain)
                    year = BattleManager.Instance.CurrentAcademyMission.year;
                else
                    year = BattleManager.Instance.CurrentAcademyMission.enemyYear;
            }
            else if (GameManager.IsCustomBattle)
            {
                if (shipHint.player.isMain)
                {
                    if (BattleManager.Instance.CurrentCustomBattle != null)
                        year = BattleManager.Instance.CurrentCustomBattle.player1.year;
                    else
                        year = CampaignController.Instance.StartYear;
                }
                else
                {
                    year = BattleManager.Instance.CurrentCustomBattle.player2.year;
                }
            }
            else
            {
                year = CampaignController.Instance.CurrentDate.AsDate().Year;
            }

            var dict = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            var citArmor = shipHint.GetCitadelArmor();
            bool isLight = shipHint.shipType.name == "dd" || shipHint.shipType.name == "tb";
            float beltMult = 1f;
            float turSideMult = 1f;
            float barbetteMult = 1f;

            switch (shipHint.shipType.name)
            {
                case "bb":
                    beltMult = 1.5f;
                    turSideMult = 0.065f;
                    barbetteMult = 0.135f;
                    break;
                case "bc":
                    beltMult = 1.385f;
                    turSideMult = 0.08f;
                    barbetteMult = 0.04f;
                    break;
                case "ca":
                    beltMult = 1.05f;
                    turSideMult = 2f;
                    barbetteMult = 1.8f;
                    break;
                case "cl":
                    beltMult = 0.38f;
                    turSideMult = 5f;
                    barbetteMult = 50f;
                    break;
                case "dd":
                    beltMult = 1.2f;
                    turSideMult = 40f;
                    barbetteMult = 75f;
                    break;
            }

            float beltYearMult = Util.Remap(year, 1900f, 1940f, 12f, beltMult * 14f, true);
            float beltVal = ArmorFunc(shipHint, beltYearMult, armorMaximal, isLight);
            SetArmor(shipHint, dict, Ship.A.Belt, beltVal);
            float foreaftBeltYearMult = Util.Remap(year, 1900f, 1940f, 3.5f, 7.5f, true);
            SetArmor(shipHint, dict, Ship.A.BeltBow, ArmorFunc(shipHint, foreaftBeltYearMult, armorMaximal, isLight));
            SetArmor(shipHint, dict, Ship.A.BeltStern, ArmorFunc(shipHint, foreaftBeltYearMult, armorMaximal, isLight));
            
            float deckYearMult = Util.Remap(year, 1900f, 1940f, 1.25f, 7.5f, true);
            SetArmor(shipHint, dict, Ship.A.Deck, ArmorFunc(shipHint, deckYearMult, armorMaximal, isLight));
            float foreaftDeck = Util.Remap(year, 1900f, 1940f, 1.5f, 5f, true);
            SetArmor(shipHint, dict, Ship.A.DeckBow, ArmorFunc(shipHint, foreaftDeck, armorMaximal, isLight));
            SetArmor(shipHint, dict, Ship.A.DeckStern, ArmorFunc(shipHint, foreaftDeck, armorMaximal, isLight));

            SetArmor(shipHint, dict, Ship.A.ConningTower, ArmorFunc(shipHint, Util.Remap(year, 1900f, 1940f, 14f, 16f, true), armorMaximal, isLight));

            float super = Util.Remap(year, 1900f, 1940f, 1f, UnityEngine.Random.Range(0.5f, 1.5f), true);
            turSideMult *= Util.Remap(year, 1900f, 1940f, 1f, 1.7f, true) * armorMaximal;
            if (GameManager.IsMission && BattleManager.Instance.CurrentAcademyMission.paramx.ContainsKey("crutch_ironclad")
                && (shipHint.hull.name.Contains("monitor") || shipHint.hull.name.Contains("virginia")))
            {
                super += 9f;
                turSideMult += 13f;
            }
            SetArmor(shipHint, dict, Ship.A.Superstructure, ArmorFunc(shipHint, super, armorMaximal, isLight));

            SetArmor(shipHint, dict, Ship.A.TurretSide, ArmorFunc(shipHint, turSideMult, armorMaximal, isLight));
            SetArmor(shipHint, dict, Ship.A.TurretTop, ArmorFunc(shipHint, Util.Remap(year, 1900f, 1940f, 3f, 12f, true), armorMaximal, isLight));
            SetArmor(shipHint, dict, Ship.A.Barbette, ArmorFunc(shipHint, barbetteMult * armorMaximal * Util.Remap(year, 1900f, 1940f, 3.75f, 5f, true), armorMaximal, isLight));

            var zonesUI = Ship.armorZonesUi.ToList();
            foreach (var z in zonesUI)
            {
                dict.TryGetValue(z, out var val);
                dict[z] = Mathf.Clamp(val, shipHint.MinArmorForZone(z), shipHint.MaxArmorForZone(z));
            }

            if (citArmor != null)
            {
                foreach (var z in citArmor)
                {
                    float level = ArmorFunc(shipHint, GenerateCitadelArmorPreFunc(z, year), armorMaximal, isLight);
                    if (shipHint.armor == null)
                        dict[z] = level;
                    else
                        SetArmor(shipHint, dict, z, level);
                }
            }

            return dict;
        }

        public static Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> GenerateArmorNew(float armorMaximal, Ship shipHint)
        {
            if (shipHint == null)
                return GenerateArmor(armorMaximal, shipHint);

            float year;
            if (GameManager.IsMission && BattleManager.Instance.CurrentAcademyMission != null)
            {
                if (shipHint.player.isMain)
                    year = BattleManager.Instance.CurrentAcademyMission.year;
                else
                    year = BattleManager.Instance.CurrentAcademyMission.enemyYear;
            }
            else if (GameManager.IsCustomBattle)
            {
                if (shipHint.player.isMain)
                {
                    if (BattleManager.Instance.CurrentCustomBattle != null)
                        year = BattleManager.Instance.CurrentCustomBattle.player1.year;
                    else
                        year = CampaignController.Instance.StartYear;
                }
                else
                {
                    year = BattleManager.Instance.CurrentCustomBattle.player2.year;
                }
            }
            else
            {
                year = CampaignController.Instance.CurrentDate.AsDate().Year;
            }

            var info = GenArmorData.GetInfoFor(shipHint, Patch_Ship._GenerateShipState >= 0 ? -1f : year);
            if (info == null)
                return GenerateArmor(armorMaximal, shipHint);

            // Divide out the scaling done by GenerateRandomShip, since we scale by year ourselves.
            if (Patch_Ship._GenerateShipState == 5)
                armorMaximal /= Util.Remap(shipHint.GetYear(shipHint), 1890f, 1940f, 1.0f, 0.85f, true);

            var dict = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            var citArmor = shipHint.GetCitadelArmor();

            float maxBelt = info.GetMaxArmorValue(shipHint, Ship.A.Belt, null);
            float portion = Mathf.Min(1f, armorMaximal / maxBelt); // estimate what lerp value to use
            for (Ship.A a = Ship.A.Belt; a < Ship.A.InnerBelt_1st; a += 1)
                dict[a] = info.GetArmorValue(shipHint, a, portion);

            var oldDict = shipHint.armor;
            shipHint.armor = dict;
            if (citArmor != null)
                foreach (var a in citArmor)
                    dict[a] = info.GetArmorValue(shipHint, a, portion);

            shipHint.armor = oldDict;
            return dict;
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

        public const float KnotsToMS = 0.514444444f;

        private static float _SpeedStep = -1f;
        private static float _SpeedStepR = -1f;
        public static float RoundSpeedToStep(float speed)
        {
            if (_SpeedStep < 0)
            {
                _SpeedStep = MonoBehaviourExt.Param("speed_step", 0.1f) * KnotsToMS;
                _SpeedStepR = 1f / _SpeedStep;
            }
            return Mathf.RoundToInt(speed * _SpeedStepR) * _SpeedStep;
        }

        private static VesselEntity.OpRange MinOpRange(Ship ship, VesselEntity.OpRange minOpRange = VesselEntity.OpRange.VeryLow)
        {
            if (ship.shipType.paramx.TryGetValue("range_min", out var minRange) && minRange.Count > 0)
            {
                if (_OpRangeValues == null)
                {
                    var vals = Enum.GetValues(typeof(VesselEntity.OpRange));
                    _OpRangeValues = new string[vals.Length];
                    for (int i = vals.Length; i-- > 0;)
                        _OpRangeValues[i] = Util.ToStringShort(vals.GetValue(i).ToString()).ToLower();
                }

                string minR = minRange[0].ToLower();
                for (int i = 0; i < _OpRangeValues.Length; ++i)
                {
                    if (_OpRangeValues[i] == minR)
                    {
                        minOpRange = (VesselEntity.OpRange)i;
                        break;
                    }
                }
            }

            return minOpRange;
        }

        public static void AdjustHullStats(Ship _this, int delta, float targetWeightRatio, Func<bool> stopCondition, bool allowEditSpeed = true, bool allowEditArmor = true, bool allowEditCrewQuarters = true, bool allowEditRange = true, Il2CppSystem.Random nativeRnd = null, float limitArmor = -1f, float limitSpeed = -1f)
        {
            float year = _this.GetYear(_this);
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> oldArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            bool isCL = _this.shipType.name == "cl";
            bool isLightCraft = _this.shipType.name == "tb" || _this.shipType.name == "dd";
            bool cldd = isCL || isLightCraft;

            var minOpRange = MinOpRange(_this, VesselEntity.OpRange.Low);

            var gaInfo = GenArmorData.GetInfoFor(_this);

            var armorMin = _this.shipType.armorMin;
            if (armorMin <= 0)
                armorMin = _this.shipType.armor;
            float armorInches;
            // All ships are expected to have a hint
            if (_this.shipType.paramx.TryGetValue("armor_min_hint", out var minArmorParam) && minArmorParam.Count > 0 && float.TryParse(minArmorParam[0], out var armorParam))
            {
                armorInches = _this.shipType.armor * armorParam;
            }
            else
            {
                armorInches = armorMin * Util.Range(1f, 1.2f, nativeRnd);
            }
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> armorMinHint;
            if (gaInfo == null)
            {
                armorMinHint = GenerateArmorNew(armorInches * 25.4f, _this);
            }
            else
            {
                armorMinHint = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                for (Ship.A a = Ship.A.Belt; a < Ship.A.InnerBelt_1st; a += 1)
                    armorMinHint[a] = gaInfo.MinArmorValue(a);
                var citA = _this.GetCitadelArmor();
                if (citA != null)
                    foreach(var a in citA)
                        armorMinHint[a] = gaInfo.MinArmorValue(a);
            }

            float hullSpeedMS = _this.hull.data.speedLimiter * KnotsToMS;

            float oldSpeed = _this.speedMax;
            Ship.CrewQuarters oldQuarters = _this.CurrentCrewQuarters;
            var oldRange = _this.opRange < minOpRange ? minOpRange : _this.opRange;
            if (_this.armor == null)
            {
                _this.armor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                _this.SetArmor(armorMinHint);
            }

            foreach (var kvp in _this.armor)
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
                minSpeed = hullSpeedMS * Util.Remap(year, 1890f, 1940f, 1.0f, 0.85f, true) * mult;
            }
            float maxMult = cldd ? 1.05f : 1.1f;
            float maxSpeed = hullSpeedMS * maxMult * Util.Remap(year, 1890f, 1940f, 1.05f, 1.0f, true);

            minSpeed = Mathf.Max(minSpeed, _this.shipType.speedMin * KnotsToMS);
            maxSpeed = Mathf.Min(maxSpeed, _this.shipType.speedMax * KnotsToMS);

            float armorLimit = limitArmor > 0f ? limitArmor : _this.shipType.armorMax * 3f * 25.4f;
            bool canMakeTarget;
            if (delta < 0)
            {
                _this.SetSpeedMax(minSpeed);
                _this.CurrentCrewQuarters = Ship.CrewQuarters.Cramped;
                _this.SetOpRange(minOpRange);

                foreach (var key in _this.armor.Keys)
                    newArmor[key] = Mathf.Max(armorMinHint[key], _this.MinArmorForZone(key));

                _this.SetArmor(newArmor);

                canMakeTarget = _this.Weight() / _this.Tonnage() <= targetWeightRatio;
                if (!canMakeTarget)
                {
                    foreach (var kvp in _this.armor)
                        newArmor[kvp.Key] = _this.MinArmorForZone(kvp.Key);

                    _this.SetArmor(newArmor);
                    canMakeTarget = _this.Weight() / _this.Tonnage() <= targetWeightRatio;
                }
            }
            else
            {
                _this.SetSpeedMax(maxSpeed);
                _this.CurrentCrewQuarters = Ship.CrewQuarters.Spacious;
                _this.SetOpRange(VesselEntity.OpRange.VeryHigh);

                for (Ship.A area = Ship.A.Belt; area <= Ship.A.Barbette; area += 1)
                    newArmor[area] = Mathf.Min(armorLimit, _this.MaxArmorForZone(area, null));

                _this.SetArmor(newArmor); // have to set before doing citadel armor
                // because citadel armor depends on base belt/deck armor.
                var citA = _this.GetCitadelArmor();
                if (citA != null)
                {
                    // We also have to set each one directly,
                    // since the next area depends on the previous.
                    foreach (var key in citA)
                        _this.SetArmor(key, Mathf.Min(armorLimit, _this.MaxArmorForZone(key, null)));

                    _this.RefreshHullStats();
                }

                canMakeTarget = targetWeightRatio * 0.997f <= _this.Weight() / _this.Tonnage();
            }

            if (!canMakeTarget)
                return;

            oldSpeed = RoundSpeedToStep(oldSpeed);

            // Reset
            _this.SetSpeedMax(oldSpeed);
            _this.CurrentCrewQuarters = oldQuarters;
            _this.SetOpRange(oldRange);
            _this.SetArmor(oldArmor);

            Dictionary<Ship.A, float> armorPriority = delta > 0 ? _AdjustPriorityArmorIncrease : _AdjustPriorityArmorReduce;
            float speedStep = MonoBehaviourExt.Param("speed_step", 0.5f) * delta * KnotsToMS;

            // We might already meet the requirements.
            if (stopCondition != null && stopCondition())
                return;

            float curArmorLerp = 0.5f;
            bool useInfo = false;
            if (gaInfo != null)
            {
                useInfo = true;

                float sum = 0f;
                float sumTotal = 0f;
                foreach (var kvp in _this.armor)
                {
                    sum += Mathf.InverseLerp(gaInfo.MinArmorValue(kvp.Key), gaInfo.MaxArmorValue(kvp.Key), kvp.Value);
                    ++sumTotal;
                }
                curArmorLerp = sumTotal > 0 ? sum / sumTotal : 0f;
            }
            for (int j = 0; j < 699; ++j)
            {
                // Recreate, since we might have touched citadel armor
                newArmor.Clear();
                oldArmor.Clear();
                foreach (var kvp in _this.armor)
                {
                    newArmor[kvp.Key] = kvp.Value;
                    oldArmor[kvp.Key] = kvp.Value;
                }

                oldSpeed = _this.speedMax;
                oldQuarters = _this.CurrentCrewQuarters;
                oldRange = _this.opRange;

                float newSpeed = speedStep + _this.speedMax;
                newSpeed = Mathf.Clamp(newSpeed, minSpeed, maxSpeed);
                newSpeed = RoundSpeedToStep(newSpeed);
                Ship.CrewQuarters newQuarters = (Ship.CrewQuarters)Mathf.Clamp((int)_this.CurrentCrewQuarters + delta, (int)Ship.CrewQuarters.Cramped, (int)Ship.CrewQuarters.Spacious);
                VesselEntity.OpRange newOpRange = (VesselEntity.OpRange)Mathf.Clamp((int)_this.opRange + delta, (int)minOpRange, (int)VesselEntity.OpRange.VeryHigh);

                // We don't have to do all this work if we're not allowed to change armor anyway
                bool armorFound = false;
                if (allowEditArmor)
                {
                    if (useInfo)
                    {
                        float inc = delta * gaInfo.lerpStep;
                        while (delta > 0 ? curArmorLerp < 1f : curArmorLerp > 0f)
                        {
                            curArmorLerp = Mathf.Clamp(curArmorLerp + inc, 0f, 1f);
                            foreach (var a in armorMinHint.Keys)
                                newArmor[a] = gaInfo.GetArmorValue(_this, a, curArmorLerp);
                            if (!ModUtils.DictsEqual(newArmor, oldArmor))
                            {
                                armorFound = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // We need to find a valid armor zone. Note
                        // due to citadel armor weirdness, we have
                        // to do this fresh each time.
                        foreach (var kvp in armorPriority)
                        {
                            float maxZone = _this.MaxArmorForZone(kvp.Key, null);
                            if (maxZone > 0f)
                            {
                                armorMinHint.TryGetValue(kvp.Key, out var minHint);
                                float minArmor = Mathf.Max(minHint, _this.MinArmorForZone(kvp.Key));
                                float maxArmor = Mathf.Min(armorLimit, maxZone);
                                _this.armor.TryGetValue(kvp.Key, out float oldLevel);
                                if ((delta > 0) ? (oldLevel < maxArmor) : (oldLevel > minArmor))
                                    _AdjustPriorityArmorLocal.Add(kvp.Key, kvp.Value);
                            }
                        }
                        if (_AdjustPriorityArmorLocal.Count > 0)
                        {
                            var randomA = ModUtils.RandomByWeights(_AdjustPriorityArmorLocal);
                            armorMinHint.TryGetValue(randomA, out var minHint);
                            float minArmor = Mathf.Max(minHint, _this.MinArmorForZone(randomA));
                            float maxArmor = Mathf.Min(armorLimit, _this.MaxArmorForZone(randomA, null));
                            _this.armor.TryGetValue(randomA, out float oldLevel);
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

                // if we can't make any more changes, we're done regardless.
                if (_AdjustHullStatsOptions.Count == 0)
                    return;

                // We're going to replace entries we pull and use with "empty"
                // so that we don't screw with our weights. (If we just removed items,
                // like stock, then quarters and range would be overrepresented.)
                AdjustHullStatsItem thingToChange = AdjustHullStatsItem.Empty;
                while ((thingToChange = ModUtils.RandomByWeights(_AdjustHullStatsOptions, null, nativeRnd)) != AdjustHullStatsItem.Empty)
                {
                    float weight = _AdjustHullStatsOptions[thingToChange];
                    _AdjustHullStatsOptions.Remove(thingToChange);
                    _AdjustHullStatsOptions.ChangeValueFor(AdjustHullStatsItem.Empty, weight);

                    switch (thingToChange)
                    {
                        case AdjustHullStatsItem.Speed:
                            _this.SetSpeedMax(newSpeed);
                            break;
                        case AdjustHullStatsItem.Quarters:
                            _this.CurrentCrewQuarters = newQuarters;
                            break;
                        case AdjustHullStatsItem.Range:
                            _this.SetOpRange(newOpRange);
                            break;
                        default:
                            _this.SetArmor(newArmor);
                            break;
                    }

                    // We have to separately check if we're OVER max weight
                    // because the stopCondition might have a ratio of 1.0,
                    // so we would never meet it unless we were overweight
                    // (or so precisely on-target as to be impossible
                    // with floats).
                    if (_this.Weight() > _this.Tonnage() || (GameManager.IsMission && _this.player.isMain && BattleManager.Instance != null && BattleManager.Instance.MissionMainShip == _this && _this.Cost() > _this.player.cash))
                    {
                        switch (thingToChange)
                        {
                            case AdjustHullStatsItem.Speed:
                                _this.SetSpeedMax(oldSpeed);
                                break;
                            case AdjustHullStatsItem.Quarters:
                                _this.CurrentCrewQuarters = oldQuarters;
                                break;
                            case AdjustHullStatsItem.Range:
                                _this.SetOpRange(oldRange);
                                break;
                            default:
                                _this.SetArmor(oldArmor);
                                break;
                        }
                        return;
                    }

                    // Now check vs requirements
                    if (stopCondition != null && stopCondition())
                        return;
                }
            }
        }

        public static void AddedAdditionalTonnageUsage(Ship _this)
        {
            G.GameData.shipTypes.TryGetValue("amc", out var amcST);
            if (_this.shipType == amcST)
                return;

            float tonnage = _this.Tonnage();
            float weight = _this.Weight();

            float weightToStopArmor = tonnage * 0.9999f;
            // The game also checks if the ship is without armor.
            // But that's a bug, because there's other things to increase.
            if (weight >= weightToStopArmor)
                return;

            _this.tempGoodWeight = weightToStopArmor;
            float weightToStopRange = (1f - _this.weightPercentForOpRange) * tonnage;
            float weightToStopSurv = (1f - _this.weightPercentForSurvivability) * tonnage;

            bool underweight = weight < _this.tempGoodWeight;
            if (!_this.IsShipWhitoutArmor())
            {
                var gaInfo = GenArmorData.GetInfoFor(_this);
                if (gaInfo != null)
                {
                    float curArmorLerp;
                    float sum = 0f;
                    float sumTotal = 0f;
                    foreach (var kvp in _this.armor)
                    {
                        sum += Mathf.InverseLerp(gaInfo.MinArmorValue(kvp.Key), gaInfo.MaxArmorValue(kvp.Key), kvp.Value);
                        ++sumTotal;
                    }
                    curArmorLerp = sumTotal > 0 ? sum / sumTotal : 0f;

                    _this.limiter = 699;
                    Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                    // we could probably just copy a ref to _this.armor before calling SetArmor since that sets it to a new dict
                    // but I don't quite trust interop GC to not screw that up.
                    Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> oldArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                    while (underweight && _this.limiter-- > 0)
                    {
                        bool stopLoop = true;
                        while (curArmorLerp < 1f)
                        {
                            curArmorLerp = Mathf.Clamp(curArmorLerp + gaInfo.lerpStep, 0f, 1f);
                            foreach (var a in _this.armor.Keys)
                            {
                                oldArmor[a] = _this.armor[a];
                                newArmor[a] = gaInfo.GetArmorValue(_this, a, curArmorLerp);
                            }
                            if (!ModUtils.DictsEqual(newArmor, _this.armor))
                            {
                                stopLoop = false;
                                break;
                            }
                        }

                        if (stopLoop)
                            break;

                        _this.SetArmor(newArmor);
                        weight = _this.Weight();
                        if (weight > _this.tempGoodWeight)
                        {
                            _this.SetArmor(oldArmor);
                            weight = _this.Weight();
                            underweight = false;
                            break;
                        }
                    }
                }
                else
                {
                    Il2CppSystem.Collections.Generic.List<int> armorQueue;
                    switch (_this.shipType.name)
                    {
                        case "bb":
                            armorQueue = _this.armorAddedQueueBB;
                            break;
                        case "bc":
                            armorQueue = _this.armorAddedQueueBC;
                            break;
                        case "ca":
                            armorQueue = _this.armorAddedQueueCA;
                            break;
                        case "cl":
                            armorQueue = _this.armorAddedQueueCL;
                            break;
                        case "dd":
                            armorQueue = _this.armorAddedQueueDD;
                            break;
                        default:
                            armorQueue = new Il2CppSystem.Collections.Generic.List<int>();
                            armorQueue.Add((int)Ship.A.TurretSide);
                            armorQueue.Add((int)Ship.A.Barbette);
                            armorQueue.Add((int)Ship.A.Belt);
                            armorQueue.Add((int)Ship.A.Deck);
                            armorQueue.Add((int)Ship.A.BeltBow);
                            armorQueue.Add((int)Ship.A.DeckBow);
                            armorQueue.Add((int)Ship.A.TurretTop);
                            armorQueue.Add((int)Ship.A.BeltStern);
                            armorQueue.Add((int)Ship.A.DeckStern);
                            armorQueue.Add((int)Ship.A.ConningTower);
                            armorQueue.Add((int)Ship.A.InnerBelt_1st);
                            armorQueue.Add((int)Ship.A.InnerDeck_1st);
                            armorQueue.Add((int)Ship.A.InnerBelt_2nd);
                            armorQueue.Add((int)Ship.A.InnerDeck_2nd);
                            armorQueue.Add((int)Ship.A.InnerBelt_3rd);
                            armorQueue.Add((int)Ship.A.InnerDeck_3rd);
                            armorQueue.Add((int)Ship.A.Superstructure);
                            break;
                    }

                    _this.limiter = 699;
                    bool changedArmor = true;
                    while (underweight && _this.limiter-- > 0 && changedArmor)
                    {
                        changedArmor = false;
                        foreach (var i in armorQueue)
                        {
                            Ship.A a = (Ship.A)i;
                            float max = _this.MaxArmorForZone(a);
                            if (!_this.armor.TryGetValue(a, out var curAmt) || curAmt >= max)
                                continue;

                            // This function is bugged. It's supposed to return
                            // a value based on how near max tonnage the ship is,
                            // but instead just returns 2 for armor and 1 for
                            // everything else
                            float stepMult = 2f; // _this.GetAdditionalWeightStepMult(true);
                            float armorStep = G.settings.armorStep;
                            float newAmt = G.settings.RoundToArmorStep(armorStep * stepMult + curAmt);
                            newAmt = Mathf.Clamp(newAmt, _this.MinArmorForZone(a), max);
                            if (newAmt == curAmt)
                                continue;

                            changedArmor = true;
                            _this.SetArmor(a, newAmt, true);
                            weight = _this.Weight();
                            if (weight > _this.tempGoodWeight)
                            {
                                _this.SetArmor(a, curAmt, true);
                                weight = _this.Weight();
                                underweight = false;
                                break;
                            }
                        }
                    }
                }
            }

            _this.limiter = 699;
            float step = MonoBehaviourExt.Param("speed_step", 0.1f);
            float maxMS = _this.shipType.speedMax * KnotsToMS;
            float minMS = _this.shipType.speedMin * KnotsToMS;
            while (underweight && _this.limiter-- > 0)
            {
                float oldSpeed = _this.speedMax;
                float newSpeed = RoundSpeedToStep(step + oldSpeed);
                _this.SetSpeedMax(newSpeed);
                weight = _this.Weight();
                if (weight > _this.tempGoodWeight)
                {
                    _this.SetSpeedMax(oldSpeed);
                    weight = _this.Weight();
                    underweight = false;
                    break;
                }
            }

            if (!_this.isOpRangeDecreased && weight < weightToStopRange)
            {
                while (_this.opRange < VesselEntity.OpRange.VeryHigh)
                {
                    var oldRange = _this.opRange;
                    var newRange = (VesselEntity.OpRange)((int)oldRange + 1);
                    _this.opRange = newRange;
                    _this.GetOpRangeInKmCalculate();
                    weight = _this.Weight();
                    if (weight > weightToStopRange)
                    {
                        _this.opRange = oldRange;
                        _this.GetOpRangeInKmCalculate();
                        weight = _this.Weight();
                        break;
                    }
                }
            }

            if (!_this.isSurvivabilityDecreased && weight < weightToStopSurv)
            {
                while (_this.survivability < Ship.Survivability.VeryHigh)
                {
                    var oldSurv = _this.survivability;
                    var newSurv = (Ship.Survivability)((int)oldSurv + 1);
                    _this.SetSurvivability(newSurv);
                    weight = _this.Weight();
                    if (weight > weightToStopSurv)
                    {
                        _this.SetSurvivability(oldSurv);
                        weight = _this.Weight();
                        break;
                    }
                }
            }
        }

        public static void ReduceWeightByReducingCharacteristics(Ship _this, Il2CppSystem.Random rnd, float tryN, float triesTotal, float randArmorRatio = 0, float speedLimit = 0)
        {
            G.GameData.shipTypes.TryGetValue("tr", out var trST);
            if (_this.shipType == trST)
                return;

            float tonnage = _this.Tonnage();
            float weight = _this.Weight();
            if (weight <= tonnage)
                return;

            float tryRatio = tryN * 100f / triesTotal;
            float weightToStopArmor = tonnage * 0.9999f;

            bool overweight = weight > _this.tempGoodWeight;
            if (!_this.IsShipWhitoutArmor())
            {
                var gaInfo = GenArmorData.GetInfoFor(_this);
                if (gaInfo != null)
                {
                    float curArmorLerp;
                    float sum = 0f;
                    float sumTotal = 0f;
                    foreach (var kvp in _this.armor)
                    {
                        sum += Mathf.InverseLerp(gaInfo.MinArmorValue(kvp.Key), gaInfo.MaxArmorValue(kvp.Key), kvp.Value);
                        ++sumTotal;
                    }
                    curArmorLerp = sumTotal > 0 ? sum / sumTotal : 0f;

                    _this.limiter = 699;
                    Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                    while (overweight && _this.limiter-- > 0)
                    {
                        bool stopLoop = true;
                        while (curArmorLerp > 0f)
                        {
                            curArmorLerp = Mathf.Clamp(curArmorLerp - gaInfo.lerpStep, 0f, 1f);
                            foreach (var a in _this.armor.Keys)
                                newArmor[a] = gaInfo.GetArmorValue(_this, a, curArmorLerp);
                            if (!ModUtils.DictsEqual(newArmor, _this.armor))
                            {
                                stopLoop = false;
                                break;
                            }
                        }

                        if (stopLoop)
                            break;

                        _this.SetArmor(newArmor);
                        weight = _this.Weight();
                        if (weight < _this.tempGoodWeight)
                        {
                            // we'll take the reduction - _this.SetArmor(a, curAmt, true);
                            //weight = _this.Weight();
                            overweight = false;
                            break;
                        }
                    }
                }
                else
                {
                    Il2CppSystem.Collections.Generic.List<int> armorQueue;
                    switch (_this.shipType.name)
                    {
                        case "bb":
                            armorQueue = _this.armorReduceQueueBB;
                            break;
                        case "bc":
                            armorQueue = _this.armorReduceQueueBC;
                            break;
                        case "ca":
                            armorQueue = _this.armorReduceQueueCA;
                            break;
                        case "cl":
                            armorQueue = _this.armorReduceQueueCL;
                            break;
                        case "dd":
                            armorQueue = _this.armorReduceQueueDD;
                            break;
                        default:
                            armorQueue = new Il2CppSystem.Collections.Generic.List<int>();
                            armorQueue.Add((int)Ship.A.BeltBow);
                            armorQueue.Add((int)Ship.A.BeltStern);
                            armorQueue.Add((int)Ship.A.DeckBow);
                            armorQueue.Add((int)Ship.A.ConningTower);
                            armorQueue.Add((int)Ship.A.DeckStern);
                            armorQueue.Add((int)Ship.A.Deck);
                            armorQueue.Add((int)Ship.A.Belt); // Typo: the game uses BeltBow here
                            armorQueue.Add((int)Ship.A.InnerBelt_1st);
                            armorQueue.Add((int)Ship.A.InnerBelt_2nd);
                            armorQueue.Add((int)Ship.A.InnerBelt_3rd);
                            armorQueue.Add((int)Ship.A.InnerDeck_1st);
                            armorQueue.Add((int)Ship.A.InnerDeck_2nd);
                            armorQueue.Add((int)Ship.A.InnerDeck_3rd);
                            armorQueue.Add((int)Ship.A.TurretSide);
                            armorQueue.Add((int)Ship.A.TurretTop);
                            armorQueue.Add((int)Ship.A.Barbette);
                            armorQueue.Add((int)Ship.A.Superstructure);
                            break;
                    }

                    _this.limiter = 699;
                    bool changedArmor = true;
                    while (overweight && _this.limiter-- > 0 && changedArmor)
                    {
                        changedArmor = false;
                        foreach (var i in armorQueue)
                        {
                            Ship.A a = (Ship.A)i;
                            float min = _this.MinArmorForZone(a);
                            if (!_this.armor.TryGetValue(a, out var curAmt) || curAmt == min)
                                continue;

                            // This function is bugged. It's supposed to return
                            // a value based on how near max tonnage the ship is,
                            // but instead just returns 28 for armor and 7 for
                            // everything else. But we're going to use 2, like the
                            // add case.
                            float stepMult = 2f;
                            float armorStep = G.settings.armorStep;
                            float newAmt = G.settings.RoundToArmorStep(curAmt - armorStep * stepMult);
                            newAmt = Mathf.Clamp(newAmt, min, _this.MaxArmorForZone(a));
                            if (newAmt == curAmt)
                                continue;

                            changedArmor = true;
                            _this.SetArmor(a, newAmt, true);
                            weight = _this.Weight();
                            if (weight < _this.tempGoodWeight)
                            {
                                // we'll take the reduction - _this.SetArmor(a, curAmt, true);
                                //weight = _this.Weight();
                                overweight = false;
                                break;
                            }
                        }
                    }
                }
            }

            if (overweight)
            {
                float lowerClamp = speedLimit;
                float remap, range;
                float speedLimiter = _this.hull.data.speedLimiter;
                float year = _this.GetYear(_this);
                if (_this.shipType.name == "dd")
                {
                    range = Util.Range(0.925f, 1f, rnd);
                    remap = Util.Remap(year, 1890f, 1940f, 0.7f, range, true);
                    if (speedLimit <= 0f)
                        lowerClamp = speedLimiter * 1.15f;
                }
                else
                {
                    switch (_this.hull.data.Generation)
                    {
                        case 1:
                            range = Util.Range(0.8f, 0.89f, rnd);
                            remap = Util.Remap(year, 1890f, 1940f, 0.85f, range, true);
                            if (speedLimit <= 0f)
                                lowerClamp = speedLimiter * 0.9f;
                            break;
                        case 2:
                            range = Util.Range(0.85f, 0.95f, rnd);
                            remap = Util.Remap(year, 1890f, 1940f, 0.75f, range, true);
                            if (speedLimit <= 0f)
                                lowerClamp = speedLimiter * 0.96f;
                            break;
                        case 3:
                            range = Util.Range(0.81f, 0.92f, rnd);
                            remap = Util.Remap(year, 1890f, 1940f, 0.75f, range, true);
                            if (speedLimit <= 0f)
                                lowerClamp = speedLimiter;
                            break;
                        default:
                        case 4:
                            range = Util.Range(0.8f, 0.92f, rnd);
                            remap = Util.Remap(year, 1890f, 1940f, 0.7f, range, true);
                            if (speedLimit <= 0f)
                                lowerClamp = speedLimiter;
                            break;
                    }
                }

                // The code is bugged here, it uses lowerClamp as the lower end of the clamp, so the range/remap
                // are never used!
                //float clampedSpeedMS = Mathf.Clamp(remap, lowerClamp, _this.shipType.speedMax) * KnotsToMS;

                float maxMS = Mathf.Min(lowerClamp, _this.shipType.speedMax) * KnotsToMS;
                float minMS = Mathf.Max(remap, _this.shipType.speedMin) * KnotsToMS;

                if (maxMS > minMS && _this.speedMax > minMS)
                {
                    float step = MonoBehaviourExt.Param("speed_step", 0.1f);

                    _this.limiter = 699;
                    while (overweight && _this.limiter-- > 0)
                    {
                        float oldSpeed = _this.speedMax;
                        float newSpeed = RoundSpeedToStep(oldSpeed - step);
                        if (newSpeed < minMS)
                            break;

                        _this.SetSpeedMax(newSpeed);
                        weight = _this.Weight();
                        if (weight < _this.tempGoodWeight)
                        {
                            overweight = false;
                            break;
                        }
                    }
                }
            }

            var minOpRange = MinOpRange(_this, VesselEntity.OpRange.Low);
            if (overweight && _this.opRange > minOpRange)
            {
                if (tryRatio > 75)
                    _this.limiter = 4;
                else if (tryRatio > 50)
                    _this.limiter = 3;
                else if (tryRatio > 25)
                    _this.limiter = 2;
                else
                    _this.limiter = 1;

                while (_this.opRange > minOpRange && _this.limiter-- > 0)
                {
                    _this.isOpRangeDecreased = true;
                    _this.opRange = (VesselEntity.OpRange)((int)_this.opRange - 1);
                    _this.GetOpRangeInKmCalculate();
                    weight = _this.Weight();
                    if (weight < _this.tempGoodWeight)
                    {
                        overweight = false;
                        break;
                    }
                }
            }

            if (overweight)
            {
                Ship.Survivability minSurv = _this.shipType.name == "dd" || _this.shipType.name == "tb" ? Ship.Survivability.Low : Ship.Survivability.High;
                while (_this.survivability > minSurv)
                {
                    _this.isSurvivabilityDecreased = true;
                    _this.SetSurvivability((Ship.Survivability)((int)_this.survivability - 1));
                    weight = _this.Weight();
                    if (weight < _this.tempGoodWeight)
                        break;
                }
            }
        }

        public static float GetCitadelMultFromBase(Ship.A a)
        {
            float mult = 1f;
            switch (a)
            {
                case Ship.A.InnerBelt_3rd: mult *= 0.8f; goto case Ship.A.InnerBelt_2nd;
                case Ship.A.InnerBelt_2nd: mult *= 0.8f; goto case Ship.A.InnerBelt_1st;
                case Ship.A.InnerBelt_1st: return mult * 0.5f;

                case Ship.A.InnerDeck_3rd: mult *= 0.8f; goto case Ship.A.InnerDeck_2nd;
                case Ship.A.InnerDeck_2nd: mult *= 0.8f; goto case Ship.A.InnerDeck_1st;
                case Ship.A.InnerDeck_1st: return mult * 0.6f;

                default: return 0f;
            }
        }

        public static float GetCitadelArmorMax(Ship.A a, float belt, float deck)
        {
            if (a < Ship.A.InnerBelt_1st)
                return 0f;

            float val = a > Ship.A.InnerBelt_3rd ? deck : belt;
            return val * GetCitadelMultFromBase(a);
        }
    }
}