//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using TweaksAndFixes;

#pragma warning disable CS8600

namespace TweaksAndFixes
{
    public static class GameDataM
    {
        public static void Update(GameData _this)
        {
            switch (_this.state)
            {
                case GameData.State.LoadVersion:
                    //MonoBehaviourExt.Fail();
                    return;
                case GameData.State.LoadData:
                    if (!_this.forceUseLocal)
                    {
                        //MonoBehaviourExt.Fail();
                        return;
                    }

                    string loadingText = null;
                    // It is unclear why stock does this, since it doesn't yield or fail, so why check for remaining loaders.
                    // But sure, let's replciate it.
                    var newList = new Il2CppSystem.Collections.Generic.List<GameData.LoadInfo>();
                    foreach (var l in _this.loaders)
                        newList.Add(l);
                    foreach (var l in newList)
                    {
                        _this.loaders.Remove(l);
                        ProcessLoadInfo(_this, l);
                    }
                    if (_this.loaders.Count > 0)
                    {
                        string loc = LocalizeManager.Localize("$Ui_Loading");
                        string name = Util.CamelCaseSplit(_this.loaders[0].name, ";", true);
                        loadingText = loc + ";" + name;
                    }
                    G.ui._loadingText_k__BackingField = loadingText;
                    if (_this.loaders.Count == 0)
                        _this.DataLoaded();

                    return;
            }
        }

