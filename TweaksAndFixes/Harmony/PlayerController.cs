using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(PlayerController))]
    internal class Patch_PlayerController
    {
        [HarmonyPatch(nameof(PlayerController.CloneShipRaw))]
        [HarmonyPostfix]
        internal static void Postfix_CloneShipRaw(Ship from, ref Ship __result)
        {
            __result.TAFData().OnClonePost(from.TAFData());
        }
    }
}
