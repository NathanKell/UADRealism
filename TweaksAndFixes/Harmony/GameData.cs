using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            // Run after other things have a chance to update GameData
            MelonCoroutines.Start(FillDatabase());
        }

        internal static System.Collections.IEnumerator FillDatabase()
        {
            yield return new WaitForEndOfFrame();
            Database.FillDatabase();
            Melon<TweaksAndFixes>.Logger.Msg("Loaded database");
        }
    }
}
