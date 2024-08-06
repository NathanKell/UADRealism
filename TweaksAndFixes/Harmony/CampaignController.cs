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

    // We could replace the _entirety_ of the scrap logic by:
    // 1. Prefixing/postfixing AI Manage Fleet's MoveNext to set a bool that we're in that method
    // 2. Changing the below method to always report a single true, all others false (using the
    //      bool in 1, such that the scrap loop runs only once)
    // 3. Prefixing CampaignController.ScrapShip such that, when it runs, if the bool in 1 is set,
    //      our new scrap logic runs on the entire fleet of the player in question.
    // For now, we can just expose the dates.
    [HarmonyPatch(typeof(CampaignController.__c__DisplayClass169_0))]
    internal class Patch_CampaignController_169
    {
        [HarmonyPatch(nameof(CampaignController.__c__DisplayClass169_0._AiManageFleet_b__3))]
        [HarmonyPrefix]
        internal static bool Prefix__AiManageFleet_b__3(CampaignController.__c__DisplayClass169_0 __instance, Ship s, ref bool __result)
        {
            __result = _AiManageFleet_b__3(__instance, s);
            return false;
        }

        private static bool _AiManageFleet_b__3(CampaignController.__c__DisplayClass169_0 _this, Ship s)
        {
            if (s.isBuilding || s.isTempForBattle)
                return false;

            if (s.isMothballed)
            {
                if (_this.__4__this.CurrentDate.YearsPassedSince(s.dateFinished) >= MonoBehaviourExt.Param("taf_scrapping_yearssincefinished_mothballed", 3.0f))
                    return true;
            }

            // this isn't an else, because the params could overlap. Stock's should have been an else though.
            if (_this.__4__this.CurrentDate.YearsPassedSince(s.dateFinished) >=
                UnityEngine.Random.Range(MonoBehaviourExt.Param("taf_scrapping_yearssincefinished_min", 4.0f),
                    MonoBehaviourExt.Param("taf_scrapping_yearssincefinished_max", 8.0f)))
                return true;

            return false;
        }
    }
}
