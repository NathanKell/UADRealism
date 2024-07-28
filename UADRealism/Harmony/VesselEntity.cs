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

        // Harmony can't patch methods that take nullable arguments.
        // So instead of patching Ship.FromStore() we have to patch
        // this, which it calls near the start.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(VesselEntity.FromBaseStore))]
        internal static void Prefix_FromBaseStore(VesselEntity __instance, VesselEntity.VesselEntityStore store, bool isSharedDesign)
        {
            Ship s = __instance.GetComponent<Ship>();
            if (s == null)
                return;

            var sStore = store.TryCast<Ship.Store>();
            if (sStore == null)
                return;
            s.ModData().FromStore(sStore);
        }
    }
}
