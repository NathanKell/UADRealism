using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using TweaksAndFixes;
using UADRealism.Data;
using Il2CppMessagePack.Formatters;

#pragma warning disable CS8600
#pragma warning disable CS8601
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
            tower_main = 0,
            tower_sec,
            funnel,
            special,
            gun,
            torpedo,
            barbette,
        }

        public enum CalVary
        {
            Random,
            Local,
            Absolute,
        }

        public enum HullRP
        {
            TowerMain = 0,
            TowerSec,
            Funnel,
            Special
        }

        [Flags]
        public enum MountType
        {
            None = 0,
            Casemate = 1 << 0,
            Center = 1 << 1,
            Side = 1 << 2,
            NonCasemate = Center | Side,
            NonCenter = Casemate | Side,
            All = Casemate | Center | Side,
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
        public Il2CppSystem.Collections.Generic.List<string> demandMounts = null;
        public Il2CppSystem.Collections.Generic.List<string> excludeMounts = null;
        public List<KeyValuePair<ShipM.RPOperation, string>> orOps;
        public List<KeyValuePair<ShipM.RPOperation, string>> andOps;
        public bool deleteUnmounted = false;
        public bool deleteRefit = false;
        public bool tr_rand_mod = false;
        public bool allSamePart = true;

        public void PostProcess()
        {
            if (order < 0)
                order = _Next++;

            if (chance < 100)
                required = false;

            if (calMax < 21)
                calMax *= 25.4f;
            if (calMin < 21)
                calMin *= 25.4f;

            paramx = ModUtils.HumanModToDictionary1D(param);
            if (paramx.TryGetValue("mount", out var dM))
                demandMounts = dM.ToNative();
            if (paramx.TryGetValue("!mount", out var xM))
                excludeMounts = xM.ToNative();
            if (paramx.ContainsKey("delete_unmounted"))
                deleteUnmounted = true;
            if (paramx.ContainsKey("delete_refit"))
                deleteRefit = true;
            if (paramx.ContainsKey("tr_rand_mod"))
                tr_rand_mod = true;
            if (groupPart == string.Empty && paramx.ContainsKey("allow_different"))
                allSamePart = false;

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
            _Lookup.ValueOrNew(key).Add(this);
        }

        public bool Check(Ship ship, string design)
        {
            if (paramx.TryGetValue("scheme", out var scheme))
            {
                if (ship.hull.hullInfo != null && !ship.hull.hullInfo.schemes.Contains(scheme[0]))
                    return false;
            }

            bool ok = orOps.Count == 0;
            foreach (var op in orOps)
            {
                if (ShipM.CheckOperationsProcess(ship.hull.data, op.Key, op.Value, ship, design))
                {
                    ok = true;
                    break;
                }
            }
            if (!ok || andOps.Count == 0)
                return ok;

            foreach (var op in andOps)
            {
                if (!ShipM.CheckOperationsProcess(ship.hull.data, op.Key, op.Value, ship, design))
                {
                    ok = false;
                    break;
                }
            }
            return ok;
        }

        public List<PartData> GetParts(List<PartData> available)
        {
            _TempParts.Clear();
            string typeStr = type.ToString();
            foreach (var data in available)
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

                if (align == Alignment.Center)
                {
                    if (data.paramx.ContainsKey("side"))
                        continue;
                }
                else if (align == Alignment.Side && data.paramx.ContainsKey("center"))
                    continue;

                _TempParts.Add(data);
            }

            return _TempParts;
        }

        public MountType GetTowerMountTypes(Ship ship)
        {
            MountType tmt = MountType.None;
            foreach (var p in ship.parts)
            {
                if (p.data.isTowerAny || p.data.isFunnel)
                {
                    foreach (var m in p.mountsInside)
                    {
                        if (ship.mountsUsed.ContainsKey(m))
                            continue;
                        if (m.barbette || m.siBarbette)
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

                        if (m.casemate)
                            tmt |= MountType.Casemate;
                        if (m.center)
                            tmt |= MountType.Center;
                        if (m.side)
                            tmt |= MountType.Side;
                    }
                }
                if (tmt == MountType.All)
                    break;
            }
            return tmt;
        }

        public static MountType GetMountType(PartData data)
        {
            MountType mt = MountType.None;
            foreach (var s in data.mounts)
            {
                switch (s)
                {
                    case "center": mt |= MountType.Center; break;
                    case "side": mt |= MountType.Side; break;
                    case "casemate": mt |= MountType.Casemate; break;
                }
            }
            return mt;
        }

        public static bool HasCenterCasemate(Ship ship)
        {
            foreach (var m in ship.hull.mountsInside)
            {
                if (!m.casemate || ship.mountsUsed.ContainsKey(m))
                    continue;

                // We could probably just use localposition here but I'm not certain
                // that there isn't a case where a mount is the child of some offset
                // gameobject.
                if (Math.Abs(ship.hull.transform.InverseTransformPoint(m.transform.position).x) < 0.01f)
                    return true;
            }

            return false;
        }

        public List<PartData> GetPartsForGunInfo(Ship ship, GunDatabase.GunInfo gi, List<PartData> available)
            => GetPartsForGunInfo(gi, available, GetTowerMountTypes(ship), HasCenterCasemate(ship));

        public List<PartData> GetPartsForGunInfo(GunDatabase.GunInfo gi, List<PartData> available, MountType towerMounts, bool centerCasemate)
        {
            _TempParts.Clear();

            if (mountPref == MountPref.TowerOnly && towerMounts == MountType.None)
                return _TempParts;

            foreach (var data in available)
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

                var mt = GetMountType(data);

                if (align == Alignment.Center)
                {
                    if ((mt & MountType.Center) == 0
                        && (!centerCasemate || (mt & MountType.Casemate) == 0))
                        continue;
                    if (data.paramx.ContainsKey("side"))
                        continue;
                }
                else if (align == Alignment.Side)
                {
                    if (((mt & MountType.Side) == 0
                    && (mt & MountType.Casemate) == 0)
                        || data.paramx.ContainsKey("center"))
                        continue;
                }

                switch (mountPref)
                {
                    case MountPref.TowerOnly:
                        if ((towerMounts & mt) == 0)
                            continue;
                        break;
                    case MountPref.DeckOnly:
                        if ((mt & MountType.NonCasemate) == 0)
                            continue;
                        break;
                    case MountPref.TowerDeck:
                        if ((towerMounts & mt) == 0 && (mt & MountType.NonCasemate) == 0)
                            continue;
                        break;

                    case MountPref.CasemateOnly:
                        if ((mt & MountType.Casemate) == 0)
                            continue;
                        break;
                }

                _TempParts.Add(data);
            }

            return _TempParts;
        }

        public bool ExistsMount(Ship ship, PartData data)
        {
            foreach (var m in ship.mounts)
                if (!ship.mountsUsed.ContainsKey(m) && m.Fits(data, demandMounts, excludeMounts))
                    return true;

            return false;
        }

        public bool IsAllowedMount(Mount m, PartData data, Ship ship)
        {
            switch (align)
            {
                case Alignment.Side:
                    if (Mathf.Abs(m.transform.position.x) < 0.01f)
                        return false;
                    break;
                case Alignment.Center:
                    if (Mathf.Abs(m.transform.position.x) > 0.01f)
                        return false;
                    break;
            }
            if (!m.Fits(data, demandMounts, excludeMounts))
                return false;
            if (ship.mountsUsed.ContainsKey(m))
                return false;

            return true;
        }

        public bool IsAllowedMount(Mount m, Part part, Ship ship)
        {
            switch (align)
            {
                case Alignment.Side:
                    if (Mathf.Abs(m.transform.position.x) < 0.01f)
                        return false;
                    break;
                case Alignment.Center:
                    if (Mathf.Abs(m.transform.position.x) > 0.01f)
                        return false;
                    break;
            }
            if (!m.Fits(part, demandMounts, excludeMounts))
                return false;
            if (ship.mountsUsed.TryGetValue(m, out var otherPart) && otherPart != part)
                return false;
            if (m.parentPart == part)
                return false;

            return true;
        }

        public void GetRangesForShip(Ship ship, out Vector2 xVec, out Vector2 zVec)
        {
            xVec = x;
            zVec = z;
            xVec += ship.allowedMountsOffset;
            zVec += ship.allowedMountsOffset;
            xVec *= ship.hullSize.extents.x + ship.hullSize.center.x;
            zVec *= ship.hullSize.extents.z + ship.hullSize.center.z;
        }

        public float GetMountWeight(Mount m)
        {
            float weight = 1f;
            switch (mountPref)
            {
                case RandPartInfo.MountPref.TowerOnly:
                    if (m.barbette || m.parentPart == null || (!m.parentPart.data.isTowerAny && !m.parentPart.data.isFunnel))
                        return 0f;
                    break;
                case RandPartInfo.MountPref.CasemateOnly:
                    if (!m.casemate || (m.parentPart != null && !m.parentPart.data.isHull))
                        return 0f;
                    break;
                case RandPartInfo.MountPref.DeckOnly:
                    if (m.casemate || (!m.barbette && m.parentPart != null && !m.parentPart.data.isHull))
                        return 0f;
                    break;
                case RandPartInfo.MountPref.TowerDeck:
                    if (m.casemate && (m.parentPart == null || m.parentPart.data.isHull))
                        return 0f;
                    break;

                case RandPartInfo.MountPref.TowerPref:
                    if (m.barbette)
                        weight = 0.001f;
                    else if (m.parentPart != null && (m.parentPart.data.isTowerAny || !m.parentPart.data.isFunnel))
                        weight = 1f;
                    else if (m.parentPart != null && !m.parentPart.data.isHull)
                        weight = 0.01f;
                    else if (m.casemate)
                        weight = 0.002f;
                    else
                        weight = 0.00001f;
                    break;
                case RandPartInfo.MountPref.DeckPref:
                    // Two-tier. Deck is best, then tower, then casemate.
                    if (!m.barbette && (m.parentPart != null && (m.parentPart.data.isTowerAny || m.parentPart.data.isFunnel)))
                        weight = 0.01f;
                    else if (m.casemate)
                        weight = 0.001f;
                    break;
                case RandPartInfo.MountPref.CasematePref:
                    if (!m.barbette && m.parentPart != null && (m.parentPart.data.isTowerAny || m.parentPart.data.isFunnel))
                        weight = 0.01f;
                    else if (!m.casemate)
                        weight = 0.001f;
                    break;
            }

            return weight;
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
        static Dictionary<Mount, float> _MountWeights = new Dictionary<Mount, float>();
        static Dictionary<Vector3, float> _MountPositionWeights = new Dictionary<Vector3, float>();

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

        Dictionary<string, PartData> _partDataForGroup = new Dictionary<string, PartData>();
        Dictionary<string, GunDatabase.GunInfo> _gunDataForGroup = new Dictionary<string, GunDatabase.GunInfo>();

        List<PartData> _availableParts = new List<PartData>();
        List<RandPartInfo> _randParts;
        string _design;

        List<RandPartUsage> _rpAll = new List<RandPartUsage>();
        List<RandPartUsage>[] _rpHull = new List<RandPartUsage>[(int)RandPartInfo.HullRP.Special + 1];
        List<RandPartUsage>[] _rpByBattery = new List<RandPartUsage>[(int)RandPartInfo.Battery.Ter + 1];
        List<RandPartUsage> _rpTorps = new List<RandPartUsage>();
        Dictionary<Part, RandPartUsage> _partToRP = new Dictionary<Part, RandPartUsage>();
        List<GunDatabase.GunInfo> _gunInfos = new List<GunDatabase.GunInfo>();

        int _lastFunnelIdxReq = -1;
        int _lastFunnelIdxOpt = -1;
        int _lastPayloadIdxReq = -1;
        int _lastHullIdxReq = -1;
        int _lastAnyIdxReq = -1;

        const int _MaxGunTries = 10;
        const int _MaxDataTries = 24;
        const int _MaxMountTries = 10;

        Dictionary<GunDatabase.GunInfo, int> _badGunTries = new Dictionary<GunDatabase.GunInfo, int>();
        Dictionary<PartData, int> _badDataTries = new Dictionary<PartData, int>();
        Dictionary<PartData, Dictionary<Mount, int>> _badMountTries = new Dictionary<PartData, Dictionary<Mount, int>>();

        HashSet<GunDatabase.GunInfo> _passBadGuns = new HashSet<GunDatabase.GunInfo>();
        HashSet<PartData> _passBadDatas = new HashSet<PartData>();
        Dictionary<PartData, HashSet<Mount>> _passBadMounts = new Dictionary<PartData, HashSet<Mount>>();

        private class RandPartUsage
        {
            public RandPartInfo _rp;
            public List<Part> _parts = new List<Part>();
            public List<PartData> _datas = new List<PartData>();
            public GunDatabase.GunInfo _gun = null;
            public int desired;
            private GenerateShip _parent;
            public Ship.TurretCaliber _tcDeck = null;
            public Ship.TurretCaliber _tcCasemate = null;

            const int _MaxGunTriesPer = 10;
            const int _MaxDataTriesPer = 24;
            const int _MaxMountTriesPer = 10;

            public Dictionary<GunDatabase.GunInfo, int> _badGunTries = new Dictionary<GunDatabase.GunInfo, int>();
            public Dictionary<PartData, int> _badDataTries = new Dictionary<PartData, int>();
            public Dictionary<PartData, Dictionary<Mount, int>> _badMountTries = new Dictionary<PartData, Dictionary<Mount, int>>();

            public HashSet<GunDatabase.GunInfo> _passBadGuns = new HashSet<GunDatabase.GunInfo>();
            public HashSet<PartData> _passBadDatas = new HashSet<PartData>();

            private static List<Mount> _TempMounts = new List<Mount>();

            public RandPartUsage(GenerateShip gs) { _parent = gs; }

            public void Succeed()
            {
                _parent._rpAll.Add(this);
                if (_rp.type <= RandPartInfo.RPType.special)
                {
                    // Note: cast is redundant but done for clarity
                    _parent._rpHull[(int)((RandPartInfo.HullRP)_rp.type)].Add(this);
                }
                else if (_rp.type == RandPartInfo.RPType.torpedo)
                {
                    _parent._rpTorps.Add(this);
                }
                else if (_rp.type == RandPartInfo.RPType.gun)
                {
                    _parent._rpByBattery[(int)_rp.battery].Add(this);
                }

                foreach (var part in _parts)
                    _parent._partToRP[part] = this;

                if (_gun != null)
                {
                    if (!_parent._gunInfos.Contains(_gun))
                        _parent._gunInfos.Add(_gun);
                    if (_rp.groupGun != string.Empty)
                        _parent._gunDataForGroup[_rp.groupGun] = _gun;
                }

                // Assert: _datas.Count == 1
                if (_rp.groupPart != string.Empty)
                    _parent._partDataForGroup[_rp.groupPart] = _datas[0];

                switch (_rp.type)
                {
                    case RandPartInfo.RPType.tower_main:
                        _parent._mainPlaced = true;
                        break;
                    case RandPartInfo.RPType.tower_sec:
                        _parent._secPlaced = true;
                        break;
                    case RandPartInfo.RPType.funnel:
                        _parent._hasFunnel = true;
                        break;
                }

                foreach (var part in _parts)
                {
                    if (ShipM.FindMatchingTurretArmor(_parent._ship, part.data) == null)
                        _parent._ship.AddShipTurretArmor(part);
                }
            }

            public void Reset()
            {
                _passBadGuns.Clear();
                _passBadDatas.Clear();

                foreach (var data in _datas)
                {
                    if (_badDataTries.IncrementValueFor(data) > _MaxDataTriesPer)
                        _parent._passBadDatas.Add(data);
                    _parent._badDataTries.IncrementValueFor(data);
                }

                if (_gun != null)
                {
                    if (_badGunTries.IncrementValueFor(_gun) > _MaxGunTriesPer)
                        _parent._passBadGuns.Add(_gun);
                    _parent._badGunTries.IncrementValueFor(_gun);
                    _parent._gunInfos.Remove(_gun);
                }

                foreach (var part in _parts)
                {
                    if (part.mount != null)
                    {
                        if (_badMountTries.ValueOrNew(part.data).IncrementValueFor(part.mount) > _MaxMountTriesPer)
                            _parent._passBadMounts.ValueOrNew(part.data).Add(part.mount);
                        _parent._badMountTries.ValueOrNew(part.data).IncrementValueFor(part.mount);
                    }
                    _parent._ship.RemovePart(part);
                }

                _parts.Clear();
                _datas.Clear();
                _gun = null;

                _parent.CleanTCs();
                _parent.CleanTAs();
            }

            public bool IsBadData(PartData data)
            {
                return _passBadDatas.Contains(data)
                    || _parent._passBadDatas.Contains(data)
                    || _parent._badDataTries.GetValueOrDefault(data) > _MaxDataTries;
            }

            public bool IsBadGun(GunDatabase.GunInfo gi)
            {
                return _passBadGuns.Contains(gi)
                    || _parent._passBadGuns.Contains(gi)
                    || _parent._badGunTries.GetValueOrDefault(gi) > _MaxGunTries;
            }

            public bool IsBadMount(PartData data, Mount mount)
            {
                return (_parent._passBadMounts.TryGetValue(data, out var mBads) && mBads.Contains(mount))
                    || (_parent._badMountTries.TryGetValue(data, out var mTriesGS) && mTriesGS.GetValueOrDefault(mount) > _MaxMountTries);
            }

            public void MarkBadTry(Part part, PartData data, GunDatabase.GunInfo gi, bool forceData, bool forceGun, bool forceMount, bool addToPass = true)
            {
                if (data != null)
                {
                    if (addToPass)
                        _passBadDatas.Add(data);
                    if (_badDataTries.IncrementValueFor(data) > _MaxDataTriesPer || forceData)
                        _parent._passBadDatas.Add(data);
                    _parent._badDataTries.IncrementValueFor(data);
                }

                if (gi != null)
                {
                    if (addToPass)
                        _passBadGuns.Add(gi);
                    if (_badGunTries.IncrementValueFor(gi) > _MaxGunTriesPer || forceGun)
                        _parent._passBadGuns.Add(gi);

                    _parent._badGunTries.IncrementValueFor(gi);
                }
                
                if (part != null)
                {
                    if (part.mount != null)
                    {
                        // VS complains about _this_ use of the extension method
                        // (but _not_ the one for the _parent dict!!)
                        if (!_badMountTries.TryGetValue(data, out var bmt))
                        {
                            bmt = new Dictionary<Mount, int>();
                            _badMountTries[data] = bmt;
                        }
                        if (bmt.IncrementValueFor(part.mount) > _MaxMountTriesPer || forceMount)
                            _parent._passBadMounts.ValueOrNew(data).Add(part.mount);
                        _parent._badMountTries.ValueOrNew(data).IncrementValueFor(part.mount);
                    }
                    _parent._ship.RemovePart(part);
                }
                if (_parent._firstPairCreated)
                {
                    _parts.Remove(_parent._firstPairCreated);
                    _parent._ship.RemovePart(_parent._firstPairCreated);
                    _parent._firstPairCreated = null;
                }
            }

            public bool IsAllowedMount(Mount m, Part part)
             => !IsBadMount(part.data, m) && _rp.IsAllowedMount(m, part, _parent._ship);

            public bool IsAllowedMount(Mount m, PartData data)
             => !IsBadMount(data, m) && _rp.IsAllowedMount(m, data, _parent._ship);

            public List<Mount> GetAllowedMounts(Part part)
            {
                _TempMounts.Clear();
                foreach (var m in _parent._ship.mounts)
                    if (IsAllowedMount(m, part))
                        _TempMounts.Add(m);

                return _TempMounts;
            }

            public List<Mount> GetAllowedMounts(PartData data)
            {
                _TempMounts.Clear();
                foreach (var m in _parent._ship.mounts)
                    if (IsAllowedMount(m, data))
                        _TempMounts.Add(m);

                return _TempMounts;
            }

            public List<List<Mount>> GetAllowedMountsInGroups(Part part)
            {
                var ret = new List<List<Mount>>();
                bool checkPos = _rp.x.x > -1f || _rp.x.y < 1f
                        || _rp.z.x > -1f || _rp.z.y < 1f;
                _rp.GetRangesForShip(_parent._ship, out var x, out var z);
                
                _TempMounts.Clear();
                foreach (var m in _parent._ship.mounts)
                {
                    if (!IsAllowedMount(m, part))
                        continue;

                    if (checkPos)
                    {
                        var pos = m.transform.position;
                        if (pos.x < x.x || pos.x > x.y || pos.z < z.x || pos.z > z.y)
                            continue;
                    }
                    _TempMounts.Add(m);
                }
                _TempMounts.Sort((a, b) => a.name.CompareTo(b.name));

                Mount last = null;
                List<Mount> curList = null;
                foreach (var m in _TempMounts)
                {
                    if (last == null 
                        || m.packNumber != last.packNumber
                        || !Mathf.Approximately(m.transform.position.x, last.transform.position.x)
                        || m.parentPart != last.parentPart)
                    {
                        curList = new List<Mount>();
                        ret.Add(curList);
                    }
                    curList.Add(m);
                    last = m;
                }

                return ret;
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

        private bool IsCalInchUsed(int calInch, RandPartInfo.MountPref mountPref)
        {
            foreach (var batt in _rpByBattery)
            {
                foreach (var rpu in batt)
                {
                    if (rpu._gun == null || rpu._gun._calInch != calInch)
                        continue;

                    foreach (var data in rpu._datas)
                    {
                        if (mountPref == RandPartInfo.MountPref.CasemateOnly)
                        {
                            if (Ship.IsCasemateGun(data))
                                return true;
                        }
                        else if (mountPref == RandPartInfo.MountPref.DeckOnly)
                        {
                            if (!Ship.IsCasemateGun(data))
                                return true;
                        }
                        else
                        {
                            // Technically we could check tower mounts
                            // but it's fine to be conservative.
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void CleanTCs()
        {
            for (int i = _ship.shipGunCaliber.Count; i-- > 0;)
            {
                var tc = _ship.shipGunCaliber[i];
                if (!IsTCUsed(tc))
                    _ship.shipGunCaliber.RemoveAt(i);
            }
        }

        private bool IsTCUsed(Ship.TurretCaliber tc)
        {
            // This is a slightly more expensive check than needed
            // because we're multi-testing RPs with the same group
            foreach (var rpu in _rpAll)
            {
                if (rpu._rp.type != RandPartInfo.RPType.gun)
                    continue;

                foreach (var data in rpu._datas)
                {
                    bool isCasemate = Ship.IsCasemateGun(data);
                    if (tc.turretPartData.caliber == data.caliber && isCasemate == tc.isCasemateGun)
                        return true;
                }
            }
            return false;
        }

        private void CleanTAs()
        {
            for (int i = _ship.shipTurretArmor.Count; i-- > 0;)
            {
                var ta = _ship.shipTurretArmor[i];
                if (!IsTAUsed(ta))
                    _ship.shipTurretArmor.RemoveAt(i);
            }
        }

        private bool IsTAUsed(Ship.TurretArmor ta)
        {
            // This is a slightly more expensive check than needed
            // because we're multi-testing RPs with the same group
            foreach (var rpu in _rpAll)
            {
                if (rpu._rp.type != RandPartInfo.RPType.gun)
                    continue;

                foreach (var data in rpu._datas)
                {
                    bool isCasemate = Ship.IsCasemateGun(data);
                    if (ta.turretPartData.caliber == data.caliber && isCasemate == ta.isCasemateGun)
                        return true;
                }
            }

            return false;
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
            for (int i = 0; i < _randParts.Count; ++i)
            {
                var rp = _randParts[i];
                if (!rp.required)
                {
                    if (rp.type == RandPartInfo.RPType.funnel)
                        _lastFunnelIdxOpt = i;
                }
                else
                {
                    _lastAnyIdxReq = i;
                    switch (rp.type)
                    {
                        case RandPartInfo.RPType.tower_sec:
                            if (_needSec)
                                _lastHullIdxReq = i;
                            break;
                        case RandPartInfo.RPType.funnel:
                            _lastFunnelIdxReq = i;
                            goto case RandPartInfo.RPType.tower_main;
                        case RandPartInfo.RPType.tower_main:
                        case RandPartInfo.RPType.special:
                            _lastHullIdxReq = i;
                            break;
                        case RandPartInfo.RPType.torpedo:
                        case RandPartInfo.RPType.gun:
                        case RandPartInfo.RPType.barbette:
                            _lastPayloadIdxReq = i;
                            break;
                    }
                }
            }

            foreach (var data in G.GameData.parts.Values)
                if (_ship.IsPartAvailable(data))
                    _availableParts.Add(data);

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

        //private void SetupGunInfo()
        //{
        //    GunDatabase.TechGunGrades(_ship, _gunGrades);
        //    float agTert = _nation == "austria" ? 47 : 88;
        //    int agTert2 = _nation == "austria" ? 66 : 88;
        //    switch (_sType)
        //    {
        //        case "tb":
        //            if (_hullYear > 1891)
        //            {
        //                switch (_nation)
        //                {
        //                    case "britain":
        //                    case "usa":
        //                    case "spain":
        //                    case "russia":
        //                        int metrMain = _nation == "spain" || _nation == "russia" ? 75 : 76;
        //                        _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, metrMain, metrMain), new CalInfo(GunCal.Main, 57, 57) };
        //                        break;

        //                    case "france":
        //                    case "austria":
        //                        int frMain = _nation == "france" ? 65 : 66;
        //                        _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, frMain, frMain), new CalInfo(GunCal.Main, 47, 47) };
        //                        break;

        //                    case "germany":
        //                        _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 50, 88), new CalInfo(GunCal.Main, 50, 55, false) };
        //                        break;

        //                    default:
        //                        _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 40, 88), new CalInfo(GunCal.Main, 30, 50, false) };
        //                        break;
        //                }
        //            }
        //            else
        //            {
        //                _calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 40, 57), new CalInfo(GunCal.Main, 30, 47, false) };
        //            }
        //            break;

        //        case "dd":_calInfos = new List<CalInfo>() { new CalInfo(GunCal.Main, 75, 140), new CalInfo(GunCal.Sec, 37, 57) }; break;

        //        case "ca":
        //            if (_hullYear < 1905)
        //            {
        //                // Traditional armored cruiser or first-class protected cruiser
        //                switch (_nation)
        //                {
        //                    case "france":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 190, 195),
        //                            new CalInfo(GunCal.Sec, 137, 165),
        //                            new CalInfo(GunCal.Ter, 75, 75),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;

        //                    case "usa":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 8, 10),
        //                            new CalInfo(GunCal.Sec, 4, 6),
        //                            new CalInfo(GunCal.Ter, 57, 77),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;

        //                    case "japan":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 8, 10),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;

        //                    case "russia":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 8, 8),
        //                            new CalInfo(GunCal.Sec, 4, 6),
        //                            new CalInfo(GunCal.Ter, 75, 75),
        //                            new CalInfo(GunCal.Ter, 37, 47) };
        //                        break;

        //                    case "austria":
        //                    case "germany":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 210, 240),
        //                            new CalInfo(GunCal.Sec, 150, 150),
        //                            new CalInfo(GunCal.Ter, agTert, agTert) };
        //                        break;

        //                    case "italy":
        //                    case "spain":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 10, 11),
        //                            new CalInfo(GunCal.Sec, 5, 7),
        //                            new CalInfo(GunCal.Sec, 3, 3),
        //                            new CalInfo(GunCal.Ter, 40, 65) };
        //                        break;

        //                    case "britain":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 9.2f, 9.2f),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                        break;

        //                    default:
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 8, 10),
        //                            new CalInfo(GunCal.Sec, 5, 6),
        //                            new CalInfo(GunCal.Ter, 47, 88) };
        //                        break;
        //                }
        //            }
        //            else if (_hullYear < 1917)
        //            {
        //                // Late armored cruiser.
        //                // These will either have unified main armament
        //                // (Blucher) or battleship-class main guns with
        //                // heavy main-class "secondaries" (Ibuki)
        //                switch (_nation)
        //                {
        //                    case "france":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 190, 195),
        //                            new CalInfo(GunCal.Ter, 75, 75) };
        //                        break;

        //                    case "russia":
        //                    case "usa":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 10, 11),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;

        //                    case "japan":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 12),
        //                            new CalInfo(GunCal.Main, 8, 8),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                        break;

        //                    case "austria":
        //                    case "germany":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 200, 210),
        //                            new CalInfo(GunCal.Sec, 150, 150),
        //                            new CalInfo(GunCal.Ter, agTert, agTert) };
        //                        break;

        //                    case "italy":
        //                    case "spain":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 10, 11),
        //                            new CalInfo(GunCal.Sec, 6, 7),
        //                            new CalInfo(GunCal.Sec, 3, 3) };
        //                        break;

        //                    default:
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 9.2f, 9.2f),
        //                            new CalInfo(GunCal.Sec, 7.5f, 7.5f),
        //                            new CalInfo(GunCal.Ter, 57, 57) };
        //                        break;
        //                }
        //            }
        //            else
        //            {
        //                // Treaty cruiser
        //                _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 8, 8),
        //                            new CalInfo(GunCal.Sec, 4, 5.5f),
        //                            new CalInfo(GunCal.Ter, 40, 88) };
        //            }
        //            break;

        //        case "cl":
        //            if (_hullYear < 1916)
        //            {
        //                _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 95, 110),
        //                            new CalInfo(GunCal.Ter, 37, 88) };
        //            }
        //            else if (_hullYear < 1919)
        //            {
        //                _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 5, 6),
        //                            new CalInfo(GunCal.Sec, 3, 4) };
        //            }
        //            else
        //            {
        //                // Treaty light cruiser
        //                _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 120, 160),
        //                            new CalInfo(GunCal.Sec, 4, 5),
        //                            new CalInfo(GunCal.Ter, 40, 3) };
        //            }
        //            break;

        //        case "bc":
        //        case "bb":
        //            // predreads
        //            if (_sType != "bc" && !_ship.hull.name.StartsWith("bb"))
        //            {
        //                switch (_nation)
        //                {
        //                    case "usa":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 13),
        //                            new CalInfo(GunCal.Sec, 6, 8),
        //                            new CalInfo(GunCal.Ter, 3, 3),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;

        //                    case "france":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 300, 13),
        //                            new CalInfo(GunCal.Main, 270, 280, false),
        //                            new CalInfo(GunCal.Sec, 137, 195),
        //                            new CalInfo(GunCal.Ter, 65, 100),
        //                            new CalInfo(GunCal.Ter, 47, 47) };
        //                        break;

        //                    case "austria":
        //                    case "germany":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 240, 290),
        //                            new CalInfo(GunCal.Sec, 150, 150),
        //                            new CalInfo(GunCal.Ter, agTert, agTert) };
        //                        break;

        //                    case "russia":
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 10, 12),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 75, 75),
        //                            new CalInfo(GunCal.Ter, 47, 47) };
        //                        break;

        //                    default:
        //                        _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 11, 13.5f),
        //                            new CalInfo(GunCal.Sec, _nation == "britain" ? 6 : 5, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3),
        //                            new CalInfo(GunCal.Ter, 47, 57) };
        //                        break;
        //                }

        //                // semi-dreads
        //                if (_ship.TechVar("use_main_side_guns") != 0f)
        //                {
        //                    if (_nation == "russia")
        //                    {
        //                        _calInfos.RemoveAt(3);
        //                        _calInfos.Insert(1, new CalInfo(GunCal.Sec, 8, 8));
        //                    }
        //                    else
        //                    {
        //                        if (_calInfos[1]._cal == GunCal.Main)
        //                        {
        //                            _calInfos.RemoveAt(1);
        //                        }
        //                        foreach (var ci in _calInfos)
        //                        {
        //                            if (ci._cal == GunCal.Sec && ci._max < 8 * 25.4f)
        //                            {
        //                                ci._cal = GunCal.Main;
        //                                ci._min = 230f;
        //                                ci._max = 270f;
        //                                break;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //            // Dreadnoughts / Battlecruisers
        //            else
        //            {
        //                if (_avgYear < 1919)
        //                {
        //                    switch (_nation)
        //                    {
        //                        case "france":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 16),
        //                            new CalInfo(GunCal.Sec, 127, 140) };
        //                            break;

        //                        case "usa":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 16),
        //                            new CalInfo(GunCal.Sec, 5, 5) }; // Technically South Carolina didn't use the 5/51.
        //                            break;

        //                        case "austria":
        //                        case "germany":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 279, 310),
        //                            new CalInfo(GunCal.Sec, 150, 150),
        //                            new CalInfo(GunCal.Ter, agTert2, agTert2) };
        //                            break;

        //                        case "japan":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 16),
        //                            new CalInfo(GunCal.Sec, 5, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3, false) };
        //                            break;

        //                        case "russia":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 16),
        //                            new CalInfo(GunCal.Sec, 119, 131),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                            break;

        //                        case "italy":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 15),
        //                            new CalInfo(GunCal.Sec, 100, 125),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                            break;

        //                        default:
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 12, 15),
        //                            new CalInfo(GunCal.Ter, 4, 4) };
        //                            break;
        //                    }
        //                    if (_hullYear >= 1910)
        //                    {
        //                        if (_nation == "britain")
        //                        {
        //                            _calInfos[1]._min = 3 * 25.4f;
        //                            _calInfos[1]._max = 3 * 25.4f;
        //                            _calInfos.Insert(1, new CalInfo(GunCal.Sec, 5, 6));
        //                        }

        //                        if (_calInfos[0]._max < 380 && GunDatabase.HasGunOrGreaterThan(_ship, 14, _gunGrades))
        //                        {
        //                            _calInfos[0]._max = 385;
        //                        }
        //                    }
        //                }
        //                else if (_hullYear < 1927)
        //                {
        //                    switch (_nation)
        //                    {
        //                        case "usa":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 14, 16),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                            break;

        //                        case "japan":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 14, 18),
        //                            new CalInfo(GunCal.Sec, 140, 140),
        //                            new CalInfo(GunCal.Ter, 120, 120, false) };
        //                            break;

        //                        case "austria":
        //                        case "germany":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 335, 420),
        //                            new CalInfo(GunCal.Sec, 150, 150),
        //                            new CalInfo(GunCal.Ter, agTert2, agTert2) };
        //                            break;

        //                        case "russia":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 13, 16),
        //                            new CalInfo(GunCal.Sec, 120, 140),
        //                            new CalInfo(GunCal.Ter, 3, 3) };
        //                            break;

        //                        case "italy":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 15, 15),
        //                            new CalInfo(GunCal.Sec, 120, 155),
        //                            new CalInfo(GunCal.Ter, 75, 120)};
        //                            break;

        //                        case "britain":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 15, 18),
        //                            new CalInfo(GunCal.Sec, 6, 6),
        //                            new CalInfo(GunCal.Sec, 120, 120)};
        //                            break;

        //                        default:
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 14, 18),
        //                            new CalInfo(GunCal.Sec, 120, 155),
        //                            new CalInfo(GunCal.Sec, 65, 120)};
        //                            break;
        //                    }
        //                }
        //                else // Fast Battleship
        //                {
        //                    switch (_nation)
        //                    {
        //                        case "france":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 380, 20),
        //                            new CalInfo(GunCal.Sec, 140, 160),
        //                            new CalInfo(GunCal.Ter, 90, 120)};
        //                            break;

        //                        case "japan":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 16, 20),
        //                            new CalInfo(GunCal.Sec, 140, 210),
        //                            new CalInfo(GunCal.Sec, 120, 130)};
        //                            break;

        //                        case "austria":
        //                        case "germany":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 380, 460),
        //                            new CalInfo(GunCal.Sec, 150, 155),
        //                            new CalInfo(GunCal.Ter, 100, 110)};
        //                            break;

        //                        case "italy":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 15, 18),
        //                            new CalInfo(GunCal.Sec, 145, 155),
        //                            new CalInfo(GunCal.Ter, 100, 125),
        //                            new CalInfo(GunCal.Ter, 65, 90)};
        //                            break;

        //                        case "usa":
        //                        case "britain":
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 14, 18),
        //                            new CalInfo(GunCal.Sec, 120, 5.25f),
        //                            new CalInfo(GunCal.Ter, 40, 80)};
        //                            break;

        //                        default:
        //                            _calInfos = new List<CalInfo>() {
        //                            new CalInfo(GunCal.Main, 14, 20),
        //                            new CalInfo(GunCal.Sec, 120, 170),
        //                            new CalInfo(GunCal.Ter, 65, 120)};
        //                            break;
        //                    }
        //                }
        //            }
        //            break;
        //    }
        //}

        private bool DecreasePayloadWeight()
        {
            return false;
        }

        private bool ModifyPayloadWeight()
        {
            return false;
        }

        private static readonly Dictionary<string, float> _DesignWeights = new Dictionary<string, float>();

        public bool SelectParts()
        {
            if (_this.isSimpleRefit)
                return true;

            //SetupGunInfo();

            _ship.EnableLogging(false);

            _prePartsFreeTngNoPayload = _tonnage - _baseHullWeight;
            _payloadBaseWeight = _prePartsWeight - _baseHullWeight; // mines/DC/etc
            // If we can't even add towers/funnels, bail
            if (!CheckSetMaxPayloadWeight())
                return false;

            for (int pass = 0; pass < 50; ++pass)
            {
                ResetPassState();
                if (_ship.hull.data.paramx.TryGetValue("designs", out var possDesigns) && possDesigns.Count % 2 == 0)
                {
                    for (int i = 0; i < possDesigns.Count; i += 2)
                        _DesignWeights.Add(possDesigns[i], float.Parse(possDesigns[i + 1]));

                    _design = ModUtils.RandomByWeights(_DesignWeights, null, _this.__8__1.rnd);
                    _DesignWeights.Clear();
                }
                else
                {
                    _design = string.Empty;
                }

                bool passSucecss = true;

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

                    if (payloadTotalWeight > _maxPayloadWeight && !DecreasePayloadWeight())
                        break;

                    // Stop condition: payload is nearly at max.
                    if (r > _lastAnyIdxReq && payloadTotalWeight >= _payloadStopWeight)
                        break;

                    float chance = rp.chance;
                    if (rp.tr_rand_mod)
                    {
                        var paramInit = MonoBehaviourExt.Param("initial_tr_transport_armed", 0.1f);
                        var armed = _ship.TechA("armed_transports");
                        chance *= paramInit * armed;
                    }

                    if (!Util.Chance(chance, _this.__8__1.rnd))
                        continue;

                    var rpu = new RandPartUsage(this);
                    rpu._rp = rp;

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

                        while (rpu._parts.Count < rpu.desired)
                        {
                            float preWeight = _ship.Weight();

                            PartData data = FindPartDataForRPU(rpu, gi);

                            if (data == null)
                                break;

                            var allowed = _ship.PartAmountTotal(data);
                            // TODO: check ahead if this will be paired
                            if (allowed != -1 && _ship.PartAmountUsed(data) >= allowed)
                            {
                                rpu.MarkBadTry(null, data, gi, true, true, false);
                                continue;
                            }
                            if (!rpu._rp.ExistsMount(_ship, data))
                            {
                                // We could be adding a barbette later
                                // so we can't just force-add this
                                rpu.MarkBadTry(null, data, null, false, false, false);
                                continue;
                            }

                            if (data.isGun)
                            {
                                if (_ship.shipGunCaliber == null)
                                    _ship.shipGunCaliber = new Il2CppSystem.Collections.Generic.List<Ship.TurretCaliber>();
                                if (ShipM.FindMatchingTurretCaliber(_ship, data) == null)
                                {
                                    var tempPart = Part.Create(data, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int, false);
                                    AddTCForGun(tempPart, gi);
                                    tempPart.Erase();
                                }
                            }

                            // Check cost now; we have to do this _after_ making the TC.
                            if (_isMissionMainShip && _ship.Cost() + _ship.CalcPartCost(data) > _ship.player.cash)
                            {
                                // This part will never be acceptable. So
                                // add it as bad for all RPUs
                                rpu.MarkBadTry(null, data, gi, true, true, false);
                                if (rpu._parts.Count < rpu._rp.countMin)
                                    continue;
                                else
                                    break;
                            }

                            var part = Part.Create(data, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int, false);
                            if (data.isGun)
                                part.UpdateCollidersSize(_ship);

                            if (!PlacePartForRPU(part, rpu))
                            {
                                rpu.MarkBadTry(part, data, gi, false, false, false);
                                if (rpu._parts.Count < rpu._rp.countMin)
                                    continue;
                                else
                                    break;
                            }

                            bool onSide = Mathf.Abs(_ship.hull.transform.InverseTransformPoint(part.transform.position).x) > 0.01f;
                            bool firstOfPair = onSide && _firstPairCreated == null;

                            float delta = _ship.Weight() - preWeight;
                            float testWeight = _ship.Weight();
                            if (firstOfPair)
                                testWeight += delta;
                            if (testWeight > _tonnage)
                            {
                                rpu.MarkBadTry(part, data, gi, false, false, false);
                                if (rpu._parts.Count < rpu._rp.countMin)
                                    continue;
                                else
                                    break;
                            }

                            bool isHull = true;
                            switch (rp.type)
                            {
                                case RandPartInfo.RPType.funnel:
                                case RandPartInfo.RPType.tower_main:
                                case RandPartInfo.RPType.tower_sec:
                                case RandPartInfo.RPType.special:
                                    _hullPartsTonnage += delta;
                                    break;

                                default:
                                    isHull = false;
                                    payloadTotalWeight += delta;
                                    break;
                            }
                            if (isHull)
                            {
                                if (!CheckSetMaxPayloadWeight())
                                {
                                    // this can only happen from adding a hull part.
                                    // So in this case we want to retry.
                                    _hullPartsTonnage -= delta;
                                    CheckSetMaxPayloadWeight();
                                    rpu.MarkBadTry(part, data, gi, false, false, false);
                                    continue;
                                }
                            }
                            else if (payloadTotalWeight + (firstOfPair ? delta : 0) > _maxPayloadWeight)
                            {
                                payloadTotalWeight -= delta;
                                rpu.MarkBadTry(part, data, gi, false, false, false);
                                if (rpu._parts.Count < rpu._rp.countMin)
                                    continue;
                                else
                                    break;
                            }

                            if (!rpu._datas.Contains(data))
                                rpu._datas.Add(data);
                            rpu._parts.Add(part);
                            if (onSide)
                            {
                                if (firstOfPair)
                                    _firstPairCreated = part;
                                else
                                    _firstPairCreated = null;
                            }
                        }

                        // We might not have made it to the desired number
                        // but if we're at/above the minimum, we're stil ok
                        if (_firstPairCreated == null && rpu._parts.Count >= rpu._rp.countMin)
                        {
                            break;
                        }
                    }

                    // Did we at least reach the minimum?
                    if (_firstPairCreated == null && rpu._parts.Count >= rpu._rp.countMin)
                    {
                        rpu.Succeed();
                    }
                    else
                    {
                        // This will delete everything from this RPI
                        rpu.Reset();

                        if (rpu._rp.required)
                        {
                            passSucecss = false;
                            break;
                        }
                    }
                }

                // Verify base requirements
                if (!passSucecss || !_mainPlaced || (!_secPlaced && _needSec) || !_hasFunnel)
                    continue;
                if (r < _lastAnyIdxReq)
                    continue;

                ModifyPayloadWeight();

                CleanTCs();
                CleanTAs();

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
            _passBadDatas.Clear();
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

        private bool IsPlacementValid(Part part)
        {
            if (!part.CanPlace())
                return false;

            if (part.data.isGun || part.data.isTorpedo)
            {
                if (!part.CanPlaceSoft())
                    return false;

                if (!Ship.IsCasemateGun(part.data) && !part.CanPlaceSoftLight())
                    return false;
            }

            foreach (var p in _ship.parts)
            {
                if (p == part)
                    continue;

                if (p.data.isTorpedo || (p.data.isGun && !Ship.IsCasemateGun(p.data)))
                {
                    if (!p.CanPlaceSoftLight())
                        return false;
                }
            }

            return true;
        }

        private PartData FindPartDataForRPU(RandPartUsage rpu, GunDatabase.GunInfo gi)
        {
            PartData data = null;

            if (_firstPairCreated != null)
            {
                data = _firstPairCreated.data;
            }
            else if (rpu._rp.groupPart != string.Empty && _partDataForGroup.TryGetValue(rpu._rp.groupPart, out data))
            {
            }
            else if (rpu._datas.Count >= 0 && rpu._rp.allSamePart)
            {
                data = rpu._datas[0];
            }
            if(data != null)
                return rpu.IsBadData(data) ? null : data;

            var tmt = RandPartInfo.MountType.None;
            bool hasCenterCasemate = false;
            List<PartData> parts;
            if (rpu._rp.type == RandPartInfo.RPType.gun)
            {
                tmt = rpu._rp.GetTowerMountTypes(_ship);
                hasCenterCasemate = RandPartInfo.HasCenterCasemate(_ship);
                parts = rpu._rp.GetPartsForGunInfo(gi, _availableParts, tmt, hasCenterCasemate);
            }
            else
            {
                parts = rpu._rp.GetParts(_availableParts);
            }
            foreach (var p in parts)
                if (!rpu.IsBadData(p))
                    _Options.Add(p);

            if (_Options.Count == 0)
                return null;

            if (rpu._rp.type == RandPartInfo.RPType.torpedo)
            {
                data = null;
                float bestVal = float.MinValue;
                foreach (var o in _Options)
                {
                    var val = TorpedoValue(o);
                    if (val > bestVal)
                    {
                        bestVal = val;
                        data = o;
                    }
                }
                _Options.Clear();
                return data;
            }

            if (rpu._rp.type == RandPartInfo.RPType.gun)
            {
                bool noCasemates = (rpu._rp.mountPref == RandPartInfo.MountPref.TowerOnly && (tmt & RandPartInfo.MountType.Casemate) == 0)
                    || rpu._rp.mountPref == RandPartInfo.MountPref.DeckOnly || rpu._rp.mountPref == RandPartInfo.MountPref.TowerDeck;

                RandPartInfo.MountType alignMask = RandPartInfo.MountType.None;
                if (rpu._rp.align != RandPartInfo.Alignment.Center)
                {
                    alignMask |= RandPartInfo.MountType.Side;
                    if (!noCasemates)
                        alignMask |= RandPartInfo.MountType.Casemate;
                }
                if (rpu._rp.align != RandPartInfo.Alignment.Side)
                {
                    alignMask |= RandPartInfo.MountType.Center;
                    if (!noCasemates && hasCenterCasemate)
                        alignMask |= RandPartInfo.MountType.Casemate;
                }

                switch (rpu._rp.mountPref)
                {
                    case RandPartInfo.MountPref.TowerPref:
                        foreach (var o in _Options)
                            _OptionWeights.Add(o, (RandPartInfo.GetMountType(o) & tmt) == 0 ? 0.001f : 100f);
                        break;
                    case RandPartInfo.MountPref.DeckPref:
                        foreach (var o in _Options)
                            _OptionWeights.Add(o, ((RandPartInfo.GetMountType(o) & RandPartInfo.MountType.NonCasemate) & alignMask) == 0 ? 0.001f : 100f);
                        break;
                    case RandPartInfo.MountPref.CasematePref:
                        foreach (var o in _Options)
                            _OptionWeights.Add(o, ((RandPartInfo.GetMountType(o) & RandPartInfo.MountType.Casemate) & alignMask) == 0 ? 0.001f : 100f);
                        break;
                    default:
                        data = _Options.Random(null, _this.__8__1.rnd);
                        _Options.Clear();
                        return data;
                }
                data = ModUtils.RandomByWeights(_OptionWeights, null, _this.__8__1.rnd);
                _Options.Clear();
                _OptionWeights.Clear();
                return data;
            }

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
                if (year > 0)
                    cost *= Mathf.Pow(2f, (year - 1890) * 0.5f);

                // Try to weight based on where ship is within the tonnage range
                cost *= 1.2f - Mathf.Abs(Mathf.InverseLerp(minTon, maxTon, o.weight) - _tngRatio) * tonMult;

                _OptionWeights[o] = cost;
            }
            data = ModUtils.RandomByWeights(_OptionWeights, null, _this.__8__1.rnd);
            _OptionWeights.Clear();
            _Options.Clear();
            return data;
        }

        private bool PlacePartForRPU(Part part, RandPartUsage rpu)
        {
            var data = part.data;

            bool useNoMount = false;
            if (_firstPairCreated != null)
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
                    _MountWeights.Clear();
                    foreach (var m in _ship.mounts)
                    {
                        if (!rpu.IsAllowedMount(m, part))
                            continue;

                        rpu._rp.GetRangesForShip(_ship, out var x, out var z);
                        if (m.transform.position.x < x.x || m.transform.position.x > x.y)
                            continue;
                        if (m.transform.position.z < z.x || m.transform.position.z > z.y)
                            continue;

                        float weight = rpu._rp.GetMountWeight(m);
                        if (weight == 0f)
                            continue;

                        _MountWeights.Add(m, weight);
                    }
                    if (_MountWeights.Count > 0)
                    {
                        part.Mount(ModUtils.RandomByWeights(_MountWeights, null, _this.__8__1.rnd));
                        _MountWeights.Clear();
                    }
                    else
                    {
                        if (!data.needsMount && rpu._rp.demandMounts == null || rpu._rp.demandMounts.Count == 0)
                        {
                            useNoMount = true;
                        }
                        else
                        {
                            var mountGroups = rpu.GetAllowedMountsInGroups(part);

                            var snap = MonoBehaviourExt.Param("snap_point_step", 0.5f);
                            foreach (var mg in mountGroups)
                            {
                                mg.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));

                                // Start from the bow (we sorted minZ first)
                                // Stock stops after the first mount point found??
                                for (int i = mg.Count - 1; i > 0 /*&& _MountPositionWeights.Count == 0*/; --i)
                                {
                                    var curM = mg[i];
                                    float weight = rpu._rp.GetMountWeight(curM);
                                    if (weight == 0f)
                                        continue;

                                    var nextM = mg[i - 1];
                                    var curPos = curM.transform.position;
                                    var nextPos = nextM.transform.position;
                                    var distHalfZ = (nextPos.z - curPos.z) * 0.5f;
                                    part.transform.position = new Vector3(curPos.x, curPos.y, curPos.z + distHalfZ + snap + 16.75f);
                                    if (!IsPlacementValid(part))
                                    {
                                        part.transform.position = new Vector3(curPos.x, curPos.y, curPos.z + distHalfZ + 16.75f);
                                        if (!IsPlacementValid(part))
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
                                        if (IsPlacementValid(part))
                                        {
                                            _MountPositionWeights.Add(part.transform.position, weight);
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

                            if (_MountPositionWeights.Count == 0)
                                return false;

                            part.transform.position = ModUtils.RandomByWeights(_MountPositionWeights, null, _this.__8__1.rnd);
                            _MountPositionWeights.Clear();
                        }
                    }
                }
            }

            if (useNoMount)
            {
                if (_firstPairCreated != null)
                {
                    _offsetX *= -1f;
                    var desiredWorldPoint = _ship.transform.TransformPoint(_ship.deckBounds.center + new Vector3(_offsetX, 0f, _offsetZ));
                    var deckAtPoint = _ship.FindDeckAtPoint(desiredWorldPoint);
                    if (deckAtPoint == null)
                        return false;
                    var deckCenter = deckAtPoint.transform.TransformPoint(deckAtPoint.center);
                    part.Place(new Vector3(desiredWorldPoint.x, deckCenter.y, desiredWorldPoint.z), true);
                }
                else
                {
                    rpu._rp.GetRangesForShip(_ship, out var x, out var z);
                    for (int i = 0; i < 30; ++i)
                    {
                        _offsetX = 0;
                        if (rpu._rp.align == RandPartInfo.Alignment.Both && !data.paramx.ContainsKey("center"))
                        {
                            if (data.paramx.ContainsKey("side") || ModUtils.Range(0f, 1f, null, _this.__8__1.rnd) < 0.75f)
                                _offsetX = ModUtils.Range(x.x, x.y, null, _this.__8__1.rnd);
                        }
                        _offsetZ = ModUtils.Range(z.x, z.y, null, _this.__8__1.rnd);

                        if (_offsetX != 0f)
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
                                if (p == part)
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
                                _offsetX = bestPart.transform.localPosition.x;
                        }

                        var desiredWorldPoint = _ship.transform.TransformPoint(_ship.deckBounds.center + new Vector3(_offsetX, 0f, _offsetZ));
                        var deckAtPoint = _ship.FindDeckAtPoint(desiredWorldPoint);
                        if (deckAtPoint == null)
                            continue;

                        var deckCenter = deckAtPoint.transform.TransformPoint(deckAtPoint.center);
                        part.Place(new Vector3(desiredWorldPoint.x, deckCenter.y, desiredWorldPoint.z), true);
                        if (!IsPlacementValid(part))
                            continue;
                    }
                }
            }

            // Found a position
            data.constructorShip = _ship;
            if (!IsPlacementValid(part))
                return false;

            return true;
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

        private Ship.TurretCaliber AddTCForGun(Part part, GunDatabase.GunInfo gi)
        {
            float diamOffset = 0f;
            float excess = (gi.caliber * 1f / 25.4f) - gi._calInch;
            if (excess > 0f || gi._calInch == 2)
                diamOffset = ModUtils.RoundToStep(excess, MonoBehaviourExt.Param("gun_diameter_step", 0.1f)) * 25.4f;

            // We have to add a dummy one first so we can calc length on the model
            _ship.AddShipTurretCaliber(part.data, diamOffset, 0);
            var tc = _ship.shipGunCaliber[_ship.shipGunCaliber.Count - 1]; // stupid that the previous method doesn't return the added TC
            Part.GunBarrelLength(part.data, _ship, true);
            
            // Now we can set length
            var gdm = new GunDataM(part.data, _ship, true);
            int len = Mathf.RoundToInt(Mathf.Clamp((gi.length / part.caliberLength - 1f) * 100f, gdm.minLengthParam, gdm.maxLengthParam));
            tc.length = len;

            return tc;
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