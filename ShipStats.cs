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
}
