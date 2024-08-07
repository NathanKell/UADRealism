using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

#pragma warning disable CS8600
#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public static class PlayerM
    {
        public static IEnumerable<Ship> GetFleetAll(this Player player)
        {
            if (CampaignController.Instance == null || !CampaignController.Instance.CampaignData.VesselsByPlayer.TryGetValue(player.data, out var allVessels))
                yield break;

            foreach (var v in allVessels)
            {
                if (v.vesselType != VesselEntity.VesselType.Ship)
                    continue;
                var s = v.TryCast<Ship>();
                if (s == null || s.isDesign)
                    continue;

                yield return s;
            }
        }

        public static IEnumerable<Ship> GetFleet(this Player player, bool includeMothballedAndLowCrew = false)
        {
            foreach (var s in player.GetFleetAll())
            {
                if (s.isSunk || s.isScrapped)
                    continue;
                if (!includeMothballedAndLowCrew && (s.status == VesselEntity.Status.Mothballed || s.status == VesselEntity.Status.LowCrew))
                    continue;

                yield return s;
            }
        }
    }
}