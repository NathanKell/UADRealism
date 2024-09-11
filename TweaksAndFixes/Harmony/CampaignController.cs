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
        // AI Scrapping patch
        // We replace the _entirety_ of the scrap logic by:
        // 1. Prefixing/postfixing AI Manage Fleet's MoveNext to set a bool that we're in that method
        // 2. Changing the predicate method that determines whether to scrap a ship to always report
        //      a single true, all others false (using the bool in 1, such that the scrap loop runs
        //      only once)
        // 3. Prefixing CampaignController.ScrapShip such that, when it runs, if the bool in 1 is set,
        //      our new scrap logic runs on the entire fleet of the player in question. To do that,
        //      we have to add a passthrough bool so we can still call scrap from that method.

        internal static bool _IsInManageFleet = false;
        internal static int _ManageFleetScrapShipsHit = 0;
        internal static bool _PassthroughScrap = false;

        [HarmonyPatch(nameof(CampaignController.GetSharedDesign))]
        [HarmonyPrefix]
        internal static bool Prefix_GetSharedDesign(CampaignController __instance, Player player, ShipType shipType, int year, bool checkTech, bool isEarlySavedShip, ref Ship __result)
        {
            __result = CampaignControllerM.GetSharedDesign(__instance, player, shipType, year, checkTech, isEarlySavedShip);
            return false;
        }

        [HarmonyPatch(nameof(CampaignController.ScrapShip))]
        [HarmonyPrefix]
        internal static bool Prefix_ScrapShip(CampaignController __instance, Ship ship, bool addCashAndCrew)
        {
            if (!Config.ScrappingChange || _PassthroughScrap || !_IsInManageFleet)
                return true;

            _PassthroughScrap = true;
            CampaignControllerM.HandleScrapping(__instance, ship.player);
            _PassthroughScrap = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CampaignController._AiManageFleet_d__169))]
    internal class Patch_CampaignController_co169
    {
        [HarmonyPatch(nameof(CampaignController._AiManageFleet_d__169.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(CampaignController._AiManageFleet_d__169 __instance)
        {
            Patch_CampaignController._IsInManageFleet = true;
            Patch_CampaignController._ManageFleetScrapShipsHit = 0;
        }

        [HarmonyPatch(nameof(CampaignController._AiManageFleet_d__169.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(CampaignController._AiManageFleet_d__169 __instance)
        {
            Patch_CampaignController._IsInManageFleet = false;
        }
    }
    
    [HarmonyPatch(typeof(CampaignController.__c__DisplayClass169_0))]
    internal class Patch_CampaignController_169
    {
        [HarmonyPatch(nameof(CampaignController.__c__DisplayClass169_0._AiManageFleet_b__3))]
        [HarmonyPrefix]
        internal static bool Prefix__AiManageFleet_b__3(CampaignController.__c__DisplayClass169_0 __instance, Ship s, ref bool __result)
        {
            if (!Config.ScrappingChange)
                return true;

            if (Patch_CampaignController._ManageFleetScrapShipsHit++ == 0)
                __result = true;
            else
                __result = false;

            return false;
        }
    }
}
