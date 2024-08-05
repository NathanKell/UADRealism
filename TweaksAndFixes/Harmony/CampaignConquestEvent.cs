using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignConquestEvent))]
    internal class Patch_CampaignConquestEvent
    {
        [HarmonyPatch(nameof(CampaignConquestEvent.GetConquestChance))]
        [HarmonyPrefix]
        internal static bool Prefix_GetConquestChance(CampaignConquestEvent __instance, bool force, ref float __result)
        {
            __result = GetConquestChance(__instance, force);
            return false;
        }

        private static float GetConquestChance(CampaignConquestEvent _this, bool force)
        {
            if (_this.cachedConquestChance != -1f && !force)
                return _this.cachedConquestChance;

            float totalReq = _this.requiredTonnage + _this.AdditionalRequiredTonnage;
            if (_this.CurrentTonnage < totalReq)
            {
                _this.cachedConquestChance = 0f;
                return 0f;
            }

            var ratioLerped = Mathf.Lerp(MonoBehaviourExt.Param("taf_conquest_event_chance_mult_starting_duration", 0.01f), MonoBehaviourExt.Param("taf_conquest_event_chance_mult_full_duration", 0.5f), _this.DurationTotal / (float)_this.maxDuration)
                * _this.CurrentTonnage / totalReq;

            float killFactor;
            // stock tests evt type - 2 <= 1. (a) we reverse for cleanliness,
            // and (b) there _aren't_ any higher-number types! Probably meant to
            // just be evt type <= 1, since that's the test used in CheckProgress
            if (_this.EventType > BaseCampaignSpecialEvent.SpecialEventType.RebellionLand)
            {
                killFactor = Mathf.Clamp01((_this.AttackerKillsTotal + 1f) / (_this.DefenderKillsTotal + 1f)) * 0.75f;
                var attacker = _this.Attacker.Player();
                var defender = _this.Defender.Player();
                if (attacker != null && attacker.isMajor && defender != null && !defender.isMajor)
                    killFactor *= 1.25f;
            }
            else
            {
                ratioLerped += MonoBehaviourExt.Param("taf_conquest_event_add_chance", 0.66f);
                // Game is bugged and has this ratio flipped
                var killRatio = (_this.AttackerKillsTotal + 1f) / (_this.DefenderKillsTotal + 1f);
                if (totalReq != -1f)
                    killRatio *= Mathf.Lerp(1f, 2f, totalReq / 500000f);
                killFactor = Mathf.Clamp01(killRatio * 0.5f);
            }

            _this.cachedConquestChance = Mathf.Clamp01(Mathf.LerpUnclamped(1f, killFactor, MonoBehaviourExt.Param("taf_conquest_event_kill_factor", 1f)) * ratioLerped);
            return _this.cachedConquestChance;
        }
    }
}
