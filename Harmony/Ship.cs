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
        internal static bool _IsChangeHull = false;
        internal static Ship.Store _ChangeHullShipStore = null;
        internal static Ship _ChangeHullShip = null;
        internal static bool _IsRefreshPatched = false;

        internal struct RefreshHullData
        {
            public float _draught;
            public float _tonnage;
            public float _yPosScaling;
            public float _oldYScale;
            public bool _valuesSet;
        }

        internal static System.Collections.IEnumerator RefreshHullRoutine(Ship ship)
        {
            yield return new WaitForEndOfFrame();
            if (ship.modelState == Ship.ModelState.Constructor || ship.modelState == Ship.ModelState.Battle)
                ship.RefreshHull(false); // will be made true if sections need updating
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        internal static void Prefix_Ship_ChangeHull(Ship __instance, Ship.Store store, ref bool byHuman, out bool __state)
        {
            _IsChangeHull = true;
            _ChangeHullShipStore = store;
            _ChangeHullShip = __instance;
            __state = byHuman;
            // This is gross. But we can't patch AdjustHullStats so we need to make
            // it not run, and run something else ourselves. We should just be
            // prefixing that, but it crashes (!) because apparently the runtime
            // interop can't handle patching methods that take nullable arguments.
            // Blergh. So instead we'll manually handle the "byHuman" case that
            // invokes AdjustHullStats when we get to our postfix.

            byHuman = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        internal static void Postfix_Ship_ChangeHull(Ship __instance, Ship.Store store, bool __state)
        {
            // Stock code bug: shiptypes give speed min/max in knots, but code
            // uses m/s. So we have to reset speedmax here. This uses the same
            // random as the game.
            __instance.speedMax = Mathf.Clamp(ModUtils.Range(0.9f, 1.1f) * __instance.hull.data.speedLimiter, __instance.shipType.speedMin, __instance.shipType.speedMax) * ShipStats.KnotsToMS;
            SetShipBeamDraughtFineness(__instance);

            if (__state) // i.e. if byHuman
            {
                float minT = __instance.TonnageMin();
                float maxT = __instance.TonnageMax();
                if (__instance.player != null)
                {
                    float tLim = __instance.player.TonnageLimit(__instance.shipType);
                    maxT = Math.Min(maxT, tLim);
                }
                var t = Util.Range(0f, 1f);
                __instance.SetTonnage(Mathf.Lerp(minT, maxT, Util.Range(0f, 1f)));

                float oldSpeed = __instance.speedMax;

                float year = __instance.GetYear(__instance);
                // No idea why this code in stock does a remap-with-clamp and then clamps again. Leaving it as a monument to posterity.
                float yearToMult = Mathf.Clamp(Util.Remap(year, 1890f, 1940f, 1.3f, 1.05f, true) * 0.5f, 0.45f, 0.65f);
                AdjustHullStats(__instance, -1, 1f - yearToMult, new System.Func<bool>(() =>
                {
                    // same deal here with the double clamp.
                    float remapped = Util.Remap(year, 1890f, 1940f, 1.33f, 1.1f, true);
                    return (1f - Mathf.Clamp(remapped * 0.5f, 0.45f, 0.75f)) >= __instance.Weight() / __instance.Tonnage();

                }));

                // If our speed was adjusted, we should change hull geometry to optimize.
                if (oldSpeed != __instance.speedMax)
                    SetShipBeamDraughtFineness(__instance);
            }

            _IsChangeHull = false;
            _ChangeHullShipStore = null;
            _ChangeHullShip = null;

            // This refresh may or may not be needed, but it won't really hurt.
            MelonCoroutines.Start(RefreshHullRoutine(__instance));
        }

        //This runs right before ChangeHull loads from store
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.GenerateArmor))]
        internal static void Postfix_GenerateArmor()
        {
            if (_IsChangeHull && _ChangeHullShipStore != null && _ChangeHullShip != null)
            {
                _ChangeHullShip.hullPartSizeZ = _ChangeHullShipStore.hullPartSizeZ;
                _ChangeHullShip.hullPartSizeY = _ChangeHullShipStore.hullPartSizeY;
            }
        }
        

        internal static void SetShipBeamDraughtFineness(Ship ship)
        {
            // First, set L/B from B/T
            float desiredBdivT = ShipStats.DefaultBdivT + ModUtils.DistributedRange(0.2f);
            float desiredLdivB = ShipStats.GetDesiredLdivB(ship, desiredBdivT);
            desiredLdivB *= 1f + ModUtils.DistributedRange(0.025f);

            float CpOffset = ModUtils.DistributedRange(0.02f, 3);
            //Melon<UADRealismMod>.Logger.Msg($"Iterating to find Cp for {(ship.speedMax / ShipStats.KnotsToMS)}kn. L/B {desiredLdivB:F2}, B/T {desiredBdivT:F2}, Cp offset {CpOffset:F3}");

            var bestSec = ShipStats.GetDesiredSections(ship, desiredLdivB, desiredBdivT, out var finalBmPct, out var finalDrPct, out _, CpOffset);

            ship.SetBeam(finalBmPct, false);
            ship.SetDraught(finalDrPct, false);

            float t = Mathf.InverseLerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, bestSec);
            ship.hullPartSizeZ = Mathf.Lerp(Patch_Ui._MinFineness, Patch_Ui._MaxFineness, 1f - t);

            // Also set freeboard
            ship.hullPartSizeY = Mathf.Lerp(Patch_Ui._MinFreeboard, Patch_Ui._MaxFreeboard, ModUtils.DistributedRange(0.5f) + 0.5f);

            //Melon<UADRealismMod>.Logger.Msg($"Setting sizeZ={ship.hullPartSizeZ:F1} from {t:F3} from {bestSec} in {ship.hull.data.sectionsMin}-{ship.hull.data.sectionsMax}");
            if (ship.modelState == Ship.ModelState.Constructor || ship.modelState == Ship.ModelState.Battle)
                ship.RefreshHull(false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Prefix_Ship_RefreshHull(Ship __instance, ref bool updateSections, out RefreshHullData __state)
        {

            __state = new RefreshHullData();
            if (_IsChangeHull)
                return;

            if (__instance == null || __instance.hull == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Ship or hull null!");
                return;
            }

            var data = __instance.hull.data;
            if (data == null)
            {
                Melon<UADRealismMod>.Logger.BigError("hull PartData null!");
                return;
            }
            var hData = ShipStats.GetData(data);
            if (hData == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(data));
                return;
            }

            _IsRefreshPatched = true;

            __state._tonnage = __instance.tonnage;
            __state._draught = __instance.draught;

            float lerp = Mathf.Lerp(data.sectionsMin, data.sectionsMax, 1f - __instance.hullPartSizeZ * 0.01f);
            int secsToUse = Mathf.RoundToInt(lerp);
            //Melon<UADRealismMod>.Logger.Msg($"Setting sections. sizeZ={__instance.hullPartSizeZ:F1} yields {lerp:F3}->{secsToUse}");

            if (__instance.hull.middles == null)
            {
                Melon<UADRealismMod>.Logger.BigError("hull middles list null!");
            }
            else
            {
                updateSections =  secsToUse != __instance.hull.middles.Count;
            }

            if (hData._statsSet.Length <= secsToUse || secsToUse < 0)
            {
                Melon<UADRealismMod>.Logger.BigError($"SecsToUse ({secsToUse}) out of bounds for StatsSet length {hData._statsSet.Length}");
                return;
            }

            float scaleFactor = ShipStats.GetHullScaleFactor(__instance, hData._statsSet[secsToUse].Vd) / __instance.hull.model.transform.localScale.z;
            float oldScaleYLocal = __instance.hull.bow.GetParent().GetParent().transform.localScale.y;
//#if LOGSHIP
            Debug.Log($"{hData._key}: tonnage desired: {__instance.tonnage:F0} in ({data.tonnageMin:F0},{data.tonnageMax:F0}). Scale {scaleFactor:F3} (total {(scaleFactor * __instance.hull.model.transform.localScale.z):F3}). Vd for 1/1={hData._statsSet[secsToUse].Vd:F0}\nS={secsToUse} ({data.sectionsMin}-{data.sectionsMax}).");
//#endif
            __instance.hull.bow.GetParent().GetParent().transform.localScale = Vector3.one * scaleFactor;

            float slider = Mathf.InverseLerp(data.sectionsMin - 0.499f, data.sectionsMax + 0.499f, secsToUse);
            __instance.tonnage = Mathf.Lerp(data.tonnageMin, data.tonnageMax, slider);
            float bmMult = 1f + __instance.beam * 0.01f;
            // We don't scale the hull height based on draught, just beam
            // (to preserve beam/draught ratio). We'll scale Y based on freeboard.
            float freeboardMult = (1f + __instance.hullPartSizeY * 0.01f);
            __state._yPosScaling = bmMult * freeboardMult;
            __state._oldYScale = oldScaleYLocal * __instance.hull.bow.transform.localScale.y;
            __instance.draught = (__state._yPosScaling - 1f) * 100f;
            __state._valuesSet = true;
        }

        private static readonly Il2CppSystem.Collections.Generic.List<GameObject> _hullSecitons = new Il2CppSystem.Collections.Generic.List<GameObject>();
        private static readonly List<Mount> _mounts = new List<Mount>();
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.RefreshHull))]
        internal static void Postfix_Ship_RefreshHull(Ship __instance, RefreshHullData __state)
        {
            if (!_IsRefreshPatched || !__state._valuesSet)
                return;

            __instance.draught = __state._draught;
            __instance.tonnage = __state._tonnage;

            var hData = ShipStats.GetData(__instance.hull.data);
            _hullSecitons.AddRange(__instance.hull.hullSections);
            foreach (var sec in _hullSecitons)
            {
                sec.transform.localPosition = new Vector3(sec.transform.localPosition.x, hData.yPos * __state._yPosScaling, sec.transform.localPosition.z);
            }
            _hullSecitons.Clear();

            float scaleRatio = __instance.hull.bow.GetParent().GetParent().transform.localScale.y * __instance.hull.bow.transform.localScale.y / __state._oldYScale;
            foreach (Part p in __instance.parts)
            {
                if (p == __instance.hull)
                    continue;
                // The above offset code makes sure the hull remains centered around the waterline, and the waterline is the origin
                // so this should just work
                p.transform.localPosition = new Vector3(p.transform.localPosition.x, p.transform.localPosition.y * scaleRatio, p.transform.localPosition.z);
            }

            // Finally, make sure we have funnel mounts
            _mounts.AddRange(__instance.hull.bow.GetComponentsInChildren<Mount>());
            _mounts.AddRange(__instance.hull.stern.GetComponentsInChildren<Mount>());
            foreach (var m in _mounts)
            {
                if (!m.funnel && Mathf.Abs(m.gameObject.transform.localPosition.x) < 0.01f)
                {
                    m.funnel = true;
                }
            }
            _mounts.Clear();
        }

