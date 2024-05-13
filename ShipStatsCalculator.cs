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

    public static class ShipStats
    {
        private static bool _isRenderingHulls = false;
        public static bool _IsRenderingHulls => _isRenderingHulls;
        
        private static bool _LoadingDone = false;
        public static bool LoadingDone => _LoadingDone;

        private static readonly Dictionary<string, HullData> _HullModelData = new Dictionary<string, HullData>();

        //public static readonly HashSet<string> _WrittenModels = new HashSet<string>();
        private static List<string> _DestoyerHullsPerModel = new List<string>();

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
            var gameData = G.GameData;
            Il2CppSystem.Diagnostics.Stopwatch sw = new Il2CppSystem.Diagnostics.Stopwatch();
            sw.Start();
            Melon<UADRealismMod>.Logger.Msg("Processing Hulls in GameData");

            // Pass 1: Figure out the min and max sections used for each model.
            // (and deal with beam/draught coefficients)
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                // Just apply beam delta to tonnage directly since we're going to use length/beam ratios
                data.tonnageMin *= Mathf.Pow(data.beamMin * 0.01f + 1f, data.beamCoef);
                data.tonnageMax *= Mathf.Pow(data.beamMax * 0.01f + 1f, data.beamCoef) * 1.2f;

                //data.sectionsMin = (int)(data.sectionsMin * 0.75f);
                data.sectionsMin = 0;
                data.beamMin = -50f;
                data.beamMax = 50f;
                data.draughtMin = -75f;
                data.draughtMax = 50f;

                data.beamCoef = 0f;

                // Draught is a linear scale to displacement at a given length/beam
                // TODO: Do we want to allow implicitly different block coefficients based on draught, as would
                // be true in reality?
                data.draughtCoef = 1f;


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
            }

            // Pass 2: Spawn and render the hull models, calculating stats
            yield return CalculateHullData();

            // Pass 3: Set starting scales for all hull parts
            Melon<UADRealismMod>.Logger.Msg("time,order,name,model,sections,tonnage,scaleMaxPct,newScale,Lwl,Beam,Bulge,Draught,L/B,B/T,year,Cb,Cm,Cp,Cwp,Cvp,Catr,Cv,bowLen,BL/B,iE,Lr/L,lcb/L,DD,Kn,SHP");
            int num = 1;
            foreach (var kvp in gameData.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                var hData = GetData(data);
                if (hData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find data for partdata {data.name} of model {data.model} with key {hData._key}");
                    continue;
                }
                var modelName = GetHullModelKey(data);
                for (int i = 0; i < hData._sectionsMax; ++i)
                {
                    if (i == 1 && i < hData._sectionsMin)
                        i = hData._sectionsMin;

                    float tVal = i > 0 ? 0.25f : 0;
                    float tng = Mathf.Lerp(data.tonnageMin, data.tonnageMax, tVal);
                    //GetSectionsAndBeamForLB(i == 1 ? data.beamMin : 0, data.sectionsMin, data.sectionsMax, data.beamMin, data.beamMax, hData, out var beamScale, out var sections);
                    //if (i == 0)
                    //    sections = 0;
                    //var stats = GetScaledStats(hData, tng, i == 0 ? data.beamMin : beamScale, 0, sections);
                    int sections = i;
                    var stats = GetScaledStats(hData, tng, 0, 0, sections);
                    float shp = GetSHPRequired(stats, data.speedLimiter * 0.51444444f, false);
                    Melon<UADRealismMod>.Logger.Msg($",{num},{data.name},{modelName},{sections},{tng:F0},{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{stats.scaleFactor:F3},{stats.Lwl:F2},{stats.B:F2},{(stats.beamBulge == stats.B ? 0 : stats.beamBulge):F2},{stats.T:F2},{(stats.Lwl / stats.B):F2},{(stats.B / stats.T):F2},{hData._year},{stats.Cb:F3},{stats.Cm:F3},{stats.Cp:F3},{stats.Cwp:F3},{stats.Cvp:F3},{stats.Catr:F3},{stats.Cv:F3},{stats.bowLength:F2},{(stats.bowLength / stats.B):F2},{stats.iE:F2},{stats.LrPct:F3},{stats.lcbPct:F4},{hData._isDDHull},{data.speedLimiter:F1},{shp:F0}");
                    ++num;
                }
            }

            foreach (var kvp in _HullModelData)
            {
                if (!kvp.Value._isDDHull)
                    continue;

                foreach (var kvpData in gameData.parts)
                {
                    if (kvpData.value.type != "hull")
                        continue;

                    if (GetHullModelKey(kvpData.value) == kvp.Key)
                    {
                        _DestoyerHullsPerModel.Add(kvpData.Key);
                    }
                }
                Debug.Log($"Model {kvp.Key} used by: {string.Join(", ", _DestoyerHullsPerModel)}");
                _DestoyerHullsPerModel.Clear();
            }

            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");

            _LoadingDone = true;
            G.ui.CompleteLoadingScreen();
        }

        public static void GetSectionsAndBeamForLB(float beam, int minSec, int maxSec, float minBeam, float maxBeam, HullData hData, out float beamScale, out int sections)
        {
            if (minSec == maxSec || hData == null)
            {
                beamScale = beam;
                sections = minSec;
                //Melon<UADRealismMod>.Logger.Msg($"For {hData._data.model}, sections always {minSec}");
                return;
            }

            float minSecLB = hData._statsSet[minSec].Lwl / hData._statsSet[minSec].B;
            float maxSecLB = hData._statsSet[maxSec].Lwl / hData._statsSet[maxSec].B;
            float minLB = minSecLB / (maxBeam * 0.01f + 1f);
            float maxLB = maxSecLB / (minBeam * 0.01f + 1f);

            float lbT = Mathf.InverseLerp(maxBeam, minBeam, beam);
            float lb = Mathf.Lerp(minLB, maxLB, lbT);
            if (lb <= minSecLB)
            {
                sections = minSec;
            }
            else if (lb >= maxSecLB)
            {
                sections = maxSec;
            }
            else
            {
                // Estimate the section to use
                sections = Mathf.RoundToInt(Util.Remap(lbT, minSecLB, maxSecLB, minSec, maxSec, true));
                float lwlDesired = hData._statsSet[sections].B * lb;

                // now find section
                sections = minSec;
                while (sections != maxSec && lwlDesired > hData._statsSet[sections].Lwl)
                    ++sections;
                float ratio = lwlDesired / hData._statsSet[sections].Lwl;

                // see if the bordering options are closer to 0 beam change
                if (ratio > 1f)
                {
                    if (sections < maxSec)
                    {
                        if (hData._statsSet[sections + 1].Lwl / lwlDesired < ratio)
                            ++sections;
                    }
                }
                else
                {
                    if (sections > minSec)
                    {
                        if (hData._statsSet[sections - 1].Lwl / lwlDesired > ratio)
                            --sections;
                    }
                }
            }

            float lbAtSec = hData._statsSet[sections].Lwl / hData._statsSet[sections].B;
            beamScale = (lbAtSec / lb - 1f) * 100f;

            //float lwl = hData._statsSet[sections].Lwl;
            //float bb = hData._statsSet[sections].B * (beamScale * 0.01f + 1f);
            //float calcLB = lwl / bb;
            //Melon<UADRealismMod>.Logger.Msg($"For {hData._data.model}, input {beam:F2} ({lbT:F2}). LB {minSecLB:F2}->{maxSecLB:F2}/{minLB:F2}->{maxLB:F2}.\nDesired {lb:F2}. B={beamScale:F2} S={sections} -> {calcLB:F2}");
        }

        public static HullData GetSectionsAndBeamForLB(float beam, PartData data, out float beamScale, out int sections)
        {
            var hData = GetData(data);
            if (hData == null)
            {
                beamScale = beam;
                sections = (data.sectionsMin + data.sectionsMax) / 2;
                return null;
            }

            GetSectionsAndBeamForLB(beam, data.sectionsMin, data.sectionsMax, data.beamMin, data.beamMax, hData, out beamScale, out sections);
            return hData;
        }

        public static HullStats GetScaledStats(Ship ship)
        {
            var hData = GetData(ship); //GetSectionsAndBeamForLB(ship.beam, ship.hull.data, out float beamScale, out int sections);
            if (hData == null)
                return new HullStats();

            //return GetScaledStats(hData, ship.tonnage, beamScale, ship.draught, sections);
            int sec = Mathf.RoundToInt(Mathf.Lerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, ship.CrewTrainingAmount * 0.01f));
            return GetScaledStats(hData, ship.tonnage, ship.beam, ship.draught, sec);
        }

        public static HullStats GetScaledStats(HullData hData, float tonnage, float beamScale, float draught, int sections)
        {
            var newStats = hData._statsSet[sections];
            //Melon<UADRealismMod>.Logger.Msg($"Pre: {newStats.Lwl:F2}x{newStats.B:F2}x{newStats.T:F2}, {newStats.Vd}t.");

            float drMult = draught * 0.01f + 1f;
            float bmMult = beamScale * 0.01f + 1f;
            float linearScale = GetHullScaleFactor(tonnage, newStats.Vd, beamScale);
            newStats.B *= bmMult * linearScale;
            newStats.beamBulge *= bmMult * linearScale;
            newStats.bulgeDepth *= drMult * linearScale;
            newStats.Lwl *= linearScale;
            newStats.T *= drMult * linearScale;
            float volMult = drMult * bmMult * linearScale * linearScale * linearScale; // should be the same as ship.Tonnage() * TonnesToCubicMetersWaters 
            newStats.Vd *= volMult;
            newStats.Cv *= volMult;
            newStats.bowLength *= linearScale;
            newStats.scaleFactor = linearScale;

            //Melon<UADRealismMod>.Logger.Msg($"Post with {linearScale:F3}x, B={bmMult:F3},T={drMult:F3}: {newStats.Lwl:F2}x{newStats.B:F2}x{newStats.T:F2}, {newStats.Vd}t.");

            return newStats;
        }

        public static float GetHullScaleFactor(Ship ship, float Vd, float beamScale)
        {
            return GetHullScaleFactor(ship.tonnage, Vd, beamScale);
        }
        public static float GetHullScaleFactor(float tonnage, float Vd, float beamScale)
        {
            float desiredVol = tonnage * TonnesToCubicMetersWater;
            return Mathf.Pow(desiredVol / (Vd * (beamScale * 0.01f + 1f)), 1f / 3f);
        }

        // water at 15C, standard salinity
        public const double WaterDensity = 1026;
        public const float TonnesToCubicMetersWater = (float)(1d / (WaterDensity * 0.001d));
        public const double WaterKinematicViscosity = 1.1892e-6d;

        public static float GetSHPRequired(Ship ship)
            => GetSHPRequired(GetScaledStats(ship), ship.speedMax);

        public static float GetSHPRequired(HullStats stats, float speedMS, bool log = true)
        {
            double L = stats.Lwl;
            double B = stats.B;
            double T = stats.T;
            double LB = L / B;
            double BT = B / T;
            double LT = L / T;
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
            double c12;
            if (TdivL > 0.05d)
                c12 = Math.Pow(TdivL, 0.228446d);
            else
                c12 = 48.2d * Math.Pow(TdivL - 0.02d, 2.078d) + 0.479948d;

            double lcb = stats.lcbPct * 100d;
            double LRperL = (1d - Cp + 0.06d * Cp * lcb / (4d * Cp - 1d));
            double iE = 1d + 89d * Math.Exp(-(Math.Pow(LB, 0.80856)) * Math.Pow(1 - Cw, 0.30484) * Math.Pow(1 - Cp - 0.0225 * lcb, 0.6367) * Math.Pow(LRperL * LB, 0.34574) * Math.Pow(100 * VolCoeff, 0.16302));
            // this is SpringSharp's "Sharpness coefficient"
            double coSharp = 0.4d * Math.Pow(B / L * 6f, 0.3333333432674408d) * Math.Sqrt(Cb / 0.52f);
            double sharpLerp = ModUtils.InverseLerp(0.35d, 0.45d, coSharp);
            double LrMax, LrMin, iEMax, iEMin;
            if (LRperL < stats.LrPct)
            {
                LrMax = stats.LrPct;
                LrMin = LRperL;
            }
            else
            {
                LrMax = LRperL;
                LrMin = stats.LrPct;
            }
            if (iE < stats.iE)
            {
                iEMax = stats.iE;
                iEMin = iE;
            }
            else
            {
                iEMax = iE;
                iEMin = stats.iE;
            }
            // lower is faster in both cases.
            LRperL = ModUtils.Lerp(LrMin, LrMax, sharpLerp);
            iE = ModUtils.Lerp(iEMin, iEMax, sharpLerp);

            // recalc 
            double formFactor = 0.93 + 0.487118 * c14 * Math.Pow(BdivL, 1.06806) * Math.Pow(TdivL, 0.46106) * Math.Pow(L / (LRperL * L), 0.121563) * Math.Pow(L3V, 0.36486) * Math.Pow(1 - Cp, -0.604247);
            double S = L * (B + 2d * T) * Math.Sqrt(Cm) * (0.453d + 0.4425d * Cb - 0.2862d * Cm - 0.003467d * BT + 0.3696d * Cw);// + 19.65 * A_bulb_at_stem / Cb
            double dynPres = 0.5d * WaterDensity * msVel * msVel;
            double Rf = Cf * formFactor * dynPres * S;

            double c3 = 0d; // 0.56 * Abt^1.5/(B*T*(0.31*sqrt(Abt)+Tp-hB)) where Tp is draught and hB is elevation of center of bulb above keel
            double c2 = Math.Exp(-1.89d * Math.Sqrt(c3));

            // Ra
            double Cb4 = Cb * Cb;
            Cb4 *= Cb4;
            double Ca = 0.006 * Math.Pow(L + 100, -0.16) - 0.00205 + 0.003 * Math.Sqrt(L / 7.5) * Cb4 * c2 * (0.004 - Math.Min(0.004, (TdivL)));
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
                c7 = 0.5d - 0.0625 * LB;
            else
                c7 = BdivL;

            double c1 = 2223105d * Math.Pow(c7, 3.78615d) * Math.Pow(TdivB, 1.07961d) * Math.Pow(90d - iE, -1.37565d);
            double c5 = 1d - 0.8 * Catr;
            double Fr = msVel / Math.Sqrt(9.80665d * L);
            double lambda = 1.446d * Cp - 0.03d * Math.Min(12d, LB);
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
            double m1 = 0.0140407 * LT - 1.75254 * V13 / L - 4.79323 * BdivL - c16;
            double c17 = 6919.3 * Math.Pow(Cm, -1.3346) * Math.Pow(VolCoeff, 2.00977) * Math.Pow(LB - 2, 1.40692);
            double m3 = -7.2035 * Math.Pow(BdivL, 0.326869) * Math.Pow(TdivB, 0.605375);
            double m4 = c15 * 0.4 * Math.Exp(-0.034 * Math.Pow(Fr, -3.29));

            const double FrAEnd = 0.35d;
            const double FrBStart = 0.50d;
            const double FrBCube = FrBStart * FrBStart * FrBStart;
            double FrA = Math.Min(Fr, FrAEnd);
            double FrB = Math.Max(Fr, FrBStart);
            double c5_mul_weight = c5 * Vd * WaterDensity * 9.80665d;
            double RwA = c1 * c2 * c5_mul_weight * Math.Exp(m1 * Math.Pow(FrA, -0.9d) + m4 * Math.Cos(lambda / (FrA * FrA)));
            double RwB = c17 * c2 * c5_mul_weight * Math.Exp(m3 * Math.Pow(FrB, -0.9) + m4 * Math.Cos(lambda / (FrB * FrB)));
            double Rw = ModUtils.Lerp(RwA, RwB, ModUtils.InverseLerp(FrAEnd, FrBStart, Fr));
            // Additional tweaks past Holtrop-Mennen
            double rwmultVolCoeff = (1d + 200d * (VolCoeff - 0.0025d)) * Math.Max(0.1d, ModUtils.InverseLerp(0.25d, 0.4d, Fr));
            double rwmultFr = Math.Pow(FrB, 3d) / FrBCube;
            double rwmultCm = 1d + (1.8d * Math.Max(0.75d, Cm) - 1) * ModUtils.InverseLerp(0.25d, 0.3d, Fr);
            Rw *= rwmultVolCoeff * rwmultFr * rwmultCm;

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

            if (log)
            {
                Debug.Log($"c1={c1:F3}, c2={c2:F3}, c3={c3:F3}, c5={c5:F3}, c6={c6:F3}, c7={c7:F3}, c12={c12:F3}, c14={c14:F3}, c15={c15:F3}, c16={c16:F3}, c17={c17:F3}, lcb={lcb:F1}, sharp={coSharp:F3}, sLerp={sharpLerp:F2}, LR={LRperL:F1}, iE={iE:F1}, "
                    + $"Re={Re:E3}, Cf={Cf:F5}, S={S:N0}, FF={formFactor:F3}, Rf={Rf:N0}, Ra={Ra:N0}, Fr={Fr:F2}, m1={m1:F3}, m3={m3:F3}, m4={m4:F3}, RwA={RwA:N0}, RwB={RwB:N0}, rwmVol={rwmultVolCoeff:F2}, "
                    + $"rwmFr={rwmultFr:F2}, rwmCm={rwmultCm:F2}, Rw={Rw:N0}, Rt={Rt:N0}, etaR={eta_R:F2}, Cv={Cv:F3}, w={w:F2}, t={t:F2}.");
            }

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

    [RegisterTypeInIl2Cpp]
    public class ShipStatsCalculator : MonoBehaviour
    {
        private static class RenderSetup
        {
            private struct ShadowInfo
            {
                public UnityEngine.Rendering.ShadowCastingMode _mode;
                public bool _receiveShadows;
            }

            private static readonly List<MonoBehaviour> _behaviours = new List<MonoBehaviour>();
            private static readonly List<Collider> _colliders = new List<Collider>();
            private static readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();
            private static readonly List<GameObject> _decor = new List<GameObject>();
            private static readonly Dictionary<Renderer, ShadowInfo> _renderers = new Dictionary<Renderer, ShadowInfo>();
            private static readonly List<Renderer> _renderersDisabled = new List<Renderer>();
            private static readonly List<GameObject> _decorStack = new List<GameObject>();
            private static Vector3 _pos;
            private static Quaternion _rot;

            private static float _ambientIntensity;
            private static Color _ambientLight;
            private static UnityEngine.Rendering.AmbientMode _ambientMode;
            private static UnityEngine.Rendering.SphericalHarmonicsL2 _ambientProbe;
            private static bool _fog;
            private static Color _fogColor;
            private static float _fogDensity;
            private static float _fogStartDistance;
            private static float _fogEndDistance;
            private static FogMode _fogMode;

            public static void SetForRender(GameObject hullModel)
            {
                _ambientIntensity = RenderSettings.ambientIntensity;
                _ambientLight = RenderSettings.ambientLight;
                _ambientMode = RenderSettings.ambientMode;
                _ambientProbe = RenderSettings.ambientProbe;
                _fog = RenderSettings.fog;
                _fogColor = RenderSettings.fogColor;
                _fogDensity = RenderSettings.fogDensity;
                _fogStartDistance = RenderSettings.fogStartDistance;
                _fogEndDistance = RenderSettings.fogEndDistance;
                _fogMode = RenderSettings.fogMode;

                //RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                //RenderSettings.ambientIntensity = 99f;
                //RenderSettings.ambientLight = new Color(99f, 0f, 0f);

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(100f, 0f, 0f);

                var okRenderers = Part.GetVisualRenderers(hullModel);

                StoreLayerRecursive(hullModel);
                SetLayerRecursive(hullModel, _CameraLayerInt);

                _pos = hullModel.transform.localPosition;
                _rot = hullModel.transform.localRotation;
                hullModel.transform.localPosition = Vector3.zero;
                hullModel.transform.localRotation = Quaternion.identity;

                // TODO: Could optimize this to just walk through all GOs
                // and then all Components on each, rather than a bunch of
                // garbage-producing calls that recheck all GOs and all comps.

                foreach (var mb in hullModel.GetComponentsInChildren<MonoBehaviour>())
                {
                    if (!mb.enabled)
                        continue;

                    _behaviours.Add(mb);
                    mb.enabled = false;
                }

                foreach (var coll in hullModel.GetComponentsInChildren<Collider>())
                {
                    if (!coll.enabled)
                        continue;

                    _colliders.Add(coll);
                    coll.enabled = false;
                }

                _decorStack.Add(hullModel);
                while (_decorStack.Count > 0)
                {
                    int max = _decorStack.Count - 1;
                    var go = _decorStack[max];
                    _decorStack.RemoveAt(max);
                    for (int i = 0; i < go.transform.GetChildCount(); ++i)
                    {
                        var child = go.transform.GetChild(i)?.gameObject;
                        if (child == null)
                            continue;

                        if (!child.activeSelf)
                            continue;

                        if (child.name.StartsWith("Decor")
                            || child.name.StartsWith("Mount")
                            || child.name == "Deck"
                            || child.name.StartsWith("DeckSize")
                            || child.name.StartsWith("DeckWall"))
                        {
                            _decor.Add(child);
                            child.active = false;
                            continue;
                        }

                        _decorStack.Add(child);
                    }
                }

                Renderer[] rs = hullModel.GetComponentsInChildren<Renderer>();

                foreach (var mr in rs)
                {
                    if (mr != null && !okRenderers.Contains(mr))
                    {
                        if (mr.enabled)
                        {
                            _renderersDisabled.Add(mr);
                            mr.enabled = false;
                        }
                        continue;
                    }
                    _renderers[mr] = new ShadowInfo() { _mode = mr.shadowCastingMode, _receiveShadows = mr.receiveShadows };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    // TODO: Change material -- it'd be great to render to depth.
                    // That would allow for much better detection of things.
                }
            }

            public static void Restore(GameObject hullModel)
            {
                foreach (var mb in _behaviours)
                {
                    mb.enabled = true;
                }
                _behaviours.Clear();

                foreach (var coll in _colliders)
                {
                    coll.enabled = true;
                }
                _colliders.Clear();

                foreach (var decor in _decor)
                {
                    decor.active = true;
                }
                _decor.Clear();

                foreach (var kvp in _renderers)
                {
                    kvp.Key.shadowCastingMode = kvp.Value._mode;
                    kvp.Key.receiveShadows = kvp.Value._receiveShadows;
                }
                _renderers.Clear();

                foreach (var r in _renderersDisabled)
                    r.enabled = true;
                _renderersDisabled.Clear();

                hullModel.transform.localPosition = _pos;
                hullModel.transform.localRotation = _rot;

                RestoreLayerRecursive(hullModel);
                _layers.Clear();

                RenderSettings.ambientIntensity = _ambientIntensity;
                RenderSettings.ambientLight = _ambientLight;
                RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientProbe = _ambientProbe;

                RenderSettings.fog = _fog;
                RenderSettings.fogColor = _fogColor;
                RenderSettings.fogDensity = _fogDensity;
                RenderSettings.fogStartDistance = _fogStartDistance;
                RenderSettings.fogEndDistance = _fogEndDistance;
                RenderSettings.fogMode = _fogMode;
            }

            private static void StoreLayerRecursive(GameObject obj)
            {
                for (int i = 0; i < obj.transform.childCount; ++i)
                    StoreLayerRecursive(obj.transform.GetChild(i).gameObject);

                _layers[obj] = obj.layer;
            }

            private static void SetLayerRecursive(GameObject obj, int newLayer)
            {
                obj.layer = newLayer;

                for (int i = 0; i < obj.transform.childCount; ++i)
                    SetLayerRecursive(obj.transform.GetChild(i).gameObject, newLayer);
            }

            private static void RestoreLayerRecursive(GameObject obj)
            {
                if (_layers.TryGetValue(obj, out int layer))
                    obj.layer = layer;

                for (int i = 0; i < obj.transform.childCount; ++i)
                    RestoreLayerRecursive(obj.transform.GetChild(i).gameObject);
            }
        }

        private static ShipStatsCalculator _Instance = null;
        public static ShipStatsCalculator Instance
        {
            get
            {
                if (_Instance == null)
                {
                    var gameObject = new GameObject("StatsCalculator");
                    gameObject.AddComponent<ShipStatsCalculator>();
                }
                return _Instance;
            }
        }

        // To avoid allocs. Compared to instantiation though it's probably not noticeable.
        private static readonly List<string> _foundVars = new List<string>();
        private static readonly List<string> _matchedVars = new List<string>();
        private static readonly List<Bounds> _sectionBounds = new List<Bounds>();

        private static readonly int _CameraLayerInt = LayerMask.NameToLayer("VisibilityFog_unused");
        private const int _ResSideFront = 512;

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Texture2D _texture;
        private short[] _beamPixelCounts = new short[_ResSideFront];
        private float[] _displacementsPerPx = new float[_ResSideFront];

        public ShipStatsCalculator(IntPtr ptr) : base(ptr) { }

        public void Awake()
        {
            if (_Instance != null)
                Destroy(_Instance);

            _renderTexture = new RenderTexture(_ResSideFront, _ResSideFront, 16, RenderTextureFormat.ARGB32);
            _texture = new Texture2D(_ResSideFront, _ResSideFront, TextureFormat.ARGB32, false);

            _camera = gameObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 1f;
            _camera.aspect = 1f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 500f;
            _camera.enabled = false;
            _camera.cullingMask = 1 << _CameraLayerInt;
            _camera.targetTexture = _renderTexture;

            _Instance = this;
        }

        public void OnDestroy()
        {
            _renderTexture.Release();
        }

        enum ShipViewDir
        {
            Front = 0,
            Bottom,
            Side,

            MAX
        }

        private static bool IsFogged(Color32 px)
        {
            return px.r > 200 && px.g < 100 && px.b < 100;
        }

        public HullStats GetStats(GameObject obj, Bounds bounds, bool isDDHull)
        {
            var stats = new HullStats();

            RenderSetup.SetForRender(obj);

            //var p = obj.GetParent().GetComponent<Part>();
            //if (p != null)
            //{
            //    //if (p.data.model == "atlanta_hull_b_var")

            //    //if (!Patch_GameData._WrittenModels.Contains(ShipStats.GetHullModelKey(p.data) + "_"))
                
            //}

            float Awp = 0f;
            float Vd = 0f;
            int maxBeamPx = 0;
            int midFwd = _ResSideFront;
            int midAft = 0;
            int bowRow = _ResSideFront;
            int sternRow = 0;
            bool hasBulge = false;
            int sternCol = 0;
            int transomCol = -1;
            int firstPropCol = -1;
            int firstEndPropCol = -1;
            var part = obj.GetParent().GetComponent<Part>();
            int sec = part == null || part.middles == null ? 0 : part.middles.Count;

            float draught = -bounds.min.y;
            stats.T = draught;
            _camera.targetTexture = _renderTexture;
            RenderTexture.active = _renderTexture;
            for (ShipViewDir view = (ShipViewDir)0; view < ShipViewDir.MAX; ++view)
            {
                float size;
                float depth;
                Vector3 dir;

                switch (view)
                {
                    default:
                    case ShipViewDir.Side:
                        size = Mathf.Max(draught, bounds.size.z);
                        depth = bounds.size.x * 0.5f;
                        dir = Vector3.left;
                        _camera.transform.position = new Vector3(bounds.max.x + 1f, -size * 0.5f, bounds.center.z);
                        break;

                    case ShipViewDir.Front:
                        size = Mathf.Max(draught, bounds.size.x);
                        //depth = bounds.size.z - GetSectionBounds(part.stern, null).size.z + 5f;
                        depth = GetSectionBounds(part.bow, null).size.z;
                        dir = Vector3.back;
                        _camera.transform.position = new Vector3(bounds.center.x, -size * 0.5f, bounds.max.z + 1f);
                        break;

                    case ShipViewDir.Bottom:
                        size = Mathf.Max(bounds.size.x, bounds.size.z);
                        dir = Vector3.up;
                        depth = draught;
                        _camera.transform.position = new Vector3(bounds.center.x, bounds.min.y - 1f, bounds.center.z);
                        break;
                }
                _camera.transform.rotation = Quaternion.LookRotation(dir);
                _camera.orthographicSize = size * 0.5f;
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = depth + 1f;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = depth;

                _camera.Render();
                _texture.ReadPixels(new Rect(0, 0, _ResSideFront, _ResSideFront), 0, 0);

                // It would be very nice to not alloc a full mb here. But
                // il2cpp/harmony doesn't support GetRawTextureData<>,
                // only GetRawTextureData (and that copies as well).
                Color32[] pixels = _texture.GetPixels32();
                int waterlinePixels = 0;
                int row;

                float sizePerPixel = size / _ResSideFront;
                switch (view)
                {
                    case ShipViewDir.Front:
                        int firstRow = _ResSideFront - 1;
                        for (row = firstRow; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                ++waterlinePixels;
                            }
                            if (waterlinePixels > 0)
                            {
                                firstRow = row;
                                break;
                            }
                        }
                        stats.B = waterlinePixels * sizePerPixel;

                        int totalMidshipsPixels = waterlinePixels;

                        int maxWidthRow = row;
                        int beamBulgePixels = waterlinePixels;
                        const int midPoint = _ResSideFront / 2 + 1;
                        int lastRow = row;
                        for (row = firstRow - 1; row >= 0; --row)
                        {
                            int pixelsInCurrentRow = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                // Check if we're below hull bottom
                                if (col > midPoint && pixelsInCurrentRow == 0)
                                    break;

                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                ++pixelsInCurrentRow;
                            }

                            if (pixelsInCurrentRow > 0)
                            {
                                lastRow = row;
                                totalMidshipsPixels += pixelsInCurrentRow;

                                // We found something wider than the waterline beam
                                if (pixelsInCurrentRow > beamBulgePixels)
                                {
                                    beamBulgePixels = pixelsInCurrentRow;
                                    maxWidthRow = row;
                                }
                            }
                        }
                        stats.beamBulge = beamBulgePixels * sizePerPixel;
                        stats.bulgeDepth = (_ResSideFront - (maxWidthRow + 1)) * sizePerPixel;
                        //float Tcalc = (firstRow - lastRow + 1) * sizePerPixel;
                        float Am = totalMidshipsPixels * sizePerPixel * sizePerPixel;

                        // Midship coefficient
                        // Using wl beam, not bulge beam
                        if (waterlinePixels == 0)
                            stats.Cm = 0f;
                        else
                            stats.Cm = Am / (stats.beamBulge * draught);

                        if (beamBulgePixels != waterlinePixels)
                        {
                            hasBulge = true;

                            // Correct Cm - take average of beam and bulge cm
                            stats.Cm = Am / (stats.B * draught) * 0.67f + stats.Cm * 0.33f;
                        }

                        break;

                    case ShipViewDir.Bottom:
                        // This will detect the midpoints _counting the bulge_
                        // but that should be ok.
                        // We'll also record the number of beam pixels at each row.
                        int lastNonPropCol = -1;
                        
                        for (row = 0; row < _ResSideFront; ++row)
                        {
                            short numPx = 0;
                            int sideCol = _ResSideFront - row - 1;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                {
                                    // Account for the case where there's prop shafts
                                    // outside the hull. Discard pixels if on left side and
                                    // we hit a gap; stop when we hit a gap if on right side.
                                    if (col < _ResSideFront / 2 - 1)
                                    {
                                        if (numPx > 0)
                                            numPx = 0;

                                        continue;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }

                                ++numPx;
                            }

                            // Prop detection
                            if (row > _ResSideFront * 3 / 4)
                            {
                                int lastSideCol = sideCol + 1;
                                short lastPx = _beamPixelCounts[lastSideCol];
                                if (numPx > lastPx)
                                {
                                    if (lastNonPropCol < 0)
                                    {
                                        lastNonPropCol = lastSideCol;
                                        if (firstPropCol < 0)
                                            firstPropCol = sideCol;
                                    }
                                }

                                if (lastNonPropCol > 0)
                                {
                                    // attempt to continue angle - this will reverse the rate
                                    // of change of angle, but it's better than continuing straight back
                                    // and local curvature will be low for the few pixels of propeller
                                    int delta = lastNonPropCol - sideCol;
                                    short deltaPx = _beamPixelCounts[lastNonPropCol + delta];
                                    short lastPropPx = _beamPixelCounts[lastNonPropCol];
                                    short predictedPx = (short)(lastPropPx - (deltaPx - lastPropPx));

                                    if (delta > 6 || (numPx < predictedPx + 2 && numPx <= lastPropPx))
                                    {
                                        lastNonPropCol = -1;
                                        if (firstEndPropCol < 0)
                                            firstEndPropCol = sideCol;
                                    }
                                    else
                                    {
                                        numPx = predictedPx;
                                        // handle output
                                        //int pxStart = (_ResSideFront - numPx) / 2;
                                        //int pxEnd = pxStart + numPx - 1;
                                        //for (int i = 0; i < _ResSideFront; ++i)
                                        //{
                                        //    pixels[row * _ResSideFront + i].a = (i < pxStart || i > pxEnd) ? (byte)0 : (byte)255;
                                        //}
                                    }
                                }
                            }

                            Awp += numPx;
                            _beamPixelCounts[sideCol] = numPx;

                            if (numPx > 0)
                            {
                                sternRow = row;

                                if (row < bowRow)
                                    bowRow = row;

                                if (numPx > maxBeamPx)
                                {
                                    midFwd = row;
                                    maxBeamPx = numPx;
                                }
                                // account for noise in the hull side:
                                // require 3+ pixels inwards to end the midsection
                                if (numPx > maxBeamPx - 3)
                                    midAft = 0;
                                else if (midAft == 0)
                                    midAft = row;
                            }
                        }
                        Awp *= sizePerPixel * sizePerPixel;
                        stats.bowLength = (midFwd - bowRow + 1) * sizePerPixel;
                        stats.iE = Mathf.Atan2(stats.B * 0.5f, stats.bowLength) * Mathf.Rad2Deg;
                        stats.LrPct = (sternRow - midAft + 1) * sizePerPixel; // will divide by Lwl once that's found.

                        // Try to scale Cm based on fineness of hull which we'll guess from
                        // the bow length vs the beam.
                        // This is because the Cm values ingame are kinda bad for the hulls, rather too low.
                        // For destroyers (or TBs), though, they are broadly correct.
                        if (!isDDHull)
                        {
                            stats.Cm = Mathf.Pow(stats.Cm, Util.Remap(stats.bowLength / stats.B, 1.5f, 2.5f, 0.25f, 0.5f, true));
                        }

                        // Detect transom
                        float minTransomWidth = stats.beamBulge * 0.25f; // otherwise can detect small cruiser sterns
                        sternCol = _ResSideFront - sternRow - 1;

                        for (int col = -1; col < _ResSideFront * 1 / 5; ++col)
                        {
                            int curCol = sternCol + col;
                            int curPx = curCol < 0 ? 0 : _beamPixelCounts[curCol];
                            const float angleDelta = 45f;
                            const int yDelta = 3;
                            int nextCol = curCol + yDelta;
                            int nextPx = _beamPixelCounts[nextCol];
                            int farCol = nextCol + yDelta;
                            float angle1 = Mathf.Atan2(yDelta * 2, nextPx - curPx);
                            float angle2 = Mathf.Atan2(yDelta * 2, _beamPixelCounts[farCol] - nextPx);
                            if (angle2 - angle1 > Mathf.Deg2Rad * angleDelta)
                            {
                                if (transomCol < nextCol && nextPx * sizePerPixel > minTransomWidth)
                                {
                                    // We have to skip props here, they can stick out and represent
                                    // sharp changes in beam
                                    bool isProp = false;
                                    for (int i = nextCol + 1; i < nextCol + 5; ++i)
                                        if (_beamPixelCounts[i] < nextPx)
                                            isProp = true;

                                    if (!isProp)
                                        transomCol = nextCol;
                                }
                            }
                            // we've passed the transom. But wait until a few pixels down in case
                            // we got noise early.
                            else if (col > 4)
                            {
                                break;
                            }
                        }

                        //if (transomCol >= 0)
                        //{
                        //if (transomCol >= 0)
                        //{
                        //Debug.Log($"{(part.data == null ? obj.name : ShipStats.GetHullModelKey(part.data))} ({sec}): Transom at col {transomCol}: {(_beamPixelCounts[transomCol] / (float)maxBeamPx):P2}");
                        //}
                        //Patch_GameData._WrittenModels.Add(ShipStats.GetHullModelKey(p.data) + "_");
                        //string hierarchy = $"{(part.data == null ? obj.name : ShipStats.GetHullModelKey(part.data))} ({sec}): {ModUtils.DumpHierarchy(obj)}";
                        //Debug.Log("---------\n" + hierarchy);
                        //}
                        //{
                        //    string filePath = "C:\\temp\\112\\uad\\nar\\5.0\\shots\\";
                        //    int bbPx = Mathf.RoundToInt(stats.beamBulge / sizePerPixel);
                        //    filePath += $"{ShipStats.GetHullModelKey(part.data).Replace(";", "+")}_{sec}_{view.ToString()}_b{maxBeamPx}_f{bbPx}.png";

                        //    var bytes = ImageConversion.EncodeToPNG(_texture);
                        //    Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
                        //}

                        break;

                    default:
                    case ShipViewDir.Side:
                        // The ones we found in Bottom can have
                        // below-waterline aft extensions, so we
                        // need to re-find these.
                        int bowCol = 0;
                        sternCol = _ResSideFront;
                        for (row = _ResSideFront - 1; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                ++waterlinePixels;
                                bowCol = col;
                                if (col < sternCol)
                                    sternCol = col;
                            }
                            if (waterlinePixels > 0)
                                break;
                        }
                        stats.Lwl = waterlinePixels * sizePerPixel;
                        stats.LrPct /= stats.Lwl; // we now know our LwL so we can divide by it.

                        // Now find displacement
                        float volFactor = sizePerPixel * sizePerPixel * sizePerPixel;
                        float bowCm = Mathf.Min(0.55f, stats.Cm);
                        int startCol = -1;
                        int lastCol = -1;
                        midFwd = _ResSideFront - midFwd - 1;
                        midAft = _ResSideFront - midAft - 1;
                        int maxDepthPx = 0;

                        int bulgeFirst = Math.Min(midAft, Mathf.RoundToInt(waterlinePixels * 0.333f));
                        int bulgeLast = Math.Max(midFwd, Mathf.RoundToInt(waterlinePixels * 0.667f));
                        float nonBulgeMult = hasBulge ? stats.B / stats.beamBulge : 1f;
                        int lastDepth = 0;
                        // go bow->stern
                        for (int col = _ResSideFront - 1; col >= 0; --col)
                        {
                            bool hasPx = false;
                            float bowT = bowCol == midFwd ? (col == bowCol ? 0f : 1f) : ((float)(bowCol - col) / (bowCol - midFwd));
                            float dispPerPx = _beamPixelCounts[col] * volFactor * Mathf.Lerp(bowCm, stats.Cm, bowT * bowT);
                            // Assume bulge is along 2/3 of the length, so correct beam in re: Cm
                            // (don't worry about the bulge slowly increasing or decreasing in width)
                            if (hasBulge && col >= bulgeFirst && col <= bulgeLast)
                                dispPerPx *= nonBulgeMult;

                            bool firstRowFog = IsFogged(pixels[(_ResSideFront - 1) * _ResSideFront + col]);
                            int numFog = 0;
                            int startRow = -1;
                            // Note: we don't have to cover the case where we don't hit an empty pixel
                            // before the end of the column, because we know the ship is going to be longer
                            // (wider in columns) than it is deep (tall in rows), and this is a square ortho
                            // projection.
                            for (int r = _ResSideFront - 1; r >= 0; --r)
                            {
                                var px = pixels[r * _ResSideFront + col];
                                if (hasPx)
                                {
                                    // Stop when we hit a gap (no holes in the hull! Otherwise we'll count
                                    // prop shafts). If we're aft of midships, we also keep track of the last depth
                                    // and stop if we go beyond it. This is to try not to count rudders. Note we can't
                                    // just use pure midships, since the max depth might be slightly aft of that.
                                    bool depthLimit = col < _ResSideFront * 2 / 3 && r < lastDepth;

                                    bool isFogged = IsFogged(px);
                                    // If we're going back deeper and we're aft, or it's an empty pixel (a hole, or below the hull),
                                    // _or_ we're aft and it's some kind of narrow rudder / keel thing (which we detect via fog), stop here.
                                    // Note: we need to make sure this whole column isn't fogged, and we need to make sure this isn't the
                                    // last row of a normal column (where the row after this will be empty, which means the fog just means
                                    // that the beam is zeroing here).
                                    // TODO: Do the same thing for rudders we do for props in bottom view: if we go below last depth,
                                    // instead of just continuing at last depth, continue the _slope_.
                                    //if (depthLimit || px.a == 0 || (!firstRowRed && col < _ResSideFront / 2 && IsFogged(px) && (r == 0 || pixels[(r - 1) * _ResSideFront + col].a > 0)))
                                    if (depthLimit || px.a == 0 || (!firstRowFog && col < _ResSideFront / 2 && numFog > 0))
                                    {
                                        Vd += (startRow - r) * dispPerPx;
                                        if (!depthLimit)
                                            lastDepth = r + 1;

                                        // handle output
                                        //for (int r2 = r; r2 >= 0; --r2)
                                        //    pixels[r2 * _ResSideFront + col].a = 0;

                                        break;
                                    }
                                    if (isFogged)
                                        ++numFog;
                                }
                                // we don't care about fog in this case
                                if (px.a == 0)
                                    continue;

                                hasPx = true;
                                if (startRow < 0)
                                    startRow = r;
                                int curDepth = _ResSideFront - r;
                                if (curDepth > maxDepthPx)
                                    maxDepthPx = curDepth;
                            }

                            if (hasPx)
                            {
                                if (startCol < 0)
                                    startCol = col;

                                lastCol = col;
                            }
                            _displacementsPerPx[col] = Vd;

                            // Handle transom
                            if (col == transomCol)
                            {
                                stats.Catr = hasPx ? (_beamPixelCounts[col] * (_ResSideFront - lastDepth)) / (float)(maxBeamPx * maxDepthPx) : 0;
                                //Debug.Log($"Transom col {transomCol}, {_beamPixelCounts[col]}x{(_ResSideFront - lastDepth)} / midships {maxBeamPx}x{maxDepthPx} = {stats.Catr:F3}");
                            }
                        }

                        float halfDisp = Vd * 0.5f;
                        stats.lcbPct = stats.Lwl * 0.5f;
                        for (int col = startCol; col >= lastCol; --col)
                        {
                            float dispAtColEnd = _displacementsPerPx[col];
                            if (dispAtColEnd > halfDisp)
                            {
                                float sternToCB = col - lastCol;
                                // assume linear change in transverse area, which will be wrong if draught is changing too
                                if (col == startCol)
                                    sternToCB += halfDisp / dispAtColEnd;
                                else
                                    sternToCB += Mathf.InverseLerp(_displacementsPerPx[col + 1], dispAtColEnd, halfDisp);

                                stats.lcbPct = (sternToCB * sizePerPixel) / stats.Lwl - 0.5f;
                                break;
                            }
                        }
                        break;
                }

                //if(view != ShipViewDir.Front || sec == 0)
                //{
                //    string filePath = "C:\\temp\\112\\uad\\nar\\5.0\\shots\\";
                //    filePath += $"{ShipStats.GetHullModelKey(part.data).Replace(";", "+")}_{sec}_{view.ToString()}";
                //    if (view == ShipViewDir.Bottom && firstPropCol > 0)
                //        filePath += $"ps{firstPropCol}_pe{firstEndPropCol}";

                //    var bytes = ImageConversion.EncodeToPNG(_texture);
                //    Il2CppSystem.IO.File.WriteAllBytes(filePath + ".png", bytes);

                //    if (view == ShipViewDir.Side)
                //    {
                //        _texture.SetPixels32(pixels);
                //        bytes = ImageConversion.EncodeToPNG(_texture);
                //        Il2CppSystem.IO.File.WriteAllBytes(filePath + "_out.png", bytes);
                //    }
                //}
            }
            RenderTexture.active = null;

            // Corrections based on inexact detection
            // First, apply the regardless-of-bulge corrections.
            Awp *= 0.98f;
            Vd *= Util.Remap(stats.Lwl / stats.B, 3f, 10f, 0.95f, 0.97f, true); // this is bad because of rudder/props, and overestimating stern fullness
            
            // Bulge case:
            if (hasBulge)
            {
                float beamRatio = stats.B / stats.beamBulge;
                Vd *= Util.Remap(beamRatio, 0.85f, 1f, 0.95f, 1f, true); // we assume there's only a bit of missing volume,
                // considering that fore and aft of the bulge the hull does extend all the way out, and even along the bulge there's
                // not that much missing.

                // Assume bulge is along 2/3 of the length, so correct beam in re: Cm
                // (don't worry about the bulge slowly increasing or decreasing in width)
                Awp *= 0.333f + 0.667f * beamRatio;
            }

            // Set the remaining stats
            stats.Vd = Vd;
            // The rest of these we'll recalculate after all section-counts are rendered.
            stats.Cb = Vd / (stats.Lwl * stats.B * draught);
            stats.Cwp = Awp / (stats.Lwl * stats.B);
            stats.Cp = stats.Cb / stats.Cm;
            stats.Cvp = stats.Cb / stats.Cwp;
            stats.scaleFactor = 1f;
            stats.Cv = 100f * Vd / (stats.Lwl * stats.Lwl * stats.Lwl);

            RenderSetup.Restore(obj);

            return stats;
        }

        public static System.Collections.IEnumerator ProcessHullData(HullData hData)
        {
            var data = hData._hulls[0];
            var part = Instance.SpawnPart(data);
            //part.gameObject.AddComponent<LogMB>();
            part.enabled = false;

            int count = hData._sectionsMax + 1;
            hData._statsSet = new HullStats[count];

            // do 0 sections in addition to all others.
            for (int secCount = 0 /*hData._sectionsMin*/; secCount < count; ++secCount)
            {
                if (secCount == 1 && secCount < hData._sectionsMin)
                    secCount = hData._sectionsMin;

                // Ship.RefreshHull (only the bits we need)
                Instance.CreateMiddles(part, secCount);
                // It's annoying to rerun this every time, but it's not exactly expensive.
                Instance.ApplyVariations(part);

                Instance.PositionSections(part);

                // Calc the stats for this layout. Note that scaling dimensions will only
                // linearly change stats, it won't require a recalc.
                var shipBounds = Instance.GetShipBounds(part.model.gameObject);
                var stats = Instance.GetStats(part.model.gameObject, shipBounds, hData._isDDHull);

                if (stats.Vd == 0f)
                    stats.Vd = 1f;

                float powCm = 1f;
                float powCwp = 1f;
                float powCb = 1f;
                if (hData._isDDHull)
                {
                    float year = 0f;
                    float divisor = 0f;
                    foreach (var hull in hData._hulls)
                    {
                        bool found = false;
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
                                        year += tech.value.year;
                                        found = true;
                                        break;
                                    }
                                }
                                if (found)
                                    break;
                            }
                            if (found)
                                break;
                        }
                        if (found)
                            ++divisor;
                    }
                    if (divisor > 0)
                        year /= divisor;
                    else
                        year = 1915f;

                    powCm = Util.Remap(year, 1900, 1925, 1f, 0.7f, true);
                    powCb = Util.Remap(year, 1900, 1925, 1.5f, 1f, true);
                    powCwp = Util.Remap(year, 1890, 1930, 2f, 1.3f, true);

                    hData._year = year;
                }

                // Recompute. For zero-section case, check if destroyer and fudge numbers.
                // If not, reset based on sec0 which has best resolution for beam/draught/Cm/etc
                if (secCount == 0)
                {
                    if (hData._isDDHull)
                    {
                        stats.Cm = Mathf.Pow(stats.Cm, powCm);
                    }
                }
                else
                {
                    stats.Cb = stats.Vd / (stats.Lwl * hData._statsSet[0].B * hData._statsSet[0].T);
                    stats.Cm = hData._statsSet[0].Cm;
                    stats.Cwp *= stats.B / hData._statsSet[0].B;
                    stats.Cp = stats.Cb / stats.Cm;
                    stats.Cvp = stats.Cb / stats.Cwp;
                    stats.Catr = hData._statsSet[0].Catr;
                    stats.B = hData._statsSet[0].B;
                    stats.T = hData._statsSet[0].T;
                    stats.beamBulge = hData._statsSet[0].beamBulge;
                    stats.bulgeDepth = hData._statsSet[0].bulgeDepth;
                    stats.LrPct = hData._statsSet[0].LrPct * hData._statsSet[0].Lwl / stats.Lwl;
                    stats.iE = hData._statsSet[0].iE;
                    stats.bowLength = hData._statsSet[0].bowLength;
                }

                if (hData._isDDHull)
                {
                    stats.Cb = Mathf.Pow(stats.Cb, powCb);
                    stats.Cwp = Mathf.Pow(stats.Cwp, powCwp);
                    stats.Cp = stats.Cb / stats.Cm;
                    stats.Cvp = stats.Cb / stats.Cwp;
                }

                hData._statsSet[secCount] = stats;
                string beam = stats.B.ToString("F2");
                if (stats.beamBulge != stats.B)
                    beam += $"({stats.beamBulge:F2})";
                Debug.Log($"{hData._key}@{secCount}: {(stats.Lwl):F2}x{beam}x{(stats.T):F2} ({(stats.Lwl/stats.B):F2},{(stats.B/stats.T):F2}), LCB={stats.lcbPct:P2}, {stats.Vd:F0}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}, Catr={stats.Catr:F3}, Cv={stats.Cv:F3}, DD={(hData._isDDHull)}");
                yield return null;
            }

            string varStr = hData._key.Substring(data.model.Length);
            if (varStr != string.Empty)
                varStr = $"({varStr.Substring(1)}) ";
            //Melon<UADRealismMod>.Logger.Msg($"Calculated stats for {data.model} {varStr}for {hData._sectionsMin} to {hData._sectionsMax} sections");

            //Melon<UADRealismMod>.Logger.Msg($"{data.model}: {(stats.Lwl * scaleFactor):F2}x{beamStr}x{(stats.D * scaleFactor):F2}, {(stats.Vd * tRatio)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}");
            Destroy(part.gameObject);
            yield return null;
            Part.CleanPartsStorage();
        }

        private Part SpawnPart(PartData data)
        {
            var partGO = new GameObject(data.name);
            var part = partGO.AddComponent<Part>();
            part.data = data;
            partGO.active = true;

            // Do what we need from Ship.ChangeHull and Part.LoadModel
            var model = Resources.Load<GameObject>(data.model);
            var instModel = Util.AttachInst(partGO, model);
            instModel.active = true;
            instModel.transform.localScale = Vector3.one;
            part.model = instModel.GetComponent<PartModel>();
            part.isShown = true;

            var visual = part.model.GetChild("Visual", false);
            var sections = visual.GetChild("Sections", false);
            part.bow = sections.GetChild("Bow", false);
            part.stern = sections.GetChild("Stern", false);
            var middles = new List<GameObject>();
            ModUtils.FindChildrenStartsWith(sections, "Middle", middles);
            middles.Sort((a, b) => a.name.CompareTo(b.name));
            part.middlesBase = new Il2CppSystem.Collections.Generic.List<GameObject>();
            foreach (var m in middles)
            {
                part.middlesBase.Add(m);
                m.active = false;
            }

            part.middles = new Il2CppSystem.Collections.Generic.List<GameObject>();
            part.hullInfo = part.model.GetComponent<HullInfo>();
            //part.RegrabDeckSizes(true);
            //part.RecalcVisualSize();

            foreach (var go in part.middlesBase)
                Util.SetActiveX(go, false);

            return part;
        }

        private Bounds GetShipBounds(GameObject obj)
        {
            var allVRs = Part.GetVisualRenderers(obj);
            Bounds shipBounds = new Bounds();
            bool foundShipBounds = false;
            foreach (var r in allVRs)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                if (foundShipBounds)
                {
                    shipBounds.Encapsulate(r.bounds);
                }
                else
                {
                    foundShipBounds = true;
                    shipBounds = r.bounds;
                }
            }

            return shipBounds;
        }

        private void CreateMiddles(Part part, int numTotal)
        {
            // Spawn middle sections
            for (int i = part.middles.Count; i < numTotal; ++i)
            {
                var mPrefab = part.middlesBase[i % part.middlesBase._size];
                var cloned = Util.CloneGameObject(mPrefab);
                part.middles.Add(cloned);
                //if (cloned.transform.parent != part.bow.transform.parent)
                //{
                //    Debug.LogError($"New middle {cloned.name} has parent {cloned.transform.parent.gameObject.name} not {part.bow.transform.parent.gameObject.name}! Reparenting.");
                //    cloned.transform.parent = part.bow.transform.parent;
                //}
                Util.SetActiveX(cloned, true);
            }
        }

        private void ApplyVariations(Part part)
        {
            part.data.paramx.TryGetValue("var", out var desiredVars);
            var varComps = part.model.gameObject.GetComponentsInChildren<Variation>(true);
            for (int i = 0; i < varComps.Length; ++i)
            {
                var varComp = varComps[i];
                string varName = string.Empty;
                if (desiredVars != null)
                {
                    foreach (var s in varComp.variations)
                        if (desiredVars.Contains(s))
                            _matchedVars.Add(s);
                }

                _foundVars.AddRange(_matchedVars);

                if (_matchedVars.Count > 1)
                    Debug.LogError("Found multiple variations on partdata " + part.data.name + ": " + string.Join(", ", _matchedVars));
                if (_matchedVars.Count > 0)
                    varName = _matchedVars[0];

                _matchedVars.Clear();

                // This is a reimplementation of Variation.Init, since that needs a Ship
                var childObjs = Util.GetChildren(varComp);
                if (childObjs.Count == 0)
                {
                    Debug.LogError("For part " + part.data.name + ", no variations!");
                    continue;
                }
                if (varComp.variations.Count != childObjs.Count)
                {
                    Debug.LogError("For part " + part.data.name + ", var count / child count mismatch!");
                    continue;
                }
                int varIdx = varComp.variations.IndexOf(varName);
                if ((varIdx < 0 && varName != string.Empty))
                {
                    Debug.LogError("For part " + part.data.name + ", can't find variation " + varName);
                }
                int selectedIdx;
                if (varIdx >= 0)
                {
                    selectedIdx = varIdx;
                }
                else if (varComp.random)
                {
                    selectedIdx = UnityEngine.Random.Range(0, varComp.variations.Count - 1);
                }
                else
                {
                    selectedIdx = varComp.variations.IndexOf(varComp.@default);
                    if (selectedIdx < 0)
                    {
                        if (varComp.@default != string.Empty)
                        {
                            Debug.LogError("For part " + part.data.name + ", can't find default variation " + varComp.@default);
                        }
                        selectedIdx = 0;
                    }
                }
                for (int v = childObjs.Count; v-- > 0;)
                    Util.SetActiveX(childObjs[v], v == selectedIdx);
            }

            // This check is not quite the same as the game's
            // but it's fine.
            if (desiredVars != null)
            {
                foreach (var desV in desiredVars)
                {
                    for (int v = _foundVars.Count; v-- > 0;)
                    {
                        if (_foundVars[v] == desV)
                            _foundVars.RemoveAt(v);
                    }
                }
                if (_foundVars.Count > 0)
                    Debug.LogError("On partdata " + part.data.name + ": Missing vars: " + string.Join(", ", _foundVars));
            }

            _foundVars.Clear();
        }

        private static readonly Bounds _EmptyBounds = new Bounds();

        private void PositionSections(Part part)
        {
            if (part == null)
            {
                Debug.LogError("Part null!\n" + Environment.StackTrace);
                return;
            }
            // Get reversed list of sections so we can position them
            var sectionsReverse = new Il2CppSystem.Collections.Generic.List<GameObject>();
            sectionsReverse.AddRange(part.hullSections);
            for (int i = 0; i < sectionsReverse.Count / 2; ++i)
            {
                int opposite = sectionsReverse.Count - 1 - i;
                if (i == opposite)
                    break;

                var tmp = sectionsReverse[i];
                sectionsReverse[i] = sectionsReverse[opposite];
                sectionsReverse[opposite] = tmp;
            }

            // Calculate bounds for all sections
            int mCount = part.middles == null ? -1 : part.middles.Count;
            if (part.bow == null)
            {
                Debug.LogError($"Part {part.name}@{mCount}: bow null!");
                return;
            }
            var sectionsGO = Util.GetParent(part.bow);
            if (sectionsGO == null)
            {
                Debug.LogError($"Part {part.name}@{mCount}: Sections null!");
                return;
            }
            var sectionsTrf = sectionsGO.transform;
            float shipLength = 0f;

            if (_sectionBounds == null)
            {
                Debug.LogError($"Part {part.name}@{mCount}: _SectionBounds is null!");
                return;
            }

            for (int i = 0; i < sectionsReverse.Count; ++i)
            {
                var sec = sectionsReverse[i];
                if (sec == null)
                {
                    Debug.LogError($"Part {part.name}@{mCount}: Section {i} is null!");
                    return;
                }
                var secBounds = GetSectionBounds(sec, sectionsTrf);
                _sectionBounds.Add(secBounds);
                shipLength += secBounds.size.z;
            }

            // Reposition/rescale sections
            float lengthOffset = shipLength * -0.5f;
            for (int i = 0; i < sectionsReverse.Count; ++i)
            {
                var sec = sectionsReverse[i];
                if (sec == null)
                {
                    Debug.LogError($"Part {part.name}@{mCount}: Section {i} is null!");
                    return;
                }
                if(_sectionBounds[i] == _EmptyBounds)
                {
                    Debug.LogError($"Part {part.name}@{mCount}: Section {(sec == null ? "NULL" : sec.name)} ({i}) has empty bounds!");
                    return;
                }
                var secBounds = _sectionBounds[i];
                Util.SetLocalZ(sec.transform, secBounds.size.z * 0.5f - secBounds.center.z + lengthOffset + sec.transform.localPosition.z);
                Util.SetScaleX(sec.transform, 1f); // beam
                Util.SetScaleY(sec.transform, 1f); // draught
                lengthOffset += secBounds.size.z;
            }

            _sectionBounds.Clear();
        }

        private Bounds GetSectionBounds(GameObject sec, Transform space)
        {
            var vr = Part.GetVisualRenderers(sec);
            Bounds secBounds = new Bounds();
            bool foundBounds = false;
            int rCount = 0;
            foreach (var r in vr)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                ++rCount;

                var trf = r.transform;
                var mesh = r.GetComponent<MeshFilter>();
                var sharedMesh = mesh.sharedMesh;
                var meshBounds = sharedMesh.bounds;
                // transform bounds to 'Sections' space
                meshBounds = Util.TransformBounds(trf, meshBounds);
                var invBounds = space == null ? meshBounds : Util.InverseTransformBounds(space, meshBounds);

                if (foundBounds)
                {
                    secBounds.Encapsulate(invBounds);
                }
                else
                {
                    secBounds = invBounds;
                    foundBounds = true;
                }
            }
            var secInfo = sec.GetComponent<SectionInfo>() ?? sec.GetComponentInChildren<SectionInfo>();
            if (secInfo != null)
            {
                //Debug.Log($"For part {data.name}, sec {sec.name}, size mods {secInfo.sizeFrontMod} + {secInfo.sizeBackMod}");
                var bSize = secBounds.size;
                bSize.z += secInfo.sizeFrontMod + secInfo.sizeBackMod;
                secBounds.size = bSize;
                var bCent = secBounds.center;
                bCent.z += (secInfo.sizeFrontMod - secInfo.sizeBackMod) * 0.5f;
                secBounds.center = bCent;
            }
            return secBounds;
        }
    }
}
