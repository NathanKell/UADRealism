using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Player))]
    internal class Patch_Player
    {
        [HarmonyPatch(nameof(Player.Flag), new Type[] { typeof(PlayerData), typeof(bool), typeof(Player), typeof(int) })]
        [HarmonyPrefix]
        internal static bool Prefix_Flag(PlayerData data, bool naval, Player player, int newYear, ref Sprite __result)
        {
            var newSprite = FlagDatabase.Instance.GetFlag(data, naval, player, newYear);
            if (newSprite != null)
            {
                __result = newSprite;
                return false;
            }

            return true;
        }

        // These are done as postfix so that TotalPopulation gets set properly
        [HarmonyPatch(nameof(Player.InitCrewPool))]
        [HarmonyPostfix]
        internal static void Postfix_InitCrewPool(Player __instance)
        {
            if (Config.UseColonyInCrewPool)
                PlayerM.InitCrewPool(__instance);
        }

        [HarmonyPatch(nameof(Player.GetBaseCrewPool))]
        [HarmonyPostfix]
        internal static void Postfix_GetBaseCrewPool(Player __instance, ref int __result)
        {
            if (Config.UseColonyInCrewPool)
                __result = PlayerM.GetBaseCrewPool(__instance);
        }

        [HarmonyPatch(nameof(Player.CrewPoolIncome))]
        [HarmonyPostfix]
        internal static void Postfix_CrewPoolIncome(Player __instance, ref int __result)
        {
            if (Config.UseColonyInCrewPool)
                __result = PlayerM.CrewPoolincome(__instance);
        }
    }
}
