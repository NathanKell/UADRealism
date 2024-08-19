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
using Il2CppInterop.Runtime.Attributes;
using Il2CppSystem.Runtime.Remoting.Messaging;

#pragma warning disable CS0649
#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public class GenArmorData : Serializer.IPostProcess
    {
        private static readonly Dictionary<string, List<GenArmorData>> _Data = new Dictionary<string, List<GenArmorData>>();

        [Serializer.Field] public string shipType;
        [Serializer.Field] public int year;
        [Serializer.Field] float beltMin;
        [Serializer.Field] float beltMax;
        [Serializer.Field] float beltExtendedMult;
        [Serializer.Field] float turretSideMult;
        [Serializer.Field] float barbetteMult;
        [Serializer.Field] float deckMin;
        [Serializer.Field] float deckMax;
        [Serializer.Field] float deckExtendedMult;
        [Serializer.Field] float turretTopMult;
        [Serializer.Field] float ctMin;
        [Serializer.Field] float ctMax;
        [Serializer.Field] float superMin;
        [Serializer.Field] float superMax;
        [Serializer.Field] public float foreAftVariation;
        [Serializer.Field] public float citadelMult;

        public Dictionary<Ship.A, float> min = new Dictionary<Ship.A, float>();
        public Dictionary<Ship.A, float> max = new Dictionary<Ship.A, float>();

        public void PostProcess()
        {
            min[Ship.A.Belt] = beltMin;
            min[Ship.A.BeltBow] = min[Ship.A.BeltStern] = beltMin * beltExtendedMult;
            min[Ship.A.Deck] = deckMin;
            min[Ship.A.DeckBow] = min[Ship.A.DeckStern] = deckMin * deckExtendedMult;
            min[Ship.A.TurretSide] = beltMin * turretSideMult;
            min[Ship.A.TurretTop] = deckMin * turretTopMult;
            min[Ship.A.Barbette] = beltMin * barbetteMult;
            min[Ship.A.ConningTower] = ctMin;
            min[Ship.A.Superstructure] = superMin;

            max[Ship.A.Belt] = beltMax;
            max[Ship.A.BeltBow] = max[Ship.A.BeltStern] = beltMax * beltExtendedMult;
            max[Ship.A.Deck] = deckMax;
            max[Ship.A.DeckBow] = max[Ship.A.DeckStern] = deckMax * deckExtendedMult;
            max[Ship.A.TurretSide] = beltMax * turretSideMult;
            max[Ship.A.TurretTop] = deckMax * turretTopMult;
            max[Ship.A.Barbette] = beltMax * barbetteMult;
            max[Ship.A.ConningTower] = ctMax;
            max[Ship.A.Superstructure] = superMax;

            foreach (var a in min.Keys)
                max[a] = max[a] * 25.4f;
            foreach (var a in max.Keys)
                min[a] = min[a] * 25.4f;

            citadelMult = Mathf.Clamp01(citadelMult);

            _Data.ValueOrNew(shipType).Add(this);
        }

        public static void LoadData()
        {
            string filename = "genarmordata.csv";
            string path = Path.Combine(Config._BasePath, filename);
            if (!File.Exists(path))
            {
                Melon<TweaksAndFixes>.Logger.Warning($"Skipped loading armor generation rules, `{filename}` note found.");
                return;
            }

            List<GenArmorData> list = new List<GenArmorData>();
            Serializer.CSV.Read<List<GenArmorData>, GenArmorData>(list, path, true);
            foreach (var lst in _Data.Values)
                lst.Sort((a, b) => a.year.CompareTo(b.year));

            Melon<TweaksAndFixes>.Logger.Msg($"Loaded {list.Count} armor generation rules");
        }

        public class GenArmorInfo
        {
            private int _lastHash = 0;

            private bool _isValid = false;
            public bool isValid => _isValid;
            private Dictionary<Ship.A, float> _min = new Dictionary<Ship.A, float>();
            private Dictionary<Ship.A, float> _max = new Dictionary<Ship.A, float>();
            private Dictionary<Ship.A, float> _current = new Dictionary<Ship.A, float>();
            private float _citadelMult;
            public float CitadelMult => _citadelMult;
            private float _foreAftVariation;
            private float _lastLerpVal = -1f;

            private float _lerpStep = 0.1f;
            public float lerpStep => _lerpStep;

            public void FillFor(Ship ship, float yearOverride = -1f)
            {
                _lastLerpVal = -1f;

                int year = Mathf.RoundToInt(yearOverride > 0 ? yearOverride : ship.GetYear(ship));
                int hash = ship.hull.data.name.GetHashCode() ^ year ^ ship.player.data.name.GetHashCode();

                if (hash == _lastHash)
                    return;

                _lastHash = hash;

                FillFor(ship.shipType.name, year);
            }

            public void FillFor(string shipType, int year)
            {
                if (!_Data.TryGetValue(shipType, out var list))
                {
                    _isValid = false;
                    return;
                }

                _isValid = true;

                _min.Clear();
                _max.Clear();
                _current.Clear();

                int idx;
                for (idx = 0; idx < list.Count && list[idx].year < year; ++idx) ;
                bool lerp = true;
                if (list.Count == 1)
                {
                    idx = 0;
                    lerp = false;
                }
                else if (idx >= list.Count)
                {
                    --idx;
                    lerp = false;
                }
                else if (list[idx].year == year || idx == 0)
                {
                    lerp = false;
                }

                if (lerp)
                {
                    var a = list[idx - 1];
                    var b = list[idx];
                    float t = Mathf.InverseLerp(a.year, b.year, year);

                    foreach (var key in a.min.Keys)
                        _min[key] = Mathf.Lerp(a.min[key], b.min[key], year);
                    foreach (var key in a.max.Keys)
                        _max[key] = Mathf.Lerp(a.max[key], b.max[key], year);
                    _citadelMult = Mathf.Lerp(a.citadelMult, b.citadelMult, year);
                    _foreAftVariation = Mathf.Lerp(a.foreAftVariation, b.foreAftVariation, year);
                }
                else
                {
                    var d = list[idx];
                    foreach (var kvp in d.min)
                        _min[kvp.Key] = kvp.Value;
                    foreach (var kvp in d.max)
                        _max[kvp.Key] = kvp.Value;
                    _citadelMult = d.citadelMult;
                    _foreAftVariation = d.foreAftVariation;
                }

                for (Ship.A a = Ship.A.InnerBelt_1st; a <= Ship.A.InnerDeck_3rd; a += 1)
                {
                    _min[a] = ShipM.GetCitadelArmorMax(a, _min[Ship.A.Belt], _min[Ship.A.Deck]) * _citadelMult;
                    _max[a] = ShipM.GetCitadelArmorMax(a, _max[Ship.A.Belt], _max[Ship.A.Deck]) * _citadelMult;
                }

                if (_foreAftVariation != 0f)
                {
                    _foreAftVariation = UnityEngine.Random.Range(0f, _foreAftVariation * 2f);
                    _foreAftVariation -= _foreAftVariation;
                    if (_foreAftVariation < 0.005f && _foreAftVariation > -0.005f)
                        _foreAftVariation = 0f;
                }

                Ship.A maxThickArea = Ship.A.Belt;
                float maxThick = -1f;
                foreach (var kvp in _max)
                {
                    if (kvp.Value > maxThick)
                    {
                        maxThick = kvp.Value;
                        maxThickArea = kvp.Key;
                    }
                }
                float delta = maxThick - _min[maxThickArea];
                float stepCount = delta / G.settings.armorStep;
                _lerpStep = 1f / stepCount;
            }

            public float MaxArmorValue(Ship.A area)
                => _max.GetValueOrDefault(area);

            public float GetMaxArmorValue(Ship ship, Ship.A area, PartData gun = null)
            {
                if (area >= Ship.A.InnerBelt_1st)
                    return ShipM.GetCitadelArmorMax(area, GetMaxArmorValue(ship, Ship.A.Belt, gun), GetMaxArmorValue(ship, Ship.A.Deck, gun)) * _citadelMult;

                return Mathf.Clamp(_max.GetValueOrDefault(area), ship.MinArmorForZone(area), ship.MaxArmorForZone(area, gun));
            }

            public float MinArmorValue(Ship.A area)
                => _min.GetValueOrDefault(area);

            public float GetMinArmorValue(Ship ship, Ship.A area, PartData gun = null)
            {
                if (area >= Ship.A.InnerBelt_1st)
                    return ShipM.GetCitadelArmorMax(area, GetMinArmorValue(ship, Ship.A.Belt, gun), GetMinArmorValue(ship, Ship.A.Deck, gun)) * _citadelMult;

                return Mathf.Clamp(_min.GetValueOrDefault(area), ship.MinArmorForZone(area), ship.MaxArmorForZone(area, gun));
            }

            public float GetArmorValue(Ship ship, Ship.A area, float portionOfMax)
            {
                // portionOfMax is already applied to the base armor area
                // (belt or deck) so we don't multiply it in again. Instead
                // we get the max current value, which takes base armor
                // (or upstream citadel armor) into account.
                if (area >= Ship.A.InnerBelt_1st)
                    return G.settings.RoundToArmorStep(ship.MaxArmorForZone(area, null) * _citadelMult);

                if (portionOfMax != _lastLerpVal)
                    UpdateForLerp(ship, portionOfMax);

                return _current.GetValueOrDefault(area);
            }

            private static readonly Ship.A[] _Extendeds = new Ship.A[] { Ship.A.BeltBow, Ship.A.BeltStern, Ship.A.DeckBow, Ship.A.DeckStern };

            private void UpdateForLerp(Ship ship, float t)
            {
                _lastLerpVal = t;

                foreach (var a in _max.Keys)
                    _current[a] = Mathf.Lerp(_min[a], _max[a], t);

                if (_foreAftVariation != 0f)
                {
                    foreach (var a in _Extendeds)
                    {
                        float mult = (int)a % 2 == 0 ? -1f : 1f;
                        _current[a] = Mathf.Clamp(_current[a] * (1f + mult * _foreAftVariation), _min[a], _max[a]);
                    }
                }

                foreach (var area in _max.Keys)
                    _current[area] = G.settings.RoundToArmorStep(Mathf.Clamp(_current[area], ship.MinArmorForZone(area), ship.MaxArmorForZone(area)));
            }
        }

        private static readonly GenArmorInfo _Info = new GenArmorInfo();

        public static GenArmorInfo GetInfoFor(Ship ship, float yearOverride = -1f)
        {
            _Info.FillFor(ship, yearOverride);
            return _Info.isValid ? _Info : null;
        }

        public static GenArmorInfo GetInfoFor(string shipType, int year)
        {
            _Info.FillFor(shipType, year);
            return _Info.isValid ? _Info : null;
        }
    }
}