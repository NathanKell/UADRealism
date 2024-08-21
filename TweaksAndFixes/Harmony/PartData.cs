using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace TweaksAndFixes
{
    // Hilariously, this causes the game to crash (!) when run on a funnel.

    //[HarmonyPatch(typeof(PartData))]
    //internal class Patch_PartData
    //{
    //    [HarmonyPostfix]
    //    [HarmonyPatch(nameof(PartData.isFreeMountAllow), MethodType.Getter)]
    //    internal static void Postfix_get_isFreeMountAllow(PartData __instance, ref bool __result)
    //    {
    //        if (Patch_Ui._InUpdateConstructor && Patch_Ship._GenerateShipState < 0 && __result && __instance != null && !__instance.needsMount)
    //            __result = false;
    //        //Melon<TweaksAndFixes>.Logger.Msg("In isFreeMountAllow");
    //    }
    //}
}
