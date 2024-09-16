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

        public static int CrewPoolincome(Player _this)
        {
            var provs = _this.homeProvinces;
            float homePop = 0;
            foreach (var p in provs)
            {
                float pop = p.GetPopulation();
                if (pop == 0)
                    Melon<TweaksAndFixes>.Logger.Msg($"Province {p.Id} has 0 pop!");
                homePop += pop;
            }
            float mult = MonoBehaviourExt.Param("crew_pool_income_modifier_max", 0.005f);
            float existingPoolMult = Mathf.Lerp(2f, 0.13f, _this.crewPool / 90000);
            float portionOfHomePop = Mathf.Clamp01(_this.crewPool * 25000 / homePop);
            Melon<TweaksAndFixes>.Logger.Msg($"Player {_this.data.name} crew pool {_this.crewPool}, pop {_this.TotalPopulation:N0} (we say {homePop:N0}), budget {_this.trainingBudget:N2}, existingMult {existingPoolMult:F3}, portion {portionOfHomePop:F3}. AI would be {(CampaignController.Instance.AiIncomeMultiplier * 1.15f):F2}x");
            float val = Mathf.Lerp(1f, 0.1f, portionOfHomePop) * _this.trainingBudget * homePop * mult * existingPoolMult;
            if (_this.isAi)
                val *= CampaignController.Instance.AiIncomeMultiplier * 1.15f;

            return (int)val;
        }
    }
}