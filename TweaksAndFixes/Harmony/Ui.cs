﻿using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UnityEngine.UI;
using static TweaksAndFixes.ModUtils;

#pragma warning disable CS8604
#pragma warning disable CS8625
#pragma warning disable CS8603

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Ui))]
    internal class Patch_Ui
    {
        internal static bool _InUpdateConstructor = false;

        [HarmonyPatch(nameof(Ui.Start))]
        [HarmonyPostfix]
        internal static void Postfix_Start(Ui __instance)
        {
            UpdateVersionString(__instance);
        }

        [HarmonyPatch(nameof(Ui.RefreshVersion))]
        [HarmonyPostfix]
        internal static void Postfix_RefreshVersion(Ui __instance)
        {
            UpdateVersionString(__instance);
        }

        internal static void UpdateVersionString(Ui ui)
        {
            if (G.GameData == null || G.GameData.paramsRaw == null || G.GameData.parms == null)
                return;

            int mode = (int)(Config.Param("taf_versiontext", 0f) + 0.01f);
            if (mode == 0)
                return;

            var vt = ui.overlayUi.Get("Version", false, false).Get<Text>("VersionText", false, false);
            string? text = Config.ParamS("taf_versiontext", string.Empty);
            switch (mode)
            {
                case 1: text = vt.text + " " + text; break;
                case 2: text = GameData.GameVersion + " " + text; break;
                // default: entirely replace
                case 4: text = string.Empty; break;
            }
            vt.text = text;
        }

        [HarmonyPatch(nameof(Ui.ChooseComponentType))]
        [HarmonyPrefix]
        internal static void Prefix_ChooseComponentType()
        {
            SpriteDatabase.Instance.OverrideResources();
        }

        // If we're autodesigning, allow selection of comptypes before parts

        // Done like this rather than KVP since that's weird managed-native
        private static readonly Il2CppSystem.Collections.Generic.List<NeedsDifferentItems> _NDIs = new Il2CppSystem.Collections.Generic.List<NeedsDifferentItems>();
        private static readonly Il2CppSystem.Collections.Generic.List<int> _NDIIdxs = new Il2CppSystem.Collections.Generic.List<int>();
        [HarmonyPatch(nameof(Ui.NeedComponentsActive))]
        [HarmonyPrefix]
        internal static void Prefix_NeedComponentsActive(CompType compType, ComponentData componentData, Ship checkShip, bool isAutoDesign, bool isNotComponentNeedUpdate)
        {
            if (!isAutoDesign || !Config.ShipGenReorder || !Patch_Ship._GenShipData.IsValid)
                return;

            for (int i = compType.needDiffItems.Count; i-- > 0;)
            {
                var ndi = compType.needDiffItems[i];
                if (Patch_Ship._GenShipData.SkipNDI(ndi))
                {
                    _NDIs.Add(ndi);
                    _NDIIdxs.Add(i);
                    compType.needDiffItems.RemoveAt(i);
                }
            }
        }

        [HarmonyPatch(nameof(Ui.NeedComponentsActive))]
        [HarmonyPostfix]
        internal static void Postfix_NeedComponentsActive(CompType compType, ComponentData componentData, Ship checkShip, bool isAutoDesign, bool isNotComponentNeedUpdate)
        {
            if (_NDIs.Count == 0)
                return;

            for (int i = _NDIs.Count; i-- > 0;)
                compType.needDiffItems.Insert(_NDIIdxs[i], _NDIs[i]);

            _NDIs.Clear();
            _NDIIdxs.Clear();
        }


        [HarmonyPatch(nameof(Ui.UpdateConstructor))]
        [HarmonyPrefix]
        internal static void Prefix_UpdateConstructor()
        {
            _InUpdateConstructor = true;
            Patch_Ui_c.Postfix_16(); // just in case we somehow died after running b15 and before b16
        }

        [HarmonyPatch(nameof(Ui.UpdateConstructor))]
        [HarmonyPostfix]
        internal static void Postfix_UpdateConstructor()
        {
            _InUpdateConstructor = false;
        }

        [HarmonyPatch(nameof(Ui.ConstructorUI))]
        [HarmonyPostfix]
        internal static void Postfix_ConstructorUI(Ui __instance)
        {
            ClearAllButtons(__instance);
            EnsureAllButtons(__instance);
        }

        [HarmonyPatch(nameof(Ui.RefreshConstructorInfo))]
        [HarmonyPrefix]
        internal static void Prefix_RefreshConstructorInfo(Ui __instance)
        {
            ClearAllButtons(__instance);
            SpriteDatabase.Instance.OverrideResources();
        }

        [HarmonyPatch(nameof(Ui.RefreshConstructorInfo))]
        [HarmonyPostfix]
        internal static void Postfix_RefreshConstructorInfo(Ui __instance)
        {
            EnsureAllButtons(__instance);
        }

        private static void ClearAllButtons(Ui ui)
        {
            if (ui == null || ui.gameObject == null)
                return;

            // Would be faster to drill down but this works.
            var objTCs = ui.gameObject.Get("TurretCalibers");
            if (objTCs != null)
                ClearButtons(objTCs);

            var objCase = ui.gameObject.Get("CasemateCalibers");
            if (objCase != null)
                ClearButtons(objCase);

            var objComps = FindArmamentsComponentList(ui);
            if (objComps != null)
                ClearButtons(objComps);
        }

        private static GameObject FindArmamentsComponentList(Ui ui)
        {
            string label = LocalizeManager.Localize("$comptypes_category_armament");
            var objComps = ui.gameObject.Get("Components");
            //Melon<TweaksAndFixes>.Logger.Msg($"Finding complist. Label {label}. Child count {objComps.transform.childCount}");

            for (int i = objComps.transform.childCount - 1; i-- > 0;)
            {
                var subTrf = objComps.transform.GetChild(i);
                //Melon<TweaksAndFixes>.Logger.Msg($"Object: {subTrf.gameObject.name}");
                if (!subTrf.gameObject.name.StartsWith("Header"))
                    continue;
                var text = subTrf.gameObject.GetComponentInChildren<Text>();
                if (text == null)
                    continue;

                //Melon<TweaksAndFixes>.Logger.Msg($"Found header text with {text.text}, compare to {label}");
                if (text.text != label)
                    continue;

                var nextObj = objComps.transform.GetChild(i + 1).gameObject;
                //Melon<TweaksAndFixes>.Logger.Msg($"Next object name is {nextObj.name}");
                if (nextObj.name.StartsWith("Components"))
                    return nextObj;
            }

            if (GameManager.IsConstructor)
                Melon<TweaksAndFixes>.Logger.Error("Could not find Armaments components list!");
            return null;
        }

        private static void ClearButtons(GameObject parent)
        {
            for (int i = parent.transform.childCount; i-- > 0;)
            {
                var subTrf = parent.transform.GetChild(i);
                if (subTrf == null || subTrf.gameObject == null || subTrf.gameObject.name != "ResetGrade")
                    continue;

                GameObject.DestroyImmediate(subTrf.gameObject);
            }
        }

        static List<Ship.TurretCaliber> _Turrets = new List<Ship.TurretCaliber>();
        static List<Ship.TurretCaliber> _Casemates = new List<Ship.TurretCaliber>();

        private static void EnsureAllButtons(Ui ui)
        {
            if (!GameManager.IsConstructor)
                return;

            if (PlayerController.Instance == null)
                return;
            var ship = PlayerController.Instance.Ship;
            if (ship == null)
                return;

            // Would be faster to drill down but this works.
            var objTCs = ui.gameObject.Get("TurretCalibers");
            if (objTCs == null)
                return;

            var objCase = ui.gameObject.Get("CasemateCalibers");
            if (objCase == null)
                return;

            if (ship.shipGunCaliber == null)
                return;

            // In case we blew up last execution
            _Turrets.Clear();
            _Casemates.Clear();

            // Part out the TCs
            foreach (var tc in ship.shipGunCaliber)
            {
                if (tc.isCasemateGun)
                    _Casemates.Add(tc);
                else
                    _Turrets.Add(tc);
            }

            _Turrets.Sort((a, b) => b.turretPartData.GetCaliber().CompareTo(a.turretPartData.GetCaliber()));
            EnsureTCButtons(ship, objTCs, _Turrets);

            _Casemates.Sort((a, b) => b.turretPartData.GetCaliber().CompareTo(a.turretPartData.GetCaliber()));
            EnsureTCButtons(ship, objCase, _Casemates);

            _Turrets.Clear();
            _Casemates.Clear();


            EnsureTorpButton(ship, ui);
        }

        private static void EnsureTCButtons(Ship ship, GameObject parent, List<Ship.TurretCaliber> tcs)
        {
            int idx = tcs.Count - 1;
            for (int i = parent.transform.childCount; i-- > 0 && idx >= 0;)
            {
                var subTrf = parent.transform.GetChild(i);
                if (subTrf == null)
                    continue;

                var obj = subTrf.gameObject;
                if (obj == null || !obj.activeSelf)
                    continue;

                var tc = tcs[idx--];
                if (tc == null || tc.turretPartData == null)
                    continue;
                if (!ship.TAFData().IsGradeOverridden(tc.turretPartData))
                    continue;

                var button = AddTCButton(obj, i + 1);
                if (button == null)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Could not add button for tc for part {tc.turretPartData.name}!");
                    continue;
                }
                button.onClick.RemoveAllListeners();
                float caliber = tc.turretPartData.caliber;
                bool isCasemate = tc.isCasemateGun;
                
                button.onClick.AddListener(new System.Action(() =>
                {
                    ship.TAFData().ResetGunGrade(caliber, isCasemate);
                }));
            }
        }

        private static Button AddTCButton(GameObject obj, int idx)
        {
            var buttonOld = obj.transform.GetChild("Less");
            if (buttonOld == null)
                return null;
            var textOld = obj.transform.GetChild("TextCaliber");
            if (textOld == null)
                return null;

            var buttonNew = GameObject.Instantiate(buttonOld);
            buttonNew.transform.SetParent(obj.transform.parent.transform, true);
            buttonNew.transform.SetSiblingIndex(idx);
            var textNew = GameObject.Instantiate(textOld);
            var le = textNew.GetComponent<LayoutElement>();
            if (le != null)
                GameObject.Destroy(le);
            var image = buttonNew.GetChild("Image");
            if (image != null && image.gameObject != null)
                GameObject.Destroy(image.gameObject);
            textNew.transform.SetParent(buttonNew.transform, true);
            textNew.name = "Text";
            var text = textNew.GetComponent<Text>();
            text.text = "Upgrade Mark";
            var trf = textNew.GetComponent<RectTransform>();
            trf.sizeDelta = new Vector2(150, 40);
            text.fontSize = 35;
            text.resizeTextMaxSize = 20;
            trf.anchoredPosition = new Vector2(114, -20);
            var button = buttonNew.GetComponent<Button>();
            button.interactable = true;

            button.gameObject.name = "ResetGrade";

            return button;
        }

        private static void EnsureTorpButton(Ship ship, Ui ui)
        {
            if (!ship.TAFData().IsTorpGradeOverridden())
                return;

            //Melon<TweaksAndFixes>.Logger.Msg("Adding torp upgrade button");
            var sName = ui.gameObject.Get("ShipNew");
            var buttonOld = sName == null ? null : sName.GetChild("Button", true);
            if (buttonOld == null)
            {
                Melon<TweaksAndFixes>.Logger.Error("Could not find button to clone!");
                return;
            }

            var compList = FindArmamentsComponentList(ui);
            if (compList == null)
                return;

            var buttonNew = GameObject.Instantiate(buttonOld);
            buttonNew.transform.SetParent(compList.transform, true);
            buttonNew.name = "ResetGrade";
            var image = buttonNew.GetChild("Image");
            if (image != null && image.gameObject != null)
                GameObject.Destroy(image.gameObject);

            var le = buttonNew.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 75;
            le.preferredWidth = 53;
            var text = buttonNew.transform.GetChild("Text").GetComponent<Text>();
            text.text = "Upgrade\nTorpedo\nMark";
            text.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            text.resizeTextMinSize = text.resizeTextMaxSize = 10;
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            buttonNew.GetChild("Bg").transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            var button = buttonNew.GetComponent<Button>();
            button.onClick.AddListener(new System.Action(() =>
            {
                ship.TAFData().ResetTorpGrade();
            }));

            buttonNew.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(Ui.__c))]
    internal class Patch_Ui_c
    {
        internal static bool _SetBackToBarbette = false;
        internal static PartData _BarbetteData = null;
        internal static bool _IsFirstCallofB15 = true;

        [HarmonyPatch(nameof(Ui.__c._UpdateConstructor_b__544_15))]
        [HarmonyPostfix]
        internal static void Postfix_15()
        {
            if (Patch_Ui._InUpdateConstructor && _IsFirstCallofB15 && Patch_Ship._GenerateShipState < 0 && G.ui.currentPart != null && G.ui.currentPart.isBarbette
                && G.ui.placingPart != null && !G.ui.placingPart.data.paramx.ContainsKey("center"))
            {
                _SetBackToBarbette = true;
                _BarbetteData = G.ui.currentPart;
                _BarbetteData.isBarbette = false;
                Patch_Part._IgnoreNextActiveBad = true;
            }
            _IsFirstCallofB15 = false;
        }

        [HarmonyPatch(nameof(Ui.__c._UpdateConstructor_b__544_16))]
        [HarmonyPostfix]
        internal static void Postfix_16()
        {
            _IsFirstCallofB15 = true;
            if (_SetBackToBarbette)
            {
                if (!Patch_Ui._InUpdateConstructor)
                    Melon<TweaksAndFixes>.Logger.Warning("Made it to end of UpdateConstructor with unrestored Barbette");

                _SetBackToBarbette = false;
                if (_BarbetteData != null)
                {
                    _BarbetteData.isBarbette = true;
                    _BarbetteData = null;
                }
            }
        }
    }
}
