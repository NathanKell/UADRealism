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
        internal struct HullData
        {
            public ShipStatsCalculator.ShipStats _stats;
            public int _sectionsForStats;
            public List<float> _sectionLengths;
        }

        internal static Dictionary<string, HullData> _ModelStats = new Dictionary<string, HullData>();
        internal static bool _IsProcessing = false;

        // To avoid allocs
        private static List<string> _foundVars = new List<string>();
        private static List<string> _matchedVars = new List<string>();
        private static Dictionary<GameObject, Bounds> _sectionBounds = new Dictionary<GameObject, Bounds>();

        private static void FindChildrenStartsWith(GameObject obj, string str, List<GameObject> list)
        {
            if (obj.name.StartsWith(str))
                list.Add(obj);
            for (int i = 0; i < obj.transform.childCount; ++i)
                FindChildrenStartsWith(obj.transform.GetChild(i).gameObject, str, list);
        }

        internal static int SectionsUsed(int secMin, int secMax) => (secMin + secMax) / 2;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameData.PostProcessAll))]
        internal static void Postfix_PostProcessAll(GameData __instance)
        {
            _IsProcessing = true;

            Melon<UADRealismMod>.Logger.Msg("time,name,model,sections,oldScale,tonnageMin,scaleMaxPct,oldScaleVd,newScale,Lwl,Beam,Bulge,Draught,Cb,Cm,Cwp,Cp,Cvp");

            var centralGO = new GameObject("calculator");
            centralGO.active = true;
            foreach (var kvp in __instance.parts)
            {
                var data = kvp.Value;
                if (data.type != "hull")
                    continue;

                data.draughtCoef = 0f;
                data.beamCoef = 0f;

                int sectionsUsed = SectionsUsed(data.sectionsMin, data.sectionsMax);
                string key = data.model + "$" + sectionsUsed;
                if (!_ModelStats.TryGetValue(key, out var hData))
                {
                    var part = SpawnPart(data, key, centralGO);
                    var shipBounds = GetShipBounds(part.model.gameObject);
                    var stats = ShipStatsCalculator.Instance.GetStats(part.model.gameObject, shipBounds);

                    if (stats.Vd == 0f)
                        stats.Vd = 1f;

                    hData = new HullData()
                    {
                        _stats = stats,
                        _sectionsForStats = sectionsUsed,
                        _sectionLengths = GetSectionLengths(part)
                    };
                    _ModelStats[key] = hData;

                    //Melon<UADRealismMod>.Logger.Msg($"{data.model}: {(stats.Lwl * scaleFactor):F2}x{beamStr}x{(stats.D * scaleFactor):F2}, {(stats.Vd * tRatio)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. Awp={(stats.Awp * scaleFactor * scaleFactor):F1}, Am={(stats.Am * scaleFactor * scaleFactor):F2}");

                    GameObject.DestroyImmediate(part.gameObject);
                    Part.CleanPartsStorage();
                }
                // Log regardless
                {
                    var stats = hData._stats;
                    float oldScale = data.scale;
                    float oldCube = oldScale * oldScale * oldScale;
                    float ratio = data.tonnageMin / hData._stats.Vd;
                    float newScale = Mathf.Pow(ratio, 1f / 3f);
                    data.scale = newScale;
                    Melon<UADRealismMod>.Logger.Msg($",{data.name},{data.model},{sectionsUsed},{oldScale:F3}x,{data.tonnageMin:F0}t,{(Mathf.Pow(data.tonnageMax / data.tonnageMin, 1f / 3f) - 1f):P0},{(stats.Vd * oldCube):F0}t,{newScale:F3}x,L={(stats.Lwl * newScale):F2},B={(stats.B * newScale):F2},BB={(stats.beamBulge * newScale):F2},T={(stats.T * newScale):F2},Cb={stats.Cb:F3},Cm={stats.Cm:F3},Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3},Cvp={stats.Cvp:F3}");
                }
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

        internal static void CreateMiddles(Part part, int startIdx, int numTotal)
        {
            // Spawn middle sections
            foreach (var go in part.middlesBase)
                Util.SetActiveX(go, false);
            for (int i = startIdx; i < numTotal; ++i)
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

        internal static List<float> GetSectionLengths(Part part)
        {
            CreateMiddles(part, SectionsUsed(part.data.sectionsMin, part.data.sectionsMax), part.middlesBase._size);
            ApplyVariations(part);
            var lst = new List<float>();
            var sections = new Il2CppSystem.Collections.Generic.List<GameObject>();
            sections.AddRange(part.hullSections);
            var sectionsGO = Util.GetParent(part.bow);
            var sectionsTrf = sectionsGO.transform;

            for(int i = 0; i < part.middles.Count; ++i)
            {
                var sec = part.middles[i];
                lst.Add(GetSectionBounds(sec, sectionsTrf).size.z);
            }
            return lst;
        }

        internal static Part SpawnPart(PartData data, string name, GameObject parent)
        {
            var partGO = new GameObject(name);
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

            CreateMiddles(part, 0, SectionsUsed(data.sectionsMin, data.sectionsMax));
            ApplyVariations(part);
            PositionSections(part);

            return part;
        }
    }
}
