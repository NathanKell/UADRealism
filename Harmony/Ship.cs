using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Linq;

namespace UADRealism
{
    [HarmonyPatch(typeof(Ship))]
    internal class Patch_Ship
    {
        //private static Dictionary<Ship.A, float> oldArmor = new Dictionary<Ship.A, float>();
        //private static bool isInCalcInstability = false;
        //private static bool hasSetFirstInstability = false;
        //private static bool isCalcInstabilityPatched = false;

        //private static void PatchArmorForCalcInstability(Ship ship)
        //{
        //    float sizeZ = ship.hullSize.size.z;
        //    if (sizeZ == 0f)
        //        return;

        //    ship.armor.TryGetValue(Ship.A.Deck, out var deckMain);
        //    if (deckMain == 0f)
        //        return;

        //    isCalcInstabilityPatched = true;

        //    Melon<UADRealismMod>.Logger.Msg("Patching armor");

        //    foreach (var kvp in ship.armor)
        //        oldArmor.Add(kvp.key, kvp.value);

        //    ship.armor.TryGetValue(Ship.A.Belt, out var beltMain);
        //    ship.armor.TryGetValue(Ship.A.BeltBow, out var beltBow);
        //    ship.armor.TryGetValue(Ship.A.BeltStern, out var beltStern);
        //    float citadelExtraBelt = 0f;
        //    float citadelExtraDeck = 0f;
        //    for (int i = (int)Ship.A.InnerBelt_1st; i <= (int)Ship.A.InnerDeck_3rd; ++i)
        //    {
        //        var armorType = (Ship.A)i;
        //        ship.armor.TryGetValue(armorType, out var citArmor);
        //        if (armorType >= Ship.A.InnerDeck_1st)
        //            citadelExtraDeck += citArmor;
        //        else
        //            citadelExtraBelt += citArmor;
        //    }
        //    beltMain += beltBow * 0.5f + beltStern * 0.5f + citadelExtraBelt * 0.25f;
        //    deckMain += citadelExtraDeck * 0.25f;

        //    float minDeck = (sizeZ + 1f) / -0.04f - beltMain * 0.5f + 0.001f;
        //    float minDeck2 = -125 - beltMain * 0.5f + 0.001f;
        //    deckMain = -deckMain;
        //    if (deckMain < minDeck)
        //        deckMain = minDeck;
        //    if (deckMain < minDeck2)
        //        deckMain = minDeck2;

        //    ship.armor[Ship.A.Deck] = deckMain;
        //    ship.armor[Ship.A.Belt] = beltMain;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch(nameof(Ship.CalcInstability))]
        //internal static void Prefix_CalcInstability(Ship __instance)
        //{
        //    if (__instance == null || __instance.armor == null || __instance.hullSize == null)
        //        return;

        //    isInCalcInstability = true;
        //    hasSetFirstInstability = false;
        //    isCalcInstabilityPatched = false;
        //    Melon<UADRealismMod>.Logger.Msg("Starting CalcInstability");
        //}

        //[HarmonyPostfix]
        //[HarmonyPatch(nameof(Ship.CalcInstability))]
        //internal static void Postfix_CalcInstability(Ship __instance)
        //{
        //    isInCalcInstability = false;
        //    if (!isCalcInstabilityPatched)
        //        return;

        //    isCalcInstabilityPatched = false;

        //    __instance.armor.Clear();
        //    foreach (var kvp in oldArmor)
        //        __instance.armor[kvp.Key] = kvp.Value;
        //}

        //[HarmonyPrefix]
        //[HarmonyPatch(nameof(Ship.instability), MethodType.Setter)]
        //internal static void Prefix_Set_Instability(Ship __instance)
        //{
        //    if (!isInCalcInstability)
        //        return;

        //    hasSetFirstInstability = true;
        //    Melon<UADRealismMod>.Logger.Msg("Set instability, now doing instab2");
        //}

        private static Vector3 _BoundsMin = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        private static Vector3 _BoundsMax = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private static Vector3 _CachedDimensions = new Vector3(1f, 1f, 1f);

