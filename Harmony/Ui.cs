using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(Ui))]
    internal class Patch_Ui
    {
        internal static bool _LoadingDone = false;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ui.CompleteLoadingScreen))]
        internal static bool Prefix_CompleteLoadingScreen(Ui __instance)
        {
            if (!ShipStats.LoadingDone)
            {
                MelonCoroutines.Start(ShipStats.ProcessGameData());
                return false;
            }

            return true;
        }
    }
}
