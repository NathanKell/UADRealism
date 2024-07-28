using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Ship.TurretCaliber))]
    internal class Patch_TurretCaliber
    {
        // Might have to patch this eventually

        //[HarmonyPatch(nameof(Ship.TurretCaliber.FromStore))]
        //[HarmonyPostfix]
        internal static void Postfix_FromStore(Ship from, ref Ship __result)
        {
            //__result.TAFData().OnClonePost(from.TAFData());
        }
    }
}
