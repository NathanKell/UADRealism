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

#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace UADRealism
{
    public struct GunDataM
    {
        private float _minLengthParam;
        private float _maxLengthParam;
        private float _length;
        private float _caliber;
        public float caliber => _caliber;
        private float _baseCalInch;
        public float baseCalInch => _baseCalInch;
        private float _calInch;
        public float calInch => _calInch;
        private float _convertedDiam;
        private float _caliberLerp;
        private PartData _partData;
        private GunData _gunData;
        public GunData gunData => _gunData;
        private GunData _otherGunData;
        private Ship _ship;
        private Ship.TurretCaliber _tc;
        public Ship.TurretCaliber tc => _tc;
        private bool _isCasemate;
        public bool isCasemate => _isCasemate;


        public GunDataM(PartData data, Ship ship, bool checkLength) : this(G.GameData.GunData(data), data, ship, ship == null ? null : ShipM.FindMatchingTurretCaliber(ship, data), Ship.IsCasemateGun(data), checkLength) { }

        public GunDataM(GunData gunData, PartData data, Ship ship, bool checkLength) : this(gunData, data, ship, ship == null ? null : ShipM.FindMatchingTurretCaliber(ship, data), Ship.IsCasemateGun(data), checkLength) { }

        public GunDataM(PartData data, Ship ship, Ship.TurretCaliber tc, bool checkLength) : this(G.GameData.GunData(data), data, ship, tc, Ship.IsCasemateGun(data), checkLength) { }

        public GunDataM(GunData gunData, PartData data, Ship ship, Ship.TurretCaliber tc, bool isCasemate, bool checkLength)
        {
            _gunData = gunData;
            _otherGunData = null;
            _partData = data;
            _ship = ship;
            _isCasemate = isCasemate;

            _caliber = data.caliber;
            _baseCalInch = data.caliber;
            _baseCalInch *= 1f / 25.4f;
            _convertedDiam = -1f;
            _caliberLerp = -1f;
            _tc = tc;
            if (tc == null)
            {
                _length = 0f;
            }
            else
            {
                _length = tc.length;
                _caliber += tc.diameter;
            }
            _calInch = _caliber * (1f / 25.4f);
            if (!checkLength)
            {
                _minLengthParam = _maxLengthParam = 0f;
                return;
            }

            float noTechParam;
            // Stock is weird here, it uses casemate params in wrong place.
            if (isCasemate)
            {
                _minLengthParam = MonoBehaviourExt.Param("min_casemate_length_mod", -10f);
                noTechParam = MonoBehaviourExt.Param("max_casemate_length_mod", 10f);
                if (ship == null)
                    _maxLengthParam = noTechParam;
                else
                    _maxLengthParam = ship.TechMax("tech_gun_length_limit_casemates", noTechParam);
            }
            else
            {
                _minLengthParam = MonoBehaviourExt.Param("min_gun_length_mod", -20f);
                noTechParam = MonoBehaviourExt.Param("max_gun_length_mod", 20f);
                if (ship == null)
                    _maxLengthParam = noTechParam;
                else if (_calInch > 2f)
                    _maxLengthParam = ship.TechMax("tech_gun_length_limit", noTechParam);
                else
                    _maxLengthParam = ship.TechMax("tech_gun_length_limit_small", noTechParam);
            }
        }

        /// <summary>
        /// Remaps length (given between min and max length params) to between minVal and maxVal
        /// with centerVal as what's returned when length is 0.
        /// </summary>
        /// <param name="minVal"></param>
        /// <param name="maxVal"></param>
        /// <param name="centerVal"></param>
        /// <returns></returns>
        public float RemapLength(float minVal, float maxVal, float centerVal)
        {
            // Fix a stock bug: in stock, since the length limit changes over time, the "zero point"
            // changes over time as well, meaning where the resulting value of the lerp is between
            // the minval and maxval, for a given length delta, changes over time.
            if (_length == 0f)
                return centerVal;
            else if (minVal == maxVal)
                return minVal;
            else if (_length > 0f)
                return Util.Remap(_length, 0f, _maxLengthParam, centerVal, maxVal);
            else
                return Util.Remap(_length, _minLengthParam, 0f, minVal, centerVal);
        }


        private const float DiameterPower = 1.5f;

        private float ConvertedDiameter
        {
            get
            {
                if (_convertedDiam > 0f)
                    return _convertedDiam;

                bool isNegative;
                float extraDiamInch;
                float baseCaliberInch = _baseCalInch;
                float extraDiam = _tc.diameter;
                if (extraDiam < 0f)
                {
                    isNegative = true;
                    extraDiamInch = (float)Math.Round(1f + extraDiam * (1f / 25.4f), 1);
                    if (baseCaliberInch > 1f)
                        baseCaliberInch -= 1f;
                }
                else
                {
                    isNegative = false;
                    extraDiamInch = (float)Math.Round(extraDiam * (1f / 25.4f), 1);
                }

                float basePowered = Mathf.Pow(baseCaliberInch, DiameterPower);
                float exactPowered = Mathf.Pow(baseCaliberInch + extraDiamInch, DiameterPower);
                float nextPowered = Mathf.Pow(baseCaliberInch + 1f, DiameterPower);
                float newExtraDiam = (exactPowered - basePowered) / (nextPowered - basePowered);

                if (isNegative)
                    newExtraDiam = newExtraDiam - 1f;

                _convertedDiam = newExtraDiam * 25.4f;
                return _convertedDiam;
            }
        }

        private float CaliberLerp
        {
            get
            {
                if (_caliberLerp < 0f)
                {
                    // Note that this may be a negative t-value for the lerp, because nextCalData
                    // could be the next _lower_ caliber if the TurretCaliber's diameter offset is negative.
                    _caliberLerp = (float)Math.Round(ConvertedDiameter * (1f / 25.4f), 1);
                    if (_caliberLerp < 0f)
                        _caliberLerp = 1f - _caliberLerp;
                }

                return _caliberLerp;
            }
        }

        private GunData GetOtherCaliberData()
        {
            var gunsData = G.GameData.guns;
            int calInch = (int)((0.001f + _partData.caliber) * (1f / 25.4f));
            int calOffset = _tc.diameter < 0 ? -1 : 1;
            string nextCalStr;

            var dataID = _partData.GetGunDataId(null);
            if (string.IsNullOrEmpty(dataID))
                return null;

            if (dataID.Contains("ironclad"))
            {
                var splits = dataID.Split('_');
                int parsedCalNext = int.Parse(splits[splits.Length - 1]) + calOffset;
                string ironcladNextCal = parsedCalNext.ToString();
                splits[splits.Length - 1] = ironcladNextCal;
                nextCalStr = string.Join("_", splits);
            }
            else
            {
                nextCalStr = (calInch + calOffset).ToString();
            }

            gunsData.TryGetValue(nextCalStr, out var nextCalData);
            return nextCalData;
        }

        private static float[] _TurretBarrelCountWeightMults = null;

        public static float GetTurretBarrelWeightMult(PartData data)
        {
            if (_TurretBarrelCountWeightMults == null)
            {
                _TurretBarrelCountWeightMults = new float[5];
                _TurretBarrelCountWeightMults[0] = 1f;
                _TurretBarrelCountWeightMults[1] = MonoBehaviourExt.Param("w_turret_barrels_2", 1.8f);
                _TurretBarrelCountWeightMults[2] = MonoBehaviourExt.Param("w_turret_barrels_3", 2.48f);
                _TurretBarrelCountWeightMults[3] = MonoBehaviourExt.Param("w_turret_barrels_4", 3.1f);
                _TurretBarrelCountWeightMults[4] = MonoBehaviourExt.Param("w_turret_barrels_5", 3.6f);
            }
            return _TurretBarrelCountWeightMults[Mathf.Clamp(data.barrels - 1, 0, 4)];
        }

        public bool GetValueNeedsDefault()
        {
            if (_tc == null)
            {
                // Not gonna bother to log this, because the TC _will_ be null
                // in the case where you have a tooltip open for a part in the part
                // selection area in the Constructor, it will only be non-null
                // for placed parts.
                //Melon<UADRealismMod>.Logger.Msg("TC null!");
                return true;
            }

            if (_otherGunData == null)
                _otherGunData = GetOtherCaliberData();

            if (_otherGunData == null)
            {
                return true;
            }

            return false;
        }

        public float GetValue(float defValue, Il2CppSystem.Func<GunData, float> func)
        {
            if (GetValueNeedsDefault())
                return defValue;

            return Mathf.Lerp(func.Invoke(_gunData), func.Invoke(_otherGunData), CaliberLerp);
        }

        public float GetValue(float defValue, System.Func<GunData, float> func)
        {
            if (GetValueNeedsDefault())
                return defValue;

            return Mathf.Lerp(func.Invoke(_gunData), func.Invoke(_otherGunData), CaliberLerp);
        }

        /// <summary>
        /// This is a copy of the stock function, with some minor fixes.
        /// When called via Harmony prefixing, minParam and maxParam are ignored.
        /// </summary>
        public float GetValue_GradeLength(int index, Il2CppSystem.Collections.Generic.Dictionary<int, float> defValue, Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>> func, float minParam, float maxParam)
        {
            if (GetValueNeedsDefault(index, defValue, out var defValFloat))
                return defValFloat;

            return GetValue_GradeLength(index, defValFloat, func.Invoke(_gunData), func.Invoke(_otherGunData), minParam, maxParam);
        }

        /// <summary>
        /// This is a copy of the stock function, with some minor fixes.
        /// When called via Harmony prefixing, minParam and maxParam are ignored.
        /// </summary>
        public float GetValue_GradeLength(int index, Il2CppSystem.Collections.Generic.Dictionary<int, float> defValue, System.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>> func, float minParam, float maxParam)
        {
            if (GetValueNeedsDefault(index, defValue, out var defValFloat))
                return defValFloat;

            return GetValue_GradeLength(index, defValFloat, func.Invoke(_gunData), func.Invoke(_otherGunData), minParam, maxParam);
        }

        private bool GetValueNeedsDefault(int index, Il2CppSystem.Collections.Generic.Dictionary<int, float> defValue, out float defValFloat)
        {
            if (!defValue.TryGetValue(index, out defValFloat))
            {
                if (defValue.Count > 0)
                {
                    Melon<UADRealismMod>.Logger.Error("defValue doesn't contain index " + index);
                    foreach (var kvp in defValue)
                    {
                        defValFloat = kvp.value;
                        return true;
                    }
                }
                Melon<UADRealismMod>.Logger.Error("defValue doesn't contain any values");
                defValFloat = 0f;
                return true;
            }

            if (_tc == null)
            {
                // Not gonna bother to log this, because the TC _will_ be null
                // in the case where you have a tooltip open for a part in the part
                // selection area in the Constructor, it will only be non-null
                // for placed parts.
                //Melon<UADRealismMod>.Logger.Error("GL___TC null!");

                return true;
            }

            if (_otherGunData == null)
                _otherGunData = GetOtherCaliberData();

            if (_otherGunData == null)
            {
                Melon<UADRealismMod>.Logger.Error("Next Cal null!");
                return true;
            }

            return false;
        }

        private float GetValue_GradeLength(int index, float defValFloat, Il2CppSystem.Collections.Generic.Dictionary<int, float> thisCalDict, Il2CppSystem.Collections.Generic.Dictionary<int, float> otherCalDict, float minParam, float maxParam)
        {
            if (!thisCalDict.TryGetValue(index, out var valThis))
            {
                Melon<UADRealismMod>.Logger.Error("this cal dict doesn't contain index " + index);
                return defValFloat;
            }

            if (!otherCalDict.TryGetValue(index, out var valOther))
            {
                Melon<UADRealismMod>.Logger.Error("next cal dict doesn't contain index " + index);
                return defValFloat;
            }

            return Mathf.Lerp(valThis, valOther, CaliberLerp)
                * (1f + RemapLength(minParam, maxParam, 0f));
        }

        private static readonly Func<GunData, float> _FuncBaseWeight = new Func<GunData, float>(d => d.baseWeight);

        public float BaseWeight()
        {
            float weight = GetValue(_gunData.baseWeight, _FuncBaseWeight)
                * RemapLength(MonoBehaviourExt.Param("gun_length_turret_weight_min", 0.9f), MonoBehaviourExt.Param("gun_length_turret_weight_max", 1.15f), 1f);

            float pmWeightMult = Part.GetModelNameScale(_partData, _ship, ModUtils._NullableEmpty_Int).weightModifier;
            if (pmWeightMult != 0f)
                weight *= pmWeightMult;

            return weight;
        }

        private static readonly Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>> _FuncBarrelWeights = new Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>>(d => d.barrelWeights);
        public float BarrelWeight(int grade)
        {
            float weight = GetValue_GradeLength(grade, _gunData.barrelWeights, _FuncBarrelWeights,
                MonoBehaviourExt.Param("gun_length_weight_min", -0.2f),
                MonoBehaviourExt.Param("gun_length_weight_max", 0.2f));

            float pmWeightMult = Part.GetModelNameScale(_partData, _ship, ModUtils._NullableEmpty_Int).weightModifier;
            if (pmWeightMult != 0f)
                weight *= pmWeightMult;

            return weight;
        }
    }
}
