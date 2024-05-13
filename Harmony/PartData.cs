using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(PartData))]
    internal class Patch_PartData
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PartData.PostProcess))]
        internal static void Postfix_PostProcess(PartData __instance)
        {
            float coeffD = __instance.draughtCoef;
            float coeffB = __instance.beamCoef;

            __instance.draughtCoef = coeffD * 3f;
            __instance.beamCoef = coeffB * 3f;
            //Melon<UADRealismMod>.Logger.Msg($"Part {__instance.name}: beam {coeffB:F3}/{__instance.beamCoef:F3}, draught {coeffD:F3}/{__instance.draughtCoef:F3}");
        }
    }
}
