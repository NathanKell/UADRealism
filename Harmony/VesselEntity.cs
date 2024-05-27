using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Linq;

namespace UADRealism
{
    [HarmonyPatch(typeof(VesselEntity))]
    internal class Patch_VesselEntity
    {
        // Since op range is still linear,
        // we shouldn't need to patch this.

        //[HarmonyPrefix]
        //[HarmonyPatch(nameof(VesselEntity.GetOpRangeInKmCalculate))]
        //internal static bool Prefix_GetOpRangeInKmCalculate(VesselEntity __instance)
        //{
            
        //}
    }
}
