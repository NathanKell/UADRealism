using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(GameData))]
    internal class Patch_GameData
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Prefix_PostProcessAll(GameData __instance)
        {
            //Serializer.CSV.TestNative();
        }

        private static readonly List<string> _FixKeys = new List<string>();
        private static void FixRandPart(RandPart rp)
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
                    case "auto_refit":
                        return;
                }
                _FixKeys.Add(kvp.Key);
            }
            foreach (var key in _FixKeys)
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
            _FixKeys.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            
            foreach (var rp in __instance.randParts.Values)
            {
                FixRandPart(rp);
            }
            foreach (var rp in __instance.randPartsRefit.Values)
            {
                FixRandPart(rp);
            }

            foreach (var data in __instance.parts.Values)
            {
                if (!data.isGun)
                    continue;

                data.GetCaliberInch();
            }

            //Serializer.CSV.TestNativePost();

            // Run after other things have a chance to update GameData
            MelonCoroutines.Start(FillDatabase());
        }

        internal static System.Collections.IEnumerator FillDatabase()
        {
            yield return new WaitForEndOfFrame();
            Database.FillDatabase();

            HashSet<string> models = new HashSet<string>();
            List<string> schemes = new List<string>();
            foreach (var p in G.GameData.parts.Values)
            {
                if (p.isHull)
                {
                    if (models.Contains(p.model))
                        continue;
                    models.Add(p.model);
                    var part = SpawnPart(p);

                    foreach (var s in part.hullInfo.schemes)
                        schemes.Add(s);
                    GameObject.Destroy(part.gameObject);
                    Part.CleanPartsStorage();

                    Melon<TweaksAndFixes>.Logger.Msg($"Hull {p.model} has {(part.hullInfo.waterLevel != 0 ? $"waterlevel {part.hullInfo.waterLevel:F3}, " : string.Empty)}stabFwd {part.hullInfo.stabilityForward:F3}, schemes: {string.Join(", ", schemes)}");
                    schemes.Clear();
                }
            }

            Melon<TweaksAndFixes>.Logger.Msg("Loaded database");
        }

        private static Part SpawnPart(PartData data)
        {
            var partGO = new GameObject(data.name);
            var part = partGO.AddComponent<Part>();
            part.data = data;
            partGO.active = true;

            // Do what we need from Ship.ChangeHull and Part.LoadModel
            var model = Resources.Load<GameObject>(data.model);
            var instModel = Util.AttachInst(partGO, model);
            instModel.active = true;
            instModel.transform.localScale = Vector3.one;
            part.model = instModel.GetComponent<PartModel>();
            part.isShown = true;

            var visual = part.model.GetChild("Visual", false);
            if (data.isHull)
            {
                var sections = visual.GetChild("Sections", false);
                part.bow = sections.GetChild("Bow", false);
                part.stern = sections.GetChild("Stern", false);
                var middles = new List<GameObject>();
                ModUtils.FindChildrenStartsWith(sections, "Middle", middles);
                middles.Sort((a, b) => a.name.CompareTo(b.name));
                part.middlesBase = new Il2CppSystem.Collections.Generic.List<GameObject>();
                foreach (var m in middles)
                {
                    part.middlesBase.Add(m);
                    m.active = false;
                }

                part.middles = new Il2CppSystem.Collections.Generic.List<GameObject>();
                part.hullInfo = part.model.GetComponent<HullInfo>();
                //part.RegrabDeckSizes(true);
                //part.RecalcVisualSize();

                foreach (var go in part.middlesBase)
                    Util.SetActiveX(go, false);
            }

            return part;
        }
    }
}
