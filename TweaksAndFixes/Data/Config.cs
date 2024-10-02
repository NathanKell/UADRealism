using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;

#pragma warning disable CS8604
#pragma warning disable CS8605
#pragma warning disable CS8618

namespace TweaksAndFixes
{
    public class Config
    {
        [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property, AllowMultiple = false)]
        public class ConfigParse : System.Attribute
        {
            public string _name;
            public string _param;
            public float _checkValue = 0;
            public bool _invertCheck = true;
            public float _exceptVal = 0;

            public ConfigParse(string n, string p, bool useTAF = true)
            {
                _name = n;
                if (useTAF)
                    _param = "taf_" + p;
                else
                    _param = p;
            }

            public ConfigParse(string n, string p, float c, bool invert = false, bool useTAF = true)
                : this(n, p, useTAF)
            { _checkValue = c; _invertCheck = invert; }

            public ConfigParse(string n, string p, float c, bool invert = false, float e = 0, bool useTAF = true)
                : this(n, p, c, invert, useTAF)
            { _exceptVal = e; }
        }

        public static int MaxGunGrade = 5;
        public static int StartingYear = 1890;

        internal static readonly string? _BasePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        internal static readonly string _FlagFile = "flags.csv";
        internal static readonly string _SpriteFile = "sprites.csv";
        internal static readonly string _PredefinedDesignsFile = "predefinedDesigns.bin";

        public enum OverrideMapOptions
        {
            Disabled,
            Enabled,
            DumpData,
            LogDifferences
        }

        [ConfigParse("New Scrapping behavior", "scrap_enable")]
        public static bool ScrappingChange = false;
        [ConfigParse("Ports/Provinces overriding", "override_map")]
        public static OverrideMapOptions OverrideMap = OverrideMapOptions.Disabled;
        [ConfigParse("Ship Autodesign Tweaks", "shipgen_tweaks")]
        public static bool ShipGenTweaks = true;
        [ConfigParse("Alliance Behavior Tweaks", "alliance_changes")]
        public static bool AllianceTweaks = false;

        public static void LoadConfig()
        {
            Melon<TweaksAndFixes>.Logger.Msg("************************************************** Loading config:");
            var fields = typeof(Config).GetFields(HarmonyLib.AccessTools.all);
            foreach (var f in fields)
            {
                var attrib = (ConfigParse?)f.GetCustomAttribute(typeof(ConfigParse));
                if (attrib == null)
                    continue;

                // Do this to suppress warning message
                if (Il2Cpp.G.GameData.parms.TryGetValue(attrib._param, out var param))
                {
                    if (f.FieldType.IsEnum)
                    {
                        if (Il2Cpp.G.GameData.paramsRaw.TryGetValue(attrib._param, out var paramObj) && !string.IsNullOrEmpty(paramObj.str))
                        {
                            if (!Enum.TryParse(f.FieldType, paramObj.str, out var eResult))
                            {
                                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: Could not parse {paramObj.str}, using default value {eResult}");
                            }
                            else
                            {
                                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: {eResult}");
                            }
                            f.SetValue(null, eResult);
                        }
                        else
                        {
                            var eArray = Enum.GetValues(f.FieldType);
                            int val = (int)param;
                            var eResult = val >= eArray.Length ? eArray.GetValue(0) : eArray.GetValue(val);
                            if (val >= eArray.Length)
                            {
                                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: Value {val} out of range, using default value {eResult}");
                            }
                            else
                            {
                                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: {eResult}");
                            }
                            f.SetValue(null, eResult);
                        }
                    }
                    else
                    {
                        bool isEnabled;
                        if (attrib._invertCheck)
                            isEnabled = param != attrib._checkValue && (attrib._checkValue == attrib._exceptVal || param != attrib._exceptVal);
                        else
                            isEnabled = param == attrib._checkValue;
                        f.SetValue(null, isEnabled);
                    }
                }
                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: {(f.FieldType.IsEnum ? f.GetValue(null) : ((bool)(f.GetValue(null)) ? "Enabled" : "Disabled"))}");
            }
        }

        public static float Param(string name, float defValue = 0f)
        {
            if (!Il2Cpp.G.GameData.parms.TryGetValue(name, out var param))
                return defValue;
            return param;
        }

        public static string? ParamS(string name, string? defValue = null)
        {
            if (!Il2Cpp.G.GameData.paramsRaw.TryGetValue(name, out var param))
                return defValue;
            return param.str;
        }
    }
}
