using System;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(GunData))]
    internal class Patch_GunData
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(float), typeof(Il2CppSystem.Func<GunData, float>) })]
        internal static bool Prefix_GetValue(GunData __instance, Ship ship, PartData partData, float defValue, Il2CppSystem.Func<GunData, float> func, ref float __result)
        {
            if (ship == null || ship.shipGunCaliber == null || partData == null || func == null)
                return true;

            var gdm = new GunDataM(__instance, partData, ship, false);
            __result = gdm.GetValue(defValue, func);
            return false;
        }

        /// <summary>
        /// We're only going to use the caliber lerp, so we ignore minParam and maxParam
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(int), typeof(Il2CppSystem.Collections.Generic.Dictionary<int, float>), typeof(Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>>), typeof(float), typeof(float) })]
        internal static bool Prefix_GetValue_GradeLength(GunData __instance, Ship ship, PartData partData, int index, Il2CppSystem.Collections.Generic.Dictionary<int, float> defValue, Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>> func, float minParam, float maxParam, ref float __result)
        {
            if (ship == null || ship.shipGunCaliber == null || partData == null || func == null || defValue == null)
                return true;

            var gdm = new GunDataM(__instance, partData, ship, true);
            __result = gdm.GetValue_GradeLength(index, defValue, func, 0f, 0f);
            return false;
        }


        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.BaseWeight))]
        internal static bool Prefix_BaseWeight(GunData __instance, Ship ship, PartData partData, ref float __result)
        {
            var gdm = new GunDataM(__instance, partData, ship, true);
            __result = gdm.BaseWeight();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.BarrelWeight))]
        internal static bool Prefix_BarrelWeight(GunData __instance, Ship ship, PartData partData, int index, ref float __result)
        {
            var gdm = new GunDataM(__instance, partData, ship, true);
            __result = gdm.BarrelWeight(index);
            return false;
        }

        // Special patch to deal with reload time using shell velocity:
        // if we're inside Part.WeaponReloadTime, report a shell velocity
        // of zero, which means the lerp will return 1.0 (a no-op multiplier)
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.ShellVelocity))]
        internal static bool Prefix_ShellVelocity(Ship ship, PartData partData, ref float __result)
        {
            if (Patch_Part.inWeaponReloadTime)
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }
}
