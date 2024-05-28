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

        // We can't patch the Ship version because it has a nullable var in it
        // and Il2Cpp patching can't do that
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
