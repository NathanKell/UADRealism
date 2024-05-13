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
        internal static Dictionary<string, ShipStatsCalculator.ShipStats> _ModelStats = new Dictionary<string, ShipStatsCalculator.ShipStats>();
        internal static bool _PrintHeader = true;
        internal static bool _IsProcessing = false;

        private static void FindChildrenStartsWith(GameObject obj, string str, List<GameObject> list)
        {
            if (obj.name.StartsWith(str))
                list.Add(obj);
            for (int i = 0; i < obj.transform.childCount; ++i)
                FindChildrenStartsWith(obj.transform.GetChild(i).gameObject, str, list);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            _IsProcessing = true;
            var centralGO = new GameObject("calculator");
            centralGO.active = true;
            //Melon<UADRealismMod>.Logger.Msg("Starting");
            //var ship = Util.AttachInst(centralGO, "Ship").GetComponent<Ship>();
            //Melon<UADRealismMod>.Logger.Msg("Made ship");
            //ship.id = Il2CppSystem.Guid.NewGuid();
            //ship.shipType = __instance.shipTypes["xx"];
            //ship.player = null;
            //ship.playerOriginal = null;
            //ship.InitCrewSkillsDispersion();
            //ship.GrabTechs();
            //ship.NeedRecalcCache();
            //ship.statsValid = false;
            //Melon<UADRealismMod>.Logger.Msg("Starting loop");
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                //if (Util.resCache == null || outputted)
                //    return;

                //outputted = true;
                //Melon<UADRealismMod>.Logger.Msg($"ResCache Count: {(Util.resCache != null ? Util.resCache.Count : 0)}");
                //foreach (var kvp in Util.resCache)
                //{
                //    Melon<UADRealismMod>.Logger.Msg($"Entry: {kvp.Key} : {kvp.Value.name} of type {kvp.Value.GetType()}");
                //}
                if (data.type != "hull")
                    continue;

                float coeffD = data.draughtCoef;
                float coeffB = data.beamCoef;

                data.draughtCoef = coeffD * 3f;
                data.beamCoef = coeffB * 3f;
                //Melon<UADRealismMod>.Logger.Msg($"Part {data.name}: beam {coeffB:F3}/{data.beamCoef:F3}, draught {coeffD:F3}/{data.draughtCoef:F3}");

                if (_PrintHeader)
                {
                    Melon<UADRealismMod>.Logger.Msg("time,name,model,sectionsMin,tonnageMin,Vd,Vd_scaled,oldScale,newScale,scaleMaxPct");
                    _PrintHeader = false;
                }

                bool isMaine = data.name == "ca_maine_threemast";
                if (!isMaine)
                    continue;

                string key = data.model + "$" + data.sectionsMin;
                if (!_ModelStats.TryGetValue(key, out var stats))
                {
                    //Melon<UADRealismMod>.Logger.Msg($"Calculating {data.name}:{key}. Type {data.shipType}");
                    //ship.shipType = data.shipType;
                    //ship.ChangeHull(data, null, null, false);
                    //if (ship.hull != null)
                    //    ship.hull.Erase(false);
                    //var fx = Util.GetChildren(ship.effectsCont);
                    //foreach (var f in fx)
                    //    GameObject.Destroy(f);
                    //ship.hull = Part.Create(data, ship, ship.hullCont, new Il2CppSystem.Nullable<int>(0), false);
                    var partGO = new GameObject(key);
                    partGO.SetParent(centralGO, true);
                    var part = partGO.AddComponent<Part>();
                    part.data = data;
                    partGO.active = true;

                    //var pmi = Part.GetModelNameScale(data, ship);
                    var model = UnityEngine.Resources.Load<GameObject>(data.model); //Util.ResourcesLoad<GameObject>(data.model); //(pmi.modelName);
                    //var processed = Part.ProcessPartModelPrefab(model);
                    var instModel = Util.AttachInst(partGO, model /*processed*/);
                    instModel.active = true;
                    instModel.transform.localScale = Vector3.one;
                    part.model = instModel.GetComponent<PartModel>();
                    part.isShown = true;
                    var visual = part.model.GetChild("Visual", false);
                    var sections = visual.GetChild("Sections", false);
                    part.bow = sections.GetChild("Bow", false);
                    part.stern = sections.GetChild("Stern", false);
                    var middles = new List<GameObject>();
                    FindChildrenStartsWith(sections, "Middle", middles);
                    middles.Sort((a, b) => a.name.CompareTo(b.name));
                    part.middlesBase = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    foreach (var m in middles)
                        part.middlesBase.Add(m);
                    part.middles = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    part.hullInfo = part.model.GetComponent<HullInfo>();
                    //part.RegrabDeckSizes(true);
                    //part.RecalcVisualSize();

                    // Start Ship.RefreshHull (only the bits we need)

                    // Spawn middle sections
                    foreach (var go in part.middlesBase)
                        Util.SetActiveX(go, false);
                    for(int i = 0; i < data.sectionsMin; ++i)
                    {
                        var mPrefab = part.middlesBase[i % part.middlesBase._size];
                        var cloned = Util.CloneGameObject(mPrefab);
                        part.middles.Add(cloned);
                        Util.SetActiveX(cloned, true);
                    }

                    // Set variations active
                    var foundVars = new List<string>();
                    if (!data.paramx.TryGetValue("var", out var desiredVars))
                        desiredVars = new Il2CppSystem.Collections.Generic.List<string>();
                    var varComps = instModel.GetComponentsInChildren<Variation>(true);
                    for (int i = 0; i < varComps.Length; ++i)
                    {
                        var varComp = varComps[i];
                        var intersect = new List<string>();
                        string varName = string.Empty;
                        foreach (var s in varComp.variations)
                            if (desiredVars.Contains(s))
                                intersect.Add(s);
                        
                        foundVars.AddRange(intersect);

                        if (intersect.Count > 1)
                            Debug.LogError("Found multiple variations on partdata " + data.name + ": " + string.Join(", ", intersect));
                        if (intersect.Count > 0)
                            varName = foundVars[0];

                        // This is a reimplementation of Variation.Init
                        var childObjs = Util.GetChildren(varComp);
                        if (childObjs.Count == 0)
                        {
                            Debug.LogError("For part " + data.name + ", no variations!");
                            continue;
                        }
                        if (varComp.variations.Count != childObjs.Count)
                        {
                            Debug.LogError("For part " + data.name + ", var count / child count mismatch!");
                            continue;
                        }
                        int varIdx = varComp.variations.IndexOf(varName);
                        if ((varIdx < 0 && varName != string.Empty))
                        {
                            Debug.LogError("For part " + data.name + ", can't find variation " + varName);
                        }
                        int selectedIdx;
                        if (varIdx >= 0)
                        {
                            selectedIdx = varIdx;
                        }
                        else if (varComp.random)
                        {
                            selectedIdx = UnityEngine.Random.Range(0, varComp.variations.Count - 1);
                        }
                        else
                        {
                            selectedIdx = varComp.variations.IndexOf(varComp.@default);
                            if (selectedIdx < 0)
                            {
                                if (varComp.@default != string.Empty)
                                {
                                    Debug.LogError("For part " + data.name + ", can't find default variation " + varComp.@default);
                                }
                                selectedIdx = 0;
                            }
                        }
                        for (int v = childObjs.Count; v-- > 0;)
                            Util.SetActiveX(childObjs[v], v == selectedIdx);
                    }
                    foreach (var desV in desiredVars)
                    {
                        for (int v = foundVars.Count; v-- > 0;)
                        {
                            if (foundVars[v] == desV)
                                foundVars.RemoveAt(v);
                        }
                    }
                    if (foundVars.Count > 0)
                        Debug.LogError("On partdata " + data.name + ": Missing vars: " + string.Join(", ", foundVars));

                    // Get reversed list of sections
                    var sectionsReverse = new Il2CppSystem.Collections.Generic.List<GameObject>();
                    sectionsReverse.AddRange(part.hullSections);
                    for (int i = 0; i < sectionsReverse.Count / 2; ++i)
                    {
                        int opposite = sectionsReverse.Count - 1 - i;
                        if (i == opposite)
                            break;
                        
                        var tmp = sectionsReverse[i];
                        sectionsReverse[i] = sectionsReverse[opposite];
                        sectionsReverse[opposite] = tmp;
                    }

                    // Calculate bounds for all sections
                    var dict = new Dictionary<GameObject, Bounds>();
                    var sectionsGO = Util.GetParent(part.bow);
                    var sectionsTrf = sectionsGO.transform;
                    float shipLength = 0f;

                    foreach (var sec in sectionsReverse)
                    {
                        var vr = Part.GetVisualRenderers(sec);
                        Bounds secBounds = new Bounds();
                        bool foundBounds = false;
                        foreach (var r in vr)
                        {
                            if (!r.gameObject.activeInHierarchy)
                                continue;

                            var trf = r.transform;
                            var mesh = r.GetComponent<MeshFilter>();
                            var sharedMesh = mesh.sharedMesh;
                            var meshBounds = sharedMesh.bounds;
                            // transform bounds to sections space
                            meshBounds = Util.TransformBounds(trf, meshBounds);
                            var invBounds = Util.InverseTransformBounds(sectionsTrf, meshBounds);

                            if (foundBounds)
                                secBounds.Encapsulate(invBounds);
                            else
                                secBounds = invBounds;
                        }
                        var secInfo = sec.GetComponent<SectionInfo>() ?? sec.GetComponentInChildren<SectionInfo>();
                        if (secInfo != null)
                        {
                            //Debug.Log($"For part {data.name}, sec {sec.name}, size mods {secInfo.sizeFrontMod} + {secInfo.sizeBackMod}");
                            var bSize = secBounds.size;
                            bSize.z += secInfo.sizeFrontMod + secInfo.sizeBackMod;
                            secBounds.size = bSize;
                            var bCent = secBounds.center;
                            bCent.z += (secInfo.sizeFrontMod - secInfo.sizeBackMod) * 0.5f;
                            secBounds.center = bCent;
                        }
                        dict[sec] = secBounds;
                        shipLength += secBounds.size.z;
                    }
                    // Reposition/rescale sections
                    float lengthOffset = shipLength * -0.5f;
                    foreach (var sec in sectionsReverse)
                    {
                        var secBounds = dict[sec];
                        Util.SetLocalZ(sec.transform, secBounds.size.z * 0.5f - secBounds.center.z + lengthOffset + sec.transform.localPosition.z );
                        Util.SetScaleX(sec.transform, 1f); // beam
                        Util.SetScaleY(sec.transform, 1f); // draught
                        lengthOffset += secBounds.size.z;
                    }

                    // Get ship bounds
                    var shipBounds = GetShipBounds(visual);

                    // Done! Now we can calculate.

                    //Melon<UADRealismMod>.Logger.Msg("Tonnage set");
                    stats = ShipStatsCalculator.Instance.GetStats(part.model.gameObject, shipBounds, isMaine ? 0f : 1f);

                    if (stats.Vd == 0f)
                        stats.Vd = 1f;

                    _ModelStats[key] = stats;

                    float tRatio = data.tonnageMin / stats.Vd;
                    float scaleFactor = Mathf.Pow(tRatio, 1f / 3f);

                    string beamStr = (stats.B * scaleFactor).ToString("F2");
                    if (stats.beamBulge != stats.B)
                    {
                        beamStr += $" ({(stats.beamBulge * scaleFactor):F2} at {(stats.bulgeDepth * scaleFactor):F2})";
                    }
                    string logMsg = $"{data.model}: {(stats.Lwl * scaleFactor):F2}x{beamStr}x{(stats.D * scaleFactor):F2}, {(stats.Vd * tRatio)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. Awp={(stats.Awp * scaleFactor * scaleFactor):F1}, Am={(stats.Am * scaleFactor * scaleFactor):F2}";
                    if (isMaine)
                    {
                        logMsg = $"Bounds {shipBounds}. {logMsg}";
                    }
                    if (check || isMaine)
                    {
                        check = false;
                        part.model.transform.localScale = Vector3.one * scaleFactor;
                        shipBounds = GetShipBounds(visual);
                        var stats2 = ShipStatsCalculator.Instance.GetStats(part.model.gameObject, shipBounds, isMaine ? 0f : 1f);
                        logMsg += $",CHECK new {stats2.Vd}";
                        if (isMaine)
                            logMsg += "Bounds: " + shipBounds;
                    }
                    Melon<UADRealismMod>.Logger.Msg(logMsg);

                    GameObject.DestroyImmediate(partGO);
                    Part.CleanPartsStorage();
                }
                float oldScale = data.scale;
                float ratio = data.tonnageMin / stats.Vd;
                float newScale = Mathf.Pow(ratio, 1f / 3f);
                data.scale = newScale;
                Melon<UADRealismMod>.Logger.Msg($",{data.name},{data.model},{data.sectionsMin},{data.tonnageMin},{stats.Vd},{stats.Vd * oldScale * oldScale * oldScale},{oldScale:F3},{data.scale:F3},{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0}");
            }
            GameObject.Destroy(centralGO);
            _IsProcessing = false;
        }

        internal static Bounds GetShipBounds(GameObject obj)
        {
            var allVRs = Part.GetVisualRenderers(obj);
            Bounds shipBounds = new Bounds();
            bool foundShipBounds = false;
            foreach (var r in allVRs)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                if (foundShipBounds)
                {
                    shipBounds.Encapsulate(r.bounds);
                }
                else
                {
                    foundShipBounds = true;
                    shipBounds = r.bounds;
                }
            }

            return shipBounds;
        }
        static bool check = true;
    }
}
