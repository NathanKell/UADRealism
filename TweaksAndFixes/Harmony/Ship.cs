using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader.CoreClrUtils;

#pragma warning disable CS8625

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship
    {
        internal static int _GenerateShipState = -1;
        internal static bool _IsLoading = false;
        internal static Ship _ShipForLoading = null;
        internal static Ship.Store _StoreForLoading = null;
        internal static readonly GenerateShip _GenShipData = new GenerateShip();

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

            __result = __instance.TAFData().GunGrade(gun, __result);
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

            __result = __instance.TAFData().TorpedoGrade(__result);
        }

        [HarmonyPatch(nameof(Ship.AddedAdditionalTonnageUsage))]
        [HarmonyPrefix]
        internal static bool Prefix_AddedAdditionalTonnageUsage(Ship __instance)
        {
            ShipM.AddedAdditionalTonnageUsage(__instance);
            return false;
        }

        [HarmonyPatch(nameof(Ship.ReduceWeightByReducingCharacteristics))]
        [HarmonyPrefix]
        internal static bool Prefix_ReduceWeightByReducingCharacteristics(Ship __instance, Il2CppSystem.Random rnd, float tryN, float triesTotal, float randArmorRatio = 0, float speedLimit = 0)
        {
            ShipM.ReduceWeightByReducingCharacteristics(__instance, rnd, tryN, triesTotal, randArmorRatio, speedLimit);
            return false;
        }

        [HarmonyPatch(nameof(Ship.GenerateArmor))]
        [HarmonyPrefix]
        internal static bool Prefix_GenerateArmor(float armorMaximal, Ship shipHint, ref Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> __result)
        {
            __result = ShipM.GenerateArmorNew(armorMaximal, shipHint);
            return false;
        }

        internal static bool _IsInChangeHullWithHuman = false;
        // Work around difficulty in patching AdjustHullStats
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        [HarmonyPrefix]
        internal static void Prefix_ChangeHull(Ship __instance, ref bool byHuman)
        {
            if (byHuman)
            {
                byHuman = false;
                _IsInChangeHullWithHuman = true;
            }
        }
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        [HarmonyPostfix]
        internal static void Postfix_ChangeHull()
        {
            _IsInChangeHullWithHuman = false;
        }
        [HarmonyPatch(nameof(Ship.SetDraught))]
        [HarmonyPostfix]
        internal static void Postfix_SetDraught(Ship __instance)
        {
            // Do what ChangeHull would do in the byHuman block
            if (_IsInChangeHullWithHuman)
            {
                float tonnageLimit = Mathf.Min(__instance.tonnage, __instance.TonnageMax());
                float tonnageToSet = Mathf.Lerp(__instance.TonnageMin(), tonnageLimit, UnityEngine.Random.Range(0f, 1f));
                __instance.SetTonnage(tonnageToSet);
                var designYear = __instance.GetYear(__instance);
                var origTargetWeightRatio = 1f - Util.Remap(designYear, 1890f, 1940f, 0.63f, 0.52f, true);
                var stopFunc = new System.Func<bool>(() =>
                {
                    return (__instance.Weight() / __instance.Tonnage()) <= (1f - Util.Remap(designYear, 1890f, 1940f, 0.63f, 0.52f, true));
                });
                ShipM.AdjustHullStats(__instance, -1, origTargetWeightRatio, stopFunc, true, true, true, true, true, null, -1f, -1f);
            }
        }

        [HarmonyPatch(nameof(Ship.ChangeRefitShipTech))]
        [HarmonyPostfix]
        internal static void Postfix_ChangeRefitShipTech(Ship __instance, Ship newDesign)
        {
            __instance.TAFData().OnRefit(newDesign);
        }

        // Hook this just so we can run this after a random gun is added. Bleh.
        // We need to do this because, if we place a part of a _new_ caliber,
        // we need to check if we are now at the limit for caliber counts for
        // that battery, and if so remove all other-caliber datas from being
        // chosen.
        [HarmonyPatch(nameof(Ship.AddShipTurretArmor), new Type[] { typeof(Part) })]
        [HarmonyPostfix]
        internal static void Postfix_AddShipTurretArmor(Ship __instance, Part part)
        {
            if (!_GenShipData.IsValid)
                return;

            _GenShipData.OnAddTurretArmor(part);
        }

        [HarmonyPatch(nameof(Ship.CheckOperations))]
        [HarmonyPrefix]
        internal static bool Prefix_CheckOperation(Ship __instance, RandPart randPart, ref bool __result)
        {
            if (!Config.ShipGenReorder)
                return true;

            if (!_GenShipData.IsValid)
            {
                Debug.LogWarning("GenShipData null!\n" + NativeStackWalk.NativeStackTrace);
                return true;
            }
            if (!_GenShipData.IsRPAllowed(randPart))
            {
                __result = false;
                return false;
            }

            return true;
        }

        [HarmonyPatch(nameof(Ship.AddPart))]
        [HarmonyPrefix]
        internal static void Prefix_AddPart(Ship __instance, Part part, out float __state)
        {
            __state = -1f;
            if (!_GenShipData.IsValid|| !_GenShipData.AddingParts)
                return;

            __state = __instance.Weight();
        }

        [HarmonyPatch(nameof(Ship.AddPart))]
        [HarmonyPostfix]
        internal static void Postfix_AddPart(Ship __instance, Part part, float __state)
        {
            if (__state < 0f)
                return;

            _GenShipData.OnAddPart(__state, part);
        }

        [HarmonyPatch(nameof(Ship.RemovePart))]
        [HarmonyPrefix]
        internal static void Prefix_RemovePart(Ship __instance, Part part, out float __state)
        {
            __state = -1f;
            if (!_GenShipData.IsValid || !_GenShipData.AddingParts)
                return;

            __state = __instance.Weight();
        }

        [HarmonyPatch(nameof(Ship.RemovePart))]
        [HarmonyPostfix]
        internal static void Postfix_RemovePart(Ship __instance, Part part, float __state)
        {
            if (__state < 0f)
                return;

            _GenShipData.OnRemovePart(__state, part);
        }
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

        internal static bool Prefix(Ship __instance, ComponentData component, ref string reason, ref bool __result, out float __state)
        {
            __state = component.weight;

            var weight = ComponentDataM.GetWeight(component, __instance.shipType);
            
            //if (weight == component.weight)
            //    return true;
            //Melon<TweaksAndFixes>.Logger.Msg($"For component {component.name} and shipType {__instance.shipType.name}, overriding weight to {weight:F0}");

            if (weight <= 0f)
            {
                __result = false;
                reason = "Ship Type";
                return false;
            }
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
        // ALSO, it's code that's shared with IsComponentAvailable. But we
        // patch that by changing weight before and after the method. So there's
        // no need to do so here. So we abort if we're not in GenerateRandomShip.
        [HarmonyPatch(nameof(Ship.__c._GenerateRandomShip_b__562_13))]
        [HarmonyPrefix]
        internal static bool Prefix_GenerateRandomShip_b__562_13(ComponentData c, ref float __result)
        {
            if (!Patch_Ship._GenShipData.IsValid)
                return true;
            __result = ComponentDataM.GetWeight(c, Patch_Ship._GenShipData._ship.shipType);
            //if(__result != c.weight)
            //    Melon<TweaksAndFixes>.Logger.Msg($"Gen: For component {c.name} and shipType {Patch_Ship._GenerateRandomShipRoutine.__4__this.shipType.name}, overriding weight to {__result:F0}");
            return false;
        }

        //[HarmonyPatch(nameof(Ship.__c._IsComponentAvailable_b__1022_2))]
        //[HarmonyPrefix]
        //internal static bool Prefix_IsComponentAvailable_b__1022_2(ComponentData c, ref float __result)
        //{
        //}
    }

    // This runs when selecting all possible parts for a RP
    // but once an RP is having parts placed, we also need to
    // knock options out whenever a caliber is picked. See
    // AddShipTurretArmor above.
    [HarmonyPatch(typeof(Ship.__c__DisplayClass578_0))]
    internal class Patch_Ship_c_AddRandomParts578_0
    {
        [HarmonyPatch(nameof(Ship.__c__DisplayClass578_0._GetParts_b__0))]
        [HarmonyPrefix]
        internal static bool Prefix_b0(Ship.__c__DisplayClass578_0 __instance, PartData a, ref bool __result)
        {
            // Super annoying we can't prefix GetParts itself to do the RP caching
            if (Patch_Ship._GenShipData.OnGetParts_ShouldSkip(__instance, a))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__562))]
    internal class Patch_ShipGenRandom
    {
        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__562.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(Ship._GenerateRandomShip_d__562 __instance, out int __state, ref bool __result)
        {
            //if (__instance.__1__state == 0)
            if (!Patch_Ship._GenShipData.IsValid)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Creating GenShip for {__instance.__4__this.hull.data.name} {__instance.__4__this.vesselName}, state {__instance.__1__state}");
                Patch_Ship._GenShipData.Bind(__instance);
            }

            __state = __instance.__1__state;
            Patch_Ship._GenerateShipState = __state;
            var ship = __instance.__4__this;
            Patch_Ship._GenShipData.OnPrefix();
        }

        [HarmonyPatch(nameof(Ship._GenerateRandomShip_d__562.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._GenerateRandomShip_d__562 __instance, int __state, bool __result)
        {
            var ship = __instance.__4__this;
            Patch_Ship._GenShipData.OnPostfix(__state);

            if (__result == false)
            {
                Patch_Ship._GenShipData.OnGenerateEnd();
                Melon<TweaksAndFixes>.Logger.Msg($"*** Done GenShip for {__instance.__4__this.hull.data.name} {__instance.__4__this.vesselName}, state {__instance.__1__state} (passed {__state})");
                Patch_Ship._GenShipData.Reset();
            }

            Patch_Ship._GenerateShipState = -1;
            //Melon<TweaksAndFixes>.Logger.Msg($"GenerateRandomShip Iteration for state {__state} ended, new state {__instance.__1__state}");
        }
    }


    [HarmonyPatch(typeof(Ship._AddRandomPartsNew_d__579))]
    internal class Patch_Ship_AddRandParts
    {
        [HarmonyPatch(nameof(Ship._AddRandomPartsNew_d__579.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(Ship._AddRandomPartsNew_d__579 __instance, out int __state)
        {
            Patch_Ship._GenShipData.OnARPNPrefix(__instance);
            __state = __instance.__1__state;
            //Melon<TweaksAndFixes>.Logger.Msg($"Iteraing AddRandomPartsNew, state {__state}");
        }

        [HarmonyPatch(nameof(Ship._AddRandomPartsNew_d__579.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(Ship._AddRandomPartsNew_d__579 __instance, int __state, bool __result)
        {
            Patch_Ship._GenShipData.OnARPNPostfix(__state);
            if (!__result)
                Patch_Ship._GenShipData.OnARPNEnd();
            //Melon<TweaksAndFixes>.Logger.Msg($"AddRandomPartsNew Iteration for state {__state} ended, new state {__instance.__1__state}");
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
