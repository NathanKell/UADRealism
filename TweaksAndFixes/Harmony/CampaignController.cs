using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignController))]
    internal class Patch_CampaignController
    {
        [HarmonyPatch(nameof(CampaignController.GetSharedDesign))]
        [HarmonyPrefix]
        internal static bool Prefix_GetSharedDesign(CampaignController __instance, Player player, ShipType shipType, int year, bool checkTech, bool isEarlySavedShip, ref Ship __result)
        {
            __result = CampaignControllerM.GetSharedDesign(__instance, player, shipType, year, checkTech, isEarlySavedShip);

            return false;
        }
    }
}
