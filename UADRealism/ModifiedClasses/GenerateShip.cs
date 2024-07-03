//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using TweaksAndFixes;
using UADRealism.Data;

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
        static Dictionary<ComponentData, float> _CompWeights = new Dictionary<ComponentData, float>();
        static List<PartData> _Options = new List<PartData>();
        static Dictionary<PartData, float> _OptionWeights = new Dictionary<PartData, float>();

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
        float _tngRatio;
        bool _mainPlaced = false;
        bool _secPlaced = false;
        bool _hasFunnel = false;
        bool _needSec = true;
        List<RandPartOperation> _selectedRandParts = new List<RandPartOperation>();
        Part _firstPairCreated = null;
        float _offsetX;
        float _offsetZ;
        Dictionary<string, PartData> _partDataForGroup = new Dictionary<string, PartData>();
        HashSet<PartData> _gunSideRefs = new HashSet<PartData>();
        HashSet<PartData> _gunCenterRefs = new HashSet<PartData>();
        HashSet<int> _avoidBarrels = new HashSet<int>();
        Dictionary<Ship.A, float> _ArmorMultipliers = new Dictionary<Ship.A, float>();
        bool _partsInitOK = false;

        HashSet<string> _seenCompTypes = new HashSet<string>();

        public enum GunCal
        {
            Main,
            Sec,
            Ter,

            COUNT
        }
        public enum GunAlign
        {
            Center,
            Side,
            Both,

            COUNT
        }

        [Flags]
        enum GunRef
        {
            None = 0,
            Center = 1 << 0,
            Side = 1 << 1,
        }
        class GunInfo
        {
            public GunCal _cal;
            public GunAlign _align;
            public HashSet<int> _calInchOptions = new HashSet<int>();
            public List<RandPartOperation> _rps = new List<RandPartOperation>();
            public float _caliber = -1f;
            public float _length = -1f;
            public PartData _mainData = null;
            public GunRef _reference;
        }
        Dictionary<RandPartOperation, GunInfo> _gunInfosByRPO = new Dictionary<RandPartOperation, GunInfo>();
        Dictionary<string, GunInfo> _gunInfosByGroup = new Dictionary<string, GunInfo>();
        List<GunInfo> _gunInfos = new List<GunInfo>();
        readonly int[] _CalCounts = new int[(int)GunCal.COUNT];
        readonly int[] _CalLimits = new int[(int)GunCal.COUNT];
        readonly bool[] _CalMerge = new bool[(int)GunCal.COUNT];

        float _baseHullWeight;
        float _totalHullWeight;
        float _armStopWeight;

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
            _tngRatio = Mathf.InverseLerp(ship.TonnageMin(), Math.Min(ship.TonnageMax(), _tngLimit), ship.Tonnage());

            if (_ship.shipType.paramx.TryGetValue("avoid_barrels", out var avoidB))
            {
                foreach (var s in avoidB)
                    _avoidBarrels.Add(int.Parse(s));
            }
            _partsInitOK = InitParts();
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

                if (_ship.armor == null)
                    _ship.armor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                for (Ship.A armor = Ship.A.Belt; armor <= Ship.A.InnerDeck_3rd; armor = (Ship.A)((int)(armor + 1)))
                    _ship.armor[armor] = 0f;
                _ship.SetArmor(_ship.armor);
                // Yes, this is illegal. But we need the ship weight with no armor.

                // There won't be any TurretArmors yet.
            }

            if (!GameManager.IsCampaign)
                _ship.CrewTrainingAmount = ModUtils.Range(17f, 100f, null, rnd);

            _ship.Weight();
            _ship.RefreshHullStats();
        }

        public enum ComponentSelectionPass
        {
            Initial,
            Armor,
            PostParts
        }

        public void SelectComponents(ComponentSelectionPass pass)
        {
            List<CompType> compTypes = new List<CompType>();
            if (pass == ComponentSelectionPass.Initial)
                _seenCompTypes.Clear();

            CompType mines = null;
            CompType dc = null;
            CompType sweep = null;

            foreach (var ct in G.GameData.compTypes.Values)
            {
                // Only some types can be changed in refits.
                if (_this._isRefitMode_5__2)
                {
                    switch (ct.name)
                    {
                        case "shell_ratio_main":
                        case "shell_ratio_sec":
                        case "ammo_shell":
                        case "ammo_torp":
                        case "rangefinder":
                        case "sonar":
                        case "radio":
                        case "radar":
                        // These are possibly illegal because
                        // they might need chamber/shell-handling redesign
                        case "shell_he":
                        case "shell_ap":
                        case "shell": // light/medium/heavy/super-heavy
                        case "propellant":
                        case "explosives":
                        // Ditto for the fueling apparatus
                        case "torpedo_prop":
                            break;

                        case "aux_eng":
                        case "rudder":
                        case "steering":
                        case "torpedo_belt":
                        case "turret_traverse":
                        case "gun_reload":
                        case "torpedo_size":
                        case "mines":
                        case "minesweep":
                        case "depthcharge":
                        case "plane":
                        // These are kinda sketchy
                        case "fuel":
                        case "engine":
                        case "boilers":
                        case "shaft":
                            if (_this.isSimpleRefit)
                                continue;
                            else
                                break;

                        default:
                            continue;
                    }
                }

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
                    if (_seenCompTypes.Contains(ct.name))
                        continue;

                    // These count as part of armament so need to get installed later.
                    if (!_this._isRefitMode_5__2)
                    {
                        if (ct.name == "mines")
                        {
                            mines = ct;
                            continue;
                        }
                        else if (ct.name == "depthcharge")
                        {
                            dc = ct;
                            continue;
                        }
                        else if (ct.name == "minesweep")
                        {
                            sweep = ct;
                            continue;
                        }
                    }

                    compTypes.Add(ct);
                    _seenCompTypes.Add(ct.name);
                }
            }

            // This check is because we have multiple passes
            if (compTypes.Count == 0 && pass != ComponentSelectionPass.Initial)
                return;

            foreach (var ct in compTypes)
                InstallRandomComponentForType(ct);

            _ship.RefreshHullStats();
            _ship.NeedRecalcCache();

            if (pass == ComponentSelectionPass.Initial)
            {
                // record weight before adding protection components
                // NOTE: turret type will be counted as part of armament weight
                // even though it's kinda hull weight. That's because it won't
                // show up unless there are gun parts on the ship.
                _baseHullWeight = _ship.Weight();

                if (!_this._isRefitMode_5__2)
                {
                    if (mines != null)
                        InstallRandomComponentForType(mines);
                    if (dc != null)
                        InstallRandomComponentForType(dc);
                    if (sweep != null)
                        InstallRandomComponentForType(sweep);
                }
                _ship.RefreshHullStats();
                _ship.NeedRecalcCache();
                _totalHullWeight = _ship.Weight();

                SelectComponents(ComponentSelectionPass.Armor);
            }
        }

        private void InstallRandomComponentForType(CompType ct)
        {
            foreach (var comp in G.GameData.components.Values)
            {
                if (_ship.IsComponentAvailable(comp, out _) && Ui.NeedComponentsActive(ct, comp, _ship, true, false))
                {
                    _CompWeights[comp] = TweaksAndFixes.ComponentDataM.GetWeight(comp, _sType);
                }
            }
            if (_CompWeights.Count == 0)
                return;
            var newComp = ModUtils.RandomByWeights(_CompWeights, null, _this.__8__1.rnd);
            if (newComp != null)
                _ship.InstallComponent(newComp, true);
            _CompWeights.Clear();
        }

        private bool InitParts()
        {
            if (_this.isSimpleRefit)
                return true;

            var st = _ship.shipType;
            if (st.randParts.Count == 0)
            {
                // Log error?
                return false;
            }

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

            return true;
        }

        private class RandPartOperation
        {
            public RandPart _rp;
            public bool _isDelete;
            public int _desiredNum;
            public PartData _data;
            public float _zMin;
            public float _zMax;
            public bool _isNormal;
        }

        private static HashSet<string> _PrevTypes = new HashSet<string>();
        private bool HandleSpecialRP(RandPart rp, RandPartOperation rpo)
        {
            _PrevTypes.Clear();
            int idx = 0;
            bool combineRPs = true;
            switch (rp.type)
            {
                case "tower_main":
                    break;
                case "tower_sec":
                    _PrevTypes.Add("tower_main");
                    break;
                case "funnel":
                    combineRPs = false;
                    _PrevTypes.Add("tower_main");
                    _PrevTypes.Add("tower_sec");
                    _PrevTypes.Add("funnel");
                    break;
                default:
                    return false;
            }
            for(; idx < _selectedRandParts.Count; ++idx)
            {
                var srp = _selectedRandParts[idx];
                if (srp._isDelete)
                    continue;
                if (srp._rp.type == rp.type)
                {
                    if (combineRPs)
                        break;
                    else
                        continue;
                }
                if (!_PrevTypes.Contains(srp._rp.type))
                    break;
            }
            if (idx < _selectedRandParts.Count && _selectedRandParts[idx] is var existingSRP && existingSRP._rp.type == rp.type)
            {
                float x = -1f;
                float y = 1f;
                if (rp.rangeZ.HasValue)
                {
                    x = rp.rangeZ.Value.x;
                    y = rp.rangeZ.Value.y;
                }
                if (existingSRP._zMin > x)
                    existingSRP._zMin = x;
                if (existingSRP._zMax > y)
                    existingSRP._zMax = y;
                return true;
            }

            if (rp.rangeZ.HasValue)
            {
                rpo._zMin = rp.rangeZ.Value.x;
                rpo._zMax = rp.rangeZ.Value.y;
            }
            else
            {
                rpo._zMin = -1f;
                rpo._zMax = 1f;
            }
            rpo._isNormal = false;
            _selectedRandParts.Insert(idx, rpo);
            return true;
        }

        private void FillSelectedRandParts()
        {
            var randParts = _this._isRefitMode_5__2 ? _ship.shipType.randPartsRefit : _ship.shipType.randParts;
            _selectedRandParts.Clear();

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

                if (rp.paramx.TryGetValue("scheme", out var scheme))
                {
                    if (_ship.hull.hullInfo != null && !_ship.hull.hullInfo.schemes.Contains(scheme[0]))
                        continue;
                }
                if (!_ship.CheckOperations(rp))
                    continue;

                var rpo = new RandPartOperation();
                rpo._rp = rp;
                rpo._isNormal = true;
                if (rp.paramx.ContainsKey("delete_unmounted") || (_this._isRefitMode_5__2 && rp.paramx.ContainsKey("delete_refit")))
                {
                    rpo._isDelete = true;
                }
                else
                {
                    int num = Util.FromTo(rp.min, rp.max, _this.__8__1.rnd);
                    if (rp.paired)
                    {
                        num = 2 * Util.RoundToIntProb(num * 0.5f, _this.__8__1.rnd);
                    }
                    rpo._desiredNum = num;

                    // Special handling for towers/funnels.
                    // We put them in order main,sec,funnels
                    // and for towers, we combine multiple RPs
                    // of the same type into one with an expanded range.
                    if (HandleSpecialRP(rp, rpo))
                        continue;
                }
                rpo._isNormal = true;
                _selectedRandParts.Add(rpo);

                if (rp.type == "gun")
                    HandleGunRP(rpo);
            }
        }

        private GunCal GunCaliberFromRP(RandPart rp)
        {
            GunCal cal = GunCal.Main;
            if (rp.condition.Contains("main"))
                cal = GunCal.Main;
            else if (rp.condition.Contains("sec"))
                cal = GunCal.Sec;
            else if (rp.condition.Contains("ter"))
                cal = GunCal.Ter;
            else if (rp.effect.Contains("main") || rp.group.Contains("mg") || rp.group.Contains("mc") || rp.group.Contains("ms") || rp.group.Contains("main"))
                cal = GunCal.Main;
            else if (rp.effect.Contains("sec") || rp.group.Contains("sec") || rp.group.Contains("sc") || rp.group.Contains("sg"))
                cal = GunCal.Sec;
            else if (rp.effect.Contains("tert") || rp.group.Contains("ter") || rp.group.Contains("tg"))
                cal = GunCal.Ter;

            return cal;
        }

        private static readonly HashSet<int> _TempCalSet = new HashSet<int>();
        private static readonly List<GunInfo> _TempGunInfos = new List<GunInfo>();
        private void HandleGunRP(RandPartOperation rpo)
        {
            _TempCalSet.Clear();

            var rp = rpo._rp;
            var parts = _ship.GetParts(rp, _this.limitCaliber);
            var refType = GunRef.None;
            foreach (var p in parts)
            {
                if (_ship.badData.Contains(p))
                    continue;

                _TempCalSet.Add(Mathf.RoundToInt(p.caliber * (1f / 25.4f)));
                refType |= GunReferenceType(p, rp);
            }

            if (rp.group != string.Empty && _gunInfosByGroup.TryGetValue(rp.group, out var foundGI))
            {
                if (rp.center != rp.side)
                {
                    if (foundGI._align == (rp.side ? GunAlign.Center : GunAlign.Side))
                        foundGI._align = GunAlign.Both;
                }
                else
                {
                    if (foundGI._align != GunAlign.Both)
                        foundGI._align = GunAlign.Both;
                }

                // Do this in reverse so if we end up with no options,
                // we don't commit.
                _TempCalSet.IntersectWith(foundGI._calInchOptions);
                if (_TempCalSet.Count > 0)
                {
                    foundGI._reference |= refType;
                    foundGI._calInchOptions.Clear();
                    foreach (var c in _TempCalSet)
                        foundGI._calInchOptions.Add(c);
                }
                foundGI._rps.Add(rpo);
                _gunInfosByRPO[rpo] = foundGI;
                return;
            }

            GunCal cal = GunCaliberFromRP(rp);

            GunAlign align = rp.center == rp.side ? GunAlign.Both : (rp.center ? GunAlign.Center : GunAlign.Side);

            foreach (var gi in _gunInfos)
            {
                if (gi._cal == cal
                    && (_TempCalSet.SetEquals(gi._calInchOptions)))
                    //|| (gi._reference != GunRef.None && refType != GunRef.None)))
                {
                    if (gi._align != align)
                        gi._align = GunAlign.Both;
                    gi._reference |= refType;

                    gi._rps.Add(rpo);
                    _gunInfosByRPO[rpo] = gi;
                    if (rp.group != string.Empty)
                        _gunInfosByGroup[rp.group] = gi;
                    return;
                }
            }

            var newGI = new GunInfo();
            newGI._cal = cal;
            newGI._align = align;
            newGI._reference = refType;
            foreach (var c in _TempCalSet)
                newGI._calInchOptions.Add(c);
            newGI._rps.Add(rpo);
            _gunInfosByRPO[rpo] = newGI;
            if (rp.group != string.Empty)
                _gunInfosByGroup[rp.group] = newGI;
            _gunInfos.Add(newGI);
            ++_CalCounts[(int)newGI._cal];
        }

        private void SetupGunInfos()
        {
            for (int i = 0; i < _CalCounts.Length; ++i)
            {
                _CalCounts[i] = 0;
                _CalLimits[i] = 1;
                _CalMerge[i] = false;
            }

            switch (_sType)
            {
                case "tb":
                    _CalLimits[(int)GunCal.Main] = 2;
                    _CalLimits[(int)GunCal.Sec] = 0;
                    _CalLimits[(int)GunCal.Ter] = 0;
                    break;

                case "dd":
                    _CalLimits[(int)GunCal.Ter] = 0;
                    break;

                case "ca":
                    if (_hullYear < 1915)
                    {
                        // Armored cruiser. Generally:
                        // 2 main turrets, sidemounted semi-main, casemate sec, ATB guns
                        // The semi-main could either be main or sec.
                        if (_CalCounts[(int)GunCal.Main] > 1)
                        {
                            if (_CalCounts[(int)GunCal.Sec] <= 1)
                                _CalLimits[(int)GunCal.Main] = 2;
                        }
                        else if (_CalCounts[(int)GunCal.Sec] > 1)
                        {
                            if (_CalCounts[(int)GunCal.Main] == 1)
                                _CalLimits[(int)GunCal.Sec] = 2;
                        }
                    }
                    break;

                case "bb":
                    if (!_ship.hull.name.StartsWith("bb"))
                    {
                        _CalLimits[(int)GunCal.Sec] = 2;
                        if (_ship.TechVar("use_main_side_guns") == 0f && _gunInfosByGroup.ContainsKey("ms") && (_gunInfosByGroup.ContainsKey("mg") || _gunInfosByGroup.ContainsKey("mc")))
                            _CalLimits[(int)GunCal.Main] = 2;
                    }
                    break;
            }
        }

        private void MergeGunInfos()
        {
            foreach (var gi in _gunInfos)
            {
                int idx = (int)gi._cal;
                if (_CalCounts[idx] > _CalLimits[idx])
                    _CalMerge[idx] = true;
            }
            
            for (int i = 0; i < (int)GunCal.COUNT; ++i)
            {
                if (!_CalMerge[i])
                    continue;

                MergeCaliber((GunCal)i, _CalLimits[i]);
            }
        }

        private void MergeCaliber(GunCal cal, int limit)
        {
            if (limit == 0)
            {
                for (int i = _gunInfos.Count; i-- > 0;)
                {
                    var gi = _gunInfos[i];
                    if (gi._cal != cal)
                        continue;

                    foreach (var rpo in gi._rps)
                    {
                        _gunInfosByRPO.Remove(rpo);
                        // This is safe because, even if there are other
                        // guninfos of this group, since they all share
                        // their caliber, they will all be removed.
                        _gunInfosByGroup.Remove(rpo._rp.group);
                    }
                    _gunInfos.RemoveAt(i);
                }

                return;
            }

            int total = 0;
            int firstGunToRemove;
            for (firstGunToRemove = 0; firstGunToRemove < _gunInfos.Count; ++firstGunToRemove)
            {
                var gi = _gunInfos[firstGunToRemove];
                if (gi._cal != cal)
                    continue;

                if (total == limit)
                    break;

                ++total;
                _TempGunInfos.Add(gi);
            }
            --firstGunToRemove;

            for(int i = _gunInfos.Count - 1; i > firstGunToRemove; --i)
            {
                var gi = _gunInfos[i];
                _gunInfos.RemoveAt(i);

                GunInfo target = null;
                bool intersect = true;
                // We could see which intersection leaves a larger set
                // but at most there will be 2 of these, so let's just
                // shuffle.
                _TempGunInfos.Shuffle();
                foreach (var ogi in _TempGunInfos)
                {
                    _TempCalSet.Clear();
                    foreach (var p in gi._calInchOptions)
                        _TempCalSet.Add(p);

                    _TempCalSet.IntersectWith(ogi._calInchOptions);
                    if (_TempCalSet.Count > 0)
                    {
                        target = ogi;
                        break;
                    }
                }
                if (target == null)
                {
                    intersect = false;
                    target = _TempGunInfos.Random(null, _this.__8__1.rnd);
                }
                if (intersect)
                {
                    target._calInchOptions.IntersectWith(gi._calInchOptions);
                }
                // FIXME: what to do in the else case?
                // For now, just drop this set on the floor and use the
                // original set.

                if (target._align != gi._align)
                    target._align = GunAlign.Both;
                target._reference |= gi._reference;


                foreach (var rpo in gi._rps)
                {
                    target._rps.Add(rpo);
                    _gunInfosByRPO[rpo] = target;
                    if (rpo._rp.group != string.Empty)
                        _gunInfosByGroup[rpo._rp.group] = target;
                }
            }
            _TempGunInfos.Clear();
        }

        private GunRef GunReferenceType(PartData data, RandPart rp)
        {
            var ret = GunRef.None;
            if (rp.group == "mc" || data.paramx.ContainsKey("main_center") || data.paramx.ContainsKey("small_center"))
                ret |= GunRef.Center;
            if (rp.group == "ms" || data.paramx.ContainsKey("main_side") || data.paramx.ContainsKey("small_side"))
                ret |= GunRef.Side;

            return ret;
        }

        public bool SelectParts()
        {
            SetupGunInfos();
            FillSelectedRandParts();
            MergeGunInfos();

            float nonHullTonnage = _ship.Tonnage() - _baseHullWeight;
            float armRatio = ShipStats.GetArmamentRatio(_sType, _avgYear);
            float prePartsWeight = _ship.Weight();

            // We need to _actually_ mount the towers/funnels
            // to know the weight.
            foreach (var rpo in _selectedRandParts)
            {
                if (rpo._isNormal)
                    break;

                if (!TryAddPartsForRandPart(rpo._rp, rpo._desiredNum))
                    return false;
            }
            if (!_hasFunnel || !_mainPlaced || (_needSec && !_secPlaced))
                return false;

            float weightDelta = _ship.Weight() - prePartsWeight; // weight of towers/funnels
            nonHullTonnage -= weightDelta; // nominal payload tonnage
            float armamentTonnage = nonHullTonnage * armRatio;
            armamentTonnage -= (_totalHullWeight - _baseHullWeight); // mines/DC/sweeping counts as armament
            if (armamentTonnage < 0f)
                armamentTonnage = 0.05f * (_ship.Tonnage() - _ship.Weight());
            if (armamentTonnage < 0f)
                return true;

            _armStopWeight = _ship.Weight() + armamentTonnage;

            List<Part> addedParts = new List<Part>();
            for (int pass = 0; pass < 50; ++pass)
            {
                foreach (var rpo in _selectedRandParts)
                {
                    var rp = rpo._rp;

                    if (rpo._isDelete)
                    {
                        if (_this._isRefitMode_5__2)
                            _ship.RemoveDeleteRefitPartsNew(rp.type, _this.isSimpleRefit);
                        else
                            _ship.DeleteUnmounted(rp.type);
                        continue;
                    }

                    if (rp.type == "gun")
                    {
                        var gi = _gunInfosByRPO[rpo];

                    }


                    if (rp.group != string.Empty)
                        _partDataForGroup[rp.group] = rpo._data;

                    // Store the ref away so we can keep main and side gun calibers in sync
                    var refType = GunReferenceType(rpo._data, rp);
                    if ((refType & GunRef.Center) != 0)
                        _gunCenterRefs.Add(rpo._data);
                    if ((refType & GunRef.Side) != 0)
                        _gunSideRefs.Add(rpo._data);
                }
            }

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

                ShipM.ClearMatCache(_ship);
            }

            _ship.StartCoroutine(_ship.RefreshDecorDelay());

            foreach (var part in _ship.parts)
                part.OnPostAdd();

            return true;
        }

        private void ResetPassState(bool countTowersFunnels = true)
        {
            _gunSideRefs.Clear();
            _gunCenterRefs.Clear();
            _partDataForGroup.Clear();
            _firstPairCreated = null;
            if (countTowersFunnels)
            {
                _mainPlaced = false;
                _secPlaced = false;
                _hasFunnel = false;
            }
        }

        public bool SelectPartsStock()
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

                ShipM.ClearMatCache(_ship);
            }

            _ship.StartCoroutine(_ship.RefreshDecorDelay());

            foreach (var part in _ship.parts)
                part.OnPostAdd();

            return true;
        }

        private bool TryAddPartsForRandPart(RandPart rp, int desiredAmount)
        {
            var parts = _ship.GetParts(rp, _this.limitCaliber);
            for (int loop = 0; loop < 250; ++loop)
            {
                PartData data = null;
                Part part = null;

                data = FindPartDataForRP(rp, parts);
                if (_ship.badData.Contains(data))
                {
                    // We could be adding main guns and gone over-limit
                    // with the last one. Or other reasons why this part
                    // might have started out good but ended up bad.
                    break;
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

                if (!PlacePart(part, rp))
                {
                    MarkBadTry(part, data);
                    continue;
                }

                float weight = _ship.Weight(true);
                if (weight > _ship.Tonnage() * 1.4f && (data.isWeapon || data.isBarbette))
                {
                    // This part will never work, it takes us over limit.
                    _ship.badData.Add(data);
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
                    _partDataForGroup[rp.group] = data;
                }

                if (data.isGun)
                {
                    _ship.AddShipTurretArmor(part);

                    // Store the ref away so we can keep main and side gun calibers in sync
                    var refType = GunReferenceType(data, rp);
                    if ((refType & GunRef.Center) != 0)
                        _gunCenterRefs.Add(data);
                    if ((refType & GunRef.Side) != 0)
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
                }
                _ship.RemovePart(part);
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

        private PartData FindPartDataForRP(RandPart rp, Il2CppSystem.Collections.Generic.List<PartData> parts)
        {
            if (rp.group != string.Empty && _partDataForGroup.TryGetValue(rp.group, out var gpart) && gpart != null)
                return gpart;

            if (rp.paired && _firstPairCreated != null)
                return _firstPairCreated.data;

            PartData ret = null;
            foreach (var data in parts)
            {
                if (_ship.badData.Contains(data) || data.type != rp.type)
                    continue;

                _Options.Add(data);
            }

            if (_Options.Count == 0)
                return null;

            if (rp.type == "torpedo")
            {
                float bestVal = float.MinValue;
                foreach (var opt in _Options)
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
                    for (int i = _Options.Count; i-- > 0;)
                    {
                        var opt = _Options[i];

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
                            _Options.RemoveAt(i);
                    }
                }
                // Game is weird here. If "sec_cal" is not there, it jumps to maincal.
                // But if "sec_cal" _is_ there, it still goes to maincal unless "ter_cal"
                // is _also_ specified.
                if (rp.condition.Contains("ter_cal"))
                {
                    float bestVal = float.MinValue;
                    foreach (var opt in _Options)
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
                    foreach (var opt in _Options)
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
            else if (_Options.Count > 0)
            {
                if (rp.type != "tower_main" && rp.type != "tower_sec")
                {
                    ret = _Options.Random(null, _this.__8__1.rnd);
                }
                else
                {
                    // try to do something smarter than random
                    float maxCost = 0f;
                    float minTon = float.MaxValue;
                    float maxTon = float.MinValue;
                    foreach (var o in _Options)
                    {
                        if (o.cost > maxCost)
                            maxCost = o.cost;
                        if (minTon > o.weight)
                            minTon = o.weight;
                        if (maxTon < o.weight)
                            maxTon = o.weight;
                    }
                    float costMult = Mathf.Pow(10f, -Mathf.Floor(Mathf.Log10(maxCost)));
                    float tonMult = 1f - minTon / maxTon;
                    foreach (var o in _Options)
                    {
                        float cost = o.cost * costMult;
                        float year = Database.GetYear(o);
                        if (year > 0f)
                            cost *= Mathf.Pow(2f, (year - 1890f) * 0.5f);

                        // Try to weight based on where ship is within the tonnage range
                        cost *= 1.2f - Mathf.Abs(Mathf.InverseLerp(minTon, maxTon, o.weight) - _tngRatio) * tonMult;

                        _OptionWeights[o] = cost;
                    }
                    ret = ModUtils.RandomByWeights(_OptionWeights, null, _this.__8__1.rnd);
                    _OptionWeights.Clear();
                }
            }
            _Options.Clear();
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
            if (_avoidBarrels.Contains(data.barrels))
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
            if (_avoidBarrels.Contains(data.barrels))
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

        public void AddArmorToLimit(float maxWeight = -1f)
        {
            if (maxWeight < 0f)
                maxWeight = _ship.Tonnage();

            if (_ship.Weight() >= maxWeight)
                return;

            float deckPortion = Util.Remap(_hullYear, 1890f, 1930f, 0.15f, 0.67f, true);
            deckPortion *= 1f + ModUtils.DistributedRange(0.1f, 4, null, _this.__8__1.rnd);
            _ArmorMultipliers[Ship.A.Deck] = deckPortion;
            _ArmorMultipliers[Ship.A.DeckBow] = _ArmorMultipliers[Ship.A.DeckStern] = deckPortion * (0.5f + ModUtils.DistributedRange(0.1f, 2, null, _this.__8__1.rnd));
            _ArmorMultipliers[Ship.A.Belt] = 1f;
            _ArmorMultipliers[Ship.A.BeltBow] = _ArmorMultipliers[Ship.A.BeltStern] = 0.5f + ModUtils.DistributedRange(0.1f, 2, null, _this.__8__1.rnd);
            _ArmorMultipliers[Ship.A.Barbette] = 1f + ModUtils.DistributedRange(0.2f, 4, null, _this.__8__1.rnd);
            _ArmorMultipliers[Ship.A.TurretSide] = 1f + Mathf.Abs(ModUtils.DistributedRange(0.5f, 2, null, _this.__8__1.rnd));
            _ArmorMultipliers[Ship.A.TurretTop] = _ArmorMultipliers[Ship.A.TurretSide] * deckPortion * (0.8f + ModUtils.DistributedRange(0.125f, 2, null, _this.__8__1.rnd));
            _ArmorMultipliers[Ship.A.ConningTower] = 1f + ModUtils.DistributedRange(0.2f, 3, null, _this.__8__1.rnd);
            _ArmorMultipliers[Ship.A.Superstructure] = Math.Max(_ArmorMultipliers[Ship.A.Belt], _ArmorMultipliers[Ship.A.ConningTower]) * (0.375f + ModUtils.DistributedRange(0.125f, 2, null, _this.__8__1.rnd));
            float maxMult = 0f;
            foreach (var m in _ArmorMultipliers.Values)
                if (maxMult < m)
                    maxMult = m;
            _ArmorMultipliers[Ship.A.InnerBelt_1st] = _ArmorMultipliers[Ship.A.InnerBelt_2nd] = _ArmorMultipliers[Ship.A.InnerBelt_3rd]
                = _ArmorMultipliers[Ship.A.InnerDeck_1st] = _ArmorMultipliers[Ship.A.InnerDeck_2nd] = _ArmorMultipliers[Ship.A.InnerDeck_3rd] = 99f;

            float armorVal = _ship.shipType.armor * 25.4f;
            for (; _ship.Weight() <= maxWeight; armorVal += G.settings.armorStep)
                SetArmorValues(armorVal);
            // we overshoot by definition, so pull back one step.
            SetArmorValues(armorVal - G.settings.armorStep);
        }

        private bool SetArmorValues(float value)
        {
            bool shouldAbort = true;
            for (Ship.A a = Ship.A.Belt; a <= Ship.A.InnerDeck_3rd; a = (Ship.A)((int)a + 1))
            {
                float val = G.settings.RoundToArmorStep(Math.Min(ShipM.MaxArmorForZone(_ship, a, null), value * _ArmorMultipliers[a]));
                _ship.armor.TryGetValue(a, out var oldVal);
                if (val != oldVal)
                {
                    _ship.armor[a] = val;
                    shouldAbort = false;
                }
            }
            foreach (var ta in _ship.shipTurretArmor)
            {
                float armLimitSideBarb = float.MaxValue;
                float armLimitTop = float.MaxValue;
                if (!Ship.IsMainCal(ta.turretPartData, _ship.shipType))
                {
                    var tc = ShipM.FindMatchingTurretCaliber(_ship, ta.turretPartData);
                    float mmCal = ta.turretPartData.caliber;
                    if (tc != null)
                        mmCal += tc.diameter;
                    armLimitSideBarb = mmCal;
                    armLimitTop = _ArmorMultipliers[Ship.A.TurretTop] / _ArmorMultipliers[Ship.A.TurretSide] * mmCal;
                }
                float val = G.settings.RoundToArmorStep(Math.Min(ShipM.MaxArmorForZone(_ship, Ship.A.TurretTop, ta.turretPartData),
                    Math.Min(armLimitTop, value * (ta.isCasemateGun ? _ArmorMultipliers[Ship.A.Deck] : _ArmorMultipliers[Ship.A.TurretTop]))));
                if (ta.topTurretArmor != val)
                {
                    ta.topTurretArmor = val;
                    shouldAbort = false;
                }

                val = G.settings.RoundToArmorStep(Math.Min(ShipM.MaxArmorForZone(_ship, Ship.A.TurretSide, ta.turretPartData),
                    Math.Min(armLimitSideBarb, value * (ta.isCasemateGun ? _ArmorMultipliers[Ship.A.Belt] * 0.5f : _ArmorMultipliers[Ship.A.TurretSide]))));
                if (ta.sideTurretArmor != val)
                {
                    ta.sideTurretArmor = val;
                    shouldAbort = false;
                }
                
                val = G.settings.RoundToArmorStep(Math.Min(ShipM.MaxArmorForZone(_ship, Ship.A.Barbette, ta.turretPartData),
                    Math.Min(armLimitSideBarb, value * _ArmorMultipliers[Ship.A.Barbette])));
                if (ta.barbetteArmor != val)
                {
                    ta.barbetteArmor = val;
                    shouldAbort = false;
                }
            }

            if (shouldAbort)
                return false;

            _ship.SetArmor(_ship.armor);
            _ship.RefreshHullStats();
            _ship.RefreshGunsStats();
            return true;
        }
    }
}