using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using Il2CppSystem.Linq;
using UnityEngine.UI;
using Il2CppCoffee.UIExtensions;
using Il2CppTMPro;
using System.Collections;

#pragma warning disable CS8602
#pragma warning disable CS8604

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignController))]
    internal class Patch_CampaignController
    {
        [HarmonyPatch(nameof(CampaignController.GetSharedDesign))]
        [HarmonyPrefix]
        internal static bool Prefix_GetSharedDesign(CampaignController __instance, Player player, ShipType shipType, int year, bool checkTech, bool isEarlySavedShip, ref Ship __result)
        {
            __result = CampaignControllerM.GetSharedDesign(__instance, player, shipType, year, checkTech, isEarlySavedShip);
            return false;
        }

        // We're going to cache off relations before the adjustment
        // and then check for changes.
        internal struct RelationInfo
        {
            public bool isWar;
            public bool isAlliance;
            public float attitude;
            public bool isValid;
            public List<Player>? alliesA;
            public List<Player>? alliesB;

            public RelationInfo(Relation old)
            {
                isValid = true;

                isWar = old.isWar;
                isAlliance = old.isAlliance;
                attitude = old.attitude;

                // Hopefully the perf hit of the GC alloc is balanced
                // by doing it native (we could avoid the alloc by finding
                // these players, but it'd be in managed code)
                alliesA = new List<Player>();
                foreach (var p in old.a.InAllianceWith().ToList())
                    alliesA.Add(p);
                alliesB = new List<Player>();
                foreach (var p in old.b.InAllianceWith().ToList())
                    alliesB.Add(p);
            }

            public RelationInfo()
            {
                isValid = false;
                isWar = isAlliance = false;
                attitude = 0;
                alliesA = alliesB = null;
            }
        }
        private static bool _PassThroughAdjustAttitude = false;
        [HarmonyPatch(nameof(CampaignController.AdjustAttitude))]
        [HarmonyPrefix]
        internal static void Prefix_AdjustAttitude(CampaignController __instance, Relation relation, float attitudeDelta, bool canFullyAdjust, bool init, string info, bool raiseEvents, bool force, bool fromCommonEnemy, out RelationInfo __state)
        {
            if (init || _PassThroughAdjustAttitude || !Config.AllianceTweaks)
            {
                __state = new RelationInfo();
                return;
            }

            __state = new RelationInfo(relation);
        }

        [HarmonyPatch(nameof(CampaignController.AdjustAttitude))]
        [HarmonyPostfix]
        internal static void Postfix_AdjustAttitude(CampaignController __instance, Relation relation, float attitudeDelta, bool canFullyAdjust, bool init, string info, bool raiseEvents, bool force, bool fromCommonEnemy, RelationInfo __state)
        {
            if (init || !__state.isValid)
                return;

            // Don't cascade. AdjustAttitude calls itself a bunch of times.
            // If we're applying relation-change events, don't rerun for each
            // sub-call of this.
            _PassThroughAdjustAttitude = true;
            if (relation.isWar != __state.isWar)
            {
                if (__state.isWar)
                {
                    // at peace now
                    // *** Commented out for now.
                    // Eventually want to have alliance leaders make peace
                    // (except for the human player). But until that code's
                    // written, no point in removing the player from alliances
                    // because the game already does that.

                    // check if the human is allied to either
                    // and is at war too. If so, break the alliance.
                    // (We don't force the player into peace.)
                    //for (int i = __state.alliesA.Count; i-- > 0;)
                    //{
                    //    Player p = __state.alliesA[i];
                    //    if (!p.isAi)
                    //    {
                    //        var relA = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                    //        var relB = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                    //        if (relA.isAlliance && relB.isWar) // had better be true
                    //        {
                    //            __instance.AdjustAttitude(relA, -relA.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                    //            __state.alliesA.RemoveAt(i);
                    //        }
                    //        break;
                    //    }
                    //}
                    //for (int i = __state.alliesB.Count; i-- > 0;)
                    //{
                    //    Player p = __state.alliesB[i];
                    //    if (!p.isAi)
                    //    {
                    //        var relA = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                    //        var relB = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                    //        if (relB.isAlliance && relA.isWar) // had better be true
                    //        {
                    //            __instance.AdjustAttitude(relB, -relB.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                    //            __state.alliesB.RemoveAt(i);
                    //        }
                    //        break;
                    //    }
                    //}

                    // TODO: Do we want to have strongest nations sign for all others?
                }
                else
                {
                    // at war now

                    // First, find overlapping allies. They break
                    // both alliances.
                    for (int i = __state.alliesA.Count; i-- > 0;)
                    {
                        Player p = __state.alliesA[i];
                        for (int j = __state.alliesB.Count; j-- > 0;)
                        {
                            if (__state.alliesB[j] == p)
                            {
                                __state.alliesA.RemoveAt(i);
                                __state.alliesB.RemoveAt(j);
                                var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                                if (rel.isAlliance) // had better be true
                                    __instance.AdjustAttitude(rel, -rel.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                                rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                                if (rel.isAlliance)
                                    __instance.AdjustAttitude(rel, -rel.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                                break;
                            }
                        }
                    }

                    // All other allies declare war
                    foreach (var p in __state.alliesA)
                    {
                        var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                        if (!rel.isWar)
                            __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                    }
                    foreach (var p in __state.alliesB)
                    {
                        var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                        if (!rel.isWar)
                            __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                    }
                    // Allies declare war on each other
                    for (int i = __state.alliesA.Count; i-- > 0;)
                    {
                        Player a = __state.alliesA[i];
                        for (int j = __state.alliesB.Count; j-- > 0;)
                        {
                            Player b = __state.alliesB[i];
                            var rel = RelationExt.Between(__instance.CampaignData.Relations, a, b);
                            if (!rel.isWar)
                                __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                        }
                    }
                }
            }

            _PassThroughAdjustAttitude = false;
        }

        [HarmonyPatch(nameof(CampaignController.ScrapOldAiShips))]
        [HarmonyPrefix]
        internal static bool Prefix_ScrapOldAiShips(CampaignController __instance, Player player)
        {
            if (Config.ScrappingChange)
            {
                CampaignControllerM.HandleScrapping(__instance, player);
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(CampaignController.CheckPredefinedDesigns))]
        [HarmonyPrefix]
        internal static void Prefix_CheckPredefinedDesigns(CampaignController __instance, bool prewarm)
        {
            if (__instance._currentDesigns == null)
            {
                if (CampaignControllerM.TryLoadOverridePredefs(out var store, out int dCount))
                {
                    if (store != null)
                    {
                        __instance._currentDesigns = CampaignDesigns.FromStore(store);
                        Melon<TweaksAndFixes>.Logger.Msg($"Overrode predefined designs by loading {dCount} ships from {Config._PredefinedDesignsFile}");
                    }
                }
                else
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Tried to override predefined designs but failed to load {Config._PredefinedDesignsFile} correctly.");
                }
                if (store == null && Config.DontClobberTechForPredefs)
                {
                    var textAsset = Resources.Load<TextAsset>("packedShips");
                    store = Util.DeserializeObjectByte<CampaignDesigns.Store>(textAsset.bytes);
                    __instance._currentDesigns = CampaignDesigns.FromStore(store);
                }
            }

            if (Config.DontClobberTechForPredefs)
            {
                // we need to force the game not to check techs
                int startYear;
                int year;
                Melon<TweaksAndFixes>.Logger.Msg("check pw: " + prewarm);
                if (prewarm)
                    startYear = __instance.StartYear;
                else
                    startYear = __instance.CurrentDate.AsDate().Year;
                Melon<TweaksAndFixes>.Logger.Msg("checked, get year");
                __instance._currentDesigns.GetNearestYear(startYear, out year);
                __instance.initedForYear = year;
            }
        }

        internal static GameObject? _Batcher = null;
        [HarmonyPatch(nameof(CampaignController.OnLoadingScreenHide))]
        [HarmonyPostfix]
        internal static void Postfix_OnLoadingScreenHide()
        {
            if (!GameManager.IsMainMenu)
                return;

            if (_Batcher != null)
            {
                GameObject.DestroyImmediate(_Batcher);
                _Batcher = null;
            }

            _Batcher = GameObject.Instantiate(G.ui.popupUi.FindDeepChild("FileConverter").gameObject);
            GameObject.Destroy(_Batcher.GetComponent<SaveFilesConverter>());
            GameObject.Destroy(_Batcher.GetComponent<CanvasRenderer>());
            GameObject.Destroy(_Batcher.GetComponent<Image>());
            _Batcher.transform.SetParent(null);
            var root = _Batcher.transform.Find("Root").gameObject;
            root.transform.Find("JsonFiles").gameObject.SetActive(false);

            var years = root.transform.Find("BinFiles").gameObject;
            years.name = "YearsList";
            var yearsList = years.transform.Find("Viewport").Find("List").gameObject;
            yearsList.DestroyAllChilds();
            var listVLG = yearsList.GetOrAddComponent<VerticalLayoutGroup>();
            var fsmNew = GameObject.Instantiate(G.ui.gameObject.FindDeepChild("Fullscreen Mode"));
            var toggleTemplate = fsmNew.GetComponentInChildren<Toggle>();
            var label = fsmNew.transform.Find("Label");
            label.SetParent(toggleTemplate.gameObject);
            GameObject.Destroy(label.GetComponent<LocalizeText>());
            GameObject.Destroy(label.GetComponent<LayoutElement>());
            toggleTemplate.SetParent(yearsList);
            toggleTemplate.gameObject.AddComponent<HorizontalLayoutGroup>();
            var go = new GameObject("dummy");
            go.AddComponent<RectTransform>();
            go.transform.SetParent(toggleTemplate.transform);
            var bg = toggleTemplate.transform.Find("Background").gameObject;
            var le = bg.AddComponent<LayoutElement>();
            le.minWidth = 20f;
            toggleTemplate.SetActive(false);
            GameObject.DestroyImmediate(fsmNew);

            var buttons = root.transform.Find("Buttons");
            var start = buttons.transform.Find("Close").GetComponent<Button>();
            start.onClick.RemoveAllListeners();
            var yearsO = buttons.transform.Find("Convert").GetComponent<Button>();
            yearsO.onClick.RemoveAllListeners();
            var yearsC = buttons.transform.Find("Delete").GetComponent<Button>();
            yearsC.onClick.RemoveAllListeners();

            var upperRow = root.transform.Find("UpperButtons").gameObject;
            upperRow.DestroyAllChilds();
            var res = G.ui.gameObject.FindDeepChild("Resolution");
            var nationDropObj = GameObject.Instantiate(res, upperRow.transform);
            var typeDropObj = GameObject.Instantiate(res, upperRow.transform);
            var bugT = G.ui.popupUi.FindDeepChild("BugReport").FindDeepChild("Title");
            var numInputObj = GameObject.Instantiate(bugT, upperRow.transform);

            var progressBox = root.transform.Find("CheckingSaves").gameObject;
            progressBox.SetParent(_Batcher);
            var pcv = progressBox.AddComponent<Canvas>();
            var pcs = progressBox.AddComponent<CanvasScaler>();
            
            var rcv = root.AddComponent<Canvas>();
            var rcs = root.AddComponent<CanvasScaler>();

            var ucv = G.ui.GetComponent<Canvas>();
            var ucs = G.ui.GetComponent<CanvasScaler>();
            var ugr = G.ui.GetComponent<GraphicRaycaster>();

            pcv.renderMode = rcv.renderMode = ucv.renderMode;
            pcv.worldCamera = rcv.worldCamera = ucv.worldCamera;
            pcs.referenceResolution = rcs.referenceResolution = ucs.referenceResolution;
            pcs.screenMatchMode = rcs.screenMatchMode = ucs.screenMatchMode;
            pcs.uiScaleMode = rcs.uiScaleMode = ucs.uiScaleMode;
            var pgr = progressBox.AddComponent<GraphicRaycaster>();
            var rgr = root.AddComponent<GraphicRaycaster>();



            progressBox.SetActive(false);
            root.SetActive(false);

            _Batcher.SetActive(true);

            Melon<TweaksAndFixes>.Logger.Msg("Created batcher");

            var bsg = _Batcher.AddComponent<BatchShipGenerator>();
            bsg.yearsButton = yearsO;
            bsg.yearsPanel = years;
            bsg.yearToggleParent = yearsList.transform;
            bsg.yearTemplate = toggleTemplate;
            bsg.closeYearsPanel = yearsC;
            bsg.startButton = start;
            bsg.shipsAmount = numInputObj.GetComponentInChildren<TMP_InputField>();
            bsg.shipTypeDropdown = typeDropObj.GetComponentInChildren<TMP_Dropdown>();
            bsg.nationDropdown = nationDropObj.GetComponentInChildren<TMP_Dropdown>();
            bsg.InitRoot = root;
            bsg.UIRoot = progressBox;
            bsg.progress = progressBox.GetComponentInChildren<TextMeshProUGUI>();
            MelonCoroutines.Start(FixText(numInputObj, typeDropObj, nationDropObj));
        }

        internal static System.Collections.IEnumerator FixText(GameObject numInputObj, GameObject typeDropObj, GameObject nationDropObj)
        {
            yield return new WaitForEndOfFrame();
            var bsg = _Batcher.GetComponent<BatchShipGenerator>();
            var root = bsg.InitRoot;
            root.transform.Find("Header").GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = "Batch Ship Generator";
            root.transform.Find("Note").GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = "Note: Generation may take a long time and will generate many Shared Designs. Clear your designs first!";
            bsg.startButton.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Generate";
            bsg.yearsButton.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Show Years";
            bsg.closeYearsPanel.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Hide Years";
            nationDropObj.transform.Find("Label").GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = "Nations";
            typeDropObj.transform.Find("Label").GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = "Types";
            numInputObj.FindDeepChild("Placeholder").GetComponent<Il2CppTMPro.TextMeshProUGUI>().text = "Num Ships Per";
        }
    }
}