        private static List<float> oldTurretArmors = new List<float>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CalcInstability))]
        internal static void Postfix_CalcInstability(Ship __instance)
        {
            if (__instance == null)
                return;

            float Cb = CalcBlockCoefficient(__instance);
            GetLengthBeam(__instance, out float lwl, out float beam);
            Melon<UADRealismMod>.Logger.Msg($"{__instance.vesselName}: Block Coefficient: {Cb:F3} for {__instance.hullSize.size.z:F2}x{__instance.hullSize.size.x:F2}x{__instance.hullSize.min.y:F2}. Calc {lwl:F2}x{beam:F2}");
            //string[] layers = Enumerable.Range(0, 31).Select(index => LayerMask.LayerToName(index)).Where(l => !string.IsNullOrEmpty(l)).ToArray();
            //Melon<UADRealismMod>.Logger.Msg("Layers: " + string.Join(", ", layers));
            //  Layers: Default, TransparentFX, Ignore Raycast, Water, UI, Navmesh, MapSelectable, PartCamera, Deck, Part, DeckBorder, PartSelect,
            //  Ship, Torpedo, ShipSelect, MapSelect, DeckWall, SectionDamage, Decor, Suimono_Water, Suimono_Depth, Suimono_Screen, Part2, Sky,
            //  water_surface, water_foam, VisibilityFog_unused, Scheme, DeckHeight, CountryHighlight

            if (Cb <= 0f)
                return;

            for (int i = 0; i < __instance.shipTurretArmor.Count; ++i)
            {
                oldTurretArmors.Add(__instance.shipTurretArmor[i].barbetteArmor);
                __instance.shipTurretArmor[i].barbetteArmor = 0f;
            }

            //float topweight = 0f;
            //foreach (var p in __instance.parts)
            //{
            //    if (!p.isShown || p == __instance.hull)
            //        continue;
            //}


            //stability = arm.weightConning * 5f + (guns.borneWeight + guns.armWeight) * (2f * guns.superFactor - 1f) * 4f + weapons.miscWeightHull * 2f + weapons.miscWeightDeck * 3f + weapons.miscWeightAbove * 4f + arm.weightBeltUpper * 2f + arm.weightBeltMain + arm.weightBeltEnds + arm.weightDeck + (engine.hullWeight + guns.gunsWeight - guns.borneWeight) * 1.5f * free.freeboard / hull.draught;
            //if ((double)roomDeck < 1.0)
            //{
            //    stability = (float)((double)stability + (double)(engine.engineWeight + weapons.miscWeightVital + weapons.voidWeight) * Math.Pow(1f - roomDeck, 2.0));
            //}
            //if (stability > 0f)
            //{
            //    stability = (float)(Math.Sqrt((double)(hull.displacement * (hull.beamBulges / hull.draught) / stability) * 0.5) * Math.Pow(8.76755 / (double)hull.lengthBeam, 0.25));
            //}

            //seaboat = (float)(Math.Sqrt((double)free.freeboardCap / (2.4 * Math.Pow(hull.displacement, 0.2))) * (Math.Pow(stability * 5f * (hull.beamBulges / hull.length), 0.2) * Math.Sqrt(free.freeboardCap / hull.length * 20f) * (double)(hull.displacement / (hull.displacement + arm.weightBeltEnds * 3f + engine.hullWeight / 3f + (guns.borneWeight + guns.armWeight) * superFactorLong))) * 8.0);
            //if ((double)(hull.draught / hull.beamBulges) < 0.3)
            //{
            //    seaboat *= (float)Math.Sqrt((double)(hull.draught / hull.beamBulges) / 0.3);
            //}
            //if ((double)(engine.frictMax / (engine.frictMax + engine.waveMax)) < 0.55 && (double)engine.speedMax != 0.0)
            //{
            //    seaboat *= (float)Math.Pow(engine.frictMax / (engine.frictMax + engine.waveMax), 2.0);
            //}
            //else
            //{
            //    seaboat *= 0.3025f;
            //}
            //seaboat = Math.Min(seaboat, 2f);
            //steadinessAdj = Math.Min((float)steadiness * seaboat, 100f);

            //if (steadinessAdj < 50f)
            //{
            //    seakeeping = seaboat * steadinessAdj / 50f;
            //}
            //else
            //{
            //    seakeeping = seaboat;
            //}

            //recoil = (float)((double)(guns.broadside / hull.displacement * free.freeDistributed * guns.superFactor / hull.beamBulges) * Math.Pow(Math.Pow(hull.displacement, 0.3333333432674408) / (double)hull.beamBulges * 3.0, 2.0) * 7.0);
            //if ((double)stabilityAdj > 0.0)
            //{
            //    recoil /= stabilityAdj * ((50f - steadinessAdj) / 150f + 1f);
            //}

            //metacentre = (float)(Math.Pow(hull.beam, 1.5) * ((double)stabilityAdj - 0.5) / 0.5 / 200.0);
            //rollPeriod = (float)(0.42 * (double)hull.beamBulges / Math.Sqrt(metacentre));


            for (int i = 0; i < __instance.shipTurretArmor.Count; ++i)
                __instance.shipTurretArmor[i].barbetteArmor = oldTurretArmors[i];

            oldTurretArmors.Clear();
        }

        private static float[] turretBarrelCountWeightMults = new float[5];
        internal static float GetTurretBaseWeight(Ship ship, PartData data)
        {
            turretBarrelCountWeightMults[1] = 1f;
            turretBarrelCountWeightMults[1] = G.GameData.Param("w_turret_barrels_2", 1.8f);
            turretBarrelCountWeightMults[2] = G.GameData.Param("w_turret_barrels_3", 2.48f);
            turretBarrelCountWeightMults[3] = G.GameData.Param("w_turret_barrels_4", 3.1f);
            turretBarrelCountWeightMults[4] = G.GameData.Param("w_turret_barrels_5", 3.6f);

            var gunData = G.GameData.GunData(data);
            float baseWeight = gunData.BaseWeight(ship, data);
            float techWeightMod = ship.TechWeightMod(data);
            // This is just used for cost
            //var tc = Patch_GunData.FindMatchingTurretCaliber(ship, data);
            //float minLengthParam, techLengthLimit;
            //if (data.GetCaliberInch() > 2f)
            //{
            //    minLengthParam = G.GameData.Param("min_gun_length_mod", -20f);
            //    techLengthLimit = ship.TechMax("tech_gun_length_limit");
            //}
            //else
            //{
            //    minLengthParam = G.GameData.Param("min_casemate_length_mod", -10f);
            //    techLengthLimit = isCasemate ? ship.TechMax("tech_gun_length_limit_casemates") : ship.TechMax("tech_gun_length_limit_small");
            //}

            float barrelMult = turretBarrelCountWeightMults[Util.Clamp(data.barrels - 1, 0, 4)];
            float casemateMult = data.mounts.Contains("casemate") ? MonoBehaviourExt.Param("w_turret_casemate_mod", 0.75f) : 1f;
            float turretWeight = baseWeight * techWeightMod * barrelMult * casemateMult;
            return turretWeight;
        }

        private static float BarbetteWeight(Ship ship, Part part, PartData data)
        {
            Ship.TurretArmor armorData = null;
            bool isCasemate = Ship.IsCasemateGun(data);
            foreach (var ta in ship.shipTurretArmor)
            {
                if (ta.turretPartData.GetCaliber() == data.GetCaliber() && ta.isCasemateGun == isCasemate)
                {
                    armorData = ta;
                    break;
                }
            }

            if (armorData == null)
                return 0f;

            

            float thickLerp = Mathf.Lerp(1.0f, 3.0f, (armorData.barbetteArmor / 25.4f) / 15.0f);
            float weightParam = MonoBehaviourExt.Param("w_armor_barbette_turret", 0.029999999f);
            float tech = ship.TechR("armor");
            float weight = armorData.barbetteArmor * thickLerp / 25.4f * weightParam * GetTurretBaseWeight(ship, data) * tech;
            return weight;
        }

        private static Camera _Camera;

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.SetLayer(layer);
            for (int i = 0; i < obj.transform.childCount; ++i)
                SetLayerRecursive(obj.transform.GetChild(i).gameObject, layer);
        }

        enum ShipViewDir
        {
            Side,
            Front
        }

        const int _TextureRes = 512;
        private static void GetLengthBeam(Ship ship, out float lwl, out float beam)
        {
            lwl = -1f;
            beam = -1f;

            if (ship == null || ship.hull == null || ship.hull.hullInfo == null)
                return;

            var cameraLayerInt = LayerMask.NameToLayer("VisibilityFog_unused");

            if (_Camera == null)
            {
                var camGO = new GameObject("OrthoCamera");
                _Camera = camGO.AddComponent<Camera>();
                UnityEngine.Object.DontDestroyOnLoad(camGO);

                _Camera.clearFlags = CameraClearFlags.SolidColor;
                _Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _Camera.orthographic = true;
                _Camera.orthographicSize = 1f;
                _Camera.aspect = 1f;
                _Camera.nearClipPlane = 0.1f;
                _Camera.farClipPlane = 500f;
                _Camera.enabled = false;

                _Camera.cullingMask = 1 << cameraLayerInt;
            }

            var renderTexture = RenderTexture.GetTemporary(_TextureRes, _TextureRes, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var tempTex = new Texture2D(_TextureRes, _TextureRes, TextureFormat.ARGB32, false);
            _Camera.targetTexture = renderTexture;

            var newShipHullInfo = (HullInfo)GameObject.Instantiate(ship.hull.hullInfo, Vector3.zero, Quaternion.identity);
            var newGO = newShipHullInfo.gameObject;
            newGO.name += "dimension getter";

            foreach (var mb in newGO.GetComponentsInChildren<MonoBehaviour>())
                mb.enabled = false;
            foreach (var coll in newGO.GetComponentsInChildren<Collider>())
                coll.enabled = false;

            SetLayerRecursive(newGO, cameraLayerInt);
            var children = newGO.GetChildren();
            for (int i = children.Count; i-- > 0;)
            {
                if (children[i] == null)
                    continue;
                if (children[i].name.Contains("Decor"))
                    GameObject.Destroy(children[i]);
            }

            Bounds bounds = new Bounds();
            bool boundsSet = false;

            Renderer[] rs = newGO.GetComponentsInChildren<Renderer>();
            for (int i = rs.Length; i-- > 0;)
            {
                Renderer mr = rs[i];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // TODO: Change material?

                if (boundsSet)
                {
                    bounds.Encapsulate(mr.bounds);
                }
                else
                {
                    bounds = mr.bounds;
                    boundsSet = true;
                }
            }

            float ySize = ship.hullSize.min.y;
            for (ShipViewDir view = ShipViewDir.Side; view <= ShipViewDir.Front; view++)
            {
                // updates the aero camera to part bounds
                float size = Mathf.Max(-ySize, view == ShipViewDir.Side ? bounds.size.z : bounds.size.x);
                float depth = view == ShipViewDir.Side ? bounds.size.x : bounds.size.z;
                Vector3 dir = view == ShipViewDir.Side ? Vector3.left : Vector3.back;
                float yPos = -size * 0.5f;
                _Camera.transform.position = view == ShipViewDir.Side ? new Vector3(bounds.max.x + 1f, yPos, bounds.center.z) : new Vector3(bounds.center.x, yPos, bounds.max.z + 1f);
                _Camera.transform.rotation = Quaternion.LookRotation(dir);
                _Camera.orthographicSize = size * 0.5f;
                _Camera.nearClipPlane = 0f;
                _Camera.farClipPlane = depth + 1f;

                _Camera.Render();
                RenderTexture.active = renderTexture;
                tempTex.ReadPixels(new Rect(0, 0, _TextureRes, _TextureRes), 0, 0);
                tempTex.Apply(false, false);
                RenderTexture.active = null;
                //byte[] bytes = tempTex.EncodeToPNG();
                //System.IO.File.WriteAllBytes("DragTextures/" + partPrefab.partInfo.name + "_" + positionNames[j] + "_" + ((DragCube.DragFace)i).ToString() + ".png", bytes);

                Color32[] pixels = tempTex.GetPixels32();
                int pxCount = 0;
                int row = 0;
                for (row = _TextureRes - 1; row >= 0; --row)
                {
                    for (int i = 0; i < _TextureRes; ++i)
                    {
                        if (pixels[row * _TextureRes + i].a != 0f)
                            ++pxCount;
                    }
                    // Sanity check, just in case there's some garbage here
                    if (pxCount > _TextureRes / 4)
                        break;
                }
                float dimension = pxCount * (size / _TextureRes);
                if (view == ShipViewDir.Front)
                    beam = dimension;
                else
                    lwl = dimension;

                //Melon<UADRealismMod>.Logger.Msg($"For direction {view.ToString()}, pixel count at row {row} is {pxCount} so dimension = {dimension:F2}");

                //string filePath = "C:\\temp\\112\\screenshot_" + newGO.name + "_" + view.ToString() + ".png";
                //var bytes = ImageConversion.EncodeToPNG(tempTex);
                //Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
            }

            GameObject.Destroy(tempTex);
            GameObject.Destroy(newGO);
            RenderTexture.ReleaseTemporary(renderTexture);
        }

        private static float CalcBlockCoefficient(Ship ship)
        {
            if (ship.hullSize == null
                || ship.hullSize.size.x == 0f
                || ship.hullSize.size.y == 0f
                || ship.hullSize.size.z == 0f
                || ship.hullSize.min.y == 0f)
                return -1f;


            if (_BoundsMax != ship.hullSize.max || _BoundsMin != ship.hullSize.min)
            {
                GetLengthBeam(ship, out _CachedDimensions.z, out _CachedDimensions.x);
                _CachedDimensions.y = -ship.hullSize.min.y;
            }

            float displacement = ship.tonnage;

            float volumeBlock = _CachedDimensions.z * _CachedDimensions.x * _CachedDimensions.y;
            float Cb = displacement / (volumeBlock * 1.024f); // assuem 1024kg/m^3 for salt water

            return Cb;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Postfix_RefreshHull(Ship __instance)
        {
            if (__instance == null)
                return;

            //Melon<UADRealismMod>.Logger.Msg($"{__instance.vesselName}: SetTonnage. Bounds size/min: {__instance.hullSize.size} / {__instance.hullSize.min}");
            
        }
    }
}
