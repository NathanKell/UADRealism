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

                if (kvp.Key == "tb")
                    kvp.Value.mainTo = 3;

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
                        case "bb_4_britain": // N3
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
                            // The default setup is to use Des Moines parts, but they're jsut Atlanta parts
                            // which look very wrong for a cruiser that looks close to the Baltimore/Oregon City classes.
                            // We're not going to remove that tag though.
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

            foreach (var kvp in __instance.torpedoTubes)
            {
                var tube = kvp.Value;
                if (tube.name == "torpedo_x0")
                    tube.baseTorpWeight *= 0.5f;
                else
                    tube.baseTorpWeight *= 0.1f;
            }

            float torpCount = MonoBehaviourExt.Param("torpedo_ammo", 2);
            foreach (var kvp in __instance.technologies)
            {
                var tech = kvp.Value;
                if (!tech.effects.ContainsKey("start") && tech.effects.TryGetValue("var", out var varLst))
                {
                    foreach (var sub in varLst)
                    {
                        if (sub.Count > 1 && sub[0] == "use_main_side_guns")
                            sub[1] = "1";
                    }
                }
                if (tech.name.StartsWith("torpedo_size_") && tech.name != "torpedo_size_end")
                {
                    int idx = int.Parse(tech.name.Replace("torpedo_size_", string.Empty));
                    if (idx > 0)
                    {
                        // Assume 1.6t launcher (i.e. 1/10th NAR).
                        // It varies from 1.6t to 1.76t (mark 1-5).
                        // Torp weights:
                        // 18" 400kg, 1892
                        // 18" 615kg, 1899
                        // 21" 680kg US, 1900-4
                        // 21" 953/1270kg, 1908
                        // 1454kg, 1913
                        // US 21" 1252-1441kg, 1914
                        // 1736kg, 1918
                        // 24" 2600kg, 1925
                        // 21" US 1559-1742kg, 1934
                        // 21" US 2177kg, 1944
                        // 21.65" Fr 2068kg, 1923
                        // 24" Jp 2700kg, 1933

                        float baseWeight = 0.320f;
                        float weight = idx switch
                        {
                            1 => 0.400f, // 16in, 1892. Treat as early Whitehead 18"
                            2 => 0.550f, // 17in, 1895. Treat as mid-late Whitehead 18"
                            3 => 0.630f, // 18in, 1899. Treat as developed 18in or early 21"
                            4 => 0.950f, // 19in, 1909. Treat as 21", mix between first and early WWI
                            5 => 1.250f, // 20in, 1917. Treat as early WWI 21"
                            6 => 1.500f, // 21in, 1921. Treat as later 21"
                            7 => 1.750f, // 22in, 1925. Treat as standard WWII 21"
                            8 => 2.068f, // 23in, 1935. Treat as heavy torp
                            _ => 2.700f, // 24in, 1939. Treat as Type 93
                        };
                        float weightDelta = (weight - baseWeight) * torpCount;
                        // Account for torpedo marks
                        float mult = -weightDelta * (1 / 1.6f) / idx switch
                        {
                            3 or 4 => 1.025f,
                            5 or 6 or 7 => 1.05f,
                            8 => 1.075f,
                            9 => 1.1f,
                            _ => 1f
                        };
                        mult *= 100f;
                        // There are also -2.5% weights in 1895,05,10,20,30
                        mult -= idx switch
                        {
                            1 => 0,
                            2 or 3 => 2.5f,
                            4 => 7.5f,
                            5 or 6 => 10f,
                            _ => 12.5f
                        };

                        foreach (var kvpE in tech.effects)
                        {
                            if (kvpE.key == "weight")
                            {
                                foreach (var lst in kvpE.Value)
                                {
                                    if (lst.Count < 2)
                                        continue;
                                    if (lst[0] == "torpedo")
                                        lst[1] = mult.ToString("G3");
                                }
                            }
                        }
                    }
                }

                // Don't make tech changes to hull weight as severe
                if (tech.componentx == null && tech.effects.TryGetValue("hull", out var hullEff))
                {
                    for (int i = 0; i < hullEff.Count; ++i)
                    {
                        var lst = hullEff[i];
                        // hullweight uses argindex 0
                        lst[0] = (Mathf.RoundToInt(float.Parse(lst[0]) * 0.75f * 10000f) * 0.0001f).ToString();
                    }
                }

                switch (tech.name)
                {
                    case "engine_engine_1":
                        ReplaceOrAddEffect(tech, "hp", "7");
                        break;
                    case "engine_engine_2": // VTE
                        tech.year = 1890; // technically earlier
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "9");
                        break;
                    case "engine_engine_3": // M-Exp 1 - Adv VTE
                        tech.year = 1897; // guess
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "10");
                        break;
                    case "engine_engine_4": // M-Exp 2 - Quad Exp
                        tech.year = 1902; // guess
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "12");
                        break;
                    case "engine_engine_5": // Turbines
                        tech.year = 1899; // Viper-class DD, LD 1899
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "15");
                        break;
                    case "engine_engine_6": // Geared Turbines
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "22");
                        break;
                    case "engine_engine_7": // Turbo-Electric
                        tech.year = 1916; // Tennessee-class LD 1916
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "13"); // FIXME
                        break;
                    case "engine_engine_8": // Geared Turbines II
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "35");
                        break;

                    // Make the alt engines sane
                    case "engine_engine_9": // Diesel I
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "14");
                        break;
                    case "engine_engine_10": // Diesel II
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "16");
                        break;
                    case "engine_engine_11": // Gas Turbine
                        tech.effects.Remove("engine");
                        ReplaceOrAddEffect(tech, "hp", "25");
                        break;

                    // Decrease weight effect
                    case "engine_special_7":
                        ReplaceOrAddEffect(tech, "engine", "2");
                        break;

                    // Rework boiler weights - Coal/Oil
                    case "engine_boiler_1": // start, coal
                        ReplaceOrAddEffect(tech, "boiler", "-15");
                        break;
                    case "engine_boiler_2":
                        tech.year = 1902; // Semi-Oil. In common use in first decade.
                        ReplaceOrAddEffect(tech, "boiler", "-5");
                        break;
                    case "engine_boiler_3":
                        tech.year = 1908; // Oil I, used on Paulding class LD 1908
                        //ReplaceOrAddEffect(tech, "boiler", "0");
                        tech.effects.Remove("boiler");
                        break;

                    // Rework boiler weights - regular upgardes
                    // Swap economizer and lighter boilers (now water-tube boilers)
                    case "engine_boiler_16":
                        tech.year = 1896;
                        ReplaceOrAddEffect(tech, "boiler", "1");
                        break;
                    case "engine_boiler_17":
                        tech.year = 1891;
                        ReplaceOrAddEffect(tech, "boiler", "1");
                        break;
                    case "engine_boiler_18":
                        ReplaceOrAddEffect(tech, "boiler", "1");
                        break;
                    case "engine_boiler_19":
                        ReplaceOrAddEffect(tech, "boiler", "2");
                        break;
                    case "engine_boiler_20":   
                        ReplaceOrAddEffect(tech, "boiler", "2");
                        break;
                    case "engine_boiler_21":
                        ReplaceOrAddEffect(tech, "boiler", "4");
                        break;
                    case "engine_boiler_22": // HP Steam
                        ReplaceOrAddEffect(tech, "boiler", "5");
                        ReplaceOrAddEffect(tech, "engine", "2");
                        break;


                    // Add later upgrades for (Very) HP steam etc
                    case "engine_boiler_14":
                        tech.year = 1935;
                        ReplaceOrAddEffect(tech, "boiler", "6");
                        ReplaceOrAddEffect(tech, "engine", "2");
                        break;
                    case "engine_boiler_15":
                        ReplaceOrAddEffect(tech, "boiler", "7");
                        ReplaceOrAddEffect(tech, "engine", "6");
                        break;

                    // Scale down engine weight increases from aux engines
                    case "engine_special_10":
                    case "engine_special_11":
                    case "engine_special_12":
                    case "engine_special_13":
                    case "engine_special_14":
                        if (float.TryParse(tech.effects["engine"][0][0], out var specE))
                        {
                            specE += 5f;
                            specE *= 0.2f;
                            specE -= 5f;
                            tech.effects["engine"][0][0] = specE.ToString("G4");
                        }
                        break;
                }
            }
        }

        private static void ReplaceOrAddEffect(TechnologyData tech, string effect, string arg)
        {
            if (tech.effects.TryGetValue(effect, out var eff))
            {
                eff[0][0] = arg;
                return;
            }

            eff = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<string>>();
            tech.effects[effect] = eff;
            eff.Add(new Il2CppSystem.Collections.Generic.List<string>());
            eff[0].Add(arg);
        }
    }
}
