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

        //[HarmonyPatch(nameof(Player.InitCrewPool))]
        //[HarmonyPostfix]
        //internal static void Postfix_InitCrewPool(Player __instance)
        //{
        //    Melon<TweaksAndFixes>.Logger.Msg($"Player {__instance.data.name}: Init crew pool. Pop {__instance.TotalPopulation:N0}, crew {__instance.crewPool}");
        //}

        //[HarmonyPatch(nameof(Player.CrewPoolIncome))]
        //[HarmonyPostfix]
        //internal static void Postfix_CrewPoolIncome(Player __instance, int __result)
        //{
        //    Melon<TweaksAndFixes>.Logger.Msg($"Player {__instance.data.name}: Crew pool income reports {__result}");
        //    Melon<TweaksAndFixes>.Logger.Msg($"**** Our calc is {PlayerM.CrewPoolincome(__instance)}");
        //}
    }
}
