//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace UADRealism
{
    public class GenerateShip
    {
        float _hullYear;
        float _designYear;
        float _avgYear;
        string _sType;
        HullData _hData;
        float _tngLimit;
        bool _isLight;
        float _desiredSpeed;
        Ship._GenerateRandomShip_d__566 _this;
        Ship _ship;
        bool _isMissionMainShip;
        int _gen;
        bool _mainPlaced = false;
        bool _secPlaced = false;
        bool _hasFunnel = false;
        bool _needSec = true;
        Part _firstPairCreated = null;
        float _offsetX;
        float _offsetZ;
        // string not PartData to avoid Il2Cpp GC
        Dictionary<string, string> _partDataForGroup = new Dictionary<string, string>();
        // Let's hope this doesn't trip GC.
        HashSet<PartData> _gunSideRefs = new HashSet<PartData>();
        HashSet<PartData> _gunCenterRefs = new HashSet<PartData>();
        HashSet<int> _avoidBarrels = null;

        HashSet<string> _seenCompTypes = new HashSet<string>();

        float _baseHullWeight;

        public GenerateShip(Ship ship)
        {
            _ship = ship;
            _hullYear = Database.GetYear(ship.hull.data);
            _designYear = ship.GetYear(ship);
            _avgYear = (_hullYear + _designYear) * 0.5f;
            _sType = ship.shipType.name;
            _hData = ShipStats.GetData(ship);
            _tngLimit = ship.player.TonnageLimit(ship.shipType);
            _isLight = _sType == "dd" || _sType == "tb";
            _gen = ship.hull.data.Generation;
            _isMissionMainShip = GameManager.IsMission && _ship.player.isMain && BattleManager.Instance.MissionMainShip == _ship;
        }

        public void DesignShipInitial(Ship._GenerateRandomShip_d__566 coroutine)
        {
            var smd = _ship.ModData();
            _this = coroutine;
            var subCoroutine = _this.__8__1;
            subCoroutine.rnd = new Il2CppSystem.Random(Util.FromTo(1, 1000000, null)); // yes, this is how stock inits it.
            var rnd = subCoroutine.rnd;


            if (_this._isRefitMode_5__2 && !_this.isSimpleRefit)
            {
                float tng = _ship.Tonnage();
                float tRatio = _tngLimit / tng;
                if (tRatio < 1f)
                {
                    float drMult = 1f + _ship.draught * 0.01f;
                    float minDrMult = 1f + _ship.hull.data.draughtMin * 0.01f;
                    float newDrMult = Mathf.Max(minDrMult, Mathf.Max(0.9f, tRatio) * drMult);
                    float requiredDrMult = tRatio * drMult;
                    if (newDrMult < drMult)
                    {
                        _ship.SetDraught((newDrMult - 1f) * 100f);
                        tRatio *= drMult / newDrMult;
                    }
                    if (newDrMult > requiredDrMult)
                    {
                        _ship.SetBeam((MathF.Sqrt(tRatio) * (1f + _ship.beam * 0.01f) - 1f) * 100f);
                    }
                    _ship.SetTonnage(_tngLimit);
                }
            }
            else
            {
                if (!_this.adjustTonnage)
                {
                    var tng = _ship.Tonnage();
                    var clampedTng = Mathf.Clamp(tng, _ship.TonnageMin(), _ship.TonnageMax());
                    if (clampedTng != tng)
                        _ship.SetTonnage(clampedTng);
                }
                else
                {
                    var tMin = _ship.TonnageMin();
                    var tMax = Math.Min(_tngLimit, _ship.TonnageMax());
                    if (tMax < tMin)
                        tMax = tMin;

                    float newTng;
                    if (_this.customTonnageRatio.HasValue)
                    {
                        newTng = Mathf.Lerp(tMin, tMax, _this.customTonnageRatio.Value);
                    }
                    else
                    {
                        float tngFloor;
                        if (_ship.shipType.paramx.ContainsKey("random_tonnage"))
                        {
                            tngFloor = Util.Range(tMin, tMax, rnd);
                        }
                        else if (_ship.shipType.paramx.ContainsKey("random_tonnage_low"))
                        {
                            tngFloor = Util.Range(tMin, Util.Chance(80f) ? Mathf.Lerp(tMin, tMax, MonoBehaviourExt.Param("tonnage_not_maximal_ratio", 0.5f)) : tMax, rnd);
                        }
                        else
                        {
                            tngFloor = Mathf.Lerp(tMin, tMax, MonoBehaviourExt.Param("tonnage_not_maximal_ratio", 0.5f) * Util.Range(0.01f, 1.5f, rnd));
                        }
                        newTng = Util.Range(tngFloor, tMax, rnd);
                    }

                    float roundedTng = Ship.RoundTonnageToStep(newTng);
                    _ship.SetTonnage(Mathf.Clamp(roundedTng, tMin, tMax));
                }
            }

            float CbAvg = 0f;
            float CpAvg = 0f;
            if (!_this._isRefitMode_5__2)
            {
                for (int i = _ship.hull.data.sectionsMin; i <= _ship.hull.data.sectionsMax; ++i)
                {
                    CbAvg += _hData._statsSet[i].Cb;
                    CpAvg += _hData._statsSet[i].Cp;
                }
                float secsRecip = 1f / (_ship.hull.data.sectionsMax - _ship.hull.data.sectionsMin + 1);
                CbAvg *= secsRecip;
                CpAvg *= secsRecip;
            }
            else
            {
                int secs = smd.SectionsFromFineness();
                CbAvg = _hData._statsSet[secs].Cb;
                CpAvg = _hData._statsSet[secs].Cp;
            }

            float speedKtsMin;
            float speedKtsMax;
            float CpMin, CpMax;
            // These will only be used in non-refit, but are declared
            // so the switch can set them.
            float desiredBdivT = ShipStats.DefaultBdivT + ModUtils.DistributedRange(0.3f, rnd);
            float extraCpOffset = 0f;
            switch (_sType)
            {
                case "tb":
                    CpMin = Util.Remap(_hullYear, 1920f, 1930f, 0.5f, 0.61f, true);
                    CpMax = Util.Remap(_hullYear, 1920f, 1930f, 0.55f, 0.65f, true);
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1935f, 21f, 32f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1935f, 26f, 39f, true);
                    extraCpOffset = Util.Remap(_hullYear, 1895f, 1925f, -0.08f, 0f);
                    break;
                case "dd":
                    CpMin = Util.Remap(_hullYear, 1920f, 1930f, 0.5f, 0.61f, true);
                    CpMax = Util.Remap(_hullYear, 1920f, 1930f, 0.55f, 0.65f, true);
                    speedKtsMin = Util.Remap(_avgYear, 1895f, 1935f, 24f, 33f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1895f, 1935f, 29f, 40f, true);
                    extraCpOffset = Util.Remap(_hullYear, 1895f, 1925f, -0.08f, 0f);
                    break;
                case "cl":
                    CpMin = 0.54f;
                    CpMax = 0.62f;
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1930f, 17f, 28f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1930f, 24f, 37f, true);
                    break;
                case "ca":
                    CpMin = 0.56f;
                    CpMax = 0.64f;
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1930f, 15f, 28f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1930f, 23f, 35f, true);
                    break;
                case "bc":
                    CpMin = 0.56f;
                    CpMax = 0.64f;
                    speedKtsMin = Util.Remap(_avgYear, 1900f, 1930f, 23f, 26f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1900f, 1930f, 28f, 34f, true);
                    break;
                case "bb":
                    CpMin = 0.57f;
                    CpMax = 0.69f;
                    speedKtsMin = Util.Remap(_avgYear, 1895f, 1935f, 14f, 21f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1895f, 1935f, 22f, 34f, true);
                    break;
                case "ic":
                    CpMin = 0.6f;
                    CpMax = 0.7f;
                    speedKtsMin = 5f;
                    speedKtsMax = 11f;
                    break;
                default: // tr, amc
                    CpMin = 0.6f;
                    CpMax = 0.75f;
                    speedKtsMin = 10f;
                    speedKtsMax = 14f;
                    break;
            }
            float speedBias = Mathf.Cos(Mathf.PI * Mathf.InverseLerp(CpMin, CpMax, CpAvg)) * 0.5f;
            float speedKts = Mathf.Lerp(speedKtsMin, speedKtsMax, (0.5f + ModUtils.DistributedRange(0.5f, rnd)) + speedBias) + ModUtils.DistributedRange(0.2f, rnd);
            speedKts = Mathf.Clamp(_ship.hull.data.shipType.speedMin, _ship.hull.data.shipType.speedMax, speedKts);
            // if this is a refit, we'll use this as our goal speed but maybe not hit it.

            if (_this._isRefitMode_5__2)
            {
                if (ModUtils.Range(0f, 1f, null, rnd) > 0.75f)
                {
                    float speedMS = Mathf.Clamp(speedKts * ShipStats.KnotsToMS, _ship.speedMax - 2f, _ship.speedMax + 2f);
                    _ship.SetSpeedMax(speedMS);
                }
            }
            else
            {
                _ship.SetSpeedMax(speedKts * ShipStats.KnotsToMS);

                // First, set L/B from B/T
                float desiredLdivB = ShipStats.GetDesiredLdivB(_ship.tonnage * ShipStats.TonnesToCubicMetersWater, CbAvg, desiredBdivT, _ship.speedMax, _sType, _hullYear);
                desiredLdivB *= 1f + ModUtils.DistributedRange(0.05f, rnd);

                // Next figure out which hull (i.e. what fineness)
                int bestSec = ShipStats.GetDesiredSections(_hData, _ship.hull.data.sectionsMin, _ship.hull.data.sectionsMax, _ship.tonnage, _ship.speedMax,
                    desiredLdivB, desiredBdivT, out var finalBmPct, out var finalDrPct, out _, ModUtils.DistributedRange(0.01f, 3, null, rnd) + extraCpOffset);
                // Center freeboard on 0.
                float freeboard = ModUtils.DistributedRange(0.5f, 3, null, rnd);
                if (freeboard < 0f)
                    freeboard = Mathf.Lerp(0f, ShipData._MinFreeboard, -freeboard);
                else
                    freeboard = Mathf.Lerp(0f, ShipData._MaxFreeboard, freeboard);

                // Apply values
                _ship.SetBeam(finalBmPct, false);
                _ship.SetDraught(finalDrPct, false);
                float t = Mathf.InverseLerp(_ship.hull.data.sectionsMin, _ship.hull.data.sectionsMax, bestSec);
                smd.SetFineness(Mathf.Lerp(ShipData._MinFineness, ShipData._MaxFineness, 1f - t));
                smd.SetFreeboard(freeboard);

                if (_ship.modelState == Ship.ModelState.Constructor || _ship.modelState == Ship.ModelState.Battle)
                    _ship.RefreshHull(false);

                _this._savedSpeedMinValue_5__3 = _ship.speedMax;
            }
            _desiredSpeed = _ship.speedMax;

            if (_this._isRefitMode_5__2)
            {
                if (!_this.isSimpleRefit)
                {
                    if (_ship.CurrentCrewQuarters < Ship.CrewQuarters.Standard)
                        _ship.CurrentCrewQuarters = Ship.CrewQuarters.Standard;
                }
            }
            else
            {
                _ship.CurrentCrewQuarters = (Ship.CrewQuarters)ModUtils.RangeToInt(ModUtils.BiasRange(ModUtils.DistributedRange(1f, 2, null, rnd), _isLight ? -0.33f : 0f), 3);

                _ship.SetOpRange(_this.customRange.HasValue ? _this.customRange.Value :
                    (VesselEntity.OpRange)ModUtils.Clamp(
                        (int)ShipStats.GetDesiredOpRange(_sType, _hullYear) + ModUtils.RangeToInt(ModUtils.DistributedRange(1f, 4, null, rnd), 3) - 1,
                        (int)VesselEntity.OpRange.VeryLow,
                        (int)VesselEntity.OpRange.VeryHigh), true);

                if (_this.customSurv.HasValue)
                {
                    _ship.survivability = _this.customSurv.Value;
                }
                else
                {
                    int offset = _isLight ? -1 : 0;
                    _ship.survivability = (Ship.Survivability)ModUtils.Clamp(
                        (int)ModUtils.RangeToInt(ModUtils.BiasRange(ModUtils.DistributedRange(1f, 4, null, rnd), _isLight ? 0f : 0.7f), 3) + 2,
                        (int)Ship.Survivability.High + offset,
                        (int)Ship.Survivability.VeryHigh);
                }

                _this._randArmorRatio_5__6 = _ship.GetRandArmorRatio(rnd);
                float mmArmor;
                if (_this.customArmor.HasValue)
                    mmArmor = _this.customArmor.Value;
                else
                    mmArmor = _ship.shipType.armor * 25.4f * _this._randArmorRatio_5__6;

                _ship.armor = Ship.GenerateArmor(mmArmor, _ship);
            }

            if (!GameManager.IsCampaign)
                _ship.CrewTrainingAmount = ModUtils.Range(17f, 100f, null, rnd);

            _ship.Weight();
            _ship.RefreshHullStats();
        }

        private static Dictionary<ComponentData, float> _CompWeights = new Dictionary<ComponentData, float>();

        public enum ComponentSelectionPass
        {
            Initial,
            Armor,
            PostParts
        }

        public void SelectComponents(ComponentSelectionPass pass)
        {
            List<CompType> compTypes = new List<CompType>();
            
            foreach (var ct in G.GameData.compTypes.Values)
            {
                if (pass != ComponentSelectionPass.PostParts)
                {
                    // Skip protection components for now.
                    switch (ct.name)
                    {
                        case "armor":
                        case "barbette":
                        case "torpedo_belt":
                        case "citadel":
                            if (pass == ComponentSelectionPass.Initial)
                                continue;
                            else
                                break;

                        default:
                            if (pass == ComponentSelectionPass.Armor)
                                continue;
                            else
                                break;
                    }
                }

                // If we're in post-parts, don't change existing components.
                // Just handle new ones.
                if (_ship.IsComponentTypeAvailable(ct) && Ui.NeedComponentsActive(ct, null, _ship, true, false))
                {
                    if (pass == ComponentSelectionPass.PostParts && _seenCompTypes.Contains(ct.name))
                        continue;

                    compTypes.Add(ct);
                    _seenCompTypes.Add(ct.name);
                }
            }

            // This check is because we have multiple passes
            if (compTypes.Count == 0 && pass != ComponentSelectionPass.Initial)
                return;

            foreach (var ct in compTypes)
            {
                foreach (var comp in G.GameData.components.Values)
                {
                    if (Ui.NeedComponentsActive(ct, comp, _ship, true, false))
                    {
                        _CompWeights[comp] = comp.weight;
                    }
                }
                if (_CompWeights.Count == 0)
                    continue;
                var newComp = ModUtils.RandomByWeights(_CompWeights, null, _this.__8__1.rnd);
                if (newComp != null)
                    _ship.InstallComponent(newComp, true);
                _CompWeights.Clear();
            }

            _ship.RefreshHullStats();

            if (pass == ComponentSelectionPass.Initial)
            {
                // record weight before adding protection components
                // NOTE: turret type will be counted as part of armament weight
                // even though it's kinda hull weight.
                _baseHullWeight = _ship.Weight();
                SelectComponents(ComponentSelectionPass.Armor);
            }
        }

        public bool SelectParts()
        {
            if (_this.isSimpleRefit)
                return true;

            _ship.EnableLogging(false);
            var st = _ship.shipType;
            if (st.randParts.Count == 0)
            {
                // Log error?
                return false;
            }
            _avoidBarrels = null;
            if (st.paramx.TryGetValue("avoid_barrels", out var avoidB))
            {
                _avoidBarrels = new HashSet<int>();
                foreach (var s in avoidB)
                    _avoidBarrels.Add(int.Parse(s));
            }

            // Not sure why the game creates this but never adds it
            // to the list of randparts...
            // This is a no-op AFAIK, I don't think PostProcess
            // actually adds it to anything.
            RandPart props = new RandPart();
            props.name = "(props)";
            props.shipTypes = string.Empty;
            props.chance = 100;
            props.min = 20;
            props.max = 40;
            props.type = "special";
            props.paired = true;
            props.group = string.Empty;
            props.effect = "prop";
            props.center = false;
            props.side = true;
            props.rangeZFrom = -1f;
            props.rangeZTo = 1f;
            props.condition = string.Empty;
            props.param = string.Empty;
            props.PostProcess();

            _mainPlaced = false;
            _secPlaced = false;
            _needSec = _sType != "tr";
            if (_needSec)
            {
                PartCategoryData secPCD = null;
                foreach (var kvp in G.GameData.partCategories)
                {
                    if (kvp.Key == "tower_sec")
                    {
                        secPCD = kvp.Value;
                    }
                }

                _needSec = false;
                foreach (var kvp in G.GameData.parts)
                {
                    if (_ship.BelongsToCategory(kvp.Value, secPCD) && _ship.IsPartAvailable(kvp.Value))
                    {
                        _needSec = true;
                        break;
                    }
                }
            }
            _hasFunnel = false;

            var randParts = _this._isRefitMode_5__2 ? st.randPartsRefit : st.randParts;
            foreach (var rp in randParts)
            {
                var chance = rp.chance;
                if (rp.paramx.TryGetValue("tr_rand_mod", out var trmod))
                {
                    var paramInit = MonoBehaviourExt.Param("initial_tr_transport_armed", 0.1f);
                    var armed = _ship.TechA("armed_transports");
                    chance *= paramInit * armed;
                }

                if (!Util.Chance(chance, _this.__8__1.rnd))
                    continue;

                if (!(_gen < 4 || _mainPlaced || rp.rangeZFrom < -0.3f || rp.rangeZTo >= 0.625f || (rp.type != "barbette" && rp.type != "funnel"))
                    || !(_gen < 4 || _secPlaced || rp.rangeZFrom < -0.45f || rp.rangeZTo >= 0.25f || (rp.type != "gun" && rp.type != "barbette" && rp.type != "funnel"))
                    || !(!_mainPlaced || rp.type != "tower_main")
                    || !(!_secPlaced || rp.type != "tower_sec")
                    // stock has rangeZFrom < -1f which is impossible.
                    || !(rp.type != "funnel" || !_needSec || _secPlaced || rp.rangeZFrom < -1f || rp.rangeZTo >= -0.5))
                    continue;

                if (rp.paramx.TryGetValue("scheme", out var scheme))
                {
                    if (_ship.hull.hullInfo != null && !_ship.hull.hullInfo.schemes.Contains(scheme[0]))
                        continue;
                }
                if (!_ship.CheckOperations(rp))
                    continue;

                if (rp.paramx.ContainsKey("delete_unmounted"))
                {
                    _ship.DeleteUnmounted(rp.type);
                }
                else if (_this._isRefitMode_5__2 && rp.paramx.ContainsKey("delete_refit"))
                {
                    _ship.RemoveDeleteRefitPartsNew(rp.type, _this.isSimpleRefit);
                }
                else
                {
                    int num = Util.FromTo(rp.min, rp.max, _this.__8__1.rnd);
                    if (rp.paired)
                    {
                        num = 2 * Util.RoundToIntProb(num * 0.5f, _this.__8__1.rnd);
                    }

                    _ship.added.Clear();
                    _ship.badData.Clear();
                    _ship.badMounts.Clear();
                    _ship.badTriesForData.Clear();
                    _firstPairCreated = null;
                    _offsetX = 0f;
                    _offsetZ = 0f;
                    TryAddPartsForRandPart(rp, num);
                }
            }
            _gunSideRefs.Clear();
            _gunCenterRefs.Clear();

            if (_ship.parts.Count > 0)
            {
                for (int i = _ship.parts.Count; i-- > 0;)
                {
                    var part = _ship.parts[i];
                    var data = part.data;
                    if (data.isGun ? _ship.IsMainCal(data) : data.isTorpedo)
                    {
                        if (!part.CanPlaceSoft(false) || !part.CanPlaceSoftLight())
                        {
                            _ship.RemovePart(part, true, true);
                        }
                    }
                }
                for (int i = _ship.shipGunCaliber.Count; i-- > 0;)
                {
                    bool remove = true;
                    foreach (var p in _ship.parts)
                    {
                        if (p.data == _ship.shipGunCaliber[i].turretPartData)
                        {
                            remove = false;
                            break;
                        }
                    }
                    if (remove)
                        _ship.shipGunCaliber.RemoveAt(i);
                }

                for (int i = _ship.shipTurretArmor.Count; i-- > 0;)
                {
                    bool remove = true;
                    foreach (var p in _ship.parts)
                    {
                        if (p.data == _ship.shipTurretArmor[i].turretPartData)
                        {
                            remove = false;
                            break;
                        }
                    }
                    if (remove)
                        _ship.shipGunCaliber.RemoveAt(i);
                }

                Patch_Ship.ClearMatCache(_ship);
            }

            _ship.StartCoroutine(_ship.RefreshDecorDelay());

            foreach (var part in _ship.parts)
                part.OnPostAdd();

            return true;
        }

        private bool TryAddPartsForRandPart(RandPart rp, int desiredAmount)
        {
            var parts = _ship.GetParts(rp, _this.limitCaliber);
            List<PartData> options = new List<PartData>();
            for (int loop = 0; loop < 250; ++loop)
            {
                PartData data = null;
                Part part = null;

                if (rp.group != string.Empty && _partDataForGroup.TryGetValue(rp.group, out var pdName) && pdName != string.Empty)
                {
                    data = G.GameData.parts[pdName];
                    if (_ship.badData.Contains(data))
                    {
                        // We could be adding main guns and gone over-limit
                        // with the last one. Or other reasons why this part
                        // might have started out good but ended up bad.
                        break;
                    }
                }
                else
                {
                    data = FindPartDataForRP(rp, parts, options);
                }

                if (data == null)
                    break;


                if (data.isGun)
                {
                    if (_ship.shipGunCaliber == null)
                        _ship.shipGunCaliber = new Il2CppSystem.Collections.Generic.List<Ship.TurretCaliber>();
                    var tc = ShipM.FindMatchingTurretCaliber(_ship, data);
                    if (tc == null)
                        AddTCForGun(data);
                }

                if (!Part.CanPlaceGeneric(data, _ship, false, out var denyReason)
                    || (_isMissionMainShip && _ship.Cost() + _ship.CalcPartCost(data) > _ship.player.cash))
                {
                    // This part will never be acceptable. So
                    // add it to badData immediately.
                    _ship.badData.Add(data);
                    MarkBadTry(part, data);
                    continue;
                }

                part = Part.Create(data, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int, false);
                if (data.isGun)
                    part.UpdateCollidersSize(_ship);

                float weight = _ship.Weight(true);
                if (weight > _ship.Tonnage() * 1.4f && !part.data.isWeapon)
                {
                    // This part will never work, it takes us over limit.
                    _ship.badData.Add(data);
                    MarkBadTry(part, data);
                    continue;
                }

                if (!PlacePart(part, rp))
                {
                    MarkBadTry(part, data);
                    continue;
                }

                switch (rp.type)
                {
                    case "tower_main":
                        _mainPlaced = true;
                        break;
                    case "tower_sec":
                        _secPlaced = true;
                        break;
                    case "funnel":
                        _hasFunnel = true;
                        break;
                }

                _ship.added.Add(part);

                if (rp.paired)
                {
                    if (_firstPairCreated)
                        _firstPairCreated = null;
                    else
                        _firstPairCreated = part;
                }

                if (rp.group != string.Empty)
                {
                    _partDataForGroup[rp.group] = data.name;
                }

                if (data.isGun)
                {
                    _ship.AddShipTurretArmor(part);

                    // Store the ref away so we can keep main and side gun calibers in sync
                    if (rp.group == "mc" || data.paramx.ContainsKey("main_center") || data.paramx.ContainsKey("small_center"))
                        _gunCenterRefs.Add(data);
                    if (rp.group == "ms" || data.paramx.ContainsKey("main_side") || data.paramx.ContainsKey("small_side"))
                        _gunSideRefs.Add(data);
                }

                if (_ship.added.Count >= desiredAmount)
                    return true;
            }

            return _ship.added.Count > 0;
        }

        private void MarkBadTry(Part part, PartData data)
        {
            _ship.badTriesForData.TryGetValue(data, out var tries);
            if (tries > 24)
            {
                _ship.badData.Add(data);
            }
            _ship.badTriesForData[data] = tries + 1;
            if (part != null)
            {
                if (part.mount != null)
                {
                    if (!_ship.badMounts.TryGetValue(part.data, out var set))
                    {
                        set = new Il2CppSystem.Collections.Generic.HashSet<Mount>();
                        _ship.badMounts[part.data] = set;
                    }
                    set.Add(part.mount);
                    _ship.RemovePart(part);
                }
            }
            if (_firstPairCreated)
            {
                _ship.added.Remove(_firstPairCreated);
                _ship.RemovePart(_firstPairCreated);
                _firstPairCreated = null;
            }
        }

        private bool PlacePart(Part part, RandPart rp)
        {
            var data = part.data;

            bool useNoMount = false;
            if (rp.paired && _firstPairCreated != null)
            {
                if (_firstPairCreated.mount == null)
                {
                    useNoMount = true;
                }
                else
                {
                    var newMountPos = _firstPairCreated.mount.transform.position;
                    newMountPos.x *= -1f;
                    part.transform.position = newMountPos;
                    part.TryFindMount(true);
                    if (part.mount == null)
                        return false;
                }
            }
            else
            {
                if (!data.allowsMount)
                {
                    useNoMount = true;
                }
                else
                {
                    _ship.allowedMountsInternal.Clear();
                    foreach (var m in _ship.mounts)
                    {
                        if (!IsAllowedMount(m, part, rp.demandMounts, rp.excludeMounts))
                            continue;

                        if (_ship.badMounts.TryGetValue(data, out var set) && set.Contains(m))
                            continue;

                        if (rp.rangeX.HasValue)
                        {
                            Vector2 trueRangeX = new Vector2(rp.rangeX.Value.x + _ship.allowedMountsOffset.x, rp.rangeX.Value.y + _ship.allowedMountsOffset.y)
                                * (_ship.deckBounds.size.x * 2.5f + _ship.deckBounds.center.x);
                            if (m.transform.position.x < trueRangeX.x || m.transform.position.x > trueRangeX.y)
                                continue;
                        }
                        if (rp.rangeZ.HasValue)
                        {
                            Vector2 trueRangeZ = new Vector2(rp.rangeZ.Value.x + _ship.allowedMountsOffset.x, rp.rangeZ.Value.y + _ship.allowedMountsOffset.y)
                                * (_ship.deckBounds.size.z * 0.63f + _ship.deckBounds.center.z);
                            if (m.transform.position.z < trueRangeZ.x || m.transform.position.z > trueRangeZ.y)
                                continue;
                        }

                        _ship.allowedMountsInternal.Add(m);
                    }
                    if (_ship.allowedMountsInternal.Count > 0)
                    {
                        part.Mount(_ship.allowedMountsInternal.Random(null, _this.__8__1.rnd), true);
                    }
                    else
                    {
                        if (!data.needsMount && (rp.demandMounts == null || rp.demandMounts.Count == 0))
                        {
                            useNoMount = true;
                        }
                        else
                        {
                            var mountGroups = _ship.GetAllowedMountsInGroups(rp, part);

                            List<Vector3> positions = new List<Vector3>();
                            var snap = MonoBehaviourExt.Param("snap_point_step", 0.5f);
                            foreach (var mg in mountGroups)
                            {
                                // We want to sort the list of mounts. But
                                // it's a native list, not a managed one.
                                // So copy to managed (ugh).
                                var mg2 = new List<Mount>();
                                foreach (var m in mg)
                                    mg2.Add(m);
                                mg2.Sort((a, b) =>
                                {
                                    if (a.transform.position.z < b.transform.position.z)
                                        return -1;
                                    else if (a.transform.position.z > b.transform.position.z)
                                        return 1;
                                    return 0;
                                });

                                // Start from the bow (we sorted minZ first)
                                for (int i = mg2.Count - 1; i > 0 && positions.Count == 0; --i)
                                {
                                    var curM = mg2[i];
                                    var nextM = mg2[i - 1];
                                    var curPos = curM.transform.position;
                                    var nextPos = nextM.transform.position;
                                    var distHalfZ = (nextPos.z - curPos.z) * 0.5f;
                                    part.transform.position = new Vector3(curPos.x, curPos.y, curPos.z + distHalfZ + snap + 16.75f);
                                    if (!part.CanPlace())
                                    {
                                        part.transform.position = new Vector3(curPos.x, curPos.y, curPos.z + distHalfZ + 16.75f);
                                        if (!part.CanPlace())
                                            continue;
                                    }

                                    float midPos = curPos.z + distHalfZ + 16.75f;
                                    bool lastOK = false;
                                    while (true)
                                    {
                                        midPos -= snap;
                                        if (midPos < nextPos.z - snap - 105f)
                                            break;

                                        part.transform.position = new Vector3(curPos.x, curPos.y, midPos);
                                        if (part.CanPlace())
                                        {
                                            positions.Add(part.transform.position);
                                            lastOK = true;
                                        }
                                        else
                                        {
                                            if (lastOK)
                                                break;
                                            lastOK = false;
                                        }
                                    }
                                }
                            }

                            if (positions.Count == 0)
                            {
                                // This part doesn't fit anywhere on the ship now
                                // so let's never try this data again.
                                _ship.badData.Add(data);
                                return false;
                            }
                            part.transform.position = positions.Random(null, _this.__8__1.rnd);
                        }
                    }
                }
            }

            if (useNoMount)
            {
                if (rp.paired && _firstPairCreated != null)
                {
                    _offsetX *= -1f;
                }
                else
                {
                    _offsetX = (rp.rangeX.HasValue ?
                        ModUtils.Range(rp.rangeX.Value.x, rp.rangeX.Value.y, null, _this.__8__1.rnd)
                        : ModUtils.Range(-1f, 1f, null, _this.__8__1.rnd))
                        * _ship.deckBounds.size.x * 0.5f;
                    _offsetZ = (rp.rangeZ.HasValue ?
                        ModUtils.Range(rp.rangeZ.Value.x, rp.rangeZ.Value.y, null, _this.__8__1.rnd)
                        : ModUtils.Range(-1f, 1f, null, _this.__8__1.rnd))
                        * _ship.deckBounds.size.z * 0.5f;

                    if (_offsetX != 0f && !data.paramx.ContainsKey("center"))
                    {
                        Vector3 partWorldPos = _ship.transform.TransformPoint(_ship.deckBounds.center + new Vector3(_offsetX, 0f, _offsetZ));
                        float maxXDist = _ship.deckBounds.size.x * 1.05f;
                        float maxZDist = _ship.deckBounds.size.z * 0.95f;
                        Part bestPart = null;
                        float bestDistX = float.MaxValue;
                        float bestDistZ = float.MaxValue;
                        foreach (var p in _ship.parts)
                        {
                            if (p.data != data)
                                continue;

                            var fromPos = Util.AbsVector(p.transform.position - partWorldPos);
                            if (fromPos.x > maxXDist || fromPos.z > maxZDist)
                                continue;

                            if (fromPos.x < bestDistX)
                            {
                                bestPart = p;
                                bestDistX = fromPos.x;
                            }
                            // Game does an orderby/thenby so the Z check is true iff X
                            // is equal
                            else if (fromPos.x == bestDistX && fromPos.z < bestDistZ)
                            {
                                bestPart = p;
                                bestDistZ = fromPos.z;
                            }
                        }
                        if (bestPart != null)
                            _offsetX = bestPart.transform.localPosition.x; // should this be negative?? Stock doesn't make sense here.
                    }
                }
                var desiredWorldPoint = _ship.transform.TransformPoint(_ship.deckBounds.center + new Vector3(_offsetX, 0f, _offsetZ));
                var deckAtPoint = _ship.FindDeckAtPoint(desiredWorldPoint);
                if (deckAtPoint == null)
                    return false;

                var deckCenter = deckAtPoint.transform.TransformPoint(deckAtPoint.center);
                part.Place(new Vector3(desiredWorldPoint.x, deckCenter.y, desiredWorldPoint.z), true);
            }

            // Found a position
            data.constructorShip = _ship;
            if (!part.CanPlace())
                return false;

            if (!Ship.IsCasemateGun(data) && !part.CanPlaceSoftLight())
                return false;

            bool isValidPlacement = true;
            foreach (var p in _ship.parts)
            {
                if (p == part)
                    continue;

                if (p.data.isTorpedo || (p.data.isGun && !Ship.IsCasemateGun(p.data)))
                {
                    if (!p.CanPlaceSoftLight())
                    {
                        isValidPlacement = true;
                        break;
                    }
                }
            }
            return isValidPlacement;
        }

        private bool IsAllowedMount(Mount m, Part part, Il2CppSystem.Collections.Generic.List<string> demandMounts, Il2CppSystem.Collections.Generic.List<string> excludeMounts)
        {
            if (!m.Fits(part, demandMounts, excludeMounts))
                return false;
            if (_ship.mountsUsed.TryGetValue(m, out var otherPart) && otherPart != part)
                return false;
            if (m.parentPart == part)
                return false;

            return true;
        }

        private Ship.TurretCaliber AddTCForGun(PartData data)
        {
            float diamOffset = 0f;
            int lenOffset = 0;
            if (_this.adjustDiameter)
            {
                var gdm = new GunDataM(data, _ship, true);
                float diamStep = MonoBehaviourExt.Param("gun_diameter_step", 0.1f);
                float maxDiam = MonoBehaviourExt.Param("max_gun_caliber_mod", 0.9f);
                diamOffset = ModUtils.DistributedRangeWithStepSize(maxDiam, diamStep, 2, null, _this.__8__1.rnd);
                if (data.GetCaliberInch() > 2f && diamOffset < 0f)
                    diamOffset = -diamOffset;

                float lenStep = MonoBehaviourExt.Param("gun_length_step", 5f);
                float lenVal = ModUtils.DistributedRange(1f, 5, null, _this.__8__1.rnd);
                if (lenVal < 0f)
                    lenVal *= -gdm.minLengthParam;
                else
                    lenVal *= gdm.maxLengthParam;
                lenOffset = Mathf.RoundToInt(ModUtils.ClampWithStep(lenVal, lenStep, gdm.minLengthParam, gdm.maxLengthParam));
            }
            _ship.AddShipTurretCaliber(data, diamOffset, lenOffset);
            return _ship.shipGunCaliber[_ship.shipGunCaliber.Count - 1]; // stupid that the previous method doesn't return the added TC
        }

        private PartData FindPartDataForRP(RandPart rp, Il2CppSystem.Collections.Generic.List<PartData> parts, List<PartData> options)
        {
            PartData ret = null;
            foreach (var data in parts)
            {
                if (_ship.badData.Contains(data) || data.type != rp.type)
                    continue;

                options.Add(data);
            }

            if (options.Count == 0)
                return null;

            if (rp.type == "torpedo")
            {
                float bestVal = float.MinValue;
                foreach (var opt in options)
                {
                    var val = TorpedoValue(opt);
                    if (val > bestVal)
                    {
                        bestVal = val;
                        ret = opt;
                    }
                }
            }
            else if (rp.type == "gun")
            {
                // Alter stock code such that we track all references
                HashSet<PartData> refData;
                if (rp.effect == "main_center" || rp.effect == "small_center" || rp.group == "mc")
                    refData = _gunSideRefs;
                else if (rp.effect == "main_side" || rp.effect == "small_side" || rp.group == "ms")
                    refData = _gunCenterRefs;
                else
                    refData = null;

                if (refData != null)
                {
                    for (int i = options.Count; i-- > 0;)
                    {
                        var opt = options[i];

                        bool remove = true;
                        foreach (var r in refData)
                        {
                            if (r.GetCaliberInch() == opt.GetCaliberInch())
                            {
                                remove = false;
                                break;
                            }
                        }
                        if (remove)
                            options.RemoveAt(i);
                    }
                }
                // Game is weird here. If "sec_cal" is not there, it jumps to maincal.
                // But if "sec_cal" _is_ there, it still goes to maincal unless "ter_cal"
                // is _also_ specified.
                if (rp.condition.Contains("ter_cal"))
                {
                    float bestVal = float.MinValue;
                    foreach (var opt in options)
                    {
                        var val = TertiaryGunValue(opt);
                        if (val > bestVal)
                        {
                            bestVal = val;
                            ret = opt;
                        }
                    }
                }
                else
                {
                    float bestVal = float.MinValue;
                    foreach (var opt in options)
                    {
                        var val = MainOrSecGunValue(opt);
                        if (val > bestVal)
                        {
                            bestVal = val;
                            ret = opt;
                        }
                    }
                }
            }
            else if (options.Count > 0)
            {
                ret = options.Random(null, _this.__8__1.rnd);
            }

            return ret;
        }

        public float TertiaryGunValue(PartData data)
        {
            var sizeAR = _ship.SizeAntiRatio();
            sizeAR *= sizeAR;
            var calInch = data.GetCaliberInch();
            var smallMin = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_min", 0f);
            var smallMax = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_max", 1f);
            var calWeight = Util.Range(smallMin, smallMax, _this.__8__1.rnd);
            var partBarrels = Mathf.Pow(data.barrels, -5f);
            var calValue = calInch * sizeAR * calWeight;
            var yearMap = Util.Remap(_designYear, 1890f, 1940f, calInch <= 8f ? -1f : 2f, calInch <= 8f ? -10f : 0.4f, true);
            var barMin = MonoBehaviourExt.Param("gun_gen_barrels_sec_min", -2f);
            var barMax = MonoBehaviourExt.Param("gun_gen_barrels_sec_max", -1f);
            float yearWeight = Util.Range(barMin, barMax, _this.__8__1.rnd);
            var yearValue = yearMap * yearWeight;
            var adjustedCalValue = partBarrels * calValue;
            return Util.Range(0f, MonoBehaviourExt.Param("gun_gen_randomness", 5f) + 1f, _this.__8__1.rnd) + adjustedCalValue * yearValue;
        }

        public float MainOrSecGunValue(PartData data)
        {
            float barrelValue;
            if (_avoidBarrels != null && _avoidBarrels.Contains(data.barrels))
                barrelValue = -25f;
            else
                barrelValue = 2f;

            var sizeR = _ship.SizeRatio();
            float sizeRsqrt = Mathf.Sqrt(sizeR);
            var calInch = data.GetCaliberInch();
            var calMin = MonoBehaviourExt.Param("gun_gen_caliber_min", 0f);
            var calMax = MonoBehaviourExt.Param("gun_gen_caliber_max", 1f);
            var calWeight = Util.Range(calMin, calMax, _this.__8__1.rnd);
            var calValue = calInch * sizeRsqrt * calWeight;
            float yearMult;
            float yearWeight;
            if (calInch <= 8f)
            {
                var smallMin = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_min", 0f);
                var smallMax = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_max", 1f);
                yearWeight = Util.Range(smallMin, smallMax, _this.__8__1.rnd);
                yearMult = Util.Remap(_designYear, 1890f, 1940f, -2f, -0.8f, true);
            }
            else
            {
                var largeMin = MonoBehaviourExt.Param("gun_gen_barrels_min", 0f);
                var largeMax = MonoBehaviourExt.Param("gun_gen_barrels_max", 1f);
                yearWeight = Util.Range(largeMin, largeMax, _this.__8__1.rnd);
                yearMult = Util.Remap(_designYear, 1890f, 1940f, -1f, -0.15f, true) * sizeR;
            }
            float yearValue = yearMult * yearWeight;
            return Util.Range(0f, MonoBehaviourExt.Param("gun_gen_randomness", 5f) + 1f, _this.__8__1.rnd) + data.barrels * yearValue + calValue + barrelValue;
        }

        public float MainGunValue_Unused(PartData data)
        {
            float barrelValue;
            if (_avoidBarrels != null && _avoidBarrels.Contains(data.barrels))
                barrelValue = -25f;
            else
                barrelValue = 2f;

            var sizeR = _ship.SizeRatio();
            var calInch = data.GetCaliberInch();
            var calMin = MonoBehaviourExt.Param("gun_gen_caliber_min", 0f);
            var calMax = MonoBehaviourExt.Param("gun_gen_caliber_max", 1f);
            var calWeight = Util.Range(calMin, calMax, _this.__8__1.rnd);
            var calValue = calInch * sizeR * calWeight;
            float yearMult;
            float yearWeight;
            if (calInch <= 8f)
            {
                var smallMin = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_min", 0f);
                var smallMax = MonoBehaviourExt.Param("gun_gen_smallgunbarrels_max", 1f);
                yearWeight = Util.Range(smallMin, smallMax, _this.__8__1.rnd);
                yearMult = Util.Remap(_designYear, 1890f, 1940f, -2f, -0.8f, true);
            }
            else
            {
                var largeMin = MonoBehaviourExt.Param("gun_gen_barrels_min", 0f);
                var largeMax = MonoBehaviourExt.Param("gun_gen_barrels_max", 1f);
                yearWeight = Util.Range(largeMin, largeMax, _this.__8__1.rnd);
                yearMult = Util.Remap(_designYear, 1890f, 1940f, -2f, -0.125f, true) * sizeR;
            }
            float yearValue = yearMult * yearWeight;
            return Util.Range(0f, MonoBehaviourExt.Param("gun_gen_randomness", 5f) + 1f, _this.__8__1.rnd) + data.barrels * yearValue + calValue + barrelValue;
        }

        public float TorpedoValue(PartData data)
        {
            return Util.Range(0f, MonoBehaviourExt.Param("torpedo_gen_randomness", 1.2f)) + data.barrels;
        }
    }
}