        private static void ProcessLoadInfo(GameData _this, GameData.LoadInfo l)
        {
            string? text = GetTextFromFile(l.name);
            if (text == null)
            {
                l.process.Invoke(l);
                return;
            }

            switch (l.name)
            {
                case "loadingScreens":
                    _this.loadingScreens = LoadCSV(text, _this.loadingScreens);
                    _this.loadingScreens = Serializer.CSV.ProcessCSV<LoadingScreenData>(text, false);
                    _this.loadingScreensByType = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<LoadingScreenData>>();
                    foreach (var ls in _this.loadingScreens.Values)
                        _this.loadingScreensByType.ValueOrNew(ls.text).Add(ls);
                    G.ui.SetupLoadingScreen();
                    break;

                case "params":
                    _this.paramsRaw = LoadCSV(text, _this.paramsRaw);
                    _this.parms = new Il2CppSystem.Collections.Generic.Dictionary<string, float>();
                    foreach (var p in _this.paramsRaw)
                        _this.parms.Add(p.key, p.value.value);
                    break;

                case "accuracies":
                    _this.accuracies = LoadCSV(text, _this.accuracies);
                    _this.accuracyGroups = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.Dictionary<string, AccuracyData>>();
                    // Trying to use the native code here rather than entirely reimplementing. Hopefully fine.
                    GameData.__c__DisplayClass199_0 coAcc = new GameData.__c__DisplayClass199_0();
                    coAcc.__4__this = _this;
                    foreach (var g in _this.accuracies.Values)
                        if (g.isGroup)
                            _this.accuracyGroups[g.nameUi] = coAcc._LoadVersionAndData_b__42(g);
                    break;

                case "events":
                    var evtData = Serializer.CSV.ProcessCSV<EventData>(text, false);
                    List<EventData> processedEvts = new List<EventData>();
                    GameData.__c__DisplayClass199_0 coEvt = new GameData.__c__DisplayClass199_0();
                    coEvt.__4__this = _this;
                    foreach (var e in evtData.Values)
                        processedEvts.Add(coEvt._LoadVersionAndData_b__48(e));
                    _this.events = new Il2CppSystem.Collections.Generic.Dictionary<string, EventData>();
                    foreach (var e in processedEvts)
                        _this.events[e.name] = e;
                    break;


                // The default case.
                // A macro would eliminate SO much code here.
                // Can't reflect because this needs to be templated.
                // So instead there needs to be a case for each.
                case "statEffects":
                    _this.statEffects = LoadCSV(text, _this.statEffects);
                    break;
                case "stats":
                    _this.stats = LoadCSV(text, _this.stats);
                    break;
                case "shipTypes":
                    _this.shipTypes = LoadCSV(text, _this.shipTypes);
                    break;
                case "guns":
                    _this.guns = LoadCSV(text, _this.guns);
                    break;
                case "torpedoTubes":
                    _this.torpedoTubes = LoadCSV(text, _this.torpedoTubes);
                    break;
                case "penetration":
                    _this.penetrations = LoadCSV(text, _this.penetrations);
                    break;
                case "players":
                    _this.players = LoadCSV(text, _this.players);
                    break;
                case "partCategories":
                    _this.partCategories = LoadCSV(text, _this.partCategories);
                    break;
                case "parts":
                    _this.parts = LoadCSV(text, _this.parts);
                    break;
                case "submarines":
                    _this.submarines = LoadCSV(text, _this.submarines);
                    break;
                case "partModels":
                    _this.partModels = LoadCSV(text, _this.partModels);
                    break;
                case "randParts":
                    _this.randParts = LoadCSV(text, _this.randParts);
                    break;
                case "randPartsRefit":
                    _this.randPartsRefit = LoadCSV(text, _this.randPartsRefit);
                    break;
                case "compTypes":
                    _this.compTypes = LoadCSV(text, _this.compTypes);
                    break;
                case "components":
                    _this.components = LoadCSV(text, _this.components);
                    break;
                case "techGroups":
                    _this.techGroups = LoadCSV(text, _this.techGroups);
                    break;
                case "techTypes":
                    _this.techTypes = LoadCSV(text, _this.techTypes);
                    break;
                case "techEffects":
                    _this.techEffects = LoadCSV(text, _this.techEffects);
                    break;
                case "technologies":
                    _this.technologies = LoadCSV(text, _this.technologies);
                    break;
                case "battleTypes":
                    _this.battleTypes = LoadCSV(text, _this.battleTypes);
                    break;
                case "missions":
                    _this.missions = LoadCSV(text, _this.missions);
                    break;
                case "tooltips":
                    _this.tooltips = LoadCSV(text, _this.tooltips);
                    break;
                case "shipNames":
                    _this.shipNames = LoadCSV(text, _this.shipNames);
                    break;
                case "help":
                    _this.help = LoadCSV(text, _this.help);
                    break;
                case "battleTypeEx":
                    _this.battleTypesEx = LoadCSV(text, _this.battleTypesEx);
                    break;
                case "crewTrainingLevels":
                    _this.crewTrainingLevels = LoadCSV(text, _this.crewTrainingLevels);
                    break;
                case "aiPersonalities":
                    _this.aiPersonalities = LoadCSV(text, _this.aiPersonalities);
                    break;
                case "aiAdmirals":
                    _this.aiAdmiralsRaw = LoadCSV(text, _this.aiAdmiralsRaw);
                    break;
                case "relationMatrix":
                    _this.relationMatrix = LoadCSV(text, _this.relationMatrix);
                    break;
                case "canals":
                    _this.canals = LoadCSV(text, _this.canals);
                    break;
                case "governmentMod":
                    _this.governmentModifiers = LoadCSV(text, _this.governmentModifiers);
                    break;
            }
        }

        // A wrapper around ProcessCSV to simplify code.
        // The dictionary argument is solely so the compiler can infer type, because annoyingly
        // it can't just from the return value vs the assignment.
        private static Il2CppSystem.Collections.Generic.Dictionary<string, T> LoadCSV<T>(string text, Il2CppSystem.Collections.Generic.Dictionary<string, T> dict, bool fillCustom = false) where T : Il2Cpp.BaseData
        {
            dict = Serializer.CSV.ProcessCSV<T>(text, fillCustom);
            return dict;
        }

        private static string? GetTextFromFile(string assetName)
        {
            string basePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!Directory.Exists(basePath))
                return null;

            string filePath = Path.Combine(basePath, assetName + ".csv");
            if (!File.Exists(filePath))
                return null;

            return File.ReadAllText(filePath);
        }

        public static string? GetTextFromFileOrAsset(string assetName)
        {
            string text = GetTextFromFile(assetName);
            if (text == null)
            {
                var textA = Util.ResourcesLoad<TextAsset>(assetName, false);
                if (textA != null)
                    text = textA.text;
            }
            if (text == null)
            {
                Melon<TweaksAndFixes>.Logger.Error($"Could not find or load asset `{assetName}`");
                return null;
            }

            return text;
        }
    }
}