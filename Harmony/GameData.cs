using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            // HACK so I don't have to fix NAR stats
            foreach (var kvp in __instance.shipTypes)
            {
                if (kvp.Key == "dd")
                    kvp.Value.armorMin = 0f;
                else
                    kvp.Value.armorMin *= 0.5f;

                if (kvp.Key != "ic")
                    kvp.Value.speedMin -= 2f;
            }

            foreach (var kvp in __instance.stats)
            {
                kvp.Value.effectx.Remove("engine_weight");

                switch (kvp.Key)
                {
                    case "beam":
                    case "draught":
                    case "hull_form":
                        kvp.Value.effectx.Remove("operating_range");
                        continue;
                }

                // The cleaner version of this code causes a IL2Cpp unhollower compile issue
                // so we're doing this the slow and stupid way.
                if (kvp.Key == "smoke_exhaust")
                    continue;
                if (!kvp.Value.effectx.ContainsKey("operating_range"))
                    continue;

                var eff = kvp.Value.effectx["operating_range"];
                float k = eff.Key;
                float v = eff.Value;
                kvp.Value.effectx["operating_range"] = new Il2CppSystem.Collections.Generic.KeyValuePair<float, float>(k * 0.25f, v * 0.25f);
            }

            var sdFB = new StatData();
            var sdBeam = __instance.stats["beam"];

            sdFB.name = "freeboard";
            sdFB.combine = sdBeam.combine;
            sdFB.group = sdBeam.group;
            sdFB.nameUi = "$Stats_name_freeboard";
            sdFB.desc = "$Stats_desc_freeboard";
            sdFB.effect = "operating_range(-10;+10)";
            sdFB.effectx = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.KeyValuePair<float, float>>();
            sdFB.effectx.Add("accuracy", new Il2CppSystem.Collections.Generic.KeyValuePair<float, float>(-5f, 5f));
            sdFB.effectx.Add("accuracy_waves", new Il2CppSystem.Collections.Generic.KeyValuePair<float, float>(-10f, 15f));
            sdFB.effectx.Add("flooding_chance", new Il2CppSystem.Collections.Generic.KeyValuePair<float, float>(5f, -5f));
            sdFB.effectx.Add("flooding_water", new Il2CppSystem.Collections.Generic.KeyValuePair<float, float>(15f, -10f));
            // blank
            sdFB.enabled = sdBeam.enabled;
            sdFB.good = sdBeam.good;
            // hope we don't have to set ID
            sdFB.op = sdBeam.op;
            sdFB.order = sdBeam.order;
            sdFB.param = sdBeam.param;
            sdFB.paramx = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>>();
            __instance.stats["freeboard"] = sdFB;

            foreach (var kvp in __instance.parts)
            {
                if (!kvp.Value.isHull)
                    continue;

                // Let's reget each time just in case.
                kvp.Value.statsx.Add(__instance.stats["freeboard"], 0f);
            }
        }
    }
}
