#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.IO;
using Il2CppMessagePack.Formatters;

#pragma warning disable CS8601
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace UADRealism
{
    public class HullData
    {
        public string _key;
        public List<PartData> _hulls;
        public int _sectionsMin;
        public int _secMinWithFunnel;
        public int _sectionsMax;
        public HullStats[] _statsSet;
        public bool _isDDHull = false;
        public float _year;
        public float _desiredCb;
        public float _hullNumber;
        public float _yPos;
    }

    public enum HullLoadState
    {
        LightShip,
        Standard,
        Normal,
        Full
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
        const int _Version = 5;

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

            Il2CppSystem.Diagnostics.Stopwatch sw = new Il2CppSystem.Diagnostics.Stopwatch();
            sw.Start();
            Melon<UADRealismMod>.Logger.Msg("Processing Hulls in GameData");

            yield return HullSteps();
            yield return PartSteps();

            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");

            _LoadingDone = true;
            G.ui.CompleteLoadingScreen();
        }

        private static System.Collections.IEnumerator HullSteps()
        {
            var gameData = G.GameData;
            int totalHulls = 0;

            // Pass 1: Figure out the min and max sections used for each model.
            // (and deal with beam/draught coefficients)
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (!data.isHull)
                    continue;

                ++totalHulls;

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

                if (data.shipType.name == "dd" || data.shipType.name == "tb")
                    data.tonnageMin *= 0.6f;


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

            // Try to load from disk. If we can't,
            // then process the hulls and save.
            if (!LoadData(totalHulls))
            {
                foreach (var kvp in _HullModelData)
                {
                    SetAverageYearAndCb(kvp.Value);
                }

                // Pass 2: Spawn and render the hull models, calculating stats
                yield return CalculateHullData(false);

                // Pass 2.5: Correct for really weird high-draught hull models
                // (and, in Monitor's case, too low draught). In the process,
                // sanify B/T values.
                float minBT = 100f, maxBT = 0f, avgBT = 0f;
                float hCount = 0f;
                foreach (var kvp in _HullModelData)
                {
                    float BdivT = kvp.Value._statsSet[0].B / kvp.Value._statsSet[0].T;
                    if (BdivT < minBT)
                        minBT = BdivT;
                    if (BdivT > maxBT)
                        maxBT = BdivT;

                    avgBT += BdivT;
                    ++hCount;
                }
                if (hCount > 0f)
                    avgBT /= hCount;
                foreach (var kvp in _HullModelData)
                {
                    for (int i = kvp.Value._statsSet.Length; i-- > 0;)
                    {
                        var stats = kvp.Value._statsSet[i];
                        float BdivT = stats.B / stats.T;

                        float newBT;
                        if (BdivT < avgBT)
                            newBT = Util.Remap(BdivT, minBT, avgBT, 2.25f, 2.8f, true);
                        else
                            newBT = Util.Remap(BdivT, avgBT, maxBT, 2.8f, 3.75f, true);

                        float tMult = BdivT / newBT;
                        if (tMult < 0.99f || tMult > 1.01f)
                        {
                            stats.bulgeDepth *= tMult;
                            stats.Cv *= tMult;
                            stats.T *= tMult;
                            stats.Vd *= tMult;
                            kvp.Value._statsSet[i] = stats;
                        }
                    }
                }

                SaveData(totalHulls);
            }

            // Pass 3: Set starting data for all hull parts
#if LOGHULLSTATS
            int num = 1;
            Melon<UADRealismMod>.Logger.Msg("time,order,name,model,sections,tonnage,scaleMaxPct,newScale,Lwl,Beam,Bulge,Draught,L/B,B/T,HN,dCb,year,Cb,Cm,Cp,Cwp,Cvp,Catr,Cv,bowLen,BL/B,iE,Lr/L,lcb/L,DD,Kn,SHP,NormTng,T,B/T,Cb,Cm,Cp,ammo,range,SHP");
#endif
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (!data.isHull)
                    continue;

                var hData = GetData(data);
                if (hData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find data for partdata {data.name} of model {data.model} with key {GetHullModelKey(data)}");
                    continue;
                }

                ApplyHullData(data, hData);

#if LOGHULLSTATS
                var modelName = hData._key;
                float year = Database.GetYear(data);
                const float desiredBdivT = DefaultBdivT * 0.867f;
                float BT = hData._statsSet[data.sectionsMin].B / hData._statsSet[data.sectionsMin].T;
                VesselEntity.OpRange range = data.shipType.name switch
                {
                    "dd" => year < 1910 ? VesselEntity.OpRange.VeryHigh : year < 1920 ? VesselEntity.OpRange.High : VesselEntity.OpRange.Medium,
                    "tb" => year < 1900 ? VesselEntity.OpRange.VeryLow : year < 1910 ? VesselEntity.OpRange.Low : VesselEntity.OpRange.Medium,
                    "ca" or "cl" => VesselEntity.OpRange.Medium,
                    _ => year < 1925 ? VesselEntity.OpRange.VeryLow : VesselEntity.OpRange.Low
                };
                for (int sections = data.sectionsMin; sections < data.sectionsMax; ++sections)
                {
                    float oldLB = hData._statsSet[sections].Lwl / hData._statsSet[sections].B;
                    float oldBT = hData._statsSet[sections].B / hData._statsSet[sections].T;
                    float desiredLdivB = GetDesiredLdivB(data.tonnageMin * TonnesToCubicMetersWater, hData._desiredCb, desiredBdivT, data.speedLimiter * KnotsToMS, data.shipType.name, year);
                    float bmMult = oldLB / desiredLdivB;
                    float drMult = BT / desiredBdivT;
                    if (float.IsNaN(bmMult) || bmMult == 0f)
                        bmMult = 1f;
                    if (float.IsNaN(drMult) || drMult == 0f)
                        drMult = 1f;

                    var stats = GetScaledStats(hData, data.tonnageMin, (bmMult - 1f) * 100f, (drMult / bmMult - 1f) * 100f, sections);
                    float shp = GetSHPRequired(stats, data.speedLimiter * KnotsToMS, false);
                    var nStats = GetLoadingStats(stats, data.tonnageMin, range, year, data.shipType.name, HullLoadState.Normal);
                    float shpNormal = GetSHPRequired(nStats, data.speedLimiter * KnotsToMS, false);
                    float ammoPct = EstimatedAmmoPct(data.shipType.name, year);
                    float opRangePct = OpRangeToPct(range, year, data.shipType.name);
                    Melon<UADRealismMod>.Logger.Msg($",{num},{data.name.Replace(',', '&')},{modelName},{sections},{data.tonnageMin:F0},{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{stats.scaleFactor:F3},{stats.Lwl:F2},{stats.B:F2},{(stats.beamBulge == stats.B ? 0 : stats.beamBulge):F2},{stats.T:F2},{(stats.Lwl / stats.B):F2},{(stats.B / stats.T):F2},{hData._hullNumber:F0},{hData._desiredCb:F3},{hData._year},{stats.Cb:F3},{stats.Cm:F3},{stats.Cp:F3},{stats.Cwp:F3},{stats.Cvp:F3},{stats.Catr:F3},{stats.Cv:F3},{stats.bowLength:F2},{(stats.bowLength / stats.B):F2},{stats.iE:F2},{stats.LrPct:F3},{stats.lcbPct:F4},{hData._isDDHull},{data.speedLimiter:F1},{shp:F0},{(nStats.Vd / TonnesToCubicMetersWater):F0},{nStats.T:F2},{(nStats.B/nStats.T):F2},{nStats.Cb:F3},{nStats.Cm:F3},{nStats.Cp:F3},{ammoPct:F2},{opRangePct:F2},{shpNormal:F0}");
                    ++num;
                }
#endif
            }
        }

        private static void ApplyHullData(PartData hull, HullData hData)
        {
            // Set min sections to lowest we can
            if (hData._secMinWithFunnel < hull.sectionsMin)
                hull.sectionsMin = hData._secMinWithFunnel;

            float t = Mathf.InverseLerp(0.45f, 0.65f, hData._statsSet[hull.sectionsMin].Cb);
            float minLB1 = Mathf.Lerp(6f, 2.75f, t);
            float minLB2 = Mathf.Lerp(7f, 4f, t);
            float maxLB1 = Mathf.Lerp(12f, 8f, t);
            float maxLB2 = Mathf.Lerp(15f, 9f, t);

            float minLB = hData._statsSet[hull.sectionsMin].Lwl / hData._statsSet[hull.sectionsMin].B;
            float maxLB = hData._statsSet[hull.sectionsMax].Lwl / hData._statsSet[hull.sectionsMax].B;

            float minBMult = Mathf.Min(minLB / maxLB1, maxLB / maxLB2);
            if (minBMult > 0.95f)
                minBMult = 0.95f;

            float maxBMult = Math.Max(minLB / minLB1, maxLB / minLB2);
            if (maxBMult < 1.05f)
                maxBMult = 1.05f;

            hull.beamMin = (minBMult - 1f) * 100f;
            hull.beamMax = (maxBMult - 1f) * 100f;

            float BT = hData._statsSet[hull.sectionsMin].B / hData._statsSet[hull.sectionsMin].T;
            hull.draughtMin = (Math.Min(0.95f, BT / 4.25f) - 1f) * 100f;
            hull.draughtMax = (Math.Max(1.05f, BT / 2f) - 1f) * 100f;
        }

        private static System.Collections.IEnumerator PartSteps()
        {
            var gameData = G.GameData;
            //List<PartData> hulls = new List<PartData>();

            // Pass 4: Compute hull scale ratios
            //Dictionary<PartData, float> hullToScaleRatio = new Dictionary<PartData, float>();
            int num = 1;
#if LOGHULLSCALES
            Melon<UADRealismMod>.Logger.Msg("time,order,name,scaleRatio,origB,origBeamMin,newB,Secs,L/B,Cp,CpDesired,Year,Kn");
#else
            // to suppress a warning
            ++num;
            --num;
#endif
//            foreach (var kvp in gameData.parts)
//            {
//                var hull = kvp.Value;
//                if (!hull.isHull)
//                    continue;

//                hulls.Add(hull);

//                var hData = GetData(hull);
//                if (hData == null)
//                {
//                    Melon<UADRealismMod>.Logger.BigError($"Hull {hull.name} has no hull data!");
//                    continue;
//                }

//                float sMax = hull.speedLimiter;
//                if (sMax > 33f && (hull.shipType.name != "dd" && hull.shipType.name != "tb"))
//                    sMax = 33f;

//                float year = Database.GetYear(hull);
//                if (year < 0f)
//                    year = 1915f;

//                const float bDivT = DefaultBdivT * 0.867f;
//                float lDivB = GetDesiredLdivB(hull.tonnageMin * TonnesToCubicMetersWater, hData._desiredCb, bDivT, sMax * KnotsToMS, hull.shipType.name, year);
//                int secs = GetDesiredSections(hData, hull.sectionsMin, hull.sectionsMax, hull.tonnageMin, sMax * KnotsToMS, lDivB, bDivT, out var bmPct, out var drPct, out var desiredCp);
//                var stats = GetScaledStats(hData, hull.tonnageMin, bmPct, drPct, secs);
//                if (secs < 0 || secs > hData._sectionsMax)
//                {
//                    Melon<UADRealismMod>.Logger.BigError($"Hull {hull.name} calculated an out of bounds section count {secs}! Max {hData._sectionsMax}! ({sMax:F0}Kn, L/B {lDivB:F2}, B/T {bDivT:F2}");
//                    continue;
//                }
//                var origStats = hData._statsSet[secs];
//                var origData = _OriginalHullData[hull.name];
//                float origB = (origStats.B * (1f + origData._beamMin * 0.01f)) * origData._scale;
//                float newB = stats.B;
//                float scaleRatio = newB / origB;
//                hullToScaleRatio[hull] = scaleRatio;
//#if LOGHULLSCALES
//                Melon<UADRealismMod>.Logger.Msg($",{num++},{hull.name},{scaleRatio:F3},{origB:F2},{origData._beamMin:F0},{newB:F2},{secs},{lDivB:F2},{origStats.Cp:F3},{desiredCp:F3},{year:F0},{sMax:F1}");
//#endif
//            }

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

                //float minScaleRatio = float.MaxValue;
                //string minHullName = "(none)";
                //var hulls = Database.GetHullNamesForPart(data);
                //if (hulls != null)
                //{
                //    foreach (var hullName in hulls)
                //    {
                //        var hull = gameData.parts[hullName];

                //        // This should never happen -- we precompute them all
                //        if (!hullToScaleRatio.TryGetValue(hull, out var scaleRatio))
                //            continue;

                //        if (scaleRatio < minScaleRatio)
                //        {
                //            minHullName = hull.name;
                //            minScaleRatio = scaleRatio;
                //        }
                //    }
                //}
                float minScaleRatio = torpedoScale;
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

            yield return null;

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
                    Melon<UADRealismMod>.Logger.BigError($"PartModel {(string.IsNullOrEmpty(kvp.key) ? "NULL/EMPTY" : kvp.key)} is null!");
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
        }

        private static bool LoadData(int totalHulls)
        {
            string basePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "UADRealism");
            if (!Directory.Exists(basePath))
                return false;

            string pathHData = Path.Combine(basePath, "hulldata.csv");
            if (!File.Exists(pathHData))
                return false;

            var hDatas = File.ReadAllLines(pathHData);
            if (hDatas.Length < 6)
                return false;

            int curLine = 0;
            if (!int.TryParse(hDatas[curLine++].Split(',')[1], out var version) || version != _Version)
                return false;
            if (!int.TryParse(hDatas[curLine++].Split(',')[1], out var dataCount) || dataCount != _HullModelData.Count)
                return false;
            if (!int.TryParse(hDatas[curLine++].Split(',')[1], out var hullCount) || hullCount != totalHulls)
                return false;
            if (!int.TryParse(hDatas[curLine++].Split(',')[1], out var partCount) || partCount != G.GameData.parts.Count)
                return false;

            ++curLine; // header row
            int procDatas = 0;
            for (; curLine < hDatas.Length; ++curLine)
            {
                if (hDatas[curLine] == "$$END$$")
                    break;

                var line = hDatas[curLine].Split(',');
                int col = 0;
                if (!_HullModelData.TryGetValue(line[col++], out var hData))
                    return false;

                hData._statsSet = new HullStats[hData._sectionsMax + 1];
                hData._isDDHull = bool.Parse(line[col++]);
                hData._year = float.Parse(line[col++]);
                hData._desiredCb = float.Parse(line[col++]);
                hData._hullNumber = float.Parse(line[col++]);
                hData._yPos = float.Parse(line[col++]);
                hData._secMinWithFunnel = int.Parse(line[col++]);
                ++procDatas;
            }
            curLine += 2; // the endline + header row
            int procSecs = 0;
            for (; curLine < hDatas.Length; ++curLine)
            {
                var line = hDatas[curLine].Split(',');
                int col = 0;
                if (!_HullModelData.TryGetValue(line[col++], out var hData))
                    return false;

                int sec = int.Parse(line[col++]);
                var stats = new HullStats();
                stats.Lwl = float.Parse(line[col++]);
                stats.B = float.Parse(line[col++]);
                stats.T = float.Parse(line[col++]);
                stats.beamBulge = float.Parse(line[col++]);
                stats.bulgeDepth = float.Parse(line[col++]);
                stats.Vd = float.Parse(line[col++]);
                stats.Cb = float.Parse(line[col++]);
                stats.Cm = float.Parse(line[col++]);
                stats.Cwp = float.Parse(line[col++]);
                stats.Cp = float.Parse(line[col++]);
                stats.Cvp = float.Parse(line[col++]);
                stats.iE = float.Parse(line[col++]);
                stats.LrPct = float.Parse(line[col++]);
                stats.bowLength = float.Parse(line[col++]);
                stats.lcbPct = float.Parse(line[col++]);
                stats.Catr = float.Parse(line[col++]);
                stats.scaleFactor = float.Parse(line[col++]);
                stats.Cv = float.Parse(line[col++]);
                hData._statsSet[sec] = stats;
                ++procSecs;
            }

            Melon<UADRealismMod>.Logger.Msg($"Loaded hull data. Processed {procDatas} HullDatas and {procSecs} HullStats");
            return true;
        }

        private static void SaveData(int totalHulls)
        {
            string basePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "UADRealism");
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            string pathHData = Path.Combine(basePath, "hulldata.csv");

            List<string> lines = new List<string>();
            lines.Add($"version,{_Version}");
            lines.Add($"Total datas,{_HullModelData.Count}");
            lines.Add($"Total hulls,{totalHulls}");
            lines.Add($"Total parts,{G.GameData.parts.Count}");
            lines.Add("key,isDDHull,year,desiredCb,hullNumber,yPos,secMinWithFunnel");

            foreach (var kvp in _HullModelData)
            {
                var h = kvp.Value;
                lines.Add($"{kvp.Key},{h._isDDHull},{h._year},{h._desiredCb},{h._hullNumber},{h._yPos},{h._secMinWithFunnel}");
            }

            lines.Add("$$END$$");
            lines.Add("key,sec,Lwl,B,T,beamBulge,bulgeDepth,Vd,Cb,Cm,Cwp,Cp,Cvp,iE,LrPct,bowLength,lcbPct,Catr,scaleFactor,Cv");
            foreach (var kvp in _HullModelData)
            {
                var h = kvp.Value;
                for (int i = 0; i < h._statsSet.Length; ++i)
                {
                    var s = h._statsSet[i];
                    lines.Add($"{kvp.Key},{i},{s.Lwl},{s.B},{s.T},{s.beamBulge},{s.bulgeDepth},{s.Vd},{s.Cb},{s.Cm},{s.Cwp},{s.Cp},{s.Cvp},{s.iE},{s.LrPct},{s.bowLength},{s.lcbPct},{s.Catr},{s.scaleFactor},{s.Cv}");
                }
            }

            File.WriteAllLines(pathHData, lines);
        }

        private static float GunModelScale(float caliber)
        {
            float calInch = caliber * (1f / 25.4f);
            return (0.9f + 0.1f * Mathf.Cos((calInch < 11.87f ?
                (Mathf.Pow(Mathf.Max(0, calInch - 4f), 0.78f) + 4)
                : (Mathf.Pow(calInch - 11.87f, 1.25f) + 11.87f)) * Mathf.PI * 0.5f)) * 0.5f + 0.85f * 0.5f;
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

        public static HullStats GetScaledStats(Ship ship)
        {
            var hData = GetData(ship);
            if (hData == null)
                return new HullStats();

            return GetScaledStats(hData, ship.tonnage, ship.beam, ship.draught, ship.ModData().SectionsFromFineness());
        }

        public static HullStats GetScaledStats(HullData hData, float tonnage, float lengthToBeamPct, float beamToDraughtPct, int sections)
        {
            var newStats = hData._statsSet[sections];
            //Melon<UADRealismMod>.Logger.Msg($"Pre: {newStats.Lwl:F2}x{newStats.B:F2}x{newStats.T:F2}, {newStats.Vd}t.");

            float linearScale = GetHullScaleFactor(tonnage, newStats.Vd, lengthToBeamPct, beamToDraughtPct);
            float bmMult = 1f + lengthToBeamPct * 0.01f;
            float drMult = bmMult * (1f + beamToDraughtPct * 0.01f);
            newStats.B *= bmMult * linearScale;
            newStats.beamBulge *= bmMult * linearScale;
            newStats.bulgeDepth *= drMult * linearScale;
            newStats.Lwl *= linearScale;
            newStats.T *= drMult * linearScale;
            float volMult = drMult * bmMult * linearScale * linearScale * linearScale;
            newStats.Vd *= volMult; // Vd should be the same as ship.Tonnage() * TonnesToCubicMetersWater
            newStats.Cv *= volMult;
            newStats.bowLength *= linearScale;
            newStats.iE = Mathf.Atan2(newStats.B * 0.5f, newStats.bowLength) * Mathf.Rad2Deg;
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

        private static float EstimatedAmmoPct(string sType, float year)
            => sType switch
            {
                "ca" or "cl" => Util.Remap(year, 1890f, 1940f, 3.4f, 3.9f, true),
                "dd" or "tb" => Mathf.Lerp(1f, 5f, Mathf.Pow(Mathf.InverseLerp(1890f, 1940f, year), 1.5f)),
                _ => Util.Remap(year, 1890f, 1920f, 4f, 5f, true)
            };

        public static HullStats GetLoadingStats(Ship ship, HullLoadState loadState)
            => GetLoadingStats(GetScaledStats(ship), ship.tonnage, ship.opRange, Database.GetYear(ship.hull.data), ship.shipType.name, loadState);

        public static HullStats GetLoadingStats(HullStats oldStats, float tonnage, VesselEntity.OpRange opRange, float year, string sType, HullLoadState loadState)
        {
            var stats = oldStats;
            if (loadState == HullLoadState.Full)
                return stats;

            // Make assumptions about ammo since we need to have a fixed full-load displacement
            // in this game rather than starting from light ship.
            float ammoPct = EstimatedAmmoPct(sType, year);
            float opRangePct = OpRangeToPct(opRange, year, sType);

            float tonnageDelta = loadState switch
            {
                HullLoadState.LightShip => tonnage * (ammoPct + opRangePct),
                HullLoadState.Standard => tonnage * opRangePct,
                HullLoadState.Normal or _ => tonnage * (ammoPct + opRangePct) * (1f / 3f)
            } * 0.01f;
            float VdDelta = tonnageDelta * TonnesToCubicMetersWater;

            // assume straight sides between light ship and full load draughts
            float Tdelta = VdDelta / (stats.B * stats.Lwl * stats.Cwp);
            float newAm = stats.Cm * stats.B * stats.T - stats.B * Tdelta;

            stats.T -= Tdelta;
            stats.Vd -= VdDelta;
            stats.Cm = newAm / (stats.B * stats.T);
            // assume proportional change in Atr so no change in Catr
            stats.Cb = stats.Vd / (stats.Lwl * stats.B * stats.T);
            stats.Cp = stats.Cb / stats.Cm;
            stats.Cvp = stats.Cb / stats.Cwp;
            stats.Cv = 100f * stats.Vd / (stats.Lwl * stats.Lwl * stats.Lwl);

            return stats;
        }

        public static void SetAverageYearAndCb(HullData hData)
        {
            float year = 0f;
            float Cb = 0f;
            float CbAlt = 0f;
            float div = 0f;
            float hn = 0f;
            foreach (var hull in hData._hulls)
            {
                float multCb = 1f;
                // god I hate special cases.
                if (hull.model == "dreadnought_hull_b_var")
                    multCb = 0.93f;
                CbAlt += GetDesiredCb(hull.shipType.name, 1915f) * multCb;
                hn += GetHullNumber(hull.shipType.name);
                float hYear = Database.GetYear(hull);
                if (hYear < 0f)
                    continue;

                Cb += GetDesiredCb(hull.shipType.name, hYear) * multCb;
                year += hYear;
                ++div;
            }
            if (div > 0)
            {
                year /= div;
                Cb /= div;
            }
            if (year == 0f)
                year = 1890f;
            if (Cb == 0f)
                Cb = CbAlt / (float)hData._hulls.Count;

            hData._year = year;
            hData._desiredCb = Cb;
            hData._hullNumber = hn / (float)hData._hulls.Count;
        }

        // water at 15C, standard salinity
        public const double WaterDensity = 1026;
        public const float TonnesToCubicMetersWater = (float)(1d / (WaterDensity * 0.001d));
        public const double WaterKinematicViscosity = 1.1892e-6d;

        public const float KnotsToMS = 0.514444444f;

        public const float DefaultBdivT = 2.9f;

        public static float GetDesiredLdivB(Ship ship, float BdivT)
            => GetDesiredLdivB(ship.tonnage * TonnesToCubicMetersWater, GetData(ship)._desiredCb, BdivT, ship.speedMax, ship.shipType.name, Database.GetYear(ship.hull.data));

        public static float GetDesiredCb(string sType, float year)
        {
            float Cb = 0.65f;
            switch (sType)
            {
                case "tb":
                case "dd":
                    Cb = Util.Remap(year, 1903f, 1930f, 0.4f, 0.485f, true);
                    break;

                case "cl":
                    Cb = Util.Remap(year, 1900f, 1930f, 0.495f, 0.53f, true);
                    break;

                case "bc":
                    Cb = Util.Remap(year, 1905f, 1930f, 0.51f, 0.56f, true);
                    break;

                case "ca":
                    Cb = Util.Remap(year, 1900f, 1930f, 0.505f, 0.53f, true);
                    break;

                case "bb":
                    Cb = Util.Remap(year, 1910f, 1930f, 0.64f, 0.57f, true);
                    break;

                case "ic":
                    Cb = 0.66f;
                    break;

                case "tr":
                case "tr_amc":
                    Cb = 0.75f;
                    break;
            }
            return Cb;
        }

        public static float GetHullNumber(string sType)
        {
            switch (sType)
            {
                case "tb":
                case "dd":
                    return 1f;

                case "cl":
                    return 10f;

                case "ca":
                    return 100f;

                case "bc":
                    return 1000f;

                case "bb":
                    return 10000f;

                case "ic":
                    return -10f;
            }
            return -100f;
        }

        public static float GetDesiredCm(float Cb, float year)
        {
            float desiredCp = Mathf.Pow(Cb, 1.25f) * 0.8f + 0.2f;
            if(Cb < 1.1f * 0.55f)
                desiredCp += Mathf.Pow(1.1f - Cb * (1f / 0.55f), 1.5f) * 0.3f;

            float oldCm = Cb / desiredCp;
            float CbPow = Cb * Cb; // ^2
            CbPow *= CbPow; // ^4
            CbPow *= CbPow; // ^8
            float modCmLowCb = Mathf.Pow(oldCm, 1.5f) - 2f * CbPow;
            float modCmHighCb = 1f - Mathf.Pow(1f - oldCm, 1.75f);
            float modCm = Util.Remap(Cb, 0.5f, 0.56f, modCmLowCb, modCmHighCb, true);
            return Util.Remap(year, 1910f, 1930f, oldCm, modCm, true);
        }

        public static float RemapGameCm(float Cm, float avgCb, float year)
        {
            float desiredCm = GetDesiredCm(avgCb, year);
            float minCm = desiredCm * 0.97f;
            if (avgCb < 0.52f)
                minCm += minCm * (Math.Max(1890f, year) - 1890f) * 0.00025f * (0.52f - avgCb) * (1f / 0.03f);
            float maxCm = Math.Min(1f - Mathf.Pow(1 - desiredCm, 1.5f) - 0.0015f, desiredCm + 0.03f);
            return Util.Remap(Cm, 0.85f, 0.997f, minCm, maxCm, true);
        }

        public static float RemapGameCb(float Cb, float avgCb)
        {
            Cb = Util.Remap(Cb, avgCb * 0.6f - 0.1f, avgCb * 1.4f + 0.1f, avgCb * 0.93f, avgCb * Util.Remap(avgCb, 0.57f, 0.63f, 1.1f, 1.06f, true), true);
            if (avgCb < 0.49f)
                Cb *= Util.Remap(avgCb, 0.38f, 0.5f, Util.Remap(Cb, 0.33f, 0.55f, 1f, 0.8f, true), Util.Remap(Cb, 0.33f, 0.55f, 1.3f, 1f, true));
            return Cb;
        }

        public static float GetYearCbCmMult(float Cb, float year)
        {
            if (year > 1925f)
                return 1f;
            float maxMul = Mathf.Lerp(0.96f, 1f, (1f + Mathf.Cos(Mathf.InverseLerp(0.5f, 0.62f, Cb) * (2f * Mathf.PI))) * 0.5f);
            return Util.Remap(year, 1890f, 1925f, maxMul, 1f);
        }

        public static float GetDesiredLdivB(float Vd, float Cb, float BdivT, float speedMS, string sType, float year)
        {
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
            const float maxFn = 0.6f;
            const float midFn = 0.35f;
            const float minFn = 0.18f;
            if (Fn > midFn)
                tVal = 1f - Mathf.Pow((Fn - midFn) / (maxFn - midFn), 0.8f);
            else
                tVal = (Fn - minFn) / (midFn - minFn);
            
            float scaling = Mathf.Cos(Mathf.Clamp01(tVal) * Mathf.PI) * 0.5f + 0.5f;
            
            return 0.57f + scaling * (Fn > midFn ? 0.06f : 0.126f);
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

        public static float GetSHPRequired(Ship ship)
            => GetSHPRequired(GetScaledStats(ship), ship.speedMax);

        public static float GetHPRequired(Ship ship) => GetSHPRequired(ship) * GetEngineIHPMult(ship);

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

        public static float OpRangeToPctFuel(Ship.OpRange opRange)
        {
            return opRange switch
            {
                VesselEntity.OpRange.VeryLow => 8,
                VesselEntity.OpRange.Low => 13,
                VesselEntity.OpRange.Medium => 16,
                VesselEntity.OpRange.High => 20,
                _ => 32
            };
        }

        public static float OpRangeToPct(Ship.OpRange opRange, float year, string sType)
        {
            float multStores = 0.15f;
            float multRFW = Util.Remap(year, 1905f, 1935f, 0.1f, 0.08f, true) * (1f - (float)opRange * 0.025f);
            if (sType == "dd" || sType == "tb")
            {
                multStores *= 0.7f;
                multRFW *= 1.2f;
            }

            return OpRangeToPctFuel(opRange) * (1f + multStores + multRFW);
        }

        public static int GetRange(Ship ship) => GetRange(ship, ship.opRange);

        public static int GetRange(Ship ship, Ship.OpRange opRange)
        {
            float fuelTech = ship.TechR("fuel");
            float effTech = ship.TechA("fuel_eff");
            float statRange = ship.StatEffectPrivate("operating_range");
            float year = Database.GetYear(ship.hull.data);
            return GetRange(GetScaledStats(ship), GetEngineIHPMult(ship),
                // TODO: handle TB/DD differently?
                Mathf.Min(ship.speedMax, (year > 1920f ? Util.Remap(year, 1920f, 1930f, 12f, 15f, true) : Util.Remap(year, 1890f, 1910f, 10f, 12f, true)) * KnotsToMS),
                opRange, 1f / fuelTech * effTech * statRange);
        }
        
        public static int GetRange(HullStats stats, float engineMult, float speedMS, Ship.OpRange opRange, float fuelMult)
            => GetRange(stats, engineMult, speedMS, fuelMult * stats.Vd * (1f / TonnesToCubicMetersWater) * OpRangeToPctFuel(opRange) * 0.01f);

        public static int GetRange(HullStats stats, float engineMult, float speedMS, float effectiveTons)
        {
            float hpCruise = GetSHPRequired(stats, speedMS, false) * engineMult;
            const float fuelToKM = 524f;
            return Mathf.RoundToInt(effectiveTons / hpCruise * speedMS * fuelToKM);
        }

        public static System.Collections.IEnumerator CalculateHullData(bool checkMountsOnly)
        {
            _isRenderingHulls = true;
            foreach (var kvp in _HullModelData)
            {
                yield return ShipStatsCalculator.ProcessHullData(kvp.Value, checkMountsOnly);
            }
            _isRenderingHulls = false;
        }
    }    
}
