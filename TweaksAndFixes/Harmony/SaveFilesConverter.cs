﻿using MelonLoader;
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
            const string predefToName = "PredefinedsToShared";
            var upperButtons = __instance.DeleteAllShared.transform.parent.gameObject;
            var hlg = upperButtons.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 40;
            // For some reason the gameobjects stick around but the text/listeners don't, during scene transitions
            Button? sharedToJson = upperButtons.GetChild(jsonName, true)?.GetComponent<Button>();
            if (sharedToJson == null)
                sharedToJson = GameObject.Instantiate(__instance.DeleteAllCampaign, upperButtons.transform);
            Button? sharedToPredefs = upperButtons.GetChild(predefName, true)?.GetComponent<Button>();
            if (sharedToPredefs == null)
                sharedToPredefs = GameObject.Instantiate(__instance.DeleteAllCampaign, upperButtons.transform); ;
            Button? predefsToShared = upperButtons.GetChild(predefToName, true)?.GetComponent<Button>();
            if (predefsToShared == null)
                predefsToShared = GameObject.Instantiate(__instance.DeleteAllCampaign, upperButtons.transform);

            sharedToJson.name = jsonName;
            sharedToJson.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Designs to JSON";
            sharedToJson.onClick.RemoveAllListeners();
            var loc = sharedToJson.GetComponentInChildren<LocalizeText>(true);
            if (loc != null)
                GameObject.Destroy(loc);
            sharedToJson.onClick.AddListener(new System.Action(() =>
            {
                int count = DesignsToJSON();
                MessageBoxUI.Show("Converted Designs to JSON", $"Converted {count} designs");
            }));

            sharedToPredefs.name = predefName;
            sharedToPredefs.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Designs to Predefined";
            sharedToPredefs.onClick.RemoveAllListeners();
            loc = sharedToPredefs.GetComponentInChildren<LocalizeText>(true);
            if (loc != null)
                GameObject.Destroy(loc);
            sharedToPredefs.onClick.AddListener(new System.Action(() =>
            {
                int count = DesignsToPredefs();
                MessageBoxUI.Show("Exported Predefined Designs", $"Exported {count} Shared Design ships to {Config._PredefinedDesignsFile}");
            }));

            predefsToShared.name = predefName;
            predefsToShared.GetComponentInChildren<Il2CppTMPro.TextMeshProUGUI>().text = "Predefined to Designs";
            predefsToShared.onClick.RemoveAllListeners();
            loc = predefsToShared.GetComponentInChildren<LocalizeText>(true);
            if (loc != null)
                GameObject.Destroy(loc);
            predefsToShared.onClick.AddListener(new System.Action(() =>
            {
                int count = PredefsToDesigns();
                MessageBoxUI.Show("Saved Predefined Designs", $"Saved {count} predefined ships to Shared Designs");
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

        private static int PredefsToDesigns()
        {
            var prefix = Storage.designsPrefix;
            if (!Directory.Exists(prefix))
                return 0;

            bool useStock = false;
            if (!PredefinedDesignsData.LoadPredefs(Path.Combine(Config._BasePath, Config._PredefinedDesignsFile), out var predefs, out int dCount, false) || predefs == null)
            {
                useStock = true;
                PredefinedDesignsData.LoadPredefs(null, out predefs, out dCount, false);
                Melon<TweaksAndFixes>.Logger.Warning("Using vanilla predefined designs to save to shared designs");
            }


            int sCount = 0;

            G.GameData.LoadSharedDesigns();

            foreach (var spy in predefs.shipsPerYear)
            {
                foreach (var spp in spy.Value.shipsPerPlayer)
                {
                    foreach (var spt in spp.Value.shipsPerType)
                    {
                        foreach (var s in spt.Value)
                        {
                            if (G.GameData.sharedDesignsPerNation != null)
                            {
                                foreach (var kvp in G.GameData.sharedDesignsPerNation)
                                {
                                    for (int i = kvp.Value.Count; i-- > 0;)
                                    {
                                        var des = kvp.Value[i].Item1;
                                        if (des.id == s.id)
                                        {
                                            // The game's way of deleting these is bugged, we do it ourselves
                                            string baseName = $"{des.YearCreated} {des.playerName} {des.vesselName}";
                                            string path = Path.Combine(prefix, baseName + ".bindesign");
                                            Storage.EraseFilePath(path);
                                            path = Path.Combine(prefix, baseName + ".design");
                                            Storage.EraseFilePath(path);

                                            kvp.Value.RemoveAt(i);

                                            break;
                                        }
                                    }
                                }
                            }
                            ++sCount;
                            if (useStock)
                                s.vesselName += " " + sCount; // stock reuses names, which would clobber files
                            string pName = s.playerName;
                            if (G.GameData.players.TryGetValue(s.playerName, out var data))
                                pName = Player.GetNameUI(null, data, s.YearCreated);
                            string fName = $"{s.YearCreated} {pName} {s.vesselName}.bindesign";
                            string fPath = Path.Combine(prefix, fName);
                            Storage.SaveSharedDesignShipByte(fPath, Util.SerializeObjectByte(s));
                        }
                    }
                }
            }
            if (sCount > 0)
                G.GameData.LoadSharedDesigns();

            return sCount;
        }

        private static int DesignsToPredefs()
        {
            int count = 0;

            if (G.GameData.sharedDesignsPerNation == null || G.GameData.sharedDesignsPerNation.Count == 0)
                G.GameData.LoadSharedDesigns();

            int sCount = 0;

            // For some very strange reason, we can't create the KVPs
            // in the store directly. So we make a real object and
            // add ships to it, then call ToStore.
            var cd = new CampaignDesigns();

            foreach (var kvp in G.GameData.sharedDesignsPerNation)
            {
                foreach (var tuple in kvp.Value)
                {
                    var ship = tuple.Item1;
                    cd.AddShip(ship);
                    ++sCount;
                }
            }
            var predefs = cd.ToStore();
            if (predefs == null)
                Melon<TweaksAndFixes>.Logger.Error("Could not convert CampaignDesigns to store");
            else
            {
                bool isValid = true;
                int dCount = 0;
                if (predefs.shipsPerYear == null)
                {
                    isValid = false;
                    Melon<TweaksAndFixes>.Logger.Error("Store has null ShipsPerYear");
                }
                else
                {
                    foreach (var spy in predefs.shipsPerYear)
                    {
                        if (spy.Value == null)
                        {
                            isValid = false;
                            Melon<TweaksAndFixes>.Logger.Msg("ShipsPerYear has null spp, key" + spy.Key);
                        }
                        else if (spy.Value.shipsPerPlayer == null)
                        {
                            isValid = false;
                            Melon<TweaksAndFixes>.Logger.Error(spy.Key + ": null shipsPerPlayer");
                        }
                        else
                        {
                            foreach (var spp in spy.Value.shipsPerPlayer)
                            {
                                if (spp.Value == null)
                                {
                                    isValid = false;
                                    Melon<TweaksAndFixes>.Logger.Error("shipsPerPlayer has null spt, but key " + spp.Key);
                                }
                                else if (spp.Value.shipsPerType == null)
                                {
                                    isValid = false;
                                    Melon<TweaksAndFixes>.Logger.Error(spp.Key + ": null shipsPerType");
                                }
                                else
                                {
                                    foreach (var spt in spp.Value.shipsPerType)
                                    {
                                        if (spt.Value == null)
                                        {
                                            isValid = false;
                                            Melon<TweaksAndFixes>.Logger.Error(spt.Key + ": null shiplist");
                                        }
                                        else
                                        {
                                            dCount += spt.Value.Count;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (!isValid || sCount != dCount)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error generating predefined designs. Valid? {isValid}, input count {sCount}, output count {dCount}");
                    return 0;
                }
                count = dCount;
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
