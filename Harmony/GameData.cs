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
            // So I don't have to edit textassets
            // (This avoids having to edit and rerelease NAR, for example)
            FixForNewSystems(__instance);

            // Make actual changes to NAR
            NARChanges(__instance);

            Database.FillDatabase();
        }

        private static void FixForNewSystems(GameData __instance)
        {
            // Fix various stats issues
            foreach (var kvp in __instance.stats)
            {
                // Remove engine weight from everything.
                // We directly calculate SHP required instead.
                kvp.Value.effectx.Remove("engine_weight");

                // We'll actually sim the hull geometry instead
                switch (kvp.Key)
                {
                    case "beam":
                    case "draught":
                    case "hull_form":
                        kvp.Value.effectx.Remove("operating_range");
                        continue;
                }

                // Elsewhere, just halve the effect of the operating range stat (except
                // the funnel-based one.
                // TODO: change how funnels work.
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

            // Add our own Freeboard stat
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

        private static void NARChanges(GameData __instance)
        {
            // Some shiptype stats are too restrictive
            foreach (var kvp in __instance.shipTypes)
            {
                if (kvp.Key == "dd")
                    kvp.Value.armorMin = 0f;
                else
                    kvp.Value.armorMin *= 0.5f;

                if (kvp.Key != "ic")
                    kvp.Value.speedMin -= 2f;
            }

            foreach (var kvp in __instance.parts)
            {
                var part = kvp.Value;
                if (part.isHull)
                {
                    switch (part.name)
                    {
                        // ** Britain **
                        // G3/N3 had transom sterns
                        case "bc_4_britain_g3":
                        case "bb_4_britain":
                            part.model = "brooklyn_hull_c_wide";
                            part.sectionsMin = 1;
                            part.sectionsMax = 5;
                            break;

                        // Minotaur class had a forecastle but also
                        // a transom stern. Not clear if "ca_6_british_largecruiser"
                        // or "ca_6_british". Either way, there's no transom +
                        // forecastle model.


                        // ** USA **
                        // Atlanta hull is the wrong shape.
                        // But use Brooklyn A rather than Brooklyn/Wichita/Cleveland/Baltimore B
                        case "ca_6_desmoines1":
                        case "ca_6_desmoines2":
                            part.model = "brooklyn_hull_a";
                            part.sectionsMin = 5;
                            part.sectionsMax = 8;
                            part.paramx.Add("CA_Heavy_USA", new Il2CppSystem.Collections.Generic.List<string>());
                            break;


                        // CA was using Bismarck hull (!) and Cleveland
                        // was using Atlanta hull.
                        case "ca_5_usa":
                        case "cl_6_cleveland":
                            part.model = "brooklyn_hull_b";
                            part.sectionsMin = 5;
                            part.sectionsMax = 7;
                            break;

                        // No good solution for Farragut/Porter--
                        // they should have forecastles but also transoms
                        // "dd_5_nose_large_usa" -- Porter
                        // "dd_5_akizuki_stern_flat" -- Farragut

                    }
                }
            }
        }
    }
}
