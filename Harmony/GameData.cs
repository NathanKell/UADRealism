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
            }

            // Pass 2: Spawn and render the hull models, calculating stats
            ShipStats.CalculateHullData();

            // Pass 3: Set starting scales for all hull parts
            Melon<UADRealismMod>.Logger.Msg("time,name,model,sections,oldScale,tonnageMin,scaleMaxPct,oldScaleVd,newScale,Lwl,Beam,Bulge,Draught,Cb,Cm,Cwp,Cp,Cvp");
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

                int secNominal = (data.sectionsMin + data.sectionsMax) / 2;
                float tng = Mathf.Lerp(data.tonnageMin, data.tonnageMax, 0.25f);
                if (secNominal < hData._sectionsMin || secNominal > hData._sectionsMax)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find section data for partdata {data.name} of model {data.model} with key {hData._key}, at section count {secNominal}");
                    continue;
                }
                var stats = hData._statsSet[secNominal];
                float oldScale = data.scale;
                float oldCube = oldScale * oldScale * oldScale;
                float ratio = tng / (stats.Vd * ShipStats.TonnesToCubicMetersWater);
                float newScale = Mathf.Pow(ratio, 1f / 3f);
                data.scale = newScale;
                Melon<UADRealismMod>.Logger.Msg($",{data.name},{data.model},{secNominal},{oldScale:F3}x,{tng:F0}t,{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{(stats.Vd * oldCube * ShipStats.TonnesToCubicMetersWater):F0}t,{newScale:F3}x,L={(stats.Lwl * newScale):F2},B={(stats.B * newScale):F2},BB={(stats.beamBulge * newScale):F2},T={(stats.T * newScale):F2},Cb={stats.Cb:F3},Cm={stats.Cm:F3},Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3},Cvp={stats.Cvp:F3}");
                //double S_hm = stats.Lwl * (stats.beamBulge + 2d * stats.T) * Math.Sqrt(stats.Cm) * (0.453d + 0.4425d * stats.Cb - 0.2862d * stats.Cm + 0.003467d * stats.beamBulge / stats.T + 0.3696d * stats.Cwp);// + 19.65 * A_bulb_at_stem / Cb
                //double S_dm = 1.7d * stats.Lwl * stats.T + stats.Vd / stats.T;
                //Melon<UADRealismMod>.Logger.Msg($",{data.name},{data.model},{data.nameUi},{S_hm:F0},{S_dm:F0},{(S_hm/S_dm):P0}");
            }

            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");
        }
    }
}
