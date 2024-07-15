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
    public class RandPartInfo : Serializer.IPostProcess
    {
        public enum Alignment
        {
            Center,
            Side,
            Both
        }

        public enum Battery
        {
            Main,
            Sec,
            Ter,
        }

        public enum MountPref
        {
            Any,
            CasemateOnly,
            CasematePref,
            DeckPref,
            DeckOnly,
            TowerPref,
            TowerOnly,
            TowerDeck
        }

        // lower-case so we can ToString() to
        // convert to stock's string-based style
        public enum RPType
        {
            tower_main,
            tower_sec,
            funnel,
            gun,
            torpedo,
            barbette,
            special,
        }

        public enum CalVary
        {
            Random,
            Local,
            Absolute,
        }

        public enum HullRP
        {
            TowerMain,
            TowerSec,
            Funnel
        }

        public enum TowerMountType
        {
            Casemate,
            Regular,
            Both,
            None
        }

        private static bool _StaticsInit = false;
        private static int _Next = 0;
        private static readonly Dictionary<int, List<RandPartInfo>> _Lookup = new Dictionary<int, List<RandPartInfo>>();
        private static readonly List<RandPartInfo> _TempList = new List<RandPartInfo>();
        private static int _LastHash = -1;
        private static readonly List<PartData> _TempParts = new List<PartData>();

        private int order = -1;
        [Serializer.Field] public string name;
        [Serializer.Field] public bool refit = false;
        [Serializer.Field] private string shipTypes;
        [Serializer.Field] private string nations;
        [Serializer.Field] public RPType type;
        [Serializer.Field] public bool required = true;
        [Serializer.Field] public int chance = 100;
        [Serializer.Field] public int countMin;
        [Serializer.Field] public int countMax;
        [Serializer.Field] public Alignment align = Alignment.Center;
        [Serializer.Field] public Vector2 x;
        [Serializer.Field] public Vector2 z;
        [Serializer.Field] public string groupPart;
        [Serializer.Field] public string groupGun;
        [Serializer.Field] public string effect;
        [Serializer.Field] public float calMin = 0;
        [Serializer.Field] public float calMax = 20.99f;
        [Serializer.Field] public CalVary calVariance = CalVary.Random;
        [Serializer.Field] public Battery battery = Battery.Main;
        [Serializer.Field] public int barrelMin = 0;
        [Serializer.Field] public int barrelMax = 99;
        [Serializer.Field] public MountPref mountPref = MountPref.Any;
        [Serializer.Field] private string param;
        private Dictionary<string, List<string>> paramx;
        public List<string> demandMounts = new List<string>();
        public List<string> excludeMounts = new List<string>();
        public List<KeyValuePair<ShipM.RPOperation, string>> orOps;
        public List<KeyValuePair<ShipM.RPOperation, string>> andOps;
        public bool deleteUnmounted = false;
        public bool deleteRefit = false;
        public bool tr_rand_mod = false;

        public void PostProcess()
        {
            if (order < 0)
                order = _Next++;

            if (calMax < 21)
                calMax *= 25.4f;
            if (calMin < 21)
                calMin *= 25.4f;

            paramx = ModUtils.HumanModToDictionary1D(param);
            if (paramx.TryGetValue("mount", out var dM))
                demandMounts = dM;
            if (paramx.TryGetValue("!mount", out var xM))
                excludeMounts = xM;
            if (paramx.ContainsKey("delete_unmounted"))
                deleteUnmounted = true;
            if (paramx.ContainsKey("delete_refit"))
                deleteRefit = true;
            if (paramx.ContainsKey("tr_rand_mod"))
                tr_rand_mod = true;

            EnsureStatics();

            int refitHash = refit ? 1 : 0;
            var stSplit = shipTypes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nSplit = nations.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (stSplit.Length > 0)
            {
                if (nSplit.Length > 0)
                {
                    foreach (var s in stSplit)
                        foreach (var n in nSplit)
                            Add(Key(s, n) ^ refitHash);
                }
                else
                {
                    foreach (var s in stSplit)
                        Add(s.GetHashCode() ^ refitHash);
                }
            }
            else if (nSplit.Length > 0)
            {
                foreach (var n in nSplit)
                    Add(n.GetHashCode() ^ refitHash);
            }
            else
            {
                Add(refitHash);
            }

            if (paramx.TryGetValue("or", out var ors))
                orOps = ShipM.CheckOperationsGetArgs(ors);
            if (paramx.TryGetValue("and", out var ands))
                andOps = ShipM.CheckOperationsGetArgs(ands);
        }

        private void Add(int key)
        {
            _Lookup.GetValueOrNew(key, out var list);
            list.Add(this);
        }

        public bool Check(Ship ship)
        {
            if (paramx.TryGetValue("scheme", out var scheme))
            {
                if (ship.hull.hullInfo != null && !ship.hull.hullInfo.schemes.Contains(scheme[0]))
                    return false;
            }

            bool ok = orOps.Count == 0;
            foreach (var op in orOps)
            {
                if (ShipM.CheckOperationsProcess(ship.hull.data, op.Key, op.Value, ship))
                {
                    ok = true;
                    break;
                }
            }
            if (!ok || andOps.Count == 0)
                return ok;

            foreach (var op in andOps)
            {
                if (!ShipM.CheckOperationsProcess(ship.hull.data, op.Key, op.Value, ship))
                {
                    ok = false;
                    break;
                }
            }
            return ok;
        }

        public List<PartData> GetParts(Ship ship)
        {
            _TempParts.Clear();
            string typeStr = type.ToString();
            foreach (var data in G.GameData.parts.Values)
            {
                if (data.type != typeStr)
                    continue;
                if (effect != string.Empty && !data.paramx.ContainsKey(effect))
                    continue;

                if (type == RPType.torpedo)
                {
                    if (data.barrels < barrelMin)
                        continue;
                    if (data.barrels > barrelMax)
                        continue;
                }

                if (!ship.IsPartAvailable(data))
                    continue;

                _TempParts.Add(data);
            }

            return _TempParts;
        }

        public List<PartData> GetPartsForGunInfo(Ship ship, GunDatabase.GunInfo gi)
        {
            _TempParts.Clear();
            TowerMountType tmt = TowerMountType.None;
            foreach (var p in ship.parts)
            {
                if (p.data.isTowerAny || p.data.isFunnel)
                {
                    foreach (var m in p.mountsInside)
                    {
                        if (m.employedPart != null)
                            continue;
                        if (m.caliberMin >= 0 && m.caliberMax > 0)
                        {
                            if (m.caliberMax * 25.4f < calMin || m.caliberMin * 25.4f > calMax)
                                continue;
                        }
                        if (m.barrelsMin >= 0 && m.barrelsMax > 0)
                        {
                            if (m.barrelsMax < barrelMin || m.barrelsMin > barrelMax)
                                continue;
                        }

                        if (m.casemate && tmt != TowerMountType.Casemate)
                            tmt = tmt == TowerMountType.None ? TowerMountType.Casemate : TowerMountType.Both;
                        if ((m.center || m.side) && tmt != TowerMountType.Regular)
                            tmt = tmt == TowerMountType.None ? TowerMountType.Regular : TowerMountType.Both;
                    }
                }
                if (tmt == TowerMountType.Both)
                    break;
            }

            if (mountPref == MountPref.TowerOnly && tmt == TowerMountType.None)
                return _TempParts;

            foreach (var data in G.GameData.parts.Values)
            {
                if (data.type != "gun")
                    continue;
                if (effect != string.Empty && !data.paramx.ContainsKey(effect))
                    continue;

                if (data.barrels < barrelMin)
                    continue;
                if (data.barrels > barrelMax)
                    continue;

                // could probably cast here but I don't trust it
                if (gi._calInch != Mathf.RoundToInt(data.GetCaliberInch()))
                    continue;

                switch (mountPref)
                {
                    case MountPref.TowerOnly:
                        if (tmt == TowerMountType.Regular)
                        {
                            if (!Ship.IsNotPureCasemateGun(data, ship.shipType))
                                continue;
                        }
                        else if (tmt == TowerMountType.Casemate && !Ship.IsCasemateGun(data))
                        {
                            continue;
                        }
                        break;
                    case MountPref.DeckOnly:
                        if (!Ship.IsNotPureCasemateGun(data, ship.shipType))
                            continue;
                        break;
                    case MountPref.TowerDeck:
                        if ((tmt == TowerMountType.None || tmt == TowerMountType.Regular) && !Ship.IsNotPureCasemateGun(data, shipType))
                            continue;
                        break;

                    case MountPref.CasemateOnly:
                        if (!Ship.IsCasemateGun(data))
                            continue;
                        break;
                }
                                
                if (!ship.IsPartAvailable(data))
                    continue;

                _TempParts.Add(data);
            }

            return _TempParts;
        }

        private static void EnsureStatics()
        {
            if (_StaticsInit)
                return;
            _StaticsInit = true;

            foreach (var st in G.GameData.shipTypes.Values)
                foreach (var n in G.GameData.playersMajor.Values)
                    _Lookup[Key(st.name, n.name)] = new List<RandPartInfo>();
        }

        private static int Key(string st, string nation)
            => st.GetHashCode() ^ nation.GetHashCode();

        private static int Key(Ship ship)
            => Key(ship.shipType.name, ship.player.data.name);

        public static List<RandPartInfo> GetRPIs(Ship ship, bool isRefit)
        {
            int refitHash = isRefit ? 1 : 0;
            var key = Key(ship) ^ refitHash;
            if (_LastHash == key)
                return _TempList;

            _LastHash = key;

            _TempList.Clear();
            if (_Lookup.TryGetValue(key, out var list))
                _TempList.AddRange(list);
            if (_Lookup.TryGetValue(ship.shipType.name.GetHashCode() ^ refitHash, out list))
                _TempList.AddRange(list);
            if (_Lookup.TryGetValue(ship.player.data.name.GetHashCode() ^ refitHash, out list))
                _TempList.AddRange(list);
            if (_Lookup.TryGetValue(refitHash, out list))
                _TempList.AddRange(list);

            _TempList.Sort((a, b) => a.order.CompareTo(b.order));

            return _TempList;
        }
    }

    public class GenerateShip
    {
        static Dictionary<ComponentData, float> _CompWeights = new Dictionary<ComponentData, float>();
        static List<PartData> _Options = new List<PartData>();
        static Dictionary<PartData, float> _OptionWeights = new Dictionary<PartData, float>();

        float _hullYear;
        float _designYear;
        float _avgYear;
        string _sType;
        string _nation;
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
        Part _firstPairCreated = null;
        float _offsetX;
        float _offsetZ;
        Dictionary<string, PartData> _partDataForGroup = new Dictionary<string, PartData>();
        Dictionary<string, GunDatabase.GunInfo> _gunDataForGroup = new Dictionary<string, GunDatabase.GunInfo>();
        HashSet<int> _avoidBarrels = new HashSet<int>();
        Dictionary<Ship.A, float> _ArmorMultipliers = new Dictionary<Ship.A, float>();
        bool _partsInitOK = false;

        HashSet<string> _seenCompTypes = new HashSet<string>();

        int[] _gunGrades = new int[21];

        float _tonnage;
        float _baseHullWeight;
        float _prePartsWeight;
        float _armRatio;
        float _prePartsFreeTngNoPayload;
        float _hullPartsTonnage = 0;

        float _payloadBaseWeight;
        float _maxPayloadWeight;
        float _payloadStopWeight;


        List<RandPartInfo> _randParts;

        List<RandPartUsage> _rpAll = new List<RandPartUsage>();
        List<RandPartUsage>[] _rpHull = new List<RandPartUsage>[(int)RandPartInfo.HullRP.Funnel + 1];
        List<RandPartUsage>[] _rpByBattery = new List<RandPartUsage>[(int)RandPartInfo.Battery.Ter + 1];
        List<RandPartUsage> _rpTorps = new List<RandPartUsage>();
        Dictionary<Part, RandPartUsage> _partToRP = new Dictionary<Part, RandPartUsage>();

        int _lastFunnelIdxReq = -1;
        int _lastFunnelIdxOpt = -1;
        int _lastPayloadIdxReq = -1;
        int _lastHullIdxReq = -1;
        int _lastAnyIdxReq = -1;

        Dictionary<GunDatabase.GunInfo, int> _badGunTries = new Dictionary<GunDatabase.GunInfo, int>();
        Dictionary<PartData, int> _badPartTries = new Dictionary<PartData, int>();
        Dictionary<PartData, Dictionary<Mount, int>> _badMountTries = new Dictionary<PartData, Dictionary<Mount, int>>();

        HashSet<PartData> _passBadParts = new HashSet<PartData>();
        Dictionary<PartData, HashSet<Mount>> _passBadMounts = new Dictionary<PartData, HashSet<Mount>>();

        private class RandPartUsage
        {
            public RandPartInfo _rp;
            public List<Part> _parts = new List<Part>();
            public List<PartData> _datas;
            public GunDatabase.GunInfo _gun;
            public int desired;

            public Dictionary<GunDatabase.GunInfo, int> _badGunTries = new Dictionary<GunDatabase.GunInfo, int>();
            public Dictionary<PartData, int> _badPartTries = new Dictionary<PartData, int>();
            public Dictionary<PartData, Dictionary<Mount, int>> _badMountTries = new Dictionary<PartData, Dictionary<Mount, int>>();

            public HashSet<PartData> _passBadParts = new HashSet<PartData>();
            public Dictionary<PartData, HashSet<Mount>> _passBadMounts = new Dictionary<PartData, HashSet<Mount>>();

            public void Reset()
            {
                _passBadParts.Clear();
                _passBadMounts.Clear();
            }
        }

        public GenerateShip(Ship ship)
        {
            _ship = ship;
            _hullYear = Database.GetYear(ship.hull.data);
            _designYear = ship.GetYear(ship);
            _avgYear = (_hullYear + _designYear) * 0.5f;
            _sType = ship.shipType.name;
            _nation = ship.player.data.name;
            _hData = ShipStats.GetData(ship);
            _tngLimit = ship.player.TonnageLimit(ship.shipType);
            _isLight = _sType == "dd" || _sType == "tb";
            _gen = ship.hull.data.Generation;
            _isMissionMainShip = GameManager.IsMission && _ship.player.isMain && BattleManager.Instance.MissionMainShip == _ship;

            for (int i = _rpByBattery.Length; i-- > 0;)
                _rpByBattery[i] = new List<RandPartUsage>();
            for (int i = _rpHull.Length; i-- > 0;)
                _rpHull[i] = new List<RandPartUsage>();

            if (_ship.shipType.paramx.TryGetValue("avoid_barrels", out var avoidB))
            {
                foreach (var s in avoidB)
                    _avoidBarrels.Add(int.Parse(s));
            }
            _armRatio = ShipStats.GetArmamentRatio(_sType, _avgYear);
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
            _tonnage = _ship.Tonnage();
            _tngRatio = Mathf.InverseLerp(_ship.TonnageMin(), Math.Min(_ship.TonnageMax(), _tngLimit), _tonnage);
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

            // FIXME need to handle reload _before_ selecting parts, but
            // it doesn't show up until guns are placed? Same for torp type.

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
                _prePartsWeight = _ship.Weight();

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

            _randParts = RandPartInfo.GetRPIs(_ship, _this._isRefitMode_5__2);
            if (_randParts.Count == 0)
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
                foreach (var p in G.GameData.parts.Values)
                {
                    if (_ship.BelongsToCategory(p, secPCD) && _ship.IsPartAvailable(p))
                    {
                        _needSec = true;
                        break;
                    }
                }
            }
            _hasFunnel = false;

            return true;
        }

        private bool CheckSetMaxPayloadWeight()
        {
            float payloadAndArmorTng = _prePartsFreeTngNoPayload - _hullPartsTonnage;
            if (_payloadBaseWeight > payloadAndArmorTng)
                return false;

            _maxPayloadWeight = payloadAndArmorTng * _armRatio;
            _maxPayloadWeight -= _payloadBaseWeight;

            if (_maxPayloadWeight < 0)
                _maxPayloadWeight = 0.05f * (payloadAndArmorTng - _payloadBaseWeight);
            if (_maxPayloadWeight < 0)
                return false;

            _payloadStopWeight = Math.Min(_maxPayloadWeight * 0.995f, _maxPayloadWeight - 1f);

            return true;
        }

        private void SetupGunInfo()
        {
            GunDatabase.TechGunGrades(_ship, _gunGrades);
            float agTert = _nation == "austria" ? 47 : 88;
            int agTert2 = _nation == "austria" ? 66 : 88;
            switch (_sType)
            {
                case "tb":
                    if (_hullYear > 1891)
                    {
                        switch (_nation)
                        {
                            case "britain":
                            case "usa":
                            case "spain":
                            case "russia":
                                int metrMain = _nation == "spain" || _nation == "russia" ? 75 : 76;
                                _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, metrMain, metrMain), new CalInfo(GunCal.Main, 57, 57) };
                                break;

                            case "france":
                            case "austria":
                                int frMain = _nation == "france" ? 65 : 66;
                                _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, frMain, frMain), new CalInfo(GunCal.Main, 47, 47) };
                                break;

                            case "germany":
                                _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 50, 88), new CalInfo(GunCal.Main, 50, 55, false) };
                                break;

                            default:
                                _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 40, 88), new CalInfo(GunCal.Main, 30, 50, false) };
                                break;
                        }
                    }
                    else
                    {
                        _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 40, 57), new CalInfo(GunCal.Main, 30, 47, false) };
                    }
                    break;

                case "dd":_calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 75, 140), new CalInfo(GunCal.Sec, 37, 57) }; break;

                case "ca":
                    if (_hullYear < 1905)
                    {
                        // Traditional armored cruiser or first-class protected cruiser
                        switch (_nation)
                        {
                            case "france":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 190, 195),
                                    new CalInfo(GunCal.Sec, 137, 165),
                                    new CalInfo(GunCal.Ter, 75, 75),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;

                            case "usa":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 8, 10),
                                    new CalInfo(GunCal.Sec, 4, 6),
                                    new CalInfo(GunCal.Ter, 57, 77),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;

                            case "japan":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 8, 10),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 3, 3),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;

                            case "russia":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 8, 8),
                                    new CalInfo(GunCal.Sec, 4, 6),
                                    new CalInfo(GunCal.Ter, 75, 75),
                                    new CalInfo(GunCal.Ter, 37, 47) };
                                break;

                            case "austria":
                            case "germany":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 210, 240),
                                    new CalInfo(GunCal.Sec, 150, 150),
                                    new CalInfo(GunCal.Ter, agTert, agTert) };
                                break;

                            case "italy":
                            case "spain":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 10, 11),
                                    new CalInfo(GunCal.Sec, 5, 7),
                                    new CalInfo(GunCal.Sec, 3, 3),
                                    new CalInfo(GunCal.Ter, 40, 65) };
                                break;

                            case "britain":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 9.2f, 9.2f),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                break;

                            default:
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 8, 10),
                                    new CalInfo(GunCal.Sec, 5, 6),
                                    new CalInfo(GunCal.Ter, 47, 88) };
                                break;
                        }
                    }
                    else if (_hullYear < 1917)
                    {
                        // Late armored cruiser.
                        // These will either have unified main armament
                        // (Blucher) or battleship-class main guns with
                        // heavy main-class "secondaries" (Ibuki)
                        switch (_nation)
                        {
                            case "france":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 190, 195),
                                    new CalInfo(GunCal.Ter, 75, 75) };
                                break;

                            case "russia":
                            case "usa":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 10, 11),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 3, 3),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;

                            case "japan":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 12),
                                    new CalInfo(GunCal.Main, 8, 8),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                break;

                            case "austria":
                            case "germany":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 200, 210),
                                    new CalInfo(GunCal.Sec, 150, 150),
                                    new CalInfo(GunCal.Ter, agTert, agTert) };
                                break;

                            case "italy":
                            case "spain":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 10, 11),
                                    new CalInfo(GunCal.Sec, 6, 7),
                                    new CalInfo(GunCal.Sec, 3, 3) };
                                break;

                            default:
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 9.2f, 9.2f),
                                    new CalInfo(GunCal.Sec, 7.5f, 7.5f),
                                    new CalInfo(GunCal.Ter, 57, 57) };
                                break;
                        }
                    }
                    else
                    {
                        // Treaty cruiser
                        _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 8, 8),
                                    new CalInfo(GunCal.Sec, 4, 5.5f),
                                    new CalInfo(GunCal.Ter, 40, 88) };
                    }
                    break;

                case "cl":
                    if (_hullYear < 1916)
                    {
                        _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 95, 110),
                                    new CalInfo(GunCal.Ter, 37, 88) };
                    }
                    else if (_hullYear < 1919)
                    {
                        _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 5, 6),
                                    new CalInfo(GunCal.Sec, 3, 4) };
                    }
                    else
                    {
                        // Treaty light cruiser
                        _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 120, 160),
                                    new CalInfo(GunCal.Sec, 4, 5),
                                    new CalInfo(GunCal.Ter, 40, 3) };
                    }
                    break;

                case "bc":
                case "bb":
                    // predreads
                    if (_sType != "bc" && !_ship.hull.name.StartsWith("bb"))
                    {
                        switch (_nation)
                        {
                            case "usa":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 13),
                                    new CalInfo(GunCal.Sec, 6, 8),
                                    new CalInfo(GunCal.Ter, 3, 3),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;

                            case "france":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 300, 13),
                                    new CalInfo(GunCal.Main, 270, 280, false),
                                    new CalInfo(GunCal.Sec, 137, 195),
                                    new CalInfo(GunCal.Ter, 65, 100),
                                    new CalInfo(GunCal.Ter, 47, 47) };
                                break;

                            case "austria":
                            case "germany":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 240, 290),
                                    new CalInfo(GunCal.Sec, 150, 150),
                                    new CalInfo(GunCal.Ter, agTert, agTert) };
                                break;

                            case "russia":
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 10, 12),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 75, 75),
                                    new CalInfo(GunCal.Ter, 47, 47) };
                                break;

                            default:
                                _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 11, 13.5f),
                                    new CalInfo(GunCal.Sec, _nation == "britain" ? 6 : 5, 6),
                                    new CalInfo(GunCal.Ter, 3, 3),
                                    new CalInfo(GunCal.Ter, 47, 57) };
                                break;
                        }

                        // semi-dreads
                        if (_ship.TechVar("use_main_side_guns") != 0f)
                        {
                            if (_nation == "russia")
                            {
                                _calInfos.RemoveAt(3);
                                _calInfos.Insert(1, new CalInfo(GunCal.Sec, 8, 8));
                            }
                            else
                            {
                                if (_calInfos[1]._cal == GunCal.Main)
                                {
                                    _calInfos.RemoveAt(1);
                                }
                                foreach (var ci in _calInfos)
                                {
                                    if (ci._cal == GunCal.Sec && ci._max < 8 * 25.4f)
                                    {
                                        ci._cal = GunCal.Main;
                                        ci._min = 230f;
                                        ci._max = 270f;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    // Dreadnoughts / Battlecruisers
                    else
                    {
                        if (_avgYear < 1919)
                        {
                            switch (_nation)
                            {
                                case "france":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 16),
                                    new CalInfo(GunCal.Sec, 127, 140) };
                                    break;

                                case "usa":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 16),
                                    new CalInfo(GunCal.Sec, 5, 5) }; // Technically South Carolina didn't use the 5/51.
                                    break;

                                case "austria":
                                case "germany":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 279, 310),
                                    new CalInfo(GunCal.Sec, 150, 150),
                                    new CalInfo(GunCal.Ter, agTert2, agTert2) };
                                    break;

                                case "japan":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 16),
                                    new CalInfo(GunCal.Sec, 5, 6),
                                    new CalInfo(GunCal.Ter, 3, 3, false) };
                                    break;

                                case "russia":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 16),
                                    new CalInfo(GunCal.Sec, 119, 131),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                    break;

                                case "italy":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 15),
                                    new CalInfo(GunCal.Sec, 100, 125),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                    break;

                                default:
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 12, 15),
                                    new CalInfo(GunCal.Ter, 4, 4) };
                                    break;
                            }
                            if (_hullYear >= 1910)
                            {
                                if (_nation == "britain")
                                {
                                    _calInfos[1]._min = 3 * 25.4f;
                                    _calInfos[1]._max = 3 * 25.4f;
                                    _calInfos.Insert(1, new CalInfo(GunCal.Sec, 5, 6));
                                }

                                if (_calInfos[0]._max < 380 && GunDatabase.HasGunOrGreaterThan(_ship, 14, _gunGrades))
                                {
                                    _calInfos[0]._max = 385;
                                }
                            }
                        }
                        else if (_hullYear < 1927)
                        {
                            switch (_nation)
                            {
                                case "usa":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 14, 16),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                    break;

                                case "japan":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 14, 18),
                                    new CalInfo(GunCal.Sec, 140, 140),
                                    new CalInfo(GunCal.Ter, 120, 120, false) };
                                    break;

                                case "austria":
                                case "germany":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 335, 420),
                                    new CalInfo(GunCal.Sec, 150, 150),
                                    new CalInfo(GunCal.Ter, agTert2, agTert2) };
                                    break;

                                case "russia":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 13, 16),
                                    new CalInfo(GunCal.Sec, 120, 140),
                                    new CalInfo(GunCal.Ter, 3, 3) };
                                    break;

                                case "italy":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 15, 15),
                                    new CalInfo(GunCal.Sec, 120, 155),
                                    new CalInfo(GunCal.Ter, 75, 120)};
                                    break;

                                case "britain":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 15, 18),
                                    new CalInfo(GunCal.Sec, 6, 6),
                                    new CalInfo(GunCal.Sec, 120, 120)};
                                    break;

                                default:
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 14, 18),
                                    new CalInfo(GunCal.Sec, 120, 155),
                                    new CalInfo(GunCal.Sec, 65, 120)};
                                    break;
                            }
                        }
                        else // Fast Battleship
                        {
                            switch (_nation)
                            {
                                case "france":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 380, 20),
                                    new CalInfo(GunCal.Sec, 140, 160),
                                    new CalInfo(GunCal.Ter, 90, 120)};
                                    break;

                                case "japan":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 16, 20),
                                    new CalInfo(GunCal.Sec, 140, 210),
                                    new CalInfo(GunCal.Sec, 120, 130)};
                                    break;

                                case "austria":
                                case "germany":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 380, 460),
                                    new CalInfo(GunCal.Sec, 150, 155),
                                    new CalInfo(GunCal.Ter, 100, 110)};
                                    break;

                                case "italy":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 15, 18),
                                    new CalInfo(GunCal.Sec, 145, 155),
                                    new CalInfo(GunCal.Ter, 100, 125),
                                    new CalInfo(GunCal.Ter, 65, 90)};
                                    break;

                                case "usa":
                                case "britain":
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 14, 18),
                                    new CalInfo(GunCal.Sec, 120, 5.25f),
                                    new CalInfo(GunCal.Ter, 40, 80)};
                                    break;

                                default:
                                    _calInfos = new List<CalInfo>() {
                                    new CalInfo(GunCal.Main, 14, 20),
                                    new CalInfo(GunCal.Sec, 120, 170),
                                    new CalInfo(GunCal.Ter, 65, 120)};
                                    break;
                            }
                        }
                    }
                    break;
            }
        }

        public bool SelectParts()
        {
            if (_this.isSimpleRefit)
                return true;

            SetupGunInfo();

            _ship.EnableLogging(false);

            _prePartsFreeTngNoPayload = _tonnage - _baseHullWeight;
            _payloadBaseWeight = _prePartsWeight - _baseHullWeight; // mines/DC/etc
            // If we can't even add towers/funnels, bail
            if (!CheckSetMaxPayloadWeight())
                return false;

            for (int pass = 0; pass < 50; ++pass)
            {
                ResetPassState();

                // this will increase when towers/funnels are added
                _hullPartsTonnage = 0;

                float payloadTotalWeight = 0;

                int r = 0;
                for (; r < _randParts.Count; ++r)
                {
                    var rp = _randParts[r];
                    if (rp.deleteUnmounted)
                    {
                        _ship.DeleteUnmounted(rp.type.ToString());
                        continue;
                    }
                    else if (rp.deleteRefit && _this._isRefitMode_5__2)
                    {
                        _ship.RemoveDeleteRefitPartsNew(rp.type.ToString(), _this.isSimpleRefit);
                        continue;
                    }

                    // Verify we can have _any_ payload,
                    // i.e. the towers/funnels we added aren't
                    // already putting us over budget.
                    if (!CheckSetMaxPayloadWeight())
                        break;

                    //if(payloadTotalWeight > maxPayloadWeight) DecreasePayloadWeight();

                    // Stop condition: payload is nearly at max.
                    if (r > _lastAnyIdxReq && payloadTotalWeight >= _payloadStopWeight)
                        break;

                    float preWeight = _ship.Weight();

                    float chance = rp.chance;
                    if (rp.tr_rand_mod)
                    {
                        var paramInit = MonoBehaviourExt.Param("initial_tr_transport_armed", 0.1f);
                        var armed = _ship.TechA("armed_transports");
                        chance *= paramInit * armed;
                    }

                    if (!Util.Chance(chance, _this.__8__1.rnd))
                        continue;

                    if (!rp.Check(_ship))
                        continue;

                    var rpu = new RandPartUsage();
                    rpu._rp = rp;

                    bool abortRun = true;
                    for (int rpass = 0; rpass < 10; ++rpass)
                    {
                        rpu.Reset();

                        rpu.desired = Util.FromTo(rp.countMin, rp.countMax, _this.__8__1.rnd);
                        if (rp.align == RandPartInfo.Alignment.Side)
                        {
                            rpu.desired = 2 * Util.RoundToIntProb(rpu.desired * 0.5f, _this.__8__1.rnd);
                        }
                        // TODO: track number used and try other numbers each pass?

                        // Select part type
                        GunDatabase.GunInfo gi = null;
                        if (rp.type == RandPartInfo.RPType.gun)
                        {
                            if (rp.groupGun == string.Empty || !_gunDataForGroup.TryGetValue(rp.groupGun, out gi))
                                gi = GunDatabase.GetGunForRPI(_ship, rp, _gunGrades, _badGunTries);
                        }


                        if (rp.groupGun != string.Empty)
                            _gunDataForGroup[rp.groupGun] = gi;
                        if (rp.groupPart != string.Empty)
                            _partDataForGroup[rp.groupPart] = rpu._data;

                        // Place part(s)
                        for (int j = rpu.desired; j-- > 0;)
                        {

                            bool isCenter = rp.align == RandPartInfo.Alignment.Center || (rp.align == RandPartInfo.Alignment.Both && j == 0);
                            int maxK = isCenter ? 1 : 2;
                            for (int k = 0; k < maxK; ++k)
                            {
                                Part p = null;
                                if (k == 0)
                                {
                                    _firstPairCreated = null;
                                    _offsetX = 0f;
                                    _offsetZ = 0f;
                                }
                                else
                                {
                                    _offsetX *= -1f;
                                }
                                rpu._parts.Add(p);
                            }
                        }

                        _rpAll.Add(rpu);
                        if (rp.type == RandPartInfo.RPType.gun)
                            _rpByBattery[(int)rp.battery].Add(rpu);
                        else if (rp.type == RandPartInfo.RPType.torpedo)
                            _rpTorps.Add(rpu);

                        abortRun = false;
                        break;
                    }

                    if (abortRun)
                    {
                        if (rpu._gun != null)
                        {
                            _badGunTries.TryGetValue(rpu._gun, out int bgt);
                            _badGunTries[rpu._gun] = bgt + 1;
                        }
                        if (rpu._data != null)
                        {
                            _badPartTries.TryGetValue(rpu._data, out int bpt);
                            _badPartTries[rpu._data] = bpt + 1;
                        }

                        bool cleanTC = rp.type == RandPartInfo.RPType.gun
                            && rpu._parts.Count > 0
                            && _ship.shipGunCaliber[_ship.shipGunCaliber.Count - 1] is var sgc
                            && sgc.isCasemateGun == 
                        if (_firstPairCreated != null)
                        {
                            _ship.RemovePart(_firstPairCreated);
                            rpu._parts.Remove(_firstPairCreated);
                            _firstPairCreated = null;
                        }

                        
                        if (rp.required)
                        {
                            if (rpu._parts.Count < rp.countMin)
                                continue;
                        }
                        else
                        {
                            for (int i = rpu._parts.Count; i-- > 0;)
                                _ship.RemovePart(rpu._parts[i]);
                            
                        }
                    }

                    float delta = _ship.Weight() - preWeight;
                    switch (rp.type)
                    {
                        case RandPartInfo.RPType.funnel:
                        case RandPartInfo.RPType.tower_main:
                        case RandPartInfo.RPType.tower_sec:
                        case RandPartInfo.RPType.special:
                            _hullPartsTonnage += delta;
                            break;

                        default:
                            payloadTotalWeight += delta;
                            break;
                    }
                }

                // Verify base requirements
                if (!_mainPlaced || (!_secPlaced && _needSec) || !_hasFunnel)
                    continue;
                if (r < _lastAnyIdxReq)
                    continue;

                //if (payloadTotalWeight < _payloadStopWeight) IncreasePayloadWeight();


                bool partBadPlacement = false;
                for (int i = _ship.parts.Count; i-- > 0;)
                {
                    var part = _ship.parts[i];
                    var data = part.data;
                    if (data.isGun ? _ship.IsMainCal(data) : data.isTorpedo)
                    {
                        if (!part.CanPlaceSoft(false) || !part.CanPlaceSoftLight())
                        {
                            partBadPlacement = true;
                            break;
                        }
                    }
                }
                if (partBadPlacement)
                    continue;

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

                _ship.StartCoroutine(_ship.RefreshDecorDelay());

                foreach (var part in _ship.parts)
                    part.OnPostAdd();

                return true;
            }

            return false;
        }

        private void ResetPassState()
        {
            _partDataForGroup.Clear();
            _gunDataForGroup.Clear();
            _passBadParts.Clear();
            _passBadMounts.Clear();
            _rpAll.Clear();
            foreach (var l in _rpByBattery)
                l.Clear();
            foreach (var l in _rpHull)
                l.Clear();
            _rpTorps.Clear();
                _partToRP.Clear();

            _mainPlaced = false;
            _secPlaced = false;
            _hasFunnel = false;
            _ship.RemoveAllParts();
            _ship.shipGunCaliber.Clear();
            _ship.shipTurretArmor.Clear();
        }

        private PartData PartForGunInfo(GunDatabase.GunInfo gi)
        {
            return null;
        }

        private PartData FindPartDataForRPI(RandPartInfo rp)
        {
            if (rp.groupPart != string.Empty && _partDataForGroup.TryGetValue(rp.groupPart, out var gpart) && gpart != null)
                return gpart;

            if (rp.groupGun != string.Empty && _gunDataForGroup.TryGetValue(rp.groupGun, out var ggun) && ggun != null)
                return PartForGunInfo(ggun);
            

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
                if (weight > _tonnage * 1.4f && (data.isWeapon || data.isBarbette))
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
                maxWeight = _tonnage;

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