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
    public class HullData
    {
        public string _key;
        public List<PartData> _hulls;
        public int _sectionsMin;
        public int _sectionsMax;
        public HullStats[] _statsSet;
        public bool _isDDHull = false;
        public float _year;
        public float yPos;
    }

    public struct HullStats
    {
        public float Lwl;
        public float B;
        public float T;
        public float beamBulge;
        public float bulgeDepth;
        public float Vd;
        public float Cb;
        public float Cm;
        public float Cwp;
        public float Cp;
        public float Cvp;
        public float iE;
        public float LrPct;
        public float bowLength;
        public float lcbPct;
        public float Catr;
        public float scaleFactor;
        public float Cv;
    }

    public struct OriginalHullData
    {
        public float _beamMin;
        public float _beamMax;
        public float _draughtMin;
        public float _draughtMax;
        public int _sectionsMin;
        public int _sectionsMax;
        public float _scale;
    }

    public static class ShipStats
    {
        private static bool _isRenderingHulls = false;
        public static bool _IsRenderingHulls => _isRenderingHulls;
        
        private static bool _LoadingDone = false;
        public static bool LoadingDone => _LoadingDone;

        private static readonly Dictionary<string, HullData> _HullModelData = new Dictionary<string, HullData>();
        private static readonly Dictionary<string, HullData> _HullToHullData = new Dictionary<string, HullData>();
        private static readonly Dictionary<string, OriginalHullData> _OriginalHullData = new Dictionary<string, OriginalHullData>();

        public static HullData GetData(Ship ship) => GetData(ship.hull.data);

        public static HullData GetData(PartData data)
        {
            _HullModelData.TryGetValue(GetHullModelKey(data), out var hData);
            return hData;
        }
        public static HullData GetData(string key)
        {
            _HullModelData.TryGetValue(key, out var hData);
            return hData;
        }

        public static HullData RegisterData(PartData data)
        {
            string key = GetHullModelKey(data);
            var hData = new HullData()
            {
                _key = key,
                _hulls = new List<PartData>() { data },
                _sectionsMin = data.sectionsMin,
                _sectionsMax = data.sectionsMax,
                _isDDHull = data.shipType.name == "dd" || data.shipType.name == "tb"
            };
            _HullModelData.Add(key, hData);
            return hData;
        }

        public static string GetHullModelKey(PartData data)
        {
            string key = data.model;
            if (data.shipType.name == "dd" || data.shipType.name == "tb")
                key += "%";
            if (data.paramx.TryGetValue("var", out var desiredVars))
            {
                key += "$";
                for (int i = 0; i < desiredVars.Count - 1; ++i)
                    key += desiredVars[i] + ";";
                key += desiredVars[desiredVars.Count - 1];
            }

            return key;
        }

        public static System.Collections.IEnumerator ProcessGameData()
        {
            // Wait to get past regular loading
            yield return null;
            yield return null;
            yield return null;

            var gameData = G.GameData;
            Il2CppSystem.Diagnostics.Stopwatch sw = new Il2CppSystem.Diagnostics.Stopwatch();
            sw.Start();
            Melon<UADRealismMod>.Logger.Msg("Processing Hulls in GameData");

            // Pass 1: Figure out the min and max sections used for each model.
            // (and deal with beam/draught coefficients)
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (!data.isHull)
                    continue;

                var origData = new OriginalHullData()
                {
                    _beamMax = data.beamMax,
                    _beamMin = data.beamMin,
                    _draughtMax = data.draughtMax,
                    _draughtMin = data.draughtMin,
                    _sectionsMin = data.sectionsMin,
                    _sectionsMax = data.sectionsMax,
                    _scale = data.scale
                };
                _OriginalHullData[data.name] = origData;

                // Just apply deltas to tonnage directly
                data.tonnageMin *= Mathf.Pow(data.beamMin * 0.01f + 1f, data.beamCoef) * Mathf.Pow(data.draughtMin * 0.01f + 1f, data.draughtCoef);
                data.tonnageMax *= Mathf.Pow(data.beamMax * 0.01f + 1f, data.beamCoef) * Mathf.Pow(data.draughtMax * 0.01f + 1f, data.draughtCoef);


                data.beamMin = -50f;
                data.beamMax = 50f;
                data.draughtMin = -75f;
                data.draughtMax = 50f;

                data.beamCoef = 0f;
                data.draughtCoef = 0f;

                // Now get var/section data
                var hData = GetData(data);
                if (hData == null)
                {
                    RegisterData(data);
                }
                else
                {
                    hData._sectionsMin = Math.Min(hData._sectionsMin, data.sectionsMin);
                    hData._sectionsMax = Math.Max(hData._sectionsMax, data.sectionsMax);
                    hData._hulls.Add(data);
                }
                _HullToHullData[data.name] = hData;
            }

            // Pass 2: Spawn and render the hull models, calculating stats
            yield return CalculateHullData();

            // Pass 3: Set starting scales for all hull parts, compute scale ratios
            int num = 1;
#if LOGHULLSTATS
            Melon<UADRealismMod>.Logger.Msg("time,order,name,model,sections,tonnage,scaleMaxPct,newScale,Lwl,Beam,Bulge,Draught,L/B,B/T,year,Cb,Cm,Cp,Cwp,Cvp,Catr,Cv,bowLen,BL/B,iE,Lr/L,lcb/L,DD,Kn,SHP,sMul");
#endif
            List<PartData> hulls = new List<PartData>();
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (!data.isHull)
                    continue;

                hulls.Add(data);

                // _now_ we set min sections to 0
                data.sectionsMin = 0;

                var hData = GetData(data);
                if (hData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find data for partdata {data.name} of model {data.model} with key {GetHullModelKey(data)}");
                    continue;
                }
                var modelName = hData._key;
                for (int sections = 0; sections < data.sectionsMax; ++sections)
                {
                    float tVal = sections > 0 ? 0.25f : 0;
                    float tng = Mathf.Lerp(data.tonnageMin, data.tonnageMax, tVal);
                    var stats = GetScaledStats(hData, tng, 0, 0, sections);
                    float shpMult = GetHullFormSHPMult(data);
                    float shp = GetSHPRequired(stats, data.speedLimiter * KnotsToMS, false) * shpMult;
#if LOGHULLSTATS
                    Melon<UADRealismMod>.Logger.Msg($",{num},{data.name},{modelName},{sections},{tng:F0},{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{stats.scaleFactor:F3},{stats.Lwl:F2},{stats.B:F2},{(stats.beamBulge == stats.B ? 0 : stats.beamBulge):F2},{stats.T:F2},{(stats.Lwl / stats.B):F2},{(stats.B / stats.T):F2},{hData._year},{stats.Cb:F3},{stats.Cm:F3},{stats.Cp:F3},{stats.Cwp:F3},{stats.Cvp:F3},{stats.Catr:F3},{stats.Cv:F3},{stats.bowLength:F2},{(stats.bowLength / stats.B):F2},{stats.iE:F2},{stats.LrPct:F3},{stats.lcbPct:F4},{hData._isDDHull},{data.speedLimiter:F1},{shp:F0},{shpMult:F2}");
#endif
                    ++num;
                }
            }


            // Pass 4: Compute hull scale ratios
            num = 1;
            Dictionary<PartData, float> hullToScaleRatio = new Dictionary<PartData, float>();
#if LOGHULLSCALES
            Melon<UADRealismMod>.Logger.Msg("time,order,name,scaleRatio,origB,origBeamMin,newB,Secs,L/B,Cp,CpDesired,Year,Kn");
#endif
            foreach (var hull in hulls)
            {
                var hData = GetData(hull);
                if (hData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Hull {hull.name} has no hull data!");
                    continue;
                }

                float sMax = hull.speedLimiter;
                if (sMax > 33f && (hull.shipType.name != "dd" && hull.shipType.name != "tb"))
                    sMax = 33f;

                float year = GetYear(hull);
                if (year < 0f)
                    year = 1915f;

                const float bDivT = DefaultBdivT * 0.867f;
                float lDivB = GetDesiredLdivB(hull.tonnageMin * TonnesToCubicMetersWater, bDivT, sMax * KnotsToMS, hull.shipType.name, year);
                int secs = GetDesiredSections(hData, hull.sectionsMin, hull.sectionsMax, hull.tonnageMin, sMax * KnotsToMS, lDivB, bDivT, out var bmPct, out var drPct, out var desiredCp);
                var stats = GetScaledStats(hData, hull.tonnageMin, bmPct, drPct, secs);
                if (secs < 0 || secs > hData._sectionsMax)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Hull {hull.name} calculated an out of bounds section count {secs}! Max {hData._sectionsMax}! ({sMax:F0}Kn, L/B {lDivB:F2}, B/T {bDivT:F2}");
                    continue;
                }
                var origStats = hData._statsSet[secs];
                var origData = _OriginalHullData[hull.name];
                float origB = (origStats.B * (1f + origData._beamMin * 0.01f)) * origData._scale;
                float newB = stats.B;
                float scaleRatio = newB / origB;
                hullToScaleRatio[hull] = scaleRatio;
#if LOGHULLSCALES
                Melon<UADRealismMod>.Logger.Msg($",{num++},{hull.name},{scaleRatio:F3},{origB:F2},{origData._beamMin:F0},{newB:F2},{secs},{lDivB:F2},{origStats.Cp:F3},{desiredCp:F3},{year:F0},{sMax:F1}");
#endif
            }

            num = 1;
