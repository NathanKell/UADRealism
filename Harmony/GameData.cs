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
        internal static bool _IsRenderingHulls = false;
        internal static Dictionary<string, HullData> _HullModelData = new Dictionary<string, HullData>();

        // To avoid allocs. Compared to instantiation though it's probably not worth it.
        private static List<string> _foundVars = new List<string>();
        private static List<string> _matchedVars = new List<string>();
        private static Dictionary<GameObject, Bounds> _sectionBounds = new Dictionary<GameObject, Bounds>();

                
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            Il2CppSystem.Diagnostics.Stopwatch sw = new Il2CppSystem.Diagnostics.Stopwatch();
            sw.Start();

            // Pass 1: Figure out the min and max sections used for each model.
            // (and deal with beam/draught coefficients)
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                // Just apply beam delta to tonnage directly since we're going to use length/beam ratios
                data.tonnageMin *= Mathf.Pow(data.beamMin * 0.01f + 1f, data.beamCoef);
                data.tonnageMax *= Mathf.Pow(data.beamMax * 0.01f + 1f, data.beamCoef);

                data.beamCoef = 0f;

                // Draught is a linear scale to displacement at a given length/beam
                // TODO: Do we want to allow implicitly different block coefficients based on draught, as would
                // be true in reality?
                data.draughtCoef = 1f;


                // Now get var/section data
                string key = ModUtils.GetHullModelKey(data);
                if (!_HullModelData.TryGetValue(key, out var hData))
                {
                    hData = new HullData() {
                        _data = data,
                        _sectionsMin = data.sectionsMin,
                        _sectionsMax = data.sectionsMax
                    };
                    _HullModelData[key] = hData;
                }
                else
                {
                    hData._sectionsMin = Math.Min(hData._sectionsMin, data.sectionsMin);
                    hData._sectionsMax = Math.Max(hData._sectionsMax, data.sectionsMax);
                }
            }

            // Pass 2: Calculate stats
            _IsRenderingHulls = true;
            var centralGO = new GameObject("calculator");
            centralGO.active = true;
            foreach (var kvp in _HullModelData)
            {
                var hData = kvp.Value;
                var data = kvp.Value._data;

                var part = SpawnPart(data, centralGO);

                int count = hData._sectionsMax + 1;
                hData._statsSet = new ShipStatsCalculator.ShipStats[count];

                for (int secCount = hData._sectionsMin; secCount < count; ++secCount)
                {
                    // Ship.RefreshHull (only the bits we need)
                    CreateMiddles(part, secCount);
                    // It's annoying to rerun this every time, but it's not exactly expensive.
                    ApplyVariations(part);

                    PositionSections(part);

                    // Calc the stats for this layout. Note that scaling dimensions will only
                    // linearly change stats, it won't require a recalc.
                    var shipBounds = GetShipBounds(part.model.gameObject);
                    var stats = ShipStatsCalculator.Instance.GetStats(part.model.gameObject, shipBounds);

                    if (stats.Vd == 0f)
                        stats.Vd = 1f;

                    hData._statsSet[secCount] = stats;
                    Melon<UADRealismMod>.Logger.Msg($"{kvp.Key}@{secCount}: {(stats.Lwl):F2}x{stats.beamBulge:F2}x{(stats.T):F2}, {(stats.Vd)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. Awp={(stats.Awp):F1}, Am={stats.Am:F1}");
                }
                string varStr = kvp.Key.Substring(data.model.Length);
                if (varStr != string.Empty)
                    varStr = $"({varStr.Substring(1)}) ";
                Melon<UADRealismMod>.Logger.Msg($"Calculated stats for {data.model} {varStr}for {hData._sectionsMin} to {hData._sectionsMax} sections");

                //Melon<UADRealismMod>.Logger.Msg($"{data.model}: {(stats.Lwl * scaleFactor):F2}x{beamStr}x{(stats.D * scaleFactor):F2}, {(stats.Vd * tRatio)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. Awp={(stats.Awp * scaleFactor * scaleFactor):F1}, Am={(stats.Am * scaleFactor * scaleFactor):F2}");

                GameObject.Destroy(part.gameObject);
                Part.CleanPartsStorage();
            }
            _IsRenderingHulls = false;
            GameObject.Destroy(centralGO);

            // Pass 3: Set starting scales for all partdata
            Melon<UADRealismMod>.Logger.Msg("time,name,model,sections,oldScale,tonnageMin,scaleMaxPct,oldScaleVd,newScale,Lwl,Beam,Bulge,Draught,Cb,Cm,Cwp,Cp,Cvp");
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                string key = ModUtils.GetHullModelKey(data);
                if (!_HullModelData.TryGetValue(key, out var hData))
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find data for partdata {data.name} of model {data.model} with key {key}");
                    continue;
                }

                int secNominal = (data.sectionsMin + data.sectionsMax) / 2;
                float tng = Mathf.Lerp(data.tonnageMin, data.tonnageMax, 0.25f);
                if (secNominal < hData._sectionsMin || secNominal > hData._sectionsMax)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Unable to find section data for partdata {data.name} of model {data.model} with key {key}, at section count {secNominal}");
                    continue;
                }
                var stats = hData._statsSet[secNominal];
                float oldScale = data.scale;
                float oldCube = oldScale * oldScale * oldScale;
                float ratio = tng / stats.Vd;
                float newScale = Mathf.Pow(ratio, 1f / 3f);
                data.scale = newScale;
                Melon<UADRealismMod>.Logger.Msg($",{data.name},{data.model},{secNominal},{oldScale:F3}x,{tng:F0}t,{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{(stats.Vd * oldCube):F0}t,{newScale:F3}x,L={(stats.Lwl * newScale):F2},B={(stats.B * newScale):F2},BB={(stats.beamBulge * newScale):F2},T={(stats.T * newScale):F2},Cb={stats.Cb:F3},Cm={stats.Cm:F3},Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3},Cvp={stats.Cvp:F3}");
            }
            var time = sw.Elapsed;
            Melon<UADRealismMod>.Logger.Msg($"Total time: {time}");
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

        internal static void CreateMiddles(Part part, int numTotal)
        {
            // Spawn middle sections
            for (int i = part.middles.Count; i < numTotal; ++i)
            {
                var mPrefab = part.middlesBase[i % part.middlesBase._size];
                var cloned = Util.CloneGameObject(mPrefab);
                part.middles.Add(cloned);
                Util.SetActiveX(cloned, true);
            }
        }

        internal static void ApplyVariations(Part part)
        {
            part.data.paramx.TryGetValue("var", out var desiredVars);
            var varComps = part.model.gameObject.GetComponentsInChildren<Variation>(true);
            for (int i = 0; i < varComps.Length; ++i)
            {
                var varComp = varComps[i];
                string varName = string.Empty;
                if (desiredVars != null)
                {
                    foreach (var s in varComp.variations)
                        if (desiredVars.Contains(s))
                            _matchedVars.Add(s);
                }

                _foundVars.AddRange(_matchedVars);

                if (_matchedVars.Count > 1)
                    Debug.LogError("Found multiple variations on partdata " + part.data.name + ": " + string.Join(", ", _matchedVars));
                if (_matchedVars.Count > 0)
                    varName = _matchedVars[0];
                
                _matchedVars.Clear();

                // This is a reimplementation of Variation.Init, since that needs a Ship
                var childObjs = Util.GetChildren(varComp);
                if (childObjs.Count == 0)
                {
                    Debug.LogError("For part " + part.data.name + ", no variations!");
                    continue;
                }
                if (varComp.variations.Count != childObjs.Count)
                {
                    Debug.LogError("For part " + part.data.name + ", var count / child count mismatch!");
                    continue;
                }
                int varIdx = varComp.variations.IndexOf(varName);
                if ((varIdx < 0 && varName != string.Empty))
                {
                    Debug.LogError("For part " + part.data.name + ", can't find variation " + varName);
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
                            Debug.LogError("For part " + part.data.name + ", can't find default variation " + varComp.@default);
                        }
                        selectedIdx = 0;
                    }
                }
                for (int v = childObjs.Count; v-- > 0;)
                    Util.SetActiveX(childObjs[v], v == selectedIdx);
            }

            // This check is not quite the same as the game's
            // but it's fine.
            if (desiredVars != null)
            {
                foreach (var desV in desiredVars)
                {
                    for (int v = _foundVars.Count; v-- > 0;)
                    {
                        if (_foundVars[v] == desV)
                            _foundVars.RemoveAt(v);
                    }
                }
                if (_foundVars.Count > 0)
                    Debug.LogError("On partdata " + part.data.name + ": Missing vars: " + string.Join(", ", _foundVars));
            }

            _foundVars.Clear();
        }

        internal static void PositionSections(Part part)
        {
            // Get reversed list of sections so we can position them
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
            var sectionsGO = Util.GetParent(part.bow);
            var sectionsTrf = sectionsGO.transform;
            float shipLength = 0f;

            foreach (var sec in sectionsReverse)
            {
                var secBounds = GetSectionBounds(sec, sectionsTrf);
                _sectionBounds[sec] = secBounds;
                shipLength += secBounds.size.z;
            }

            // Reposition/rescale sections
            float lengthOffset = shipLength * -0.5f;
            foreach (var sec in sectionsReverse)
            {
                var secBounds = _sectionBounds[sec];
                Util.SetLocalZ(sec.transform, secBounds.size.z * 0.5f - secBounds.center.z + lengthOffset + sec.transform.localPosition.z);
                Util.SetScaleX(sec.transform, 1f); // beam
                Util.SetScaleY(sec.transform, 1f); // draught
                lengthOffset += secBounds.size.z;
            }

            _sectionBounds.Clear();
        }

        internal static Bounds GetSectionBounds(GameObject sec, Transform space)
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
                // transform bounds to 'Sections' space
                meshBounds = Util.TransformBounds(trf, meshBounds);
                var invBounds = Util.InverseTransformBounds(space, meshBounds);

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

            return secBounds;
        }

        internal static Part SpawnPart(PartData data, GameObject parent)
        {
            var partGO = new GameObject(data.name);
            partGO.SetParent(parent, true);
            var part = partGO.AddComponent<Part>();
            part.data = data;
            partGO.active = true;

            // Do what we need from Ship.ChangeHull and Part.LoadModel
            var model = UnityEngine.Resources.Load<GameObject>(data.model); //Util.ResourcesLoad<GameObject>(data.model); //(pmi.modelName);
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
            ModUtils.FindChildrenStartsWith(sections, "Middle", middles);
            middles.Sort((a, b) => a.name.CompareTo(b.name));
            part.middlesBase = new Il2CppSystem.Collections.Generic.List<GameObject>();
            foreach (var m in middles)
                part.middlesBase.Add(m);

            part.middles = new Il2CppSystem.Collections.Generic.List<GameObject>();
            part.hullInfo = part.model.GetComponent<HullInfo>();
            //part.RegrabDeckSizes(true);
            //part.RecalcVisualSize();

            foreach (var go in part.middlesBase)
                Util.SetActiveX(go, false);

            return part;
        }
    }
}
