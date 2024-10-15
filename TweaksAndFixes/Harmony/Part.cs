using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Reflection;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Startup;

#pragma warning disable CS8603

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Part))]
    internal class Patch_Part
    {
        //private static string? _MountErrorLoc = null;
        internal static bool _IgnoreNextActiveBad = false;

        [HarmonyPatch(nameof(Part.SetVisualMode))]
        [HarmonyPrefix]
        internal static void Prefix_SetVisualMode(Part __instance, ref Part.VisualMode m)
        {
            if (m == Part.VisualMode.ActiveBad && _IgnoreNextActiveBad) //Patch_Ui._InUpdateConstructor && ((Patch_Ui_c._SetBackToBarbette && __instance.data == Patch_Ui_c._BarbetteData) || __instance.data.isBarbette))
            {
                _IgnoreNextActiveBad = false;
                //if (_MountErrorLoc == null)
                //    _MountErrorLoc = LocalizeManager.Localize("$Ui_Constr_MustPlaceOnMount");

                //if (G.ui.constructorCentralText2.text.Contains(_MountErrorLoc) || G.ui.constructorCentralText2.text == "mount1")
                if (Part.CanPlaceGeneric(__instance.data, __instance.ship == null ? G.ui.mainShip : __instance.ship, true, out _) && !__instance.CanPlace(out var deny) && (deny == "mount 1" || deny == "mount1"))
                {
                    m = Part.VisualMode.Highlight;
                    //if (/*(__instance.data.isWeapon || __instance.data.isBarbette) &&*/ G.ui.placingPart == __instance)
                    //{
                    if (!Util.FocusIsInInputField())
                    {
                        if (GameManager.CanHandleKeyboardInput())
                        {
                            var b = G.settings.Bindings;
                            float angle = UnityEngine.Input.GetKeyDown(b.RotatePartLeft.Code) ? -45f :
                                UnityEngine.Input.GetKeyDown(b.RotatePartRight.Code) ? 45f : 0f;
                            if (angle != 0f)
                            {
                                __instance.transform.Rotate(Vector3.up, angle);
                                __instance.AnimateRotate(angle);
                                G.ui.OnConShipChanged(false);
                            }
                        }
                    }
                    //}
                }
            }
        }

        //[HarmonyPatch(nameof(Part.TryFindMount))]
        //[HarmonyPostfix]
        internal static void Postfix_TryFindMount(Part __instance, bool autoRotate)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"Called TryFindMount on {__instance.name} ({__instance.data.name}) {(__instance.mount != null ? "Mounted" : string.Empty)}");
            if (!__instance.CanPlace(out string denyReason))
            {
                Melon<TweaksAndFixes>.Logger.Msg($"Can't place. Deny reason {(denyReason == null ? "<null>" : denyReason)}");
            }
        }

        //[HarmonyPatch(nameof(Part.AnimateRotate))]
        //[HarmonyPrefix]
        //internal static void Prefix_AnimateRotate(Part __instance, float angle)
        //{
        //    Melon<TweaksAndFixes>.Logger.Msg($"Rotate {__instance.name} by {angle:F0}");
        //}
    }

    // We can't target ref arguments in an attribute, so
    // we have to make this separate class to patch with a
    // TargetMethod call.
    //[HarmonyPatch(typeof(Part))]
    //internal class Patch_Part_CanPlace
    //{
    //    internal static MethodBase TargetMethod()
    //    {
    //        //return AccessTools.Method(typeof(Part), nameof(Part.CanPlace), new Type[] { typeof(string).MakeByRefType(), typeof(List<Part>).MakeByRefType(), typeof(List<Collider>).MakeByRefType() });

    //        // Do this manually
    //        var methods = AccessTools.GetDeclaredMethods(typeof(Part));
    //        foreach (var m in methods)
    //        {
    //            if (m.Name != nameof(Part.CanPlace))
    //                continue;

    //            if (m.GetParameters().Length == 3)
    //                return m;
    //        }

    //        return null;
    //    }

    //    internal static void Postfix()//Part __instance, string denyReason, ref bool __result) //, out List<Part> overlapParts, out List<Collider> overlapBorders)
    //    {
    //        // We could try to be fancier, but let's just clobber.
    //        // Note we won't necessarily be in the midst of the barbette patch, so
    //        // we can't rely on checking that. But it's possible the reset failed,
    //        // so we take the setback case too.
    //        //if (Patch_Ui._InUpdateConstructor && ((Patch_Ui_c._SetBackToBarbette && __instance.data == Patch_Ui_c._BarbetteData) || __instance.data.isBarbette))
    //        //{
    //        //    if (denyReason == "mount1")
    //        //        __result = true;
    //        //}

    //    }
    //}

    //internal unsafe class Native_Part
    //{
    //    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    //    private delegate IntPtr CanPlaceDelegate(
    //        IntPtr _this, // Part *
    //        IntPtr* denyReason, // ystem_String **
    //        IntPtr* overlapParts, // System_Collections_Generic_List_Part **
    //        IntPtr* overlapBorders, // System_Collections_Generic_List_Collider **
    //        IntPtr methodInfo); // const MethodInfo *

    //    private static CanPlaceDelegate _patchCanPlace;
    //    private static CanPlaceDelegate _original;
    //    private static bool _hasPatched = false;

    //    public static unsafe IntPtr CanPlace(IntPtr _this, IntPtr* denyReason, IntPtr* overlapParts, IntPtr* overlapBorders, IntPtr methodInfo)
    //    {
    //        IntPtr result = _original(_this, denyReason, overlapParts, overlapBorders, methodInfo);
    //        //bool inResult = IL2CPP.PointerToValueGeneric<bool>(result, false, false);

    //        //bool* ptr = (bool*) result.ToPointer();
    //        //inResult = true;
    //        //*ptr = inResult;
    //        Melon<TweaksAndFixes>.Logger.Msg("CanPlace");

    //        return result;
    //    }

    //    private static MethodInfo GetCanPlace()
    //    {
    //        var methods = AccessTools.GetDeclaredMethods(typeof(Part));
    //        foreach (var m in methods)
    //        {
    //            if (m.Name != nameof(Part.CanPlace))
    //                continue;

    //            if (m.GetParameters().Length == 3)
    //                return m;
    //        }

    //        return null;
    //    }

    //    public static unsafe void Patch()
    //    {
    //        if (_hasPatched)
    //            return;

    //        _hasPatched = true;

    //        CanPlaceDelegate canPlacePatch = CanPlace;

    //        var method = GetCanPlace();

    //        var tgtPtr = *(IntPtr*)(IntPtr)typeof(Part).GetField("NativeMethodInfoPtr_CanPlace_Public_Boolean_byref_String_byref_List_1_Part_byref_List_1_Collider_0", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    //        var dstPtr = canPlacePatch.Method.MethodHandle.GetFunctionPointer();

    //        MelonUtils.NativeHookAttach((IntPtr)(&tgtPtr), dstPtr);

    //        _original = Marshal.GetDelegateForFunctionPointer<CanPlaceDelegate>(tgtPtr);

    //        MelonLogger.Msg("Patched!");
    //    }
    //}
}
