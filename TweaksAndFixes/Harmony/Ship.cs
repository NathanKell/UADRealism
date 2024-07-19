using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using System.Reflection;

#pragma warning disable CS8625

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship
    {
        internal static bool _IsGenerating = false;
        internal static Ship _ShipForGenerateRandom = null;

        // I'm sure more will get patched later.
        //[HarmonyPatch(nameof(Ship.IsPartAvailable), new Type[] { typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship) })]
        //[HarmonyPrefix]
        //internal static bool Prefix_IsPartAvailable(Ship __instance, PartData part, ref bool __result)
        //{
        //    //if (__instance.shipType.name != "cl" && __instance.shipType.name != "ca")
        //    //    return true;

        //    var year = Database.GetYear(part);
        //    if (year < 1860 || year > 1918)
        //        return true;

        //    var set = Database.GetHullNamesForPart(part);
        //    if (set == null)
        //        return true;

        //    foreach (var s in set)
        //    {
        //        if (G.GameData.parts.TryGetValue(s, out var hull) && (hull.shipType.name == "cl" || hull.shipType.name == "ca"))
        //        {
        //            __result = true;
        //            return false;
        //        }
        //    }

        //    return true;
        //}

    }

    // We can't target ref arguments in an attribute, so
    // we have to make this separate class to patch with a
    // TargetMethod call.
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_IsComponentAvailable
    {
        private static string _VesselName = string.Empty;

        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Ship), nameof(Ship.IsComponentAvailable), new Type[] { typeof(ComponentData), typeof(string).MakeByRefType() });
        }

        internal static bool Prefix(Ship __instance, ComponentData component, out string reason, ref bool __result, out float __state)
        {
            __state = component.weight;

            var weight = ComponentDataM.GetWeight(component, __instance.shipType.name);
            if (weight <= 0f)
            {
                __result = false;
                reason = "Ship Type";
                return false;
            }
            reason = string.Empty;
            component.weight = weight;
            return true;
        }

        internal static void Postfix(ComponentData component, float __state)
        {
            component.weight = __state;
        }
    }

    [HarmonyPatch(typeof(Ship.__c))]
    internal class Patch_Ship_c
    {
        // This method is called by the component selection process to set up
        // the weighted-random dictionary. So we need to patch it too. But
        // it doesn't know the ship in question. So we have to patch the calling
        // method to pass that on.
        [HarmonyPatch(nameof(Ship.__c._GenerateRandomShip_b__566_13))]
        [HarmonyPrefix]
        internal static bool Prefix_GenerateRandomShip_b__566_13(ComponentData c, ref float __result)
        {
            var ship = Patch_Ship._ShipForGenerateRandom;
            if (ship == null)
                return true;

            __result = ComponentDataM.GetWeight(c, ship.shipType.name);
            return false;
        }
    }

    [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__566))]
    internal class Patch_ShipGenRandom
    {
        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__566.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(Ship._GenerateRandomShip_d__566 __instance, out int __state, ref bool __result)
        {
            Patch_Ship._IsGenerating = true;
            Patch_Ship._ShipForGenerateRandom = __instance.__4__this;
            //Melon<TweaksAndFixes>.Logger.Msg($"In ship generation for {__instance.__4__this.vesselName}, state {__instance.__1__state}");

            // So we know what state we started in.
            __state = __instance.__1__state;
        }

        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__566.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._GenerateRandomShip_d__566 __instance, int __state)
        {
            Patch_Ship._IsGenerating = false;
            Patch_Ship._ShipForGenerateRandom = null;
            //Melon<TweaksAndFixes>.Logger.Msg($"Iteration for state {__state} ended, new state {__instance.__1__state}");
        }
    }
}
