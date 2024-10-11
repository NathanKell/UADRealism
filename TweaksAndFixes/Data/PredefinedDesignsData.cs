﻿//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine.UI;
using Il2CppTMPro;
using System.ComponentModel.Design;
using UnityEngine.Windows.Speech;

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public class PredefinedDesignsData
    {
        public class PredefData
        {
            [Serializer.Field] public string name;
            [Serializer.Field] public string filename;
            [Serializer.Field] public float skipChance;
            [Serializer.Field] public int yearRange;
            public readonly Dictionary<string, List<int>> yearsPerPlayer = new Dictionary<string, List<int>>();
            public bool loadedData = false;
            public float _curSkipChance;
        }

        private static PredefinedDesignsData? _Instance = null;

        private static void Ensure()
        {
            if (_Instance == null)
            {
                _Instance = new PredefinedDesignsData();
            }
            if (!_Instance._loaded)
                _Instance.LoadData();
        }

        public static PredefinedDesignsData Instance
        {
            get
            {
                Ensure();
                return _Instance;
            }
        }

        private const string _StockAssetName = "<stock predefined designs>";
        private const string _StockAssetFilenameProxy = "--";
        private static GameObject? _BatchGenUI = null;

        private bool _loaded = false;
        private bool _noFile = true;
        private bool _lastLoadCared = false;
        public bool LastLoadWasRestrictive => _lastLoadCared;
        private readonly List<PredefData> _predefFileData = new List<PredefData>();
        private int _lastValidData = 0;
        private bool _needUseNewFindCode = false;
        public bool NeedUseNewFindCode => _needUseNewFindCode;
        public static bool NeedLoadRestrictive(bool prewarm)
            => CampaignController.Instance.designsUsage == CampaignController.DesignsUsage.FullPredefined ||
                            (prewarm && CampaignController.Instance.designsUsage != CampaignController.DesignsUsage.FullGenerated);

        private void LoadData()
        {
            _predefFileData.Clear();

            string text;
            if (Config._PredefinedDesignsDataFile.Exists)
            {
                _noFile = false;
                text = File.ReadAllText(Config._PredefinedDesignsDataFile.path);
            }
            else
            {
                _noFile = true;
                text = $"@{nameof(PredefData.name)},{nameof(PredefData.filename)},{nameof(PredefData.skipChance)},{nameof(PredefData.yearRange)}\nAutogenerated,{Config._PredefinedDesignsFile},0,-1";
            }

            // If a file is specified, even if it uses stock assets, it may use
            // a yearRange. So we can't trust it. And if we're not clobbering
            // tech, then we need to use the new logic.
            _needUseNewFindCode = !_noFile || Config.DontClobberTechForPredefs;

            Melon<TweaksAndFixes>.Logger.Msg("Loading predefined design database");
            Serializer.CSV.Read<List<PredefData>, PredefData>(text, _predefFileData);
            _loaded = true;
        }

        public bool LoadPredefSets(bool prewarm)
        {
            int totalCount = 0;
            _lastValidData = 0;
            _lastLoadCared = false; // if we fail, remember this load as less restrictive
            int iC = _predefFileData.Count;
            bool isLast = true;
            bool loadedNonStock = false;
            bool careAboutPredefs = NeedLoadRestrictive(prewarm);

            for (int i = iC; i-- > 0;)
            {
                var data = _predefFileData[i];
                data.yearsPerPlayer.Clear();
                data.loadedData = false;

                string? path;
                string errorFilename;
                if (data.filename == _StockAssetFilenameProxy)
                {
                    path = null;
                    errorFilename = _StockAssetName;
                }
                else
                {
                    errorFilename = data.filename;
                    path = Path.Combine(Config._BasePath, data.filename);
                    if (!File.Exists(path))
                    {
                        if (_noFile)
                        {
                            path = null;
                            errorFilename = _StockAssetName;
                        }
                        else
                        {
                            Melon<TweaksAndFixes>.Logger.Error($"Failed to find predefined designs file {errorFilename} referenced in {Config._PredefinedDesignsDataFile}");
                            if (careAboutPredefs)
                                return false;
                            else
                                continue;
                        }
                    }
                }

                bool careAboutThisPredef = isLast && careAboutPredefs;
                if (!LoadPredefs(path, out var store, out int dCount, careAboutThisPredef))
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Tried to load predefined designs file {errorFilename} but failed to load it correctly.");
                    if (careAboutThisPredef)
                        return false;
                    else
                        continue;
                }

                if (path != null)
                    loadedNonStock = true;

                if (isLast)
                {
                    isLast = false;
                    _lastValidData = i;
                    data._curSkipChance = 0;
                    CampaignController.Instance._currentDesigns = CampaignDesigns.FromStore(store);
                    if (_needUseNewFindCode)
                    {
                        foreach (var spy in store.shipsPerYear)
                        {
                            foreach (var spp in spy.Value.shipsPerPlayer)
                            {
                                data.yearsPerPlayer.ValueOrNew(spp.Key).Add(spy.Key);
                            }
                        }
                    }
                }
                else
                {
                    data._curSkipChance = data.skipChance;
                    foreach (var spy in store.shipsPerYear)
                    {
                        foreach (var spp in spy.Value.shipsPerPlayer)
                        {
                            data.yearsPerPlayer.ValueOrNew(spp.Key).Add(spy.Key);

                            string pName = $"{i}_{spp.Key}";
                            foreach (var spt in spp.Value.shipsPerType)
                            {
                                foreach (var ship in spt.Value)
                                {
                                    var oldName = ship.playerName;
                                    ship.playerName = pName;
                                    CampaignController.Instance._currentDesigns.AddShip(ship);
                                    ship.playerName = oldName;
                                }
                            }
                        }
                    }
                }
                foreach (var kvp in data.yearsPerPlayer)
                    kvp.Value.Sort();

                totalCount += dCount;
                data.loadedData = true;
                Melon<TweaksAndFixes>.Logger.Msg($"Loaded {dCount} designs from {errorFilename}");
            }
            if (loadedNonStock)
                Melon<TweaksAndFixes>.Logger.Msg($"Overrode predefined designs by loading {totalCount} ships.");
            _lastLoadCared = prewarm;
            return true;
        }

        public static bool LoadPredefs(string? path, out CampaignDesigns.Store? store, out int dCount, bool checkMissing)
        {
            dCount = 0;
            if (path == null)
            {
                path = _StockAssetName; // for logging
                var textAsset = Resources.Load<TextAsset>("packedShips");
                store = Util.DeserializeObjectByte<CampaignDesigns.Store>(textAsset.bytes);
            }
            else
            {
                var bytes = File.ReadAllBytes(path);
                store = Util.DeserializeObjectByte<CampaignDesigns.Store>(bytes);
                if (store == null)
                    return false;
            }

            if (store.shipsPerYear == null)
            {
                Melon<TweaksAndFixes>.Logger.Error("Failed to load shipsPerYear from " + path);
                return false;
            }

            foreach (var spy in store.shipsPerYear)
            {
                if (spy.Value.shipsPerPlayer == null)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Failed to load shipsPerPlayer for {spy.Key} from {path}");
                    return false;
                }

                foreach (var p in G.GameData.playersMajor.Values)
                {
                    if (checkMissing)
                    {
                        bool missing = true;
                        foreach (var spp2 in spy.Value.shipsPerPlayer)
                        {
                            if (spp2.Key == p.name)
                            {
                                missing = false;
                                break;
                            }
                        }
                        if (missing)
                        {
                            Melon<TweaksAndFixes>.Logger.Error($"Year {spy.Key} lacks nation {p.name} from {path}");
                            return false;
                        }
                    }
                }
                foreach (var spp in spy.Value.shipsPerPlayer)
                {
                    if (spp.Value.shipsPerType == null)
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Failed to load shipsPerType for {spy.Key} and player {spp.Key} from {path}");
                        return false;
                    }

                    foreach (var spt in spp.Value.shipsPerType)
                    {
                        if (spt.Value == null)
                        {
                            Melon<TweaksAndFixes>.Logger.Error($"Failed to load ship list for type {spt.Key} for {spy.Key} and player {spp.Key} from {path}");
                            return false;
                        }

                        dCount += spt.Value.Count;
                    }
                }
            }

            return true;
        }

        public Ship.Store GetRandomShip(Player player, ShipType type, int desiredYear)
        {
            string pName = player.data.name;

            int maxTechYear = Config.DontClobberTechForPredefs ? CampaignControllerM.CachePlayerTechs(player, true) : desiredYear;
            if (maxTechYear < Config.StartingYear)
                maxTechYear = Config.StartingYear;

            for (int i = 0; i < _predefFileData.Count; ++i)
            {
                var data = _predefFileData[i];
                if (!data.loadedData || data.skipChance > UnityEngine.Random.value || !data.yearsPerPlayer.TryGetValue(pName, out var ypp))
                    continue;

                string sppName =  i == _lastValidData ? pName : $"{i}_{pName}";

                int tries = 0;
                for (int j = ypp.Count; j-- > 0;)
                {
                    int year = ypp[j];
                    if (year > maxTechYear)
                        continue;
                    if (data.yearRange >= 0 ? year < maxTechYear - data.yearRange : tries++ > 0)
                        break;

                    if (!CampaignController.Instance._currentDesigns.shipsPerYear.TryGetValue(year, out var spy) || !spy.shipsPerPlayer.TryGetValue(sppName, out var spp))
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"Predefined designs: yearsPerPlayer claims year {year} exists but no spp for player {pName} exists here!");
                        if(spy != null)
                        {
                            string lstr = "Year " + year;
                            foreach (var spp2 in spy.shipsPerPlayer)
                            {
                                lstr += " " + spp2.Key;
                            }
                            Debug.Log(lstr);
                        }
                        Debug.Log("********** Full State of CD:");
                        foreach (var spy2 in CampaignController.Instance._currentDesigns.shipsPerYear)
                        {
                            string lstr = "Year " + spy2.Key;
                            foreach (var spp2 in spy2.Value.shipsPerPlayer)
                            {
                                lstr += " " + spp2.Key;
                            }
                            Debug.Log(lstr);
                        }
                        continue;
                    }
                    var ship = spp.RandomShipOfType(player, type);
                    if (ship != null)
                    {
                        if (Config.DontClobberTechForPredefs)
                            CampaignControllerM.CleanupSDCaches();
                        return ship;
                    }
                }
            }
            if (Config.DontClobberTechForPredefs)
                CampaignControllerM.CleanupSDCaches();

            return null;
        }

        public static void AddUIforBSG()
        {
            if (_BatchGenUI != null)
            {
                GameObject.DestroyImmediate(_BatchGenUI);
                _BatchGenUI = null;
            }

            _BatchGenUI = GameObject.Instantiate(G.ui.popupUi.FindDeepChild("FileConverter").gameObject);
            GameObject.Destroy(_BatchGenUI.GetComponent<SaveFilesConverter>());
            GameObject.Destroy(_BatchGenUI.GetComponent<CanvasRenderer>());
            GameObject.Destroy(_BatchGenUI.GetComponent<Image>());
            _BatchGenUI.transform.SetParent(null);
            var root = _BatchGenUI.transform.Find("Root").gameObject;
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
            progressBox.SetParent(_BatchGenUI);
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

            _BatchGenUI.SetActive(true);

            //Melon<TweaksAndFixes>.Logger.Msg("Created batcher UI");

            var bsg = _BatchGenUI.AddComponent<BatchShipGenerator>();
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
            //Melon<TweaksAndFixes>.Logger.Msg("Setup BSG");
            MelonCoroutines.Start(FixBSGText(numInputObj, typeDropObj, nationDropObj));
        }

        internal static System.Collections.IEnumerator FixBSGText(GameObject numInputObj, GameObject typeDropObj, GameObject nationDropObj)
        {
            // TODO: We could just keep the LocalizeText component?
            yield return new WaitForEndOfFrame();
            var bsg = _BatchGenUI.GetComponent<BatchShipGenerator>();
            var root = bsg.InitRoot;

            var locs = bsg.gameObject.GetComponentsInChildren<LocalizeText>(true);
            for (int i = locs.Length; i-- > 0;)
                GameObject.Destroy(locs[i]);
            yield return new WaitForEndOfFrame();

            root.transform.Find("Header").GetComponent<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_Title");
            root.transform.Find("Note").GetComponent<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_Note");
            bsg.startButton.GetComponentInChildren<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_Generate");
            bsg.yearsButton.GetComponentInChildren<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_ShowYears");
            bsg.closeYearsPanel.GetComponentInChildren<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_HideYears");
            nationDropObj.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_Nations");
            typeDropObj.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_Types");
            numInputObj.FindDeepChild("Placeholder").GetComponent<TextMeshProUGUI>().text = LocalizeManager.Localize("$TAF_Ui_BatchShipGenerator_NumShips");
        }
    }
}