#if LOGPARTSTATS
            Melon<UADRealismMod>.Logger.Msg("time$order$name$model$scaleRatio$minHull$origScale$newScale");
#endif
            const float torpedoScale = 0.85f;

            // Pass 5: change part sizes
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (data.isHull)
                    continue;

                if (data.isTorpedo)
                {
                    if (data.model != "(custom")
                        data.scale *= torpedoScale;

                    continue;
                }

                // Guns handled in next loop for logging purposes.
                // Assume no regular part is has "(custom)" for model

                // Hate to do n^2 but we don't have a lookup, stock does hashset overlaps.
                float minScaleRatio = float.MaxValue;
                string minHullName = "(none)";
                foreach(var hull in hulls)
                {
                    if (!IsPartAllowed(hull, hull.shipType, data))
                        continue;

                    // This should never happen -- we precompute them all
                    if (!hullToScaleRatio.TryGetValue(hull, out var scaleRatio))
                        continue;

                    if (scaleRatio < minScaleRatio)
                    {
                        minHullName = hull.name;
                        minScaleRatio = scaleRatio;
                    }
                }
                float origScale = data.scale;
                if (minScaleRatio == float.MaxValue)
                    minScaleRatio = 1f;

                // TODO: Should we scale up? For now,
                // only scale down.
                if (minScaleRatio < 1f)
                {
                    minScaleRatio = Mathf.Max(0.8f, minScaleRatio);
                    data.scale *= minScaleRatio;
                }
#if LOGPARTSTATS
                Melon<UADRealismMod>.Logger.Msg($"${num++}${data.name}${data.model}${minScaleRatio:F3}${minHullName}${origScale:F3}${data.scale:F3}");
#endif
            }

            // Pass 6: Rescale guns
            num = 1;
            HashSet<PartModelData> rescaledPMDs = new HashSet<PartModelData>();
            _PMDKVPs = new List<KeyValuePair<int, float>>();
#if LOGGUNSTATS
            Melon<UADRealismMod>.Logger.Msg("time$order$name$partData$caliber$scaleRatio");
#endif
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.value;
                if (!data.isWeapon || data.isTorpedo)
                    continue;


                // It appears the "(custom)" and non-custom paths
                // are the same for guns. And we already did torps.
                string str = data.name;
                Util.TryRemovePostfix(ref str, "_side");

                if (!gameData.partModels.TryGetValue(str, out var mData))
                {
                    Melon<UADRealismMod>.Logger.BigError($"Part {data.name} is a gun but has no matching partmodel!");
                    continue;
                }

                if (mData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Part {data.name} is a gun but has a null partmodel!");
                    continue;
                }

                if (rescaledPMDs.Contains(mData))
                    continue;

                rescaledPMDs.Add(mData);
                float calInch = data.caliber * (1f / 25.4f);
                float scaleFactor = GunModelScale(calInch);
                ApplyScale(mData, scaleFactor);
#if LOGGUNSTATS
                Melon<UADRealismMod>.Logger.Msg($"${num++}${mData.name}${data.name}${calInch:F0}${scaleFactor:F3}");
