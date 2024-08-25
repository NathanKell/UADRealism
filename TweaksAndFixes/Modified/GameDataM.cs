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

            var tAsset = Util.ResourcesLoad<TextAsset>(l.name);
            if (text != null && textOverride != null)
                Serializer.CSV.SetTempTextAssetText(Serializer.CSV.MergeCSV(text, textOverride));
            else if (textOverride != null)
            {
                Serializer.CSV.SetTempTextAssetText(Serializer.CSV.MergeCSV(tAsset.text, textOverride));
            }
            else
                Serializer.CSV.SetTempTextAssetText(text);

            string oName = l.name;
            l.name = Serializer.CSV._TempTextAssetName;
            l.process.Invoke(l);
            l.name = oName;
        }
    }
}