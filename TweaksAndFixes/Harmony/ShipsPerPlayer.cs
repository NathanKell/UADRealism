using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UnityEngine.UI;

#pragma warning disable CS8625

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(ShipsPerPlayer))]
    internal class Patch_ShipsPerPlayer
    {
        private static readonly List<Ship.Store> _ShipOptions = new List<Ship.Store>();

        // Patching this rather than PlayerController.CampaignCanUsePredefinedDesign
        // so that we only need to jump out to managed code once, and collect techs once.
        [HarmonyPatch(nameof(ShipsPerPlayer.RandomShipOfType))]
        [HarmonyPostfix]
        internal static void Postfix_RandomShipOfType(ShipsPerPlayer __instance, Player player, ShipType shipType, ref Ship.Store __result)
        {
            if (!Config.DontClobberTechForPredefs || __result == null)
                return;

            if (CampaignControllerM.TechMatchRatio(__result) < 0)
            {
                _ShipOptions.Clear();
                foreach (var s in __instance.validDesigns)
                {
                    if (s == __result || CampaignControllerM.TechMatchRatio(s) < 0)
                        continue;
                    _ShipOptions.Add(s);
                }
                if (_ShipOptions.Count > 0)
                    __result = _ShipOptions.Random();
                else
                    __result = null;
            }
        }
    }
}
