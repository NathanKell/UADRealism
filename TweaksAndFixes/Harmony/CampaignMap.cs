using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignMap))]
    internal class Patch_CampaignMap
    {
        [HarmonyPatch(nameof(CampaignMap.CanMove))]
        [HarmonyPrefix]
        internal static bool Prefix_CanMove(CampaignMap __instance, Vector3 desiredPosition, float averageRange, ref bool __result)
        {
            __result = CampaignMapM.CanMove(desiredPosition, averageRange);
            return false;
        }

        [HarmonyPatch(nameof(CampaignMap.PreInit))]
        [HarmonyPrefix]
        internal static void Prefix_PreInit(CampaignMap __instance)
        {
            if (MonoBehaviourExt.Param("taf_override_map", 0) > 0)
                MapData.LoadMapData(__instance);
        }
    }
}
