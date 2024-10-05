using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UnityEngine.UI;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(ShipsPerPlayer))]
    internal class Patch_ShipsPerPlayer
    {
        private static bool _PassthroughRandomShip = false;
        private static readonly List<Ship.Store> _ShipOptions = new List<Ship.Store>();

        // Patching this rather than PlayerController.CampaignCanUsePredefinedDesign
        // so that we only need to jump out to managed code once, and collect techs once.
        [HarmonyPatch(nameof(ShipsPerPlayer.RandomShipOfType))]
        [HarmonyPostfix]
        internal static void Postfix_RandomShipOfType(ShipsPerPlayer __instance, Player player, ShipType shipType, ref Ship.Store __result)
        {
            if (!Config.DontClobberTechForPredefs || _PassthroughRandomShip)
                return;

            CampaignControllerM.CachePlayerTechs(player);
            if (CampaignControllerM.TechMatchRatio(__result) < 0)
            {
                _PassthroughRandomShip = true;
                int year = __result.YearCreated;
                Ship.Store? newShip = null;
                do
                {
                    newShip = PickShipFromSPP(__instance);
                    if (newShip != null || year == CampaignController.Instance._currentDesigns.years[0])
                        break;
                    CampaignController.Instance._currentDesigns.GetNearestYear(year - 1, out year);
                    CampaignController.Instance._currentDesigns.shipsPerYear[year].shipsPerPlayer[player.data.name].RandomShipOfType(player, shipType);
                } while (true);
            }
            _ShipOptions.Clear();
            CampaignControllerM.CleanupSDCaches();
            _PassthroughRandomShip = false;
        }

        private static Ship.Store PickShipFromSPP(ShipsPerPlayer __instance)
        {
            _ShipOptions.Clear();
            foreach (var s in __instance.validDesigns)
            {
                if (CampaignControllerM.TechMatchRatio(s) < 0)
                    continue;
                _ShipOptions.Add(s);
            }
            return _ShipOptions.RandomOrNull();
        }
    }
}
