using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;

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

        internal static readonly string? _BasePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        internal static readonly string _FlagFile = "flags.csv";

        [ConfigParse("New Scrapping behavior", "scrap_enable")]
        public static bool ScrappingChange = false;
        [ConfigParse("Ports/Provinces overriding", "override_map", _exceptVal = 2f) ]
        public static bool OverrideMap = false;
        [ConfigParse("Map dumping", "override_map", _checkValue = 2f, _invertCheck = false)]
        public static bool DumpMap = false;

        public static void LoadConfig()
        {
            Melon<TweaksAndFixes>.Logger.Msg("Loading config:");
            var fields = typeof(Config).GetFields(HarmonyLib.AccessTools.all);
            foreach (var f in fields)
            {
                var attrib = (ConfigParse?)f.GetCustomAttribute(typeof(ConfigParse));
                if (attrib == null)
                    continue;

                // Do this to suppress warning message
                Il2Cpp.G.GameData.parms.TryGetValue(attrib._param, out var param);
                bool isEnabled;
                if (attrib._invertCheck)
                    isEnabled = param != attrib._checkValue && (attrib._checkValue == attrib._exceptVal || param != attrib._exceptVal);
                else
                    isEnabled = param == attrib._checkValue;
                f.SetValue(null, isEnabled);

                Melon<TweaksAndFixes>.Logger.Msg($"{attrib._name}: {((bool)(f.GetValue(null)) ? "Enabled" : "Disabled")}");
            }
        }
    }
}
