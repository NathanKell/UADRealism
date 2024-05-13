﻿using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(GunData))]
    internal class Patch_GunData
    {
        internal static List<float> oldDiameters = new List<float>();
        internal static bool isPatched = false;

        private const float DiameterPower = 1.5f;
        internal static float ConvertDiameter(float baseCaliberInch, float extraDiam)
        {
            bool isNegative;
            float extraDiamInch;
            if (extraDiam < 0f)
            {
                isNegative = true;
                extraDiamInch = (float)Math.Round(1f + (extraDiam / 25.4f), 1);
                if (baseCaliberInch > 1f)
                    baseCaliberInch -= 1f;
            }
            else
            {
                isNegative = false;
                extraDiamInch = (float)Math.Round(extraDiam / 25.4f, 1);
            }

            float basePowered = Mathf.Pow(baseCaliberInch, DiameterPower);
            float exactPowered = Mathf.Pow(baseCaliberInch + extraDiamInch, DiameterPower);
            float nextPowered = Mathf.Pow(baseCaliberInch + 1f, DiameterPower);
            float newExtraDiam = (exactPowered - basePowered) / (nextPowered - basePowered);

            if (isNegative)
                newExtraDiam = newExtraDiam - 1f;

            return newExtraDiam * 25.4f;
        }

        /// <summary>
        /// It's much faster to just patch all TurretCalibers than to find the matching one
        /// (because of the weird way casemates are detected)
        /// </summary>
        /// <param name="ship"></param>
        internal static void PatchDiameters(Ship ship)
        {
            foreach (var tc in ship.shipGunCaliber)
            {
                oldDiameters.Add(tc.diameter);
                if (tc.turretPartData != null)
                    tc.diameter = ConvertDiameter(tc.turretPartData.GetCaliberInch(), tc.diameter);
            }
        }

        internal static void UnpatchDiameters(Ship ship)
        {
            for (int i = 0; i < ship.shipGunCaliber.Count; ++i)
                ship.shipGunCaliber[i].diameter = oldDiameters[i];

            oldDiameters.Clear();
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(float), typeof(Il2CppSystem.Func<GunData, float>) })]
        internal static void Prefix_GetValue(GunData __instance, Ship ship)
        {
            if (isPatched || ship == null || ship.shipGunCaliber == null)
                return;

            isPatched = true;

            PatchDiameters(ship);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(float), typeof(Il2CppSystem.Func<GunData, float>) })]
        internal static void Postfix_GetValue(Ship ship)
        {
            if (!isPatched)
                return;

            isPatched = false;

            if (ship == null || ship.shipGunCaliber == null)
                return;

            UnpatchDiameters(ship);
        }


        /// <summary>
        /// We're only going to use the caliber lerp, which means we need to zero out
        /// the length params passed to GetValue since that leads to double-counting
        /// caliber changes when done manually. We also patch diameters like in the simple
        /// case.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(int), typeof(Il2CppSystem.Collections.Generic.Dictionary<int, float>), typeof(Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>>), typeof(float), typeof(float) })]
        internal static void Prefix_GetValue_GradeLength(Ship ship, ref float minParam, ref float maxParam)
        {
            minParam = 0f;
            maxParam = 0f;

            if (isPatched || ship == null || ship.shipGunCaliber == null)
                return;

            isPatched = true;

            PatchDiameters(ship);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(int), typeof(Il2CppSystem.Collections.Generic.Dictionary<int, float>), typeof(Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>>), typeof(float), typeof(float) })]
        internal static void Postfix_GetValue_GradeLength(Ship ship)
        {
            if (!isPatched)
                return;

            isPatched = false;

            if (ship == null || ship.shipGunCaliber == null)
                return;

            UnpatchDiameters(ship);
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