#if disabled
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
#endif

        private static List<float> oldTurretArmors = new List<float>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CalcInstability))]
        internal static void Postfix_CalcInstability(Ship __instance)
        {
            if (ShipStats._IsRenderingHulls || __instance == null)
                return;

            if (__instance.hullSize == null
                || __instance.hullSize.size.x == 0f
                || __instance.hullSize.size.y == 0f
                || __instance.hullSize.size.z == 0f
                || __instance.hullSize.min.y == 0f)
                return;

            var stats = ShipStats.GetScaledStats(__instance);
            float SHP = ShipStats.GetSHPRequired(__instance);
            float ihpMult = ShipStats.GetEngineIHPMult(__instance);
            string beamStr = stats.B.ToString("F2");
            if (stats.beamBulge != stats.B)
            {
                beamStr += $" ({stats.beamBulge:F2} at {stats.bulgeDepth:F2})";
            }
            string hp = ihpMult == 1f ? $"{SHP:N0} SHP" : $"{(SHP * ihpMult):N0} IHP";
#if LOGSHIP
            Melon<UADRealismMod>.Logger.Msg($"{__instance.vesselName}: {stats.Lwl:F2}x{beamStr}x{stats.T:F2} ({(stats.Lwl/stats.beamBulge):F1},{(stats.beamBulge/stats.T):F1}), {(stats.Vd * ShipStats.WaterDensity * 0.001d):F0}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. {hp} ({ShipStats.GetHullFormSHPMult(__instance):F2}x{ihpMult:F2}) for {(__instance.speedMax/ShipStats.KnotsToMS):F1}kn");
#endif
            Patch_Ui._ShipForEnginePower = __instance;
            Melon<UADRealismMod>.Logger.Msg($"{__instance.vesselName}: {stats.Lwl:F2}x{beamStr}x{stats.T:F2} Cb={stats.Cb:F3}/Cwp={stats.Cwp:F3}/Cp={stats.Cp:F3}, {Ui.FormatEnginePower(SHP * ihpMult)}");


            //for (int i = 0; i < __instance.shipTurretArmor.Count; ++i)
            //{
            //    oldTurretArmors.Add(__instance.shipTurretArmor[i].barbetteArmor);
            //    __instance.shipTurretArmor[i].barbetteArmor = 0f;
            //}

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


            //for (int i = 0; i < __instance.shipTurretArmor.Count; ++i)
            //    __instance.shipTurretArmor[i].barbetteArmor = oldTurretArmors[i];

            //oldTurretArmors.Clear();
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

        internal struct HullSizePreserve
        {
            public float sizeZ;
            public float sizeY;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.CheckHullSizeMaxMin))]
        internal static void Prefix_CheckHullSizeMaxMin(Ship __instance, out HullSizePreserve __state)
        {
            __state.sizeZ = __instance.hullPartSizeZ;
            __state.sizeY = __instance.hullPartSizeY;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CheckHullSizeMaxMin))]
        internal static void Postfix_CheckHullSizeMaxMin(Ship __instance, HullSizePreserve __state)
        {
            __instance.hullPartSizeZ = __state.sizeZ;
            __instance.hullPartSizeY = __state.sizeY;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.GetDamagePlanTooltipText))]
        internal static void Prefix_GetDamagePlanTooltipText(Ship __instance)
        {
            Patch_Ui._ShipForEnginePower = __instance;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.EnginePower))]
        internal static void Postfix_EnginePower(Ship __instance, ref float __result)
        {
            float SHP = ShipStats.GetSHPRequired(__instance);
            float ihpMult = ShipStats.GetEngineIHPMult(__instance);
            float hp = SHP * ihpMult;
            //Debug.Log($"SHP calc for {__instance.GetNameUI()}: {hp:N0} {(ihpMult == 1f ? "SHP" : "IHP")}, stock {__result:N0}");
            __result = hp;
        }

        // Reimplemented methods, with tweaks

        private static Dictionary<Ship.A, float> _AdjustPriorityArmorReduce = GenerateAdjustArmorPriorities(false);
        private static Dictionary<Ship.A, float> _AdjustPriorityArmorIncrease = GenerateAdjustArmorPriorities(true);
        private static Dictionary<AdjustHullStatsItem, float> _AdjustHullStatsOptions = new Dictionary<AdjustHullStatsItem, float>();
        private static System.Array _OpRangeValues = null;
        internal enum AdjustHullStatsItem
        {
            Speed,
            Range,
            Quarters,
            Armor,
            Empty
        }

        private static Dictionary<Ship.A, float> GenerateAdjustArmorPriorities(bool increase)
        {
            var armorPriority = new Dictionary<Ship.A, float>();
            if (increase)
            {
                armorPriority.Add(Ship.A.Belt, 31);
                armorPriority.Add(Ship.A.BeltBow, 18);
                armorPriority.Add(Ship.A.BeltStern, 17);
                armorPriority.Add(Ship.A.Deck, 18);
                armorPriority.Add(Ship.A.DeckBow, 16);
                armorPriority.Add(Ship.A.DeckStern, 15);
                armorPriority.Add(Ship.A.ConningTower, 12);
                armorPriority.Add(Ship.A.Superstructure, 12);
                armorPriority.Add(Ship.A.TurretTop, 25);
                armorPriority.Add(Ship.A.TurretSide, 25);
                armorPriority.Add(Ship.A.Barbette, 25);
                armorPriority.Add(Ship.A.InnerBelt_1st, 25);
                armorPriority.Add(Ship.A.InnerBelt_2nd, 24);
                armorPriority.Add(Ship.A.InnerBelt_3rd, 23);
                armorPriority.Add(Ship.A.InnerDeck_1st, 25);
                armorPriority.Add(Ship.A.InnerDeck_2nd, 24);
                armorPriority.Add(Ship.A.InnerDeck_3rd, 23);
            }
            else
            {
                armorPriority.Add(Ship.A.Belt, 4);
                armorPriority.Add(Ship.A.BeltBow, 10);
                armorPriority.Add(Ship.A.BeltStern, 12);
                armorPriority.Add(Ship.A.Deck, 7);
                armorPriority.Add(Ship.A.DeckBow, 10);
                armorPriority.Add(Ship.A.DeckStern, 12);
                armorPriority.Add(Ship.A.ConningTower, 10);
                armorPriority.Add(Ship.A.Superstructure, 10);
                armorPriority.Add(Ship.A.TurretTop, 10);
                armorPriority.Add(Ship.A.TurretSide, 10);
                armorPriority.Add(Ship.A.Barbette, 10);
                armorPriority.Add(Ship.A.InnerBelt_1st, 4);
                armorPriority.Add(Ship.A.InnerBelt_2nd, 4);
                armorPriority.Add(Ship.A.InnerBelt_3rd, 4);
                armorPriority.Add(Ship.A.InnerDeck_1st, 5);
                armorPriority.Add(Ship.A.InnerDeck_2nd, 5);
                armorPriority.Add(Ship.A.InnerDeck_3rd, 5);
            }

            return armorPriority;
        }

        internal static void AdjustHullStats(Ship ship, int delta, float targetWeight, Func<bool> stopCondition, bool allowEditSpeed = true, bool allowEditArmor = true, bool allowEditCrewQuarters = true, bool allowEditRange = true, System.Random rnd = null, float limitArmor = -1, float limitSpeed = 0f)
        {
            float year = ship.GetYear(ship);
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> newArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> oldArmor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
            bool isCL = ship.shipType.name == "cl";
            bool isLightCraft = ship.shipType.name == "tb" || ship.shipType.name == "dd";
            bool cldd = isCL || isLightCraft;

            VesselEntity.OpRange minOpRange = VesselEntity.OpRange.VeryLow;
            if (ship.shipType.paramx.TryGetValue("range_min", out var minRange) && minRange.Count > 0)
            {
                if (_OpRangeValues == null)
                    _OpRangeValues = Enum.GetValues(typeof(VesselEntity.OpRange));

                foreach (var e in _OpRangeValues)
                {
                    var eStr = Util.ToStringShort(e.ToString()).ToLower();
                    if (eStr == minRange[0].ToLower())
                    {
                        minOpRange = (VesselEntity.OpRange)e;
                        break;
                    }
                }
            }

            var armorMin = ship.shipType.armorMin;
            if (armorMin <= 0)
                armorMin = ship.shipType.armor;
            float armorInches;
            // All ships are expected to have a hint
            if (ship.shipType.paramx.TryGetValue("armor_min_hint", out var minArmorParam) && minArmorParam.Count > 0 && float.TryParse(minArmorParam[0], out var armorParam))
            {
                armorInches = armorMin * armorParam;
            }
            else if (ship.shipType.name == "dd")
            {
                float armorVal = ship.shipType.armor;
                float randMult = ModUtils.Range(1f, 1.3f, rnd);
                float yearMult = Util.Remap(year, 1890f, 1940f, 1f, 0.85f, true);

                armorInches = armorVal * randMult * yearMult;
            }
            else
            {
                armorInches = armorMin * ModUtils.Range(1f, 1.2f, rnd);
            }
            var armorMinHint = Ship.GenerateArmor(armorInches * 25.4f, ship);

            float hullSpeedMS = ship.hull.data.speedLimiter * ShipStats.KnotsToMS;

            float oldSpeed = ship.speedMax;
            Ship.CrewQuarters oldQuarters = ship.CurrentCrewQuarters;
            foreach (var kvp in ship.armor)
                oldArmor[kvp.Key] = kvp.Value;
            var oldRange = ship.opRange < minOpRange ? minOpRange : ship.opRange;

            float minSpeed;
            if (limitSpeed > 0f)
            {
                minSpeed = limitSpeed;
            }
            else
            {
                float mult;
                if (isLightCraft)
                    mult = 0.92f;
                else if (isCL)
                    mult = 0.86f;
                else
                    mult = 0.79f;
                minSpeed = hullSpeedMS * Util.Remap(year, 1890f, 1940f, 0.9f, 0.8f, true) * mult;
            }
            float maxMult = cldd ? 1.05f : 1.1f;
            float maxSpeed = hullSpeedMS * maxMult * Util.Remap(year, 1890f, 1940f, 1.1f, 1.06f, true);
            
            float armorLimit = limitArmor > 0f ? limitArmor : ship.shipType.armorMax * 3f * 25.4f;

            bool canMakeTarget;
            if (delta < 0)
            {

                ship.SetSpeedMax(minSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Cramped;
                ship.SetOpRange(minOpRange);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Max(armorMinHint[kvp.Key], ship.MinArmorForZone(kvp.Key));

                ship.SetArmor(newArmor);

                canMakeTarget = ship.Weight() / ship.Tonnage() <= targetWeight;
            }
            else
            {
                ship.SetSpeedMax(maxSpeed);
                ship.CurrentCrewQuarters = Ship.CrewQuarters.Spacious;
                ship.SetOpRange(VesselEntity.OpRange.VeryHigh);

                foreach (var kvp in ship.armor)
                    newArmor[kvp.Key] = Mathf.Min(limitArmor, ship.MaxArmorForZone(kvp.Key));

                ship.SetArmor(newArmor);

                canMakeTarget = targetWeight * 0.997f <= ship.Weight() / ship.Tonnage();
            }

            if (!canMakeTarget)
                return;

            minSpeed = Mathf.Max(minSpeed, ship.shipType.speedMin * ShipStats.KnotsToMS);
            maxSpeed = Mathf.Min(maxSpeed, ship.shipType.speedMax * ShipStats.KnotsToMS);
            oldSpeed = Ship.RoundSpeedToStep(oldSpeed);

            // Reset
            ship.SetSpeedMax(oldSpeed);
            ship.CurrentCrewQuarters = oldQuarters;
            ship.SetArmor(oldArmor);
            ship.SetOpRange(oldRange);
            
            if (stopCondition())
                return;

            float speedStep = G.GameData.Param("speed_step", 0.5f);
            Dictionary<Ship.A, float> armorPriority = delta > 0 ? _AdjustPriorityArmorIncrease : _AdjustPriorityArmorReduce;

            for (int j = 0; j < 699; ++j)
            {
                float step = speedStep * delta * ShipStats.KnotsToMS;
                float newSpeed = step + ship.speedMax;
                newSpeed = Mathf.Clamp(newSpeed, minSpeed, maxSpeed);
                newSpeed = Ship.RoundSpeedToStep(newSpeed);
                Ship.CrewQuarters newQuarters = (Ship.CrewQuarters)Mathf.Clamp((int)ship.CurrentCrewQuarters + delta, (int)Ship.CrewQuarters.Cramped, (int)Ship.CrewQuarters.Spacious);
                VesselEntity.OpRange newOpRange = (VesselEntity.OpRange)Mathf.Clamp((int)ship.opRange + delta, (int)minOpRange, (int)VesselEntity.OpRange.VeryHigh);

                var randomA = ModUtils.RandomByWeights(armorPriority);
                float minHint = armorMinHint == null ? ship.shipType.armorMin : armorMinHint[randomA];
                float minArmor = Mathf.Max(minHint, ship.MinArmorForZone(randomA));
                float maxArmor = Mathf.Min(armorLimit, ship.MaxArmorForZone(randomA));
                float newArmorLevel = Mathf.Clamp((delta * G.settings.armorStep) * 25.4f + ship.armor[randomA], minArmor, maxArmor);
                if (delta < 0)
                    newArmorLevel = Mathf.Floor(newArmorLevel);
                else
                    newArmorLevel = Mathf.Ceil(newArmorLevel);
                newArmor[randomA] = newArmorLevel;

                _AdjustHullStatsOptions.Clear();
                if (allowEditSpeed && newSpeed != ship.speedMax)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Speed, delta > 0 ? 350f : 690f);
                if (allowEditCrewQuarters && newQuarters != ship.CurrentCrewQuarters)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Quarters, 150f);
                if (allowEditArmor && !Util.DictionaryEquals(ship.armor, newArmor))
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Armor, delta > 0 ? 850f : 450f);
                if (allowEditRange && newOpRange != ship.opRange)
                    _AdjustHullStatsOptions.Add(AdjustHullStatsItem.Range, delta > 0 ? 75f : 150f);

                if (_AdjustHullStatsOptions.Count == 0)
                    return;

                // We're going to replace entries we pull and use with "empty"
                // so that we don't screw with our weights. (If we just removed items,
                // like stock, then quarters and range would be overrepresented.)
                AdjustHullStatsItem thingToChange = AdjustHullStatsItem.Empty;
                while ((thingToChange = ModUtils.RandomByWeights(_AdjustHullStatsOptions, rnd)) != AdjustHullStatsItem.Empty)
                {
                    float weight = _AdjustHullStatsOptions[thingToChange];
                    _AdjustHullStatsOptions.Remove(thingToChange);
                    if (_AdjustHullStatsOptions.TryGetValue(AdjustHullStatsItem.Empty, out var unusedWeight))
                        weight += unusedWeight;
                    _AdjustHullStatsOptions[AdjustHullStatsItem.Empty] = weight;
                    switch (thingToChange)
                    {
                        case AdjustHullStatsItem.Speed:
                            ship.SetSpeedMax(newSpeed);
                            break;
                        case AdjustHullStatsItem.Quarters:
                            ship.CurrentCrewQuarters = newQuarters;
                            break;
                        case AdjustHullStatsItem.Range:
                            ship.SetOpRange(newOpRange);
                            break;
                        default:
                            ship.SetArmor(newArmor);
                            break;
                    }
                    if (stopCondition())
                        return;

                    // We have to manually reset this, since
                    // we don't want to freshly clone all of ship.armor
                    // every time.
                    newArmor[randomA] = ship.armor[randomA];
                }
            }
        }
    }
}
