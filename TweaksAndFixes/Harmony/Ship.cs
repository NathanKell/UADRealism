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
        internal static bool _IsLoading = false;
        internal static Ship _ShipForLoading = null;
        internal static Ship.Store _StoreForLoading = null;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.ToStore))]
        internal static void Postfix_ToStore(Ship __instance, ref Ship.Store __result)
        {
            __instance.TAFData().ToStore(__result, false);
        }

        // We can't patch FromStore because it has a nullable argument.
        // It has multiple early-outs. We're skipping:
        // * shipType can't be found in GameData
        // * tech not in GameData.
        // * part hull not in GameData
        // * can't find design
        // But we will patch the regular case
        internal static void Postfix_FromStore(Ship __instance)
        {
            if (__instance != null && _StoreForLoading != null)
                __instance.TAFData().ToStore(_StoreForLoading, true);

            _IsLoading = false;
            _ShipForLoading = null;
            _StoreForLoading = null;
        }

        // Successful FromStore
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.Init))]
        internal static void Postfix_Init(Ship __instance)
        {
            if (_IsLoading)
                Postfix_FromStore(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.TechGunGrade))]
        internal static void Postfix_TechGunGrade(Ship __instance, PartData gun, bool requireValid, ref int __result)
        {
            // Let's hope the gun grade cache is only used in this method!
            // If it's used elsewhere, we won't catch that case. The reason
            // is that we can't patch the cache if we want to use it at all,
            // because we need to preserve the _real_ grade but we also
            // don't want to cache-bust every time.
            int newGrade = __instance.TAFData().GunGrade(gun, __result);
            if (newGrade != __result)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"For ship {__instance.name}, replaced gun grade for part {gun.name} with {newGrade} (was {__result})");
                __result = newGrade;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.TechTorpedoGrade))]
        internal static void Postfix_TechTorpedoGrade(Ship __instance, PartData torpedo, bool requireValid, ref int __result)
        {
            // Let's hope the torp grade cache is only used in this method!
            // If it's used elsewhere, we won't catch that case. The reason
            // is that we can't patch the cache if we want to use it at all,
            // because we need to preserve the _real_ grade but we also
            // don't want to cache-bust every time.
            int newGrade = __instance.TAFData().TorpedoGrade(__result);
            if (newGrade != __result)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"For ship {__instance.name}, replaced torpedo grade for part {torpedo.name} with {newGrade} (was {__result})");
                __result = newGrade;
            }
        }

        // I'm sure more will get patched later.
        //[HarmonyPatch(nameof(Ship.IsPartAvailable), new Type[] { typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship) })]
        //[HarmonyPrefix]
        //internal static bool Prefix_IsPartAvailable(PartData part, ref bool __result)
        //{
        //    var year = Database.GetYear(part);
        //    if (year < 1860 || year > 1920)
        //        return true;

        //    if (part.isHull)
        //    {
        //        string hmk = GetHullModelKey(part);
        //        foreach (var p in G.GameData.parts.Values)
        //        {
        //            if (!p.isHull || Database.GetYear(p) > 1920)
        //                continue;
        //            if (p == part)
        //            {
        //                __result = true;
        //                return false;
        //            }
        //            if (hmk == GetHullModelKey(p))
        //            {
        //                __result = false;
        //                return false;
        //            }
        //        }
        //    }

        //    //var set = Database.GetHullNamesForPart(part);
        //    //if (set == null)
        //    //    return true;

        //    //foreach (var s in set)
        //    //{
        //    //    if (G.GameData.parts.TryGetValue(s, out var hull) && (hull.shipType.name == "cl" || hull.shipType.name == "ca"))
        //    //    {
        //    //        __result = true;
        //    //        return false;
        //    //    }
        //    //}

        //    return true;
        //}

    }

    // We can't target ref arguments in an attribute, so
    // we have to make this separate class to patch with a
    // TargetMethod call.
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship_IsComponentAvailable
    {
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
            if(__state == 0)
                __instance.__4__this.TAFData().ResetAllGrades();
        }

        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__566.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._GenerateRandomShip_d__566 __instance, int __state)
        {
            // For now, we're going to reset all grades regardless.
            //if (__state == 1 && (!__instance._isRefitMode_5__2 || !__instance.isSimpleRefit))
            //    __instance.__4__this.TAFData().ResetAllGrades();

            Patch_Ship._IsGenerating = false;
            Patch_Ship._ShipForGenerateRandom = null;
            //Melon<TweaksAndFixes>.Logger.Msg($"Iteration for state {__state} ended, new state {__instance.__1__state}");
        }
    }

    [HarmonyPatch(typeof(VesselEntity))]
    internal class Patch_VesselEntityFromStore
    {
        // Harmony can't patch methods that take nullable arguments.
        // So instead of patching Ship.FromStore() we have to patch
        // this, which it calls near the start.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(VesselEntity.FromBaseStore))]
        internal static void Prefix_FromBaseStore(VesselEntity __instance, VesselEntity.VesselEntityStore store, bool isSharedDesign)
        {
            Ship ship = __instance.GetComponent<Ship>();
            if (ship == null)
                return;

            var sStore = store.TryCast<Ship.Store>();
            if (sStore == null)
                return;

            if (sStore.mission != null && LoadSave.Get(sStore.mission, G.GameData.missions) == null)
                return;

            Patch_Ship._IsLoading = true;
            Patch_Ship._ShipForLoading = ship;
            Patch_Ship._StoreForLoading = sStore;
            ship.TAFData().FromStore(sStore);
        }
    }
}
