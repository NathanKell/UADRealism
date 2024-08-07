using MelonLoader;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(TweaksAndFixes.TweaksAndFixes), "TweaksAndFixes", "3.1.0", "NathanKell")]
[assembly: MelonColor(255, 220, 220, 0)]
namespace TweaksAndFixes
{
    public static class Config
    {
        public static int MaxGunGrade = 5;
        public static bool Patch_scrap_enable = false;
        public static bool Patch_override_map = false;
        public static bool DumpMap = false;

        public static void LoadConfig()
        {
            var fields = typeof(Config).GetFields(BindingFlags.Static);
            foreach (var f in fields)
            {
                if (f.Name.StartsWith("Patch_"))
                    f.SetValue(null, Il2Cpp.MonoBehaviourExt.Param("taf" + f.Name.Substring(5), 0f) > 0f);
            }

            DumpMap = Il2Cpp.MonoBehaviourExt.Param("taf_override_map", 0f) == 2f;
        }
    }

    public class TweaksAndFixes : MelonMod
    {
        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
        }

        public override void OnDeinitializeMelon()
        {
            base.OnDeinitializeMelon();
        }
    }
}
