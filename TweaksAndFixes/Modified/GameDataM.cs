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
            string? text = Serializer.CSV.GetTextFromFile(l.name);
            if (text == null)
            {
                l.process.Invoke(l);
                return;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"Overriding built-in asset {l.name} with {l.name}.csv");

            switch (l.name)
            {
                case "loadingScreens":
                    _this.loadingScreens = LoadCSV(text, _this.loadingScreens);
                    _this.loadingScreensByType = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<LoadingScreenData>>();
                    foreach (var ls in _this.loadingScreens.Values)
                        _this.loadingScreensByType.ValueOrNew(ls.text).Add(ls);
                    G.ui.SetupLoadingScreen();
                    break;

                case "params":
                    // Weird special handling for this one.
                    // Was getting issues doing this entirely in managed code.
                    l.process.Invoke(l);
                    var newParams = Serializer.CSV.ProcessCSV<ParamData>(text, false);
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
                    _this.relationMatrix = LoadCSV(text, _this.relationMatrix, true);
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
    }
}