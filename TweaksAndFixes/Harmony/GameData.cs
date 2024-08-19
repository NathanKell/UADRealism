using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

#pragma warning disable CS8600

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.LoadVersionAndData))]
        internal static void Postfix_LoadVersionAndData(GameData __instance)
        {
            GameDataM.LoadData(__instance);
        }        

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Prefix_PostProcessAll(GameData __instance)
        {
            Patch_PlayerData.PatchPlayerMaterials();
            //Serializer.CSV.TestNative();

            GradeExtensions.LoadData();
            GenArmorData.LoadData();
        }

        private static readonly List<string> _FixKeys = new List<string>();
        private static void FixRandPart(RandPart rp)
        {
            foreach (var kvp in rp.paramx)
            {
                switch (kvp.Key)
                {
                    case "and":
                    case "or":
                    case "mount":
                    case "!mount":
                    case "delete_unmounted":
                    case "delete_refit":
                    case "scheme":
                    case "tr_rand_mod":
                    case "auto_refit":
                        return;
                }
                _FixKeys.Add(kvp.Key);
            }
            foreach (var key in _FixKeys)
            {
                var newVal = key.Replace(")", string.Empty).Replace("(", string.Empty);
                rp.paramx.Remove(key);
                // we use and unless it doesn't exist and or does.
                if (!rp.paramx.TryGetValue("and", out var lst) && !rp.paramx.TryGetValue("or", out lst))
                {
                    lst = new Il2CppSystem.Collections.Generic.List<string>();
                    rp.paramx["and"] = lst;
                }
                lst.Add(newVal);
                Melon<TweaksAndFixes>.Logger.Msg($"Fixing Randpart {rp.name} with param {rp.param}, invalid key {key}. New value {newVal}");
            }
            _FixKeys.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            Debug.Log("Finished internal PostProcessAll");
            foreach (var rp in __instance.randParts.Values)
            {
                FixRandPart(rp);
            }
            foreach (var rp in __instance.randPartsRefit.Values)
            {
                FixRandPart(rp);
            }

            foreach (var data in __instance.parts.Values)
            {
                if (!data.isGun)
                    continue;

                data.GetCaliberInch();
            }
            //Serializer.CSV.TestNativePost();

            // Run after other things have a chance to update GameData
            MelonCoroutines.Start(FillDatabase());
        }

        internal static System.Collections.IEnumerator FillDatabase()
        {
            yield return new WaitForEndOfFrame();
            Database.FillDatabase();
            Config.LoadConfig();
            Melon<TweaksAndFixes>.Logger.Msg("Loaded database and config");

            if (!Directory.Exists(Config._BasePath))
            {
                Melon<TweaksAndFixes>.Logger.Error("Failed to find Mods directory: " + Config._BasePath);
            }
            else
            {
                string filePath = Path.Combine(Config._BasePath, Config._FlagFile);
                if (!File.Exists(filePath))
                {
                    Melon<TweaksAndFixes>.Logger.Warning("Failed to find Flags file " + filePath);
                }
            }

            //foreach (var hull in G.GameData.parts.Values)
            //    if (hull.isHull)
            //        hull.nameUi = Patch_Ship.GetHullModelKey(hull);

            //var playerToShips = new Dictionary<PlayerData, Dictionary<ShipType, List<Tuple<PartData, int>>>>();
            //foreach (var hull in G.GameData.parts.Values)
            //{
            //    if (!hull.isHull)
            //        continue;

            //    foreach (var pd in hull.countriesx)
            //    {
            //        var year = Database.GetYear(hull);
            //        if (year < 0)
            //            continue;
            //        playerToShips.ValueOrNew(pd).ValueOrNew(hull.shipType).Add(new Tuple<PartData, int>(hull, year));
            //    }
            //}

            //string logstr = "\nname,nameUi,type,year,model,scale,country,tonnageMin,tonnageMax,speedLimiter,param";
            //foreach (var kvp in playerToShips)
            //{
            //    foreach (var list in kvp.Value.Values)
            //    {
            //        list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            //        foreach (var tpl in list)
            //        {
            //            var hull = tpl.Item1;
            //            var year = tpl.Item2;
            //            string countries = kvp.Key.name;
            //            if (hull.countriesx.Count > 1)
            //            {
            //                foreach (var c in hull.countriesx)
            //                    if (c != kvp.Key)
            //                        countries += ", " + c.name;
            //                countries = "\"" + countries + "\"";
            //            }
            //            logstr += $"\n{hull.name},{hull.nameUi},{hull.shipType.name},{year},{hull.model},{hull.scale:F2},{countries},{hull.tonnageMin:F0},{hull.tonnageMax:F0},{hull.speedLimiter:F1},\"{hull.param}\"";
            //        }
            //    }
            //}
            //Debug.Log(logstr);
        }
    }
}
