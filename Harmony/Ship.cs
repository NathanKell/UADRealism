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
        
        internal static bool _AboutToAdjustHullStats = false;
        internal static bool _InAdjustHullStats = false;

        internal struct RefreshHullData
        {
            public float _draught;
            public float _tonnage;
            public float _yPosScaling;
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
        internal static void Prefix_Ship_ChangeHull(Ship __instance, Ship.Store store = null)
        {
            _IsChangeHull = true;
            _ChangeHullShipStore = store;
            _ChangeHullShip = __instance;
            _AboutToAdjustHullStats = false;
            _InAdjustHullStats = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.ChangeHull))]
        internal static void Postfix_Ship_ChangeHull(Ship __instance, Ship.Store store = null)
        {
            _IsChangeHull = false;
            _ChangeHullShipStore = null;
            _ChangeHullShip = null;
            _AboutToAdjustHullStats = false;
            _InAdjustHullStats = false;

            // Second pass, after speed adjustment
            if (store == null)
                SetShipBeamDraughtFineness(__instance);

            // This refresh may or may not be needed, but it won't really hurt.
            MelonCoroutines.Start(RefreshHullRoutine(__instance));
        }

        //This runs right before ChangeHull loads from store
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.GenerateArmor))]
        internal static void Postfix_GenerateArmor()
        {
            if (_IsChangeHull && _ChangeHullShipStore != null && _ChangeHullShip != null)
                _ChangeHullShip.hullPartSizeZ = _ChangeHullShipStore.hullPartSizeZ;
        }

        // This is extremely gross. We have to patch both SetTonnage _and_ GetYear
        // so that we can set fineness in ChangeHull before AdjustHullStats runs.
        // We should just be prefixing that, but it crashes (!) because apparently
        // the runtime interop can't handle patching methods that take nullable
        // arguments. Blergh.
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.SetTonnage))]
        internal static void Postfix_SetTonnage()
        {
            if (_IsChangeHull && !_AboutToAdjustHullStats && !_InAdjustHullStats)
                _AboutToAdjustHullStats = true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.GetYear))]
        internal static void Prefix_GetYear(Ship __instance)
        {
            if (!_InAdjustHullStats && _IsChangeHull && _AboutToAdjustHullStats)
            {
                _InAdjustHullStats = true;

                // If we're in ChangeHull, and we've got our bool set, and not loading
                // from store, we should set fineness based on the speed set
                if (_ChangeHullShipStore == null)
                {
                    // we need to set this now because AdjustHullStats also
                    // calls GetYear a bunch.
                    _AboutToAdjustHullStats = false;

                    SetShipBeamDraughtFineness(__instance);
                }
            }
        }

        internal static void SetShipBeamDraughtFineness(Ship ship)
        {
            // First, set L/B from B/T
            float desiredBdivT = 3.0f + ModUtils.DistributedRange(0.2f);
            float desiredLdivB = ShipStats.GetDesiredLdivB(ship.tonnage * ShipStats.TonnesToCubicMetersWater, desiredBdivT, ship.speedMax, ship.shipType.name, ship.GetYear(ship));
            desiredLdivB *= 1f + ModUtils.DistributedRange(0.025f);

            float CpOffset = ModUtils.DistributedRange(0.02f, 3);
            //Melon<UADRealismMod>.Logger.Msg($"Iterating to find Cp for {(ship.speedMax / 0.51444444f)}kn. L/B {desiredLdivB:F2}, B/T {desiredBdivT:F2}, Cp offset {CpOffset:F3}");

            // Find the section count with closest-to-desired Cp.
            // We could try to binary search here, but this is fast enough
            float bestDiff = 1f;
            int bestSec = ship.hull.data.sectionsMin;
            float finalBmPct = 0f;
            float finalDrPct = 0f;
            for (int secs = ship.hull.data.sectionsMin; secs <= ship.hull.data.sectionsMax; ++secs)
            {
                var hData = ShipStats.GetData(ship);
                float bmMult = (hData._statsSet[secs].Lwl / hData._statsSet[secs].B) / desiredLdivB;
                float drMult = (hData._statsSet[secs].B * bmMult / hData._statsSet[secs].T) / desiredBdivT;
                float bmPct = (bmMult - 1f) * 100f;
                float drPct = (drMult / bmMult - 1f) * 100f;
                var stats = ShipStats.GetScaledStats(hData, ship.tonnage, bmPct, drPct, secs);
                float Fn = ship.speedMax / Mathf.Sqrt(9.80665f * stats.Lwl);
                float desiredCp = ShipStats.GetDesiredCpForFn(Fn) + CpOffset;
                float delta = Mathf.Abs(desiredCp - stats.Cp);
                if (delta < bestDiff)
                {
                    bestDiff = delta;
                    bestSec = secs;
                    finalBmPct = bmPct;
                    finalDrPct = drPct;
                    //Melon<UADRealismMod>.Logger.Msg($"Iterating@{secs} {hData._statsSet[secs].Lwl:F2}x{hData._statsSet[secs].B:F2}x{hData._statsSet[secs].T:F2}->{stats.Lwl:F2}x{stats.B:F2}x{stats.T:F2} with {bmPct:F0}%,{drPct:F0}%. Fn={Fn:F2}, desired={desiredCp:F3}, Cp={stats.Cp:F3}");
                }
                // Once we overshoot, everything after will have a bigger delta.
                if (stats.Cp > desiredCp)
                    break;
            }

            ship.SetBeam(finalBmPct, false);
            ship.SetDraught(finalDrPct, false);

            float t = Mathf.InverseLerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, bestSec);
            ship.hullPartSizeZ = (1f - t) * 100f;
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
                Debug.LogError("Ship or hull null!");
                return;
            }

            var data = __instance.hull.data;
            if (data == null)
            {
                Debug.LogError("hull PartData null!");
                return;
            }
            var hData = ShipStats.GetData(data);
            if (hData == null)
            {
                Debug.LogError("Couldn't find hulldata for " + ShipStats.GetHullModelKey(data));
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
                Debug.LogError("hull middles list null!");
            }
            else
            {
                updateSections =  secsToUse != __instance.hull.middles.Count;
            }

            if (hData._statsSet.Length <= secsToUse || secsToUse < 0)
            {
                Debug.LogError($"SecsToUse ({secsToUse}) out of bounds for StatsSet length {hData._statsSet.Length}");
                return;
            }

            float scaleFactor = ShipStats.GetHullScaleFactor(__instance, hData._statsSet[secsToUse].Vd) / __instance.hull.model.transform.localScale.z;
