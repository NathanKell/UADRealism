using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(Part))]
    internal class Patch_Part
    {
        internal static bool inWeaponReloadTime = false;
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Part.WeaponReloadTime))]
        internal static void Prefix_WeaponReloadTime()
        {
            inWeaponReloadTime = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Part.WeaponReloadTime))]
        internal static void Postfix_WeaponReloadTime()
        {
            inWeaponReloadTime = false;
        }
    }
}
