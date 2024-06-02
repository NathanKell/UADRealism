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
        private const float DiameterPower = 1.5f;

        private static float ConvertDiameter(float baseCaliberInch, float extraDiam)
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

        private static GunData GetOtherCaliberData(PartData partData, Ship.TurretCaliber tc)
        {
            var gunsData = G.GameData.guns;
            int calInch = (int)((0.001f + partData.caliber) / 25.4f);
            int calOffset = tc.diameter < 0 ? -1 : 1;
            string nextCalStr;

            var dataID = partData.GetGunDataId(null);
            if (string.IsNullOrEmpty(dataID))
                return null;

            if (dataID.Contains("ironclad"))
            {
                var splits = dataID.Split('_');
                int parsedCalNext = int.Parse(splits[splits.Length - 1]) + calOffset;
                string ironcladNextCal = parsedCalNext.ToString();
                splits[splits.Length - 1] = ironcladNextCal;
                nextCalStr = string.Join("_", splits);
            }
            else
            {
                nextCalStr = (calInch + calOffset).ToString();
            }

            gunsData.TryGetValue(nextCalStr, out var nextCalData);
            return nextCalData;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(float), typeof(Il2CppSystem.Func<GunData, float>) })]
        internal static bool Prefix_GetValue(GunData __instance, Ship ship, PartData partData, float defValue, Il2CppSystem.Func<GunData, float> func, ref float __result)
        {
            if (ship == null || ship.shipGunCaliber == null || partData == null || func == null)
                return true;

            var tc = ShipM.FindMatchingTurretCaliber(ship, partData);
            if (tc == null)
            {
                // Not gonna bother to log this, because the TC _will_ be null
                // in the case where you have a tooltip open for a part in the part
                // selection area in the Constructor, it will only be non-null
                // for placed parts.
                //Melon<UADRealismMod>.Logger.Msg("TC null!");
                __result = defValue;
                return false;
            }

            var nextCalData = GetOtherCaliberData(partData, tc);

            if (nextCalData == null)
            {
                __result = defValue;
                return false;
            }

            int calInch = (int)((0.001f + partData.caliber) / 25.4f);
            // Note that this may be a negative t-value for the lerp. That's fine, because nextCalData
            // could be the next _lower_ caliber if the TurretCaliber's diameter offset is negative.
            float lerpT = ConvertDiameter(calInch, tc.diameter);
            __result = Mathf.Lerp(defValue, func.Invoke(nextCalData), lerpT);
            return false;
        }


        /// <summary>
        /// We're only going to use the caliber lerp, so we ignore minParam and maxParam
        /// and just have a copy of the simple GetValue, just this time using the gun grade too.
        /// case.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GunData.GetValue), new Type[] { typeof(Ship), typeof(PartData), typeof(int), typeof(Il2CppSystem.Collections.Generic.Dictionary<int, float>), typeof(Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>>), typeof(float), typeof(float) })]
        internal static bool Prefix_GetValue_GradeLength(GunData __instance, Ship ship, PartData partData, int index, Il2CppSystem.Collections.Generic.Dictionary<int, float> defValue, Il2CppSystem.Func<GunData, Il2CppSystem.Collections.Generic.Dictionary<int, float>> func, float minParam, float maxParam, ref float __result)
        {
            if (ship == null || ship.shipGunCaliber == null || partData == null || func == null || defValue == null)
                return true;

            if (!defValue.TryGetValue(index, out float defValFloat))
            {
                Melon<UADRealismMod>.Logger.Msg("defValue doesn't contain index " + index);
                return true;
            }

            var tc = ShipM.FindMatchingTurretCaliber(ship, partData);
            if (tc == null)
            {
                // Not gonna bother to log this, because the TC _will_ be null
                // in the case where you have a tooltip open for a part in the part
                // selection area in the Constructor, it will only be non-null
                // for placed parts.
                //Melon<UADRealismMod>.Logger.Msg("GL___TC null!");

                __result = defValFloat;
                return false;
            }

            var nextCalData = GetOtherCaliberData(partData, tc);

            if (nextCalData == null)
            {
                Melon<UADRealismMod>.Logger.Msg("Next Cal null!");
                __result = defValFloat;
                return false;
            }

            int calInch = (int)((0.001f + partData.caliber) / 25.4f);
            var nextCalDict = func.Invoke(nextCalData);

            if (nextCalDict == null)
            {
                Melon<UADRealismMod>.Logger.Msg("can't find next cal dict");
                return true;
            }
            if (!nextCalDict.ContainsKey(index))
            {
                Melon<UADRealismMod>.Logger.Msg("next cal dict doesn't contain index " + index);
                return true;
            }
            // Note that this may be a negative t-value for the lerp. That's fine, because nextCalData
            // could be the next _lower_ caliber if the TurretCaliber's diameter offset is negative.
            float lerpT = ConvertDiameter(calInch, tc.diameter);
            __result = Mathf.Lerp(defValFloat, nextCalDict[index], lerpT);
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