//#if LOGSHIP
            Debug.Log($"{hData._key}: tonnage desired: {__instance.tonnage:F0} in ({data.tonnageMin:F0},{data.tonnageMax:F0}). Scale {scaleFactor:F3} (total {(scaleFactor * __instance.hull.model.transform.localScale.z):F3}). Vd for 1/1={hData._statsSet[secsToUse].Vd:F0}\nS={secsToUse} ({data.sectionsMin}-{data.sectionsMax}).");
//#endif
            __instance.hull.bow.GetParent().GetParent().transform.localScale = Vector3.one * scaleFactor;

            float slider = Mathf.InverseLerp(data.sectionsMin - 0.499f, data.sectionsMax + 0.499f, secsToUse);
            __instance.tonnage = Mathf.Lerp(data.tonnageMin, data.tonnageMax, slider);
            float bmMult = 1f + __instance.beam * 0.01f;
            float drMult = bmMult * (1f + __instance.draught * 0.01f);
            __instance.draught = (drMult - 1f) * 100f;
            __state._yPosScaling = 1f + __instance.draught * 0.01f;
            __state._valuesSet = true;
        }

        private static readonly Il2CppSystem.Collections.Generic.List<GameObject> _hullSecitons = new Il2CppSystem.Collections.Generic.List<GameObject>();

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
            Melon<UADRealismMod>.Logger.Msg($"{__instance.vesselName}: {stats.Lwl:F2}x{beamStr}x{stats.T:F2} ({(stats.Lwl/stats.beamBulge):F1},{(stats.beamBulge/stats.T):F1}), {(stats.Vd * ShipStats.WaterDensity * 0.001d):F0}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}. {hp} ({ShipStats.GetHullFormSHPMult(__instance):F2}x{ihpMult:F2}) for {(__instance.speedMax/0.5144444f):F1}kn");
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

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Ship.CheckHullSizeMaxMin))]
        internal static void Prefix_CheckHullSizeMaxMin(Ship __instance, out float __state)
        {
            __state = __instance.hullPartSizeZ;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CheckHullSizeMaxMin))]
        internal static void Postfix_CheckHullSizeMaxMin(Ship __instance, float __state)
        {
            __instance.hullPartSizeZ = __state;
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
    }

    [RegisterTypeInIl2Cpp]
    public class ShipUpdater : MonoBehaviour
    {
        public ShipUpdater(IntPtr ptr) : base(ptr) { }

        public void Update()
        {
            if (GameManager.IsConstructor)
            {
                var ship = G.ui.mainShip;
                int secsToUse = Mathf.RoundToInt(Mathf.Lerp(ship.hull.data.sectionsMin, ship.hull.data.sectionsMax, ship.CrewTrainingAmount * 0.01f));

                if (secsToUse != ship.hull.middles.Count)
                    ship.RefreshHull(true);
            }
        }
    }
}
