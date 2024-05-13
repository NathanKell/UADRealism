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
        //[HarmonyPostfix]
        //[HarmonyPatch(nameof(GameData.PostProcessAll))]
        //internal static void Postfix_PostProcessAll(GameData __instance)
        //{
        //    var ssc = ShipStatsCalculator.Instance;
        //    ssc = null;

        //    MelonCoroutines.Start(ShipStats.ProcessGameData());
        //}
    }
}
