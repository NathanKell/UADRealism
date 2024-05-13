using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(Ui))]
    internal class Patch_Ui
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ui.CompleteLoadingScreen))]
        internal static bool Prefix_CompleteLoadingScreen(Ui __instance)
        {
            if (!ShipStats.LoadingDone)
            {
                MelonCoroutines.Start(ShipStats.ProcessGameData());
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ui.RefreshConstructorInfo))]
        internal static void Postfix_RefreshConstructorInfo(Ui __instance)
        {
            var fineSlider = __instance.gameObject.FindDeepChild("Fineness");
            // We'll wait for ConstructorUI() to create this
            //if (fineSlider == null)
            //    fineSlider = AddFinenessSlider(__instance);
            if (fineSlider == null)
            {
                Debug.LogError("Refresh: Fineness slider null!");
                return;
            }

            UpdateFinenessSlider(fineSlider);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ui.ConstructorUI))]
        internal static void Postfix_ConstructorUI(Ui __instance)
        {
            if (!GameManager.IsConstructor)
                return;

            if (__instance.gameObject.FindDeepChild("Fineness") == null)
            {
                AddFinenessSlider(__instance);
            }
        }

        private static Il2CppSystem.Nullable<Color> _ColorNumber;
        private static Il2CppSystem.Object[] _FinenessLoc = new Il2CppSystem.Object[2];

        private static GameObject AddFinenessSlider(Ui ui)
        {
            _ColorNumber = new Il2CppSystem.Nullable<Color>(Ui.colorNumber);
            var beamSlider = ui.gameObject.FindDeepChild("Beam");
            if (beamSlider == null)
            {
                Debug.LogError("Couldn't find Beam slider!");
                return null;
            }

            var fineSlider = GameObject.Instantiate(beamSlider);
            fineSlider.name = "Fineness";
            fineSlider.transform.SetParent(beamSlider.transform.parent, false);
            fineSlider.active = true;
            fineSlider.transform.SetSiblingIndex(beamSlider.transform.GetSiblingIndex());
            //List<string> cmps = new List<string>();
            //foreach (var c in beamSlider.GetComponentsInChildren<Component>(true))
            //    cmps.Add($"{c.GetType()} on {c.gameObject.name}");
            //string beamC = string.Join(", ", cmps);
            //cmps.Clear();
            //foreach(var c in fineSlider.GetComponentsInChildren<Component>(true))
            //    cmps.Add($"{c.GetType()} on {c.gameObject.name}");
            //string fC = string.Join(", ", cmps);
            //Debug.Log($"Children of beam: {beamSlider.transform.childCount}; fine: {fineSlider.transform.childCount}.\nBeam components: {beamC}\nFine components: {fC}");
            var slider = fineSlider.GetComponentInChildren<UnityEngine.UI.Slider>();
            if (slider == null)
            {
                Debug.LogError("Can't find slider component!");
                return null;
            }

            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.value = 0f;
            ui.sliderValues[slider] = slider.value;
            slider.onValueChanged.AddListener(new System.Action<float>(val =>
            {
                if (GameManager.IsConstructor)
                {
                    var ship = G.ui.mainShip;
                    if (ship != null)
                    {
                        if (UndoCommandManager.CanRecordFor(slider))
                        {
                            if (!G.ui.sliderValues.TryGetValue(slider, out var oldVal))
                                oldVal = 0f;
                            UndoCommandManager.RecordSliderChange(slider, oldVal);
                            G.ui.sliderValues[slider] = val;
                        }
                        ship.hullPartSizeZ = val;
                        ship.RefreshHull(false); // let it figure out
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                    }
                }
            }));

            fineSlider.GetComponent<OnEnter>().action = new System.Action(() =>
            {
                G.ui.ShowTooltip(Ui.ApplyTextFormatting(LocalizeManager.Localize("$tooltip_con_fineness")), fineSlider);
            });

            fineSlider.GetComponent<OnLeave>().action = new System.Action(() =>
            {
                G.ui.HideTooltip();
            });

            UpdateFinenessSlider(fineSlider);

            return fineSlider;
        }

        private static void UpdateFinenessSlider(GameObject fineSlider)
        {
            var text = fineSlider.GetComponentInChildren<UnityEngine.UI.Text>();
            var slider = fineSlider.GetComponentInChildren<UnityEngine.UI.Slider>();

            if (text == null || slider == null)
            {
                Debug.LogError("Failed to find Fineness slider's children");
                return;
            }

            if (GameManager.IsConstructor)
            {
                var ship = G.ui.mainShip;
                if (ship != null)
                    slider.value = ship.hullPartSizeZ;
            }

            _FinenessLoc[0] = Ui.Colorize(_ColorNumber, slider.value.ToString("F0"), false);
            _FinenessLoc[1] = Ui.Colorize(_ColorNumber, "*", false);
            text.text = LocalizeManager.Localize("$Ui_Constr_FinenessDP01", _FinenessLoc);
        }
    }
}