#endif

                foreach (var pmd in mData.overrides)
                {
                    if (rescaledPMDs.Contains(pmd))
                        continue;

                    if (pmd == null)
                    {
                        Melon<UADRealismMod>.Logger.BigError($"Part {data.name} with PartModel {mData.name} has a null PMD in its overrides!");
                        continue;
                    }

                    rescaledPMDs.Add(pmd);
                    ApplyScale(pmd, scaleFactor);
#if LOGGUNSTATS
                    Melon<UADRealismMod>.Logger.Msg($",{num++},{mData.name},{data.name},{calInch:F0},{scaleFactor:F3}");
#endif
                }
            }

            // Pass 7: fix up torpedo scaling
            foreach (var kvp in gameData.partModels)
            {
                if (kvp.value == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"PartModel {(string.IsNullOrEmpty(kvp.key) ? "NULL/EMPTY" :  kvp.key)} is null!");
                    continue;
                }

                if (!kvp.key.StartsWith("torp"))
                {
                    if (!rescaledPMDs.Contains(kvp.value))
                    {
                        // special handling: this exists but no gun_1 exists.
                        if (kvp.key == "casemate_1")
                        {
                            ApplyScale(kvp.value, GunModelScale(1f));
                            rescaledPMDs.Add(kvp.value);
                        }
                        else
                        {
                            Melon<UADRealismMod>.Logger.Warning($"Found PartModelData {kvp.key} that hasn't been rescaled! Has subName {kvp.value.subName} and shiptypes {kvp.value.shipTypes}");
                        }
                    }
                    continue;
                }

                ApplyScale(kvp.value, torpedoScale);
            }

            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");

            _LoadingDone = true;
            G.ui.CompleteLoadingScreen();
        }

        private static float GunModelScale(float caliber)
        {
            float calInch = caliber * (1f / 25.4f);
            return 0.9f + 0.1f * Mathf.Cos((calInch < 11.87f ?
                (Mathf.Pow(Mathf.Max(0, calInch - 4f), 0.78f) + 4)
                : (Mathf.Pow(calInch - 11.87f, 1.25f) + 11.87f)) * Mathf.PI * 0.5f);
        }

        private static List<KeyValuePair<int, float>> _PMDKVPs;

        private static void ApplyScale(PartModelData pmd, float scaleFactor)
        {
            if (pmd == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Got to ApplyScale with null PMD!");
                return;
            }

            pmd.scale_1 *= scaleFactor;
            pmd.scale_2 *= scaleFactor;
            pmd.scale_3 *= scaleFactor;
            pmd.scale_4 *= scaleFactor;
            pmd.scale_5 *= scaleFactor;

            pmd.max_scale_1 *= scaleFactor;
            pmd.max_scale_2 *= scaleFactor;
            pmd.max_scale_3 *= scaleFactor;
            pmd.max_scale_4 *= scaleFactor;
            pmd.max_scale_5 *= scaleFactor;

            if (_PMDKVPs == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"PartModel {pmd.name}: _Keys null!");
                _PMDKVPs = new List<KeyValuePair<int, float>>();
            }

            if (pmd.scales == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"PartModel {pmd.name} has null scales!");
            }
            else
            {
                foreach (var kvp in pmd.scales)
                    _PMDKVPs.Add(new KeyValuePair<int, float>(kvp.key, kvp.value));
                foreach (var kvp in _PMDKVPs)
                    pmd.scales[kvp.Key] = kvp.Value * scaleFactor;
                _PMDKVPs.Clear();
            }

            if (pmd.maxScales == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"PartModel {pmd.name} has null maxScales!");
            }
            else
            {
                foreach (var kvp in pmd.maxScales)
                    _PMDKVPs.Add(new KeyValuePair<int, float>(kvp.key, kvp.value));
                foreach (var kvp in _PMDKVPs)
                    pmd.maxScales[kvp.Key] = kvp.Value * scaleFactor;
                _PMDKVPs.Clear();
            }
        }

        private static bool IsPartAllowed(PartData hull, ShipType sType, PartData part)
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

        public static HullStats GetScaledStats(Ship ship)
        {
            var hData = GetData(ship);
            if (hData == null)
                return new HullStats();

            int sec = Mathf.RoundToInt(Mathf.Lerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, 1f - ship.hullPartSizeZ * 0.01f));
            return GetScaledStats(hData, ship.tonnage, ship.beam, ship.draught, sec);
        }

        public static HullStats GetScaledStats(HullData hData, float tonnage, float lengthToBeamPct, float beamToDraughtPct, int sections)
        {
            var newStats = hData._statsSet[sections];
            //Melon<UADRealismMod>.Logger.Msg($"Pre: {newStats.Lwl:F2}x{newStats.B:F2}x{newStats.T:F2}, {newStats.Vd}t.");

            float linearScale = GetHullScaleFactor(tonnage, newStats.Vd, lengthToBeamPct, beamToDraughtPct);
            float bmMult = lengthToBeamPct * 0.01f + 1f;
            float drMult = bmMult * (beamToDraughtPct * 0.01f + 1f);
            newStats.B *= bmMult * linearScale;
            newStats.beamBulge *= bmMult * linearScale;
            newStats.bulgeDepth *= drMult * linearScale;
            newStats.Lwl *= linearScale;
            newStats.T *= drMult * linearScale;
            float volMult = drMult * bmMult * linearScale * linearScale * linearScale;
            newStats.Vd *= volMult; // Vd should be the same as ship.Tonnage() * TonnesToCubicMetersWater
            newStats.Cv *= volMult;
            newStats.bowLength *= linearScale;
            newStats.scaleFactor = linearScale;

            //Melon<UADRealismMod>.Logger.Msg($"Post with {linearScale:F3}x, B={bmMult:F3},T={drMult:F3}: {newStats.Lwl:F2}x{newStats.B:F2}x{newStats.T:F2}, {newStats.Vd}t.");

            return newStats;
        }

        public static float GetHullScaleFactor(Ship ship, float Vd)
        {
            return GetHullScaleFactor(ship.tonnage, Vd, ship.beam, ship.draught);
        }

        public static float GetHullScaleFactor(float tonnage, float Vd, float lengthToBeamPct, float beamToDraughtPct)
        {
            float desiredVol = tonnage * TonnesToCubicMetersWater;
            float bmMult = 1f + lengthToBeamPct * 0.01f;
            float drMult = bmMult * (1f + beamToDraughtPct * 0.01f);
            return Mathf.Pow(desiredVol / (Vd * bmMult * drMult ), 1f / 3f);
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

        public static float GetAverageYear(HullData hData)
        {
            float year = 0f;
            float divisor = 0f;
            foreach (var hull in hData._hulls)
            {
                float hYear = GetYear(hull);
                if (hYear < 0f)
                    continue;

                year += hYear;
                ++divisor;
            }
            if (divisor > 0)
                year /= divisor;
            else
                year = 1915f;

            return year;
        }

        // water at 15C, standard salinity
        public const double WaterDensity = 1026;
        public const float TonnesToCubicMetersWater = (float)(1d / (WaterDensity * 0.001d));
        public const double WaterKinematicViscosity = 1.1892e-6d;

        public const float KnotsToMS = 0.514444444f;

        public const float DefaultBdivT = 3f;

        public static float GetDesiredLdivB(Ship ship, float BdivT)
            => GetDesiredLdivB(ship.tonnage * TonnesToCubicMetersWater, BdivT, ship.speedMax, ship.shipType.name, GetYear(ship.hull.data));

        public static float GetDesiredLdivB(float Vd, float BdivT, float speedMS, string sType, float year)
        {
            float Cb = 0.65f;
            switch (sType)
            {
                case "tb":
                case "dd":
                    Cb = Util.Remap(year, 1890f, 1920f, 0.38f, 0.54f, true);
                    break;

                case "cl":
                case "ic":
                    Cb = 0.55f;
                    break;

                case "bc":
                case "ca":
                    Cb = 0.6f;
                    break;

                case "bb":
                    Cb = Util.Remap(year, 1890f, 1930f, 0.65f, 0.62f, true);
                    break;
            }

            float blockVolume = Vd / Cb;

            // Use block to estimate a starting L/B
            float desiredLdivB = LdivBFromBlock(Cb, year);
            for (int i = 0; i < 8; ++i)
            {
                float B = Mathf.Pow(blockVolume * BdivT / desiredLdivB, 1f / 3f);
                float L = desiredLdivB * B;
                float Fn = speedMS / Mathf.Sqrt(9.8066f * L);
                float oldLB = desiredLdivB;
                desiredLdivB = LdivBFromFn(Fn, year);
                //if (_LoadingDone)
                //    Melon<UADRealismMod>.Logger.Msg($"Iterating to find L/B. Cb={Cb:F3} ({sType}), {L:F2}x{B:F2}, Fn={Fn:F3}, old={oldLB:F2},new={desiredLdivB:F2}");
                float ratio = oldLB > desiredLdivB ? desiredLdivB / oldLB : oldLB / desiredLdivB;
                if (ratio > 0.99f)
                    break;
            }
            return desiredLdivB;
        }

        private static float LdivBFromFn(float Fn, float year)
        {
            return Mathf.Clamp(5f + (Fn - 0.2f) * 21f, Util.Remap(year, 1890f, 1930f, 4f, 5f, true), Util.Remap(year, 1890f, 1930f, 11f, 9.5f, true));
        }

        private static float LdivBFromBlock(float Cb, float year)
        {
            float t = Mathf.InverseLerp(0.45f, 0.65f, Cb);
            return Mathf.Lerp(11f, Util.Remap(year, 1890f, 1930f, 5f, 6.5f), t * t);
        }

        public static int GetDesiredSections(Ship ship, float desiredLdivB, float desiredBdivT, out float finalBmPct, out float finalDrPct, out float desiredCp, float CpOffset = 0f)
            => GetDesiredSections(GetData(ship.hull.data), ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, ship.tonnage, ship.speedMax, desiredLdivB, desiredBdivT, out finalBmPct, out finalDrPct, out desiredCp, CpOffset);

        public static int GetDesiredSections(HullData hData, int sectionsMin, int sectionsMax, float tonnage, float speedMS, float desiredLdivB, float desiredBdivT, out float finalBmPct, out float finalDrPct, out float desiredCp, float CpOffset = 0f)
        {
            // Find the section count with closest-to-desired Cp.
            // We could try to binary search here, but this is fast enough
            float bestDiff = 1f;
            int bestSec = sectionsMin;
            finalBmPct = 0f;
            finalDrPct = 0f;
            desiredCp = 0f;
            for (int secs = sectionsMin; secs <= sectionsMax; ++secs)
            {
                float bmMult = (hData._statsSet[secs].Lwl / hData._statsSet[secs].B) / desiredLdivB;
                float drMult = (hData._statsSet[secs].B * bmMult / hData._statsSet[secs].T) / desiredBdivT;
                float bmPct = (bmMult - 1f) * 100f;
                float drPct = (drMult / bmMult - 1f) * 100f;
                var stats = GetScaledStats(hData, tonnage, bmPct, drPct, secs);
                float Fn = speedMS / Mathf.Sqrt(9.80665f * stats.Lwl);
                float desCp = GetDesiredCpForFn(Fn) + CpOffset;
                float delta = Mathf.Abs(desCp - stats.Cp);
                if (delta < bestDiff)
                {
                    bestDiff = delta;
                    bestSec = secs;
                    finalBmPct = bmPct;
                    finalDrPct = drPct;
                    desiredCp = desCp - CpOffset;
                    //if (_LoadingDone)
                    //    Melon<UADRealismMod>.Logger.Msg($"Iterating@{secs} {hData._statsSet[secs].Lwl:F2}x{hData._statsSet[secs].B:F2}x{hData._statsSet[secs].T:F2}->{stats.Lwl:F2}x{stats.B:F2}x{stats.T:F2} with {bmPct:F0}%,{drPct:F0}%. Fn={Fn:F2}, desired={desiredCp:F3}, Cp={stats.Cp:F3}");
                }
                // Once we overshoot, everything after will have a bigger delta.
                if (stats.Cp > desCp)
                    break;
            }

            return bestSec;
        }

        public static float GetDesiredCpForFn(float Fn)
        {
            float tVal;
            if (Fn > 0.33f)
                tVal = 1f - Mathf.Pow((Fn - 0.33f) / (0.6f - 0.33f), 0.8f);
            else
                tVal = (Fn - 0.18f) / (0.33f - 0.18f);
            
            float scaling = Mathf.Cos(Mathf.Clamp01(tVal) * Mathf.PI) * 0.5f + 0.5f;
            
            return 0.58f + scaling * (Fn > 0.33f ? 0.06f : 0.126f);
        }

        public static float GetEngineIHPMult(Ship ship)
        {
            foreach (var kvp in ship.components)
            {
                if (kvp.key.name == "engine")
                {
                    return GetEngineIHPMult(kvp.value);
                }
            }
            return 1f;
        }

        public static float GetEngineIHPMult(ComponentData comp)
        {
            switch (comp.name)
            {
                case "main_engine_1": return 1f / 0.8f;         // basic
                case "main_engine_2": return 1f / 0.88f;        // Triple Exp
                case "main_engine_3": return 1f / 0.905f;        // Quad Exp
                case "main_engine_3_adv": return 1f / 0.92f;    // Adv Quad
                default: return 1f;
            }
        }

        public static float GetHullFormSHPMult(Ship ship) => GetHullFormSHPMult(ship.hull.data);

        public static float GetHullFormSHPMult(PartData data)
        {
            if (!G.GameData.stats.TryGetValue("hull_form", out var sd))
                return 1f;
            if (!data.statsx.TryGetValue(sd, out var hullForm))
                return 1f;

            float influence = Mathf.InverseLerp(80f, 20f, hullForm);
            return Mathf.Lerp(1f, Util.Remap(hullForm, 20f, 80f, 1.35f, 1f, true), influence * influence);
        }

        public static float GetSHPRequired(Ship ship)
        {
            float shp = GetSHPRequired(GetScaledStats(ship), ship.speedMax);
            return shp * GetHullFormSHPMult(ship);
        }

#if LOGSHP
        private static int _LastLogFrame = -1;
#endif
        public static float GetSHPRequired(HullStats stats, float speedMS, bool log = true)
        {
            double L = stats.Lwl;
            double B = stats.B;
            double T = stats.T;
            double LdivB = L / B;
            double BdivT = B / T;
            double LdivT = L / T;
            double BdivL = B / L;
            double TdivB = T / B;
            double TdivL = T / L;
            double Cp = stats.Cp;
            double Cb = stats.Cb;
            double Cm = stats.Cm;
            double Cw = stats.Cwp;
            double Vd = stats.Vd;
            double VolCoeff = Vd / (L * L * L);
            double L3V = L * L * L / Vd;
            double Catr = stats.Catr;
            double V13 = Math.Pow(Vd, 1d / 3d);
            double msVel = speedMS;

            // Rf
            double Re = msVel * L / WaterKinematicViscosity;
            // ITTC-57 line
            double log10Re = Math.Log10(Re);
            double denom = log10Re - 2d;
            denom *= denom;
            double Cf = 0.075d / denom;
            // LL08 correction - we only need the higher-Re coefficients
            double corr = log10Re - 7.5;
            Cf *= (1.0245 + 0.03311 * (log10Re - 7.5) - 0.006028 * corr * corr);
            double c14 = 1d; // stern coefficient, varies 0.9725-1.011

            double lcb = stats.lcbPct * 100d;
            double LRperL = (1d - Cp + 0.06d * Cp * lcb / (4d * Cp - 1d));
            double iE = 1d + 89d * Math.Exp(-(Math.Pow(LdivB, 0.80856)) * Math.Pow(1 - Cw, 0.30484) * Math.Pow(1 - Cp - 0.0225 * lcb, 0.6367) * Math.Pow(LRperL * LdivB, 0.34574) * Math.Pow(100 * VolCoeff, 0.16302));
            // Just use the calculated iE and LR/L.

            // this is SpringSharp's "Sharpness coefficient"
            //double coSharp = 0.4d * Math.Pow(B / L * 6f, 0.3333333432674408d) * Math.Sqrt(Cb / 0.52f);
            //double sharpLerp = ModUtils.InverseLerp(0.35d, 0.45d, coSharp);
            //double LrMax, LrMin, iEMax, iEMin;
            //if (LRperL < stats.LrPct)
            //{
            //    LrMax = stats.LrPct;
            //    LrMin = LRperL;
            //}
            //else
            //{
            //    LrMax = LRperL;
            //    LrMin = stats.LrPct;
            //}
            //if (iE < stats.iE)
            //{
            //    iEMax = stats.iE;
            //    iEMin = iE;
            //}
            //else
            //{
            //    iEMax = iE;
            //    iEMin = stats.iE;
            //}
            //// lower is faster in both cases.
            //LRperL = ModUtils.Lerp(LrMin, LrMax, sharpLerp);
            //iE = ModUtils.Lerp(iEMin, iEMax, sharpLerp);

            // recalc 
            double formFactor = 0.93 + 0.487118 * c14 * Math.Pow(BdivL, 1.06806) * Math.Pow(TdivL, 0.46106) * Math.Pow(1d / LRperL, 0.121563) * Math.Pow(L3V, 0.36486) * Math.Pow(1 - Cp, -0.604247);
            double S = L * (B + 2d * T) * Math.Sqrt(Cm) * (0.453d + 0.4425d * Cb - 0.2862d * Cm - 0.003467d * BdivT + 0.3696d * Cw);// + 19.65 * A_bulb_at_stem / Cb
            double dynPres = 0.5d * WaterDensity * msVel * msVel;
            double Rf = Cf * formFactor * dynPres * S;

            double c3 = 0d; // 0.56 * Abt^1.5/(B*T*(0.31*sqrt(Abt)+Tp-hB)) where Tp is draught and hB is elevation of center of bulb above keel
            double c2 = Math.Exp(-1.89d * Math.Sqrt(c3));

            // Ra
            double Cb4 = Cb * Cb;
            Cb4 *= Cb4;
            double Ca = 0.006d * Math.Pow(L + 100d, -0.16d) - 0.00205d + 0.003d * Math.Sqrt(L / 7.5d) * Cb4 * c2 * (0.04d - Math.Min(0.04d, TdivL));
            double Ra = Ca * dynPres * S;

            // Rtr
            double Atr = Catr * B * T * Cm;
            double c6 = Catr > 0 ? 0.2 * (1d - 0.2 * Math.Min(5, msVel / Math.Sqrt(2d * 9.80665 * Atr / (B * (1 + Cw))))) : 0d;
            double Rtr = dynPres * Atr * c6;

            // Rw
            double c7;
            if (BdivL < 0.11d)
                c7 = 0.229577d * Math.Pow(BdivL, 0.3333);
            else if (BdivL > 0.25d)
                c7 = 0.5d - 0.0625 * LdivB;
            else
                c7 = BdivL;

            double c1 = 2223105d * Math.Pow(c7, 3.78615d) * Math.Pow(TdivB, 1.07961d) * Math.Pow(90d - iE, -1.37565d);
            double c5 = 1d - 0.8 * Catr;
            double Fr = msVel / Math.Sqrt(9.80665d * L);
            double lambda = 1.446d * Cp - 0.03d * Math.Min(12d, LdivB);
            double c16;
            if (Cp < 0.8d)
                c16 = 8.07981d * Cp - 13.8673d * Cp * Cp + 6.984388d * Cp * Cp * Cp;
            else
                c16 = 1.73013d - 0.7067d * Cp;
            double c15;
            if (L3V < 512d)
                c15 = -1.69385d;
            else if (L3V > 1727d)
                c15 = 0d;
            else
                c15 = -1.69385d + (L / V13 - 8d) / 2.36d;
            double m1 = 0.0140407 * LdivT - 1.75254 * V13 / L - 4.79323 * BdivL - c16;
            double c17 = 6919.3 * Math.Pow(Cm, -1.3346) * Math.Pow(VolCoeff, 2.00977) * Math.Pow(LdivB - 2, 1.40692);
            double m3 = -7.2035 * Math.Pow(BdivL, 0.326869) * Math.Pow(TdivB, 0.605375);
            double m4 = c15 * 0.4 * Math.Exp(-0.034 * Math.Pow(Fr, -3.29));

            const double FrAEnd = 0.35d;
            const double FrBStart = 0.50d;
            double FrA = Math.Min(Fr, FrAEnd);
            double FrB = Math.Max(Fr, FrBStart);
            double c5_mul_weight = c5 * Vd * WaterDensity * 9.80665d;
            double RwA = c1 * c2 * c5_mul_weight * Math.Exp(m1 * Math.Pow(FrA, -0.9d) + m4 * Math.Cos(lambda / (FrA * FrA)));
            double RwB = c17 * c2 * c5_mul_weight * Math.Exp(m3 * Math.Pow(FrB, -0.9) + m4 * Math.Cos(lambda / (FrB * FrB)));
            double Rw = ModUtils.Lerp(RwA, RwB, ModUtils.InverseLerp(FrAEnd, FrBStart, Fr));
            // Additional tweaks past Holtrop-Mennen
            double rwmultVolCoeff = Math.Max(0.85d, 1d + 200d * (VolCoeff - 0.0025d) * Math.Max(0.1d, ModUtils.InverseLerp(0.25d, 0.4d, Fr)));
            double rwmultCm = 1d + (1.8d * Math.Max(0.75d, Cm) - 1) * ModUtils.InverseLerp(0.25d, 0.3d, Fr);
            
            double rwmultFr = 1d;
            double rwmultCb = 1d;
            const double highFrStart = 0.4d;
            if (Fr > highFrStart)
            {
                rwmultFr += Fr * Fr / (highFrStart * highFrStart) * 0.55d;
                rwmultCb += (0.16d - 0.5 * ModUtils.InverseLerp(0.3d, 0.5d, Cb)) * ModUtils.InverseLerp(0.4d, 0.5d, Fr);
            }

            Rw *= rwmultVolCoeff * rwmultFr * rwmultCm * rwmultCb;

            double Rt = Rf + Rw + Rtr + Ra;

            // Propulsion
            const double eta_o = 0.65d; // open-water propeller efficiency. Eventually grab from tech?
            const double PdivD = 0.9d; // pitch / diameter ratio. Eventually make this tunable?
            const double DdivSqrtBT = 0.29d; // diameter / sqrt( beam * draught) -- eventually grab from prop?
            double eta_R = 0.9737 + 0.111 * (Cp - 0.0225 * lcb) - 0.06325 * PdivD;
            double Cv = Cf * formFactor + Ca;
            double w = 0.3095 * Cb + 10 * Cb * Cv - 0.23 * DdivSqrtBT;
            double t = 0.3095 * Cb - 0.18851 * DdivSqrtBT;
            const double eta_shaft = 0.99d;

            double Pe = Rt * msVel;
            double Ps = Pe / (eta_R * eta_shaft * eta_o * (1 - t) / (1 - w));
#if LOGSHP
            int frames = Time.frameCount;
            if (log && frames != _LastLogFrame)
            {
                _LastLogFrame = frames;
                Debug.Log($"c1={c1:F3}, c2={c2:F3}, c3={c3:F3}, c5={c5:F3}, c6={c6:F3}, c7={c7:F3}, c14={c14:F3}, c15={c15:F3}, c16={c16:F3}, c17={c17:F3}, lcb={lcb:F1}, sharp={coSharp:F3}, sLerp={sharpLerp:F2}, LR={(LRperL*L):F1}, iE={iE:F1}, "
                    + $"Cvol={VolCoeff:F5}, labmda={lambda:F3}, Re={Re:E3}, Cf={Cf:F5}, S={S:N0}, FF={formFactor:F3}, Rf={Rf:N0}, Ra={Ra:N0}, Fr={Fr:F2}, m1={m1:F3}, m3={m3:F3}, m4={m4:F3}, RwA={RwA:N0}, RwB={RwB:N0}, rwmVol={rwmultVolCoeff:F2}, "
                    + $"rwmFr={rwmultFr:F2}, rwmCm={rwmultCm:F2}, rwmCb={rwmultCb:F2}, Rw={Rw:N0}, Rt={Rt:N0}, etaR={eta_R:F2}, Cv={Cv:F3}, w={w:F2}, t={t:F2}.");
            }
#endif

            return (float)(Ps / 745.7d);
        }

        public static System.Collections.IEnumerator CalculateHullData()
        {
            _isRenderingHulls = true;
            foreach (var kvp in _HullModelData)
            {
                yield return ShipStatsCalculator.ProcessHullData(kvp.Value);
            }
            _isRenderingHulls = false;
        }
    }    
}
