using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(PartData))]
    internal class Patch_PartData
    {
        [HarmonyPrefix]
        [HarmonyPatch("get_isFreeMountAllow")]
        internal static bool Prefix_get_isFreeMountAllow(PartData __instance, ref bool __result)
        {
            if (Patch_Ship._GenerateShipState < 0 && __instance.isBarbette && !__instance.needsMount)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
