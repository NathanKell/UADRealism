using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using UnityEngine.UI;
using Il2CppUiExt;

#pragma warning disable CS8602
#pragma warning disable CS8604

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(SaveFilesConverter))]
    internal class Patch_SaveFilesConverter
    {
        [HarmonyPatch(nameof(SaveFilesConverter.Start))]
        [HarmonyPostfix]
        internal static void Postfix_Start(SaveFilesConverter __instance)
        {
            const string jsonName = "SharedToJSON";
            const string predefName = "SharedToPredefineds";
            var upperButtons = __instance.DeleteAllShared.transform.parent.gameObject;

            // For some reason the gameobjects stick around but the text/listeners don't, during scene transitions
            Button? sharedToJson = upperButtons.GetChild(jsonName, true)?.GetComponent<Button>();
            if (sharedToJson == null)
                sharedToJson = GameObject.Instantiate(__instance.DeleteAllCampaign, upperButtons.transform);
            Button? sharedToPredefs = upperButtons.GetChild(predefName, true)?.GetComponent<Button>();
            if (sharedToPredefs == null)
                sharedToPredefs = GameObject.Instantiate(__instance.DeleteAllCampaign, upperButtons.transform); ;

            sharedToJson.name = jsonName;
            sharedToJson.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Designs to JSON";
            sharedToJson.onClick.RemoveAllListeners();
            sharedToJson.onClick.AddListener(new System.Action(() =>
            {
                int count = DesignsToJSON();
                MessageBoxUI.Show("Converted Designs to JSON", $"Converted {count} designs");
            }));

            sharedToPredefs.name = predefName;
            sharedToPredefs.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Designs to Predefined";
            sharedToPredefs.onClick.RemoveAllListeners();
            sharedToPredefs.onClick.AddListener(new System.Action(() =>
            {
                int count = DesignsToPredefs();
                MessageBoxUI.Show("Saved Predefined Designs", $"Saved {count} Shared Design ships to {Config._PredefinedDesignsFile}");
            }));
        }

        private static int DesignsToJSON()
        {
            var prefix = Storage.designsPrefix;
            if (!Directory.Exists(prefix))
                return 0;

            int count = 0;
            var files = Directory.GetFiles(prefix, "*.bindesign");
            foreach (var f in files)
            {
                var store = Util.DeserializeObjectByte<Ship.Store>(File.ReadAllBytes(f));
                var json = Util.SerializeObject(store);
                var baseName = Path.GetFileNameWithoutExtension(f);
                var path = Path.Combine(Path.GetDirectoryName(f), baseName + ".design");
                File.WriteAllText(path, json);
                ++count;
            }

            return count;
        }

        private static int DesignsToPredefs()
        {
            int count = 0;

            var predefs = new CampaignDesigns.Store();
            predefs.year = 0;
            if (G.GameData.sharedDesignsPerNation == null || G.GameData.sharedDesignsPerNation.Count == 0)
                G.GameData.LoadSharedDesigns();

            Dictionary<int, Dictionary<string, Dictionary<string, List<Ship.Store>>>> predefBase = new Dictionary<int, Dictionary<string, Dictionary<string, List<Ship.Store>>>>();
            int sCount = 0;
            foreach (var kvp in G.GameData.sharedDesignsPerNation)
            {
                foreach (var tuple in kvp.Value)
                {
                    var ship = tuple.Item1;

                    predefBase.ValueOrNew(ship.YearCreated).ValueOrNew(kvp.Key).ValueOrNew(ship.shipType).Add(ship);
                    ++sCount;
                }
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Seen {sCount} ships");

            predefs.shipsPerYear = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<int, ShipsPerYear.Store>>(predefBase.Count);
            foreach (var spyB in predefBase)
            {
                var spy = new ShipsPerYear.Store();
                spy.year = 0; // stock uses 0, not the actual year
                spy.shipsPerPlayer = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<string, ShipsPerPlayer.Store>>(spyB.Value.Count);
                foreach (var sppB in spyB.Value)
                {
                    var spp = new ShipsPerPlayer.Store();
                    spp.shipsPerType = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<string, Il2CppSystem.Collections.Generic.List<Ship.Store>>>(sppB.Value.Count);
                    foreach (var sptB in sppB.Value)
                    {
                        var list = new Il2CppSystem.Collections.Generic.List<Ship.Store>(sptB.Value.Count);
                        foreach (var s in sptB.Value)
                            list.Add(s);
                        int tC = spp.shipsPerType.Count;
                        spp.shipsPerType.Add(new Il2CppSystem.Collections.Generic.KeyValuePair<string, Il2CppSystem.Collections.Generic.List<Ship.Store>>(sptB.Key, list));
                        spp.shipsPerType[tC].key = sptB.Key;
                        spp.shipsPerType[tC].value = list;
                    }
                    int pC = spy.shipsPerPlayer.Count;
                    spy.shipsPerPlayer.Add(new Il2CppSystem.Collections.Generic.KeyValuePair<string, ShipsPerPlayer.Store>(sppB.Key, spp));
                    spy.shipsPerPlayer[pC].key = sppB.Key;
                    spy.shipsPerPlayer[pC].value = spp;
                }
                int yC = predefs.shipsPerYear.Count;
                predefs.shipsPerYear.Add(new Il2CppSystem.Collections.Generic.KeyValuePair<int, ShipsPerYear.Store>(spyB.Key, spy));
                predefs.shipsPerYear[yC].key = spyB.Key;
                predefs.shipsPerYear[yC].value = spy;
                Debug.Log($"Null? {(predefs.shipsPerYear[predefs.shipsPerYear.Count - 1] == null)} with key {predefs.shipsPerYear[predefs.shipsPerYear.Count - 1].Key} and val null? {(predefs.shipsPerYear[predefs.shipsPerYear.Count-1].Value == null)}. Desired: {spyB.Key} / {spy == null}");
            }

            if (predefs == null)
                Melon<TweaksAndFixes>.Logger.Msg("Null store");
            else
            {
                Melon<TweaksAndFixes>.Logger.Msg("got store");
                bool isValid = true;
                int dCount = 0;
                if (predefs.shipsPerYear == null)
                {
                    isValid = false;
                    Melon<TweaksAndFixes>.Logger.Error("spy");
                }
                else
                {
                    Melon<TweaksAndFixes>.Logger.Msg("got spy");
                    foreach (var spy in predefs.shipsPerYear)
                    {
                        if (spy.Value == null)
                        {
                            isValid = false;
                            Melon<TweaksAndFixes>.Logger.Msg("spy val null, but key " + spy.Key);
                        }
                        else if (spy.Value.shipsPerPlayer == null)
                        {
                            isValid = false;
                            Melon<TweaksAndFixes>.Logger.Error(spy.Key + ": spp");
                        }
                        else
                        {
                            Melon<TweaksAndFixes>.Logger.Msg("got spp");
                            foreach (var spp in spy.Value.shipsPerPlayer)
                            {
                                if (spp.Value == null)
                                {
                                    isValid = false;
                                    Melon<TweaksAndFixes>.Logger.Error("spp value null, but key " + spp.Key);
                                }
                                else if (spp.Value.shipsPerType == null)
                                {
                                    isValid = false;
                                    Melon<TweaksAndFixes>.Logger.Error(spp.Key + ": spt");
                                }
                                else
                                {
                                    Melon<TweaksAndFixes>.Logger.Msg("got spt");
                                    foreach (var spt in spp.Value.shipsPerType)
                                    {
                                        if (spt.Value == null)
                                        {
                                            isValid = false;
                                            Melon<TweaksAndFixes>.Logger.Error(spt.Key + ": spt");
                                        }
                                        else
                                        {
                                            dCount += spt.Value.Count;
                                            Melon<TweaksAndFixes>.Logger.Msg("got list");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                Melon<TweaksAndFixes>.Logger.Msg($"valid? {isValid} : {dCount}");
            }

            var bytes = Util.SerializeObjectByte<CampaignDesigns.Store>(predefs);
            string path = Path.Combine(Config._BasePath, Config._PredefinedDesignsFile);
            if (File.Exists(path))
                File.Delete(path);
            File.WriteAllBytes(path, bytes);
            return count;
        }
    }
}
