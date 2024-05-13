using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UnityEngine.UI;

namespace UADRealism
{
    [HarmonyPatch(typeof(Ui))]
    internal class Patch_Ui
    {
        private static GameObject _Fineness = null;
        private static Text _BeamText = null;
        private static Text _DraughtText = null;
        private static Il2CppSystem.Nullable<Color> _ColorNumber = new Il2CppSystem.Nullable<Color>(new Color(0.667f, 0.8f, 0.8f, 1f));
        private static Il2CppSystem.Object[] _LocArray = new Il2CppSystem.Object[2];

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
            if (_Fineness != null)
                UpdateSliders();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ui.ConstructorUI))]
        internal static void Postfix_ConstructorUI(Ui __instance)
        {
            if (!GameManager.IsConstructor)
                return;

            // Just in case we get out of sync
            if (_Fineness == null)
                _Fineness = __instance.gameObject.FindDeepChild("Fineness");

            if(_Fineness == null)
            {
                _BeamText = __instance.gameObject.FindDeepChild("Beam").GetComponentInChildren<Text>();
                _DraughtText = __instance.gameObject.FindDeepChild("Draught").GetComponentInChildren<Text>();
                AddFinenessSlider(__instance);
            }
        }

        private static void AddFinenessSlider(Ui ui)
        {
            var beamSlider = ui.gameObject.FindDeepChild("Beam");
            if (beamSlider == null)
            {
                Debug.LogError("Couldn't find Beam slider!");
                return;
            }

            _Fineness = GameObject.Instantiate(beamSlider);
            _Fineness.name = "Fineness";
            _Fineness.transform.SetParent(beamSlider.transform.parent, false);
            _Fineness.active = true;
            _Fineness.transform.SetSiblingIndex(beamSlider.transform.GetSiblingIndex());

            var slider = _Fineness.GetComponentInChildren<UnityEngine.UI.Slider>();
            if (slider == null)
            {
                Debug.LogError("Can't find slider component!");
                GameObject.Destroy(_Fineness);
                return;
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
                        SetFineness(ship, val);
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                    }
                }
            }));

            _Fineness.GetComponent<OnEnter>().action = new System.Action(() =>
            {
                G.ui.ShowTooltip(Ui.ApplyTextFormatting(LocalizeManager.Localize("$tooltip_con_fineness")), _Fineness);
            });

            _Fineness.GetComponent<OnLeave>().action = new System.Action(() =>
            {
                G.ui.HideTooltip();
            });

            var text = _Fineness.GetChild("Text").GetComponent<UnityEngine.UI.Text>();
            var edit = _Fineness.GetChild("Edit").GetComponent<UnityEngine.UI.InputField>();
            edit.text = slider.value.ToString("F0");

            var click = text.GetComponent<OnClickH>();
            click.action = new System.Action<UnityEngine.EventSystems.PointerEventData>(pData =>
            {
                text.SetActiveX(false);
                edit.SetActiveX(true);
                edit.Select();
                edit.ActivateInputField();
                edit.text = slider.value.ToString("F0");
                G.ui.textFieldValues[edit] = edit.text;
            });

            edit.onEndEdit.RemoveAllListeners();
            edit.onEndEdit.AddListener(new System.Action<string>(value =>
            {
                if (G.ui.allowEdit && float.TryParse(value, out var val))
                {
                    var ship = G.ui.mainShip;
                    if (ship != null)
                    {
                        val = Mathf.Clamp(val, 0f, 100f);
                        SetFineness(ship, val);
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                        if (G.ui.textFieldValues.TryGetValue(edit, out var oldVal))
                        {
                            UndoCommandManager.RecordInputFieldChange(edit, oldVal);
                            G.ui.textFieldValues.Remove(edit);
                        }
                    }
                    text.SetActiveX(true);
                    edit.SetActiveX(false);
                }
            }));

            UpdateSliders();
        }

        private static void SetFineness(Ship ship, float val)
        {
            // no need to recalc stats
            //ship.CStats();
            ship.hullPartSizeZ = val;
            ship.RefreshHull(false); // let it figure out
            // would change ship.stats[item]'s value here
            //ship.statEffectsCache.Clear();
        }

        private static void UpdateSliders()
        {
            var text = _Fineness.GetComponentInChildren<UnityEngine.UI.Text>();
            var slider = _Fineness.GetComponentInChildren<UnityEngine.UI.Slider>();

            if (text == null || slider == null)
            {
                Debug.LogError("Failed to find Fineness slider's children");
                return;
            }

            float LdivB = 0f;
            float BdivT = 0f;

            if (GameManager.IsConstructor)
            {
                var ship = G.ui.mainShip;
                if (ship != null)
                {
                    slider.value = ship.hullPartSizeZ;
                    var hData = ShipStats.GetData(ship.hull.data);
                    if (hData == null)
                    {
                        Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                        return;
                    }
                    else
                    {
                        var stats = hData._statsSet[ship.hull.middles.Count];
                        LdivB = stats.Lwl / (stats.B * (1f + ship.beam * 0.01f));
                        BdivT = stats.B / (stats.T * (1f + ship.draught * 0.01f)); // since draught is stored/set relative to beam
                    }
                }
            }


            _LocArray[0] = Ui.Colorize(_ColorNumber, slider.value.ToString("F0"), false);
            // TODO track change
            _LocArray[1] = Ui.Colorize(_ColorNumber, "*", false);
            text.text = LocalizeManager.Localize("$Ui_Constr_FinenessDP01", _LocArray);

            _LocArray[0] = Ui.Colorize(_ColorNumber, LdivB.ToString("F2"), false);
            _LocArray[1] = G.ui.beamWasChanged ? "*" : string.Empty;
            _BeamText.text = LocalizeManager.Localize("$Ui_Constr_BeamDP0Per1", _LocArray);

            _LocArray[0] = Ui.Colorize(_ColorNumber, BdivT.ToString("F2"), false);
            _LocArray[1] = G.ui.draughtWasChanged ? "*" : string.Empty;
            _DraughtText.text = LocalizeManager.Localize("$Ui_Constr_DraughtDP0Per1", _LocArray);
        }
    }

    [HarmonyPatch(typeof(Ui.__c__DisplayClass490_4))]
    internal class Patch_Ui_DisplayClass490_4
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ui.__c__DisplayClass490_4._RefreshConstructorInfo_b__9))]
        internal static void Postfix_BeamSet_InputStart(Ui.__c__DisplayClass490_4 __instance)
        {
            var ship = G.ui.mainShip;
            if (ship != null)
            {
                var hData = ShipStats.GetData(ship.hull.data);
                if (hData == null)
                {
                    Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.hull.middles.Count];
                    __instance.textBeamEdit.text = (stats.Lwl / (stats.B * (1f + ship.beam * 0.01f))).ToString("F4");
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ui.__c__DisplayClass490_4._RefreshConstructorInfo_b__10))]
        internal static void Prefix_BeamSet_InputEnd(Ui.__c__DisplayClass490_4 __instance, ref string valueStr)
        {
            var ship = G.ui.mainShip;
            if (ship != null)
            {
                var hData = ShipStats.GetData(ship.hull.data);
                if (hData == null)
                {
                    Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.hull.middles.Count];
                    if (float.TryParse(valueStr, out var val) && val != 0)
                    {
                        valueStr = (((stats.Lwl / val) / stats.B - 1f) * 100f).ToString();
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ui.__c__DisplayClass490_4._RefreshConstructorInfo_b__12))]
        internal static void Prefix_DraughtSet_InputStart(Ui.__c__DisplayClass490_4 __instance)
        {
            var ship = G.ui.mainShip;
            if (ship != null)
            {
                var hData = ShipStats.GetData(ship.hull.data);
                if (hData == null)
                {
                    Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.hull.middles.Count];
                    __instance.textDraughtEdit.text = (stats.B / (stats.T * (1f + ship.draught * 0.01f))).ToString("F4");
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ui.__c__DisplayClass490_4._RefreshConstructorInfo_b__13))]
        internal static void Prefix_DraughtSet_InputEnd(Ui.__c__DisplayClass490_4 __instance, ref string valueStr)
        {
            var ship = G.ui.mainShip;
            if (ship != null)
            {
                var hData = ShipStats.GetData(ship.hull.data);
                if (hData == null)
                {
                    Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.hull.middles.Count];
                    if (float.TryParse(valueStr, out var val) && val != 0)
                    {
                        valueStr = (((stats.B / val) / stats.T - 1f) * 100f).ToString();
                    }
                }
            }
        }
    }
}
