using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    //[HarmonyPatch(typeof(CampaignController.TaskForce))]
    internal class Patch_TaskForce
    {
        //[HarmonyPatch(nameof(CampaignController.TaskForce.CheckMinefieldOnPath))]
        //[HarmonyPrefix]
        internal static bool Prefix_CheckMinefieldOnPath(CampaignController.TaskForce __instance)
        {
            TaskForceM.CheckMinefieldOnPath(__instance);
            return false;
        }
    }
}
