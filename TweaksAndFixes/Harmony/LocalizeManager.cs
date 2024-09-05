using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(LocalizeManager))]
    internal class Patch_LocalizeManager
    {
        private static readonly HashSet<string> _SeenKeys = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(LocalizeManager.LoadLanguage))]
        internal static void Postfix_LoadLanguage(LocalizeManager __instance, string currentLanguage, ref LocalizeManager.LanguagesData __result)
        {
            var overrideLines = Serializer.CSV.GetLinesFromFile(currentLanguage + ".lng");
            if (overrideLines == null)
                return;

            for(int j = 0; j < overrideLines.Length; ++j)
            {
                var line = overrideLines[j];
                var split = line.Split(';');
                if (split.Length < 2)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {currentLanguage}.lng, line {j + 1} `{line}` lacks key or value");
                    continue;
                }

                string key = split[0];
                if (_SeenKeys.Contains(key))
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {currentLanguage}.lng, line {j + 1} `{line}` is a duplicate key");
                    continue;
                }
                _SeenKeys.Add(key);

                string[] newArr = new string[split.Length - 1];
                for (int i = 1; i < split.Length; ++i)
                    newArr[i - 1] = LocalizeManager.__c.__9__24_0.Invoke(split[i]);

                __result.Data[key] = newArr;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"Overriding language {currentLanguage} with {_SeenKeys.Count} lines");
            _SeenKeys.Clear();
        }
    }
}
