using System;
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

namespace UADRealism
{
    [HarmonyPatch(typeof(Ui))]
    internal class Patch_Ui
    {
        private static GameObject _Fineness = null;
        private static Text _FineText = null;
        private static Slider _FineS = null;
        private static GameObject _Freeboard = null;
        private static Text _FreeText = null;
        private static Slider _FreeS = null;

        private static Text _BeamText = null;
        private static Text _DraughtText = null;
        private static Color _ColorNumber = new Color(0.667f, 0.8f, 0.8f, 1f);
        private static Il2CppSystem.Nullable<Color> _ColorNumberN = new Il2CppSystem.Nullable<Color>(_ColorNumber);
        private static Il2CppSystem.Object[] _LocArray = new Il2CppSystem.Object[2];

        internal static Ship _ShipForEnginePower = null;

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

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ui.RefreshConstructorInfo))]
        internal static void Prefix_RefreshConstructorInfo()
        {
            if (PlayerController.Instance != null)
                _ShipForEnginePower = PlayerController.Instance.Ship;
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
            if (_Freeboard == null)
                _Freeboard = __instance.gameObject.FindDeepChild("Freeboard");

            if (_Fineness == null || _Freeboard == null)
            {
                _BeamText = __instance.gameObject.FindDeepChild("Beam").GetComponentInChildren<Text>();
                _DraughtText = __instance.gameObject.FindDeepChild("Draught").GetComponentInChildren<Text>();
                if (_Fineness == null)
                    AddFinenessSlider(__instance);
                if(_Freeboard == null)
                    AddFreeboardSlider(__instance);

                UpdateSliders();
            }
        }

        private static void AddFinenessSlider(Ui ui)
        {
            var beamSlider = ui.gameObject.FindDeepChild("Beam");
            if (beamSlider == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Couldn't find Beam slider!");
                return;
            }

            _Fineness = GameObject.Instantiate(beamSlider);
            _Fineness.name = "Fineness";
            _Fineness.transform.SetParent(beamSlider.transform.parent, false);
            _Fineness.active = true;
            _Fineness.transform.SetSiblingIndex(beamSlider.transform.GetSiblingIndex());

            _FineS = _Fineness.GetComponentInChildren<UnityEngine.UI.Slider>();
            if (_FineS == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Can't find slider component!");
                GameObject.Destroy(_Fineness);
                return;
            }

            _FineS.onValueChanged.RemoveAllListeners();
            _FineS.minValue = 0f;
            _FineS.maxValue = ShipData._MaxFineness - ShipData._MinFineness;
            _FineS.value = ShipData._MinFineness;
            ui.sliderValues[_FineS] = _FineS.value;
            _FineS.onValueChanged.AddListener(new System.Action<float>(val =>
            {
            if (GameManager.IsConstructor)
            {
                var ship = G.ui.mainShip;
                    if (ship != null)
                    {
                        if (UndoCommandManager.CanRecordFor(_FineS))
                        {
                            if (!G.ui.sliderValues.TryGetValue(_FineS, out var oldVal))
                                oldVal = 0f;
                            UndoCommandManager.RecordSliderChange(_FineS, oldVal);
                            G.ui.sliderValues[_FineS] = val;
                        }
                        val = Mathf.Clamp(val, 0f, _FineS.maxValue);
                        float rounded = Mathf.RoundToInt(val);

                        SetFineness(ship, ShipData._MinFineness + rounded);
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

            _FineText = _Fineness.GetChild("Text").GetComponent<UnityEngine.UI.Text>();
            var edit = _Fineness.GetChild("Edit").GetComponent<UnityEngine.UI.InputField>();
            edit.text = (ShipData._MinFineness + _FineS.value).ToString("F0");

            var click = _FineText.GetComponent<OnClickH>();
            click.action = new System.Action<UnityEngine.EventSystems.PointerEventData>(pData =>
            {
                _FineText.SetActiveX(false);
                edit.SetActiveX(true);
                edit.Select();
                edit.ActivateInputField();
                edit.text = (ShipData._MinFineness + _FineS.value).ToString("F0");
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
                        val = Mathf.Clamp(val, ShipData._MinFineness, ShipData._MaxFineness);
                        SetFineness(ship, val);
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                        if (G.ui.textFieldValues.TryGetValue(edit, out var oldVal))
                        {
                            UndoCommandManager.RecordInputFieldChange(edit, oldVal);
                            G.ui.textFieldValues.Remove(edit);
                        }
                    }
                    _FineText.SetActiveX(true);
                    edit.SetActiveX(false);
                }
            }));
        }

        private static void AddFreeboardSlider(Ui ui)
        {
            var draughtSlider = ui.gameObject.FindDeepChild("Beam");
            if (draughtSlider == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Couldn't find Draught slider!");
                return;
            }

            _Freeboard = GameObject.Instantiate(draughtSlider);
            _Freeboard.name = "Freeboard";
            _Freeboard.transform.SetParent(draughtSlider.transform.parent, false);
            _Freeboard.active = true;
            _Freeboard.transform.SetSiblingIndex(draughtSlider.transform.GetSiblingIndex() + 1);

            _FreeS = _Freeboard.GetComponentInChildren<UnityEngine.UI.Slider>();
            if (_FreeS == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Freeboard: Can't find slider component!");
                GameObject.Destroy(_Freeboard);
                return;
            }

            _FreeS.onValueChanged.RemoveAllListeners();
            _FreeS.minValue = 0f;
            _FreeS.maxValue = (ShipData._MaxFreeboard - ShipData._MinFreeboard) / G.GameData.Param("beam_draught_step", 0.5f);
            _FreeS.value = _FreeS.maxValue * 0.5f;
            ui.sliderValues[_FreeS] = _FreeS.value;
            _FreeS.onValueChanged.AddListener(new System.Action<float>(val =>
            {
                if (GameManager.IsConstructor)
                {
                    var ship = G.ui.mainShip;
                    if (ship != null)
                    {
                        if (UndoCommandManager.CanRecordFor(_FreeS))
                        {
                            if (!G.ui.sliderValues.TryGetValue(_FreeS, out var oldVal))
                                oldVal = 0f;
                            UndoCommandManager.RecordSliderChange(_FreeS, oldVal);
                            G.ui.sliderValues[_FreeS] = val;
                        }
                        val = Mathf.Clamp(val, 0f, _FreeS.maxValue);
                        float step = G.GameData.Param("beam_draught_step", 0.5f);
                        float rounded = Mathf.RoundToInt(step * val * 10f) * 0.1f;

                        SetFreeboard(ship, ShipData._MinFreeboard + rounded);
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                    }
                }
            }));

            _Freeboard.GetComponent<OnEnter>().action = new System.Action(() =>
            {
                G.ui.ShowTooltip(Ui.ApplyTextFormatting(LocalizeManager.Localize("$tooltip_con_freeboard")), _Freeboard);
            });

            _Freeboard.GetComponent<OnLeave>().action = new System.Action(() =>
            {
                G.ui.HideTooltip();
            });

            _FreeText = _Freeboard.GetChild("Text").GetComponent<UnityEngine.UI.Text>();
            var edit = _Freeboard.GetChild("Edit").GetComponent<UnityEngine.UI.InputField>();
            edit.text = (ShipData._MinFreeboard + _FreeS.value * G.GameData.Param("beam_draught_step", 0.5f)).ToString("F1");

            var click = _FreeText.GetComponent<OnClickH>();
            click.action = new System.Action<UnityEngine.EventSystems.PointerEventData>(pData =>
            {
                _FreeText.SetActiveX(false);
                edit.SetActiveX(true);
                edit.Select();
                edit.ActivateInputField();
                edit.text = (ShipData._MinFreeboard + _FreeS.value * G.GameData.Param("beam_draught_step", 0.5f)).ToString("F1");
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
                        val = Mathf.Clamp(val, ShipData._MinFreeboard, ShipData._MaxFreeboard);
                        SetFreeboard(ship, val);
                        G.ui.OnConShipChanged(true);
                        G.ui.RefreshConstructorInfo();
                        if (G.ui.textFieldValues.TryGetValue(edit, out var oldVal))
                        {
                            UndoCommandManager.RecordInputFieldChange(edit, oldVal);
                            G.ui.textFieldValues.Remove(edit);
                        }
                    }
                    _FreeText.SetActiveX(true);
                    edit.SetActiveX(false);
                }
            }));
        }

        private static void SetFineness(Ship ship, float val)
        {
            // no need to recalc stats
            //ship.CStats();
            ship.ModData().SetFineness(val);
            if (ship.hull != null && ship.hull.middles != null && ship.ModData().SectionsFromFineness() != ship.hull.middles.Count)
                ship.RefreshHull(false);
            // would change ship.stats[item]'s value here
            //ship.statEffectsCache.Clear();
        }

        private static void SetFreeboard(Ship ship, float val)
        {
            ship.CStats();
            ship.ModData().SetFreeboard(val);
            if (ship.stats_.TryGetValue(G.GameData.stats["freeboard"], out var svFB))
            {
                svFB.basic = Util.Remap(val, ShipData._MinFreeboard, ShipData._MaxFreeboard, 0f, 100f);
                if (ship.statEffectsCache != null)
                    ship.statEffectsCache.Clear();
            }
            else
                Debug.LogError("Didn't find stat `freeboard` in ship stats!");
            ship.RefreshHull(false); // let it figure out
        }

        private static void UpdateSliders()
        {
            if (_FineText == null || _FreeText == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Failed to find Fineness/Freeboard slider's children");
                return;
            }

            float LdivB = 0f;
            float BdivT = 0f;

            if (GameManager.IsConstructor)
            {
                var ship = G.ui.mainShip;
                if (ship != null)
                {
                    _FineS.value = ship.ModData().Fineness;
                    _FreeS.value = (ship.ModData().Freeboard - ShipData._MinFreeboard) / G.GameData.Param("beam_draught_step", 0.5f);
                    var hData = ShipStats.GetData(ship.hull.data);
                    if (hData == null)
                    {
                        Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                        return;
                    }
                    else
                    {
                        var stats = hData._statsSet[ship.ModData().SectionsFromFineness()];
                        LdivB = stats.Lwl / (stats.B * (1f + ship.beam * 0.01f));
                        BdivT = stats.B / (stats.T * (1f + ship.draught * 0.01f)); // since draught is stored/set relative to beam
                    }
                }
            }

            _ColorNumberN.value = _ColorNumber;
            _ColorNumberN.has_value = true;
            
            _LocArray[0] = Ui.Colorize(_ColorNumberN, _FineS.value.ToString("F0"), false);
            // TODO track change
            _LocArray[1] = Ui.Colorize(_ColorNumberN, "*", false);
            _FineText.text = LocalizeManager.Localize("$Ui_Constr_FinenessDP01", _LocArray);

            _LocArray[0] = Ui.Colorize(_ColorNumberN, (ShipData._MinFreeboard + _FreeS.value * G.GameData.Param("beam_draught_step", 0.5f)).ToString("F1"), false);
            // TODO track change
            _LocArray[1] = Ui.Colorize(_ColorNumberN, "*", false);
            _FreeText.text = LocalizeManager.Localize("$Ui_Constr_FreeboardDP0Per1", _LocArray);

            _LocArray[0] = Ui.Colorize(_ColorNumberN, LdivB.ToString("F2"), false);
            _LocArray[1] = G.ui.beamWasChanged ? "*" : string.Empty;
            _BeamText.text = LocalizeManager.Localize("$Ui_Constr_BeamDP0Per1", _LocArray);

            _LocArray[0] = Ui.Colorize(_ColorNumberN, BdivT.ToString("F2"), false);
            _LocArray[1] = G.ui.draughtWasChanged ? "*" : string.Empty;
            _DraughtText.text = LocalizeManager.Localize("$Ui_Constr_DraughtDP0Per1", _LocArray);
        }

        // Engine

        [HarmonyPatch(nameof(Ui.GetShipDetailInfo))]
        [HarmonyPrefix]
        internal static void Prefix_GetShipDetailInfo(Ship ship)
        {
            _ShipForEnginePower = ship;
        }

        [HarmonyPatch(nameof(Ui.GetShipInfoText))]
        [HarmonyPrefix]
        internal static void Prefix_GetShipInfoText(Ship ship)
        {
            _ShipForEnginePower = ship;
        }

        [HarmonyPatch(nameof(Ui.RefreshShipInfo))]
        [HarmonyPrefix]
        internal static void Prefix_RefreshShipInfo(Ui __instance)
        {
            _ShipForEnginePower = GetRefreshShipInfoShip(__instance);
        }

        private static Ship GetRefreshShipInfoShip(Ui ui)
        {
            if (ui.hoveredShips != null && ui.hoveredShips.Count > 0)
                return ui.hoveredShips[0];

            if (ui.selectedShipMain != null)
                return ui.selectedShipMain;

            if (ui.selectedShips != null && ui.selectedShips.Count > 0)
                return ui.selectedShips[0];

            return null;
        }

        [HarmonyPatch(nameof(Ui.FormatEnginePower))]
        [HarmonyPrefix]
        internal static bool Prefix_FormatEnginePower(float hp, bool compact, bool isKnots, ref string __result)
        {
            string locstr;
            bool isI = _ShipForEnginePower == null ? false : (ShipStats.GetEngineIHPMult(_ShipForEnginePower) == 1f ? false : true);
            if (compact)
            {
                hp *= 0.001f;
                locstr = isI ? "$Ui_EnginePower_kIHP" : "$Ui_EnginePower_kSHP";
            }
            else
            {
                locstr = isI ? "$Ui_EnginePower_IHP" : "$Ui_EnginePower_SHP";
            }
            locstr = LocalizeManager.Localize(locstr);
            __result = $"{hp:N0} {locstr}";

            return false;
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
                    Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.ModData().SectionsFromFineness()];
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
                    Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.ModData().SectionsFromFineness()];
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
                    Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.ModData().SectionsFromFineness()];
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
                    Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(ship.hull.data));
                    return;
                }
                else
                {
                    var stats = hData._statsSet[ship.ModData().SectionsFromFineness()];
                    if (float.TryParse(valueStr, out var val) && val != 0)
                    {
                        valueStr = (((stats.B / val) / stats.T - 1f) * 100f).ToString();
                    }
                }
            }
        }
    }
}
