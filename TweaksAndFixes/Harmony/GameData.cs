using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            List<string> fixKeys = new List<string>();
            foreach (var rp in __instance.randParts.Values)
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
                            continue;
                    }
                    fixKeys.Add(kvp.Key);
                }
                foreach (var key in fixKeys)
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
                fixKeys.Clear();
            }

            foreach (var data in __instance.parts.Values)
            {
                if (!data.isGun)
                    continue;

                data.GetCaliberInch();
            }

            // Run after other things have a chance to update GameData
            MelonCoroutines.Start(FillDatabase());
        }

        internal static System.Collections.IEnumerator FillDatabase()
        {
            yield return new WaitForEndOfFrame();
            Database.FillDatabase();
            Melon<TweaksAndFixes>.Logger.Msg("Loaded database");
        }
    }
}
