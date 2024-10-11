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

        private static int LoadLocFromFile(LocalizeManager.LanguagesData __result, string filename, bool underData, bool clobber)
        {
            var lines = Serializer.CSV.GetLinesFromFile(filename, underData);
            if (lines == null)
                return -1;

            for (int j = 0; j < lines.Length; ++j)
            {
                var line = lines[j];
                var split = line.Split(';');
                if (split.Length < 2)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {filename}, line {j + 1} `{line}` lacks key or value");
                    continue;
                }

                string key = split[0];
                if (_SeenKeys.Contains(key))
                {
                    Melon<TweaksAndFixes>.Logger.Error($"Error loading language file {filename}, line {j + 1} `{line}` is a duplicate key");
                    continue;
                }
                _SeenKeys.Add(key);
                if (!clobber && __result.Data.ContainsKey(key))
                    continue;

                string[] newArr = new string[split.Length - 1];
                for (int i = 1; i < split.Length; ++i)
                    newArr[i - 1] = LocalizeManager.__c.__9__24_0.Invoke(split[i]);

                __result.Data[key] = newArr;
            }
            int count = _SeenKeys.Count;
            _SeenKeys.Clear();
            return count;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(LocalizeManager.LoadLanguage))]
        internal static void Postfix_LoadLanguage(LocalizeManager __instance, string currentLanguage, ref LocalizeManager.LanguagesData __result)
        {
            int overrideCount = LoadLocFromFile(__result, currentLanguage + ".lng", false, true);
            if(overrideCount >= 0)
                Melon<TweaksAndFixes>.Logger.Msg($"Overriding language {currentLanguage} with {overrideCount} lines");

            if (LoadLocFromFile(__result, Config._LocFile, true, false) < 0)
                Melon<TweaksAndFixes>.Logger.Error($"Unable to find base TAF loc file {Config._LocFile} in {Config._DataDir}");
        }
    }
}
