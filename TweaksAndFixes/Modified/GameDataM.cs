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
        public static void LoadData(GameData _this)
        {
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
        }

        private static void ProcessLoadInfo(GameData _this, GameData.LoadInfo l)
        {
            string filename = l.name + ".csv";
            string fileOver = l.name + "_override.csv";
            string? text = Serializer.CSV.GetTextFromFile(filename);
            string? textOverride = Serializer.CSV.GetTextFromFile(fileOver);
            if (text == null && textOverride == null)
            {
                l.process.Invoke(l);
                return;
            }

            string oText;
            if (text != null)
            {
                oText = $"Replacing built-in asset {l.name} with {filename}";
                if (textOverride != null)
                    oText += $" and overriding with {fileOver}";
            }
            else
                oText = $"Overriding built-in asset {l.name} with {fileOver}";

            Melon<TweaksAndFixes>.Logger.Msg(oText);

            // There's no raw data that events load into
            // so we have to load even built-in config manually
            // since we do postprocessing ourselves.
            // Params, by contrast, we always load this way first.
            if (l.name == "params" || (text == null && l.name != "events"))
                l.process.Invoke(l);

            switch (l.name)
            {
                case "loadingScreens":
                    _this.loadingScreens = LoadCSV(text, textOverride, _this.loadingScreens);
                    _this.loadingScreensByType = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<LoadingScreenData>>();
                    foreach (var ls in _this.loadingScreens.Values)
                        _this.loadingScreensByType.ValueOrNew(ls.text).Add(ls);
                    G.ui.SetupLoadingScreen();
                    break;

                case "params":
                    // Weird special handling for this one.
                    // Was getting issues doing this entirely in managed code.
                    Il2CppSystem.Collections.Generic.Dictionary<string, ParamData> newParams;
                    if (text != null)
                    {
                        newParams = Serializer.CSV.ProcessCSV<ParamData>(text, false);
                    }
                    else
                    {
                        newParams = new Il2CppSystem.Collections.Generic.Dictionary<string, ParamData>();
                    }
                    if (textOverride != null)
                    {
                        var newPar2 = Serializer.CSV.ProcessCSV<ParamData>(textOverride, false);
                        foreach (var kvp in newPar2)
                            newParams[kvp.Key] = kvp.Value;
                    }

                    foreach (var np in newParams)
                    {
                        if (!_this.paramsRaw.TryGetValue(np.Key, out var pd))
                        {
                            //Debug.Log($"Found new param: {np.Key} has value {np.Value.value} and str `{np.Value.str}`");
                            _this.paramsRaw[np.key] = np.Value;
                        }
                        else
                        {
                            pd.str = np.Value.str;
                            pd.value = np.Value.value;
                        }
                        if (!_this.parms.ContainsKey(np.key))
                        {
                            //Debug.Log($"Adding new parm {np.Key} with value {np.Value.value}");
                            _this.parms[np.Key] = np.Value.value;
                        }
                        else if (_this.parms[np.Key] != np.Value.value)
                        {
                            //Debug.Log($"Parm {np.Key}: old value {_this.parms[np.Key]}, new value {np.Value.value}");
                            _this.parms[np.Key] = np.Value.value;
                        }
                    }
                    break;

                case "accuracies":
                    _this.accuracies = LoadCSV(text, textOverride, _this.accuracies);
                    _this.accuracyGroups = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.Dictionary<string, AccuracyData>>();
                    // Trying to use the native code here rather than entirely reimplementing. Hopefully fine.
                    GameData.__c__DisplayClass199_0 coAcc = new GameData.__c__DisplayClass199_0();
                    coAcc.__4__this = _this;
                    foreach (var g in _this.accuracies.Values)
                        if (g.isGroup)
                            _this.accuracyGroups[g.nameUi] = coAcc._LoadVersionAndData_b__42(g);
                    break;

                case "events":
                    Il2CppSystem.Collections.Generic.Dictionary<string, EventData> evtData;
                    if (text == null)
                        evtData = G.GameData.ProcessCsv<EventData>(l, false);
                    else
                        evtData = Serializer.CSV.ProcessCSV<EventData>(text, false);
                    if (textOverride != null)
                        evtData = Serializer.CSV.ProcessCSV<EventData>(textOverride, false, evtData);
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
                    _this.statEffects = LoadCSV(text, textOverride, _this.statEffects);
                    break;
                case "stats":
                    _this.stats = LoadCSV(text, textOverride, _this.stats);
                    break;
                case "shipTypes":
                    _this.shipTypes = LoadCSV(text, textOverride, _this.shipTypes);
                    break;
                case "guns":
                    _this.guns = LoadCSV(text, textOverride, _this.guns);
                    break;
                case "torpedoTubes":
                    _this.torpedoTubes = LoadCSV(text, textOverride, _this.torpedoTubes);
                    break;
                case "penetration":
                    _this.penetrations = LoadCSV(text, textOverride, _this.penetrations);
                    break;
                case "players":
                    _this.players = LoadCSV(text, textOverride, _this.players);
                    break;
                case "partCategories":
                    _this.partCategories = LoadCSV(text, textOverride, _this.partCategories);
                    break;
                case "parts":
                    _this.parts = LoadCSV(text, textOverride, _this.parts);
                    break;
                case "submarines":
                    _this.submarines = LoadCSV(text, textOverride, _this.submarines);
                    break;
                case "partModels":
                    _this.partModels = LoadCSV(text, textOverride, _this.partModels);
                    break;
                case "randParts":
                    _this.randParts = LoadCSV(text, textOverride, _this.randParts);
                    break;
                case "randPartsRefit":
                    _this.randPartsRefit = LoadCSV(text, textOverride, _this.randPartsRefit);
                    break;
                case "compTypes":
                    _this.compTypes = LoadCSV(text, textOverride, _this.compTypes);
                    break;
                case "components":
                    _this.components = LoadCSV(text, textOverride, _this.components);
                    break;
                case "techGroups":
                    _this.techGroups = LoadCSV(text, textOverride, _this.techGroups);
                    break;
                case "techTypes":
                    _this.techTypes = LoadCSV(text, textOverride, _this.techTypes);
                    break;
                case "techEffects":
                    _this.techEffects = LoadCSV(text, textOverride, _this.techEffects);
                    break;
                case "technologies":
                    _this.technologies = LoadCSV(text, textOverride, _this.technologies);
                    break;
                case "battleTypes":
                    _this.battleTypes = LoadCSV(text, textOverride, _this.battleTypes);
                    break;
                case "missions":
                    _this.missions = LoadCSV(text, textOverride, _this.missions);
                    break;
                case "tooltips":
                    _this.tooltips = LoadCSV(text, textOverride, _this.tooltips);
                    break;
                case "shipNames":
                    _this.shipNames = LoadCSV(text, textOverride, _this.shipNames);
                    break;
                case "help":
                    _this.help = LoadCSV(text, textOverride, _this.help);
                    break;
                case "battleTypeEx":
                    _this.battleTypesEx = LoadCSV(text, textOverride, _this.battleTypesEx);
                    break;
                case "crewTrainingLevels":
                    _this.crewTrainingLevels = LoadCSV(text, textOverride, _this.crewTrainingLevels);
                    break;
                case "aiPersonalities":
                    _this.aiPersonalities = LoadCSV(text, textOverride, _this.aiPersonalities);
                    break;
                case "aiAdmirals":
                    _this.aiAdmiralsRaw = LoadCSV(text, textOverride, _this.aiAdmiralsRaw);
                    break;
                case "relationMatrix":
                    _this.relationMatrix = LoadCSV(text, textOverride, _this.relationMatrix, true);
                    break;
                case "canals":
                    _this.canals = LoadCSV(text, textOverride, _this.canals);
                    break;
                case "governmentMod":
                    _this.governmentModifiers = LoadCSV(text, textOverride, _this.governmentModifiers);
                    break;
            }
        }

        // A wrapper around ProcessCSV to simplify code.
        // Can be passed either the base input, the override
        // input, or both. If no base input, use the passed
        // dictionary.
        private static Il2CppSystem.Collections.Generic.Dictionary<string, T> LoadCSV<T>(string? text, string? textOverride, Il2CppSystem.Collections.Generic.Dictionary<string, T> dict, bool fillCustom = false) where T : Il2Cpp.BaseData
        {
            if (text != null)
                dict = Serializer.CSV.ProcessCSV<T>(text, fillCustom);
            if (textOverride != null)
                dict = Serializer.CSV.ProcessCSV<T>(textOverride, fillCustom, dict);

            return dict;
        }
    }
}