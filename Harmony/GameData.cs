using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        public static readonly Dictionary<string, string> _DestroyerVariants = new Dictionary<string, string>();
        public static readonly HashSet<string> _WrittenModels = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            Il2CppSystem.Diagnostics.Stopwatch sw = new Il2CppSystem.Diagnostics.Stopwatch();
            sw.Start();

            // Pass 1: Figure out the min and max sections used for each model.
            // (and deal with beam/draught coefficients)
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                // Just apply beam delta to tonnage directly since we're going to use length/beam ratios
                data.tonnageMin *= Mathf.Pow(data.beamMin * 0.01f + 1f, data.beamCoef);
                data.tonnageMax *= Mathf.Pow(data.beamMax * 0.01f + 1f, data.beamCoef);

                data.beamCoef = 0f;

                // Draught is a linear scale to displacement at a given length/beam
                // TODO: Do we want to allow implicitly different block coefficients based on draught, as would
                // be true in reality?
                data.draughtCoef = 1f;


                // Now get var/section data
                var hData = ShipStats.GetData(data);
                if (hData == null)
                {
                    hData = ShipStats.RegisterData(data);
                }
                else
                {
                    hData._sectionsMin = Math.Min(hData._sectionsMin, data.sectionsMin);
                    hData._sectionsMax = Math.Max(hData._sectionsMax, data.sectionsMax);
                }
                if (data.shipType.name == "dd" || data.shipType.name == "tb")
                    hData._isDDHull = true;
            }

            // Pass 2: Spawn and render the hull models, calculating stats
            ShipStats.CalculateHullData();

            // Pass 3: Set starting scales for all hull parts
            Melon<UADRealismMod>.Logger.Msg("time,order,name,model,sections,tonnage,scaleMaxPct,newScale,Lwl,Beam,Bulge,Draught,L/B,B/T,Cb,Cm,Cp,Cwp,Cvp,Catr,Cv,bowLen,BL/B,iE,Lr/L,lcb/L,Kn,SHP");
            int num = 1;
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                var hData = ShipStats.GetData(data);
                if (hData == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find data for partdata {data.name} of model {data.model} with key {hData._key}");
                    continue;
                }
                var modelName = ShipStats.GetHullModelKey(data);
                for (int i = 0; i < 3; ++i)
                {
                    float tVal = i > 0 ? 0.25f : 0;
                    float tng = Mathf.Lerp(data.tonnageMin, data.tonnageMax, tVal);
                    ShipStats.GetSectionsAndBeamForLB(i == 1 ? data.beamMin : 0, data.sectionsMin, data.sectionsMax, data.beamMin, data.beamMax, hData, out var beamScale, out var sections);
                    if (i == 0)
                        sections = 0;
                    var stats = ShipStats.GetScaledStats(hData, tng, i == 0 ? data.beamMin : beamScale, 0, sections);
                    float shp = ShipStats.GetSHPRequired(stats, data.speedLimiter * 0.51444444f, false);
                    Melon<UADRealismMod>.Logger.Msg($",{num},{data.name},{modelName},{sections},{tng:F0},{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{stats.scaleFactor:F3},{stats.Lwl:F2},{stats.B:F2},{(stats.beamBulge == stats.B ? 0 : stats.beamBulge):F2},{stats.T:F2},{(stats.Lwl / stats.B):F2},{(stats.B / stats.T):F2},{stats.Cb:F3},{stats.Cm:F3},{stats.Cp:F3},{stats.Cwp:F3},{stats.Cvp:F3},{stats.Catr:F3},{stats.Cv:F3},{stats.bowLength:F2},{(stats.bowLength / stats.B):F2},{stats.iE:F2},{stats.LrPct:F3},{stats.lcbPct:F4},{data.speedLimiter:F1},{shp:F0}");
                    ++num;
                }
            }

            foreach (var kvp in _DestroyerVariants)
            {
                Debug.Log(kvp.Value);
            }

            foreach (var kvp in __instance.parts)
            {
                if (kvp.value.model == "atlanta_hull_b_var")
                    kvp.value.sectionsMin = 0;
            }

            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");
        }
    }
}
