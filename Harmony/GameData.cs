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
            // HACK so I don't have to fix NAR stats
            foreach (var kvp in __instance.shipTypes)
            {
                if (kvp.Key == "dd")
                    kvp.Value.armorMin = 0f;
                else
                    kvp.Value.armorMin *= 0.5f;

                if (kvp.Key != "ic")
                    kvp.Value.speedMin -= 2f;
            }
        }
    }
}
