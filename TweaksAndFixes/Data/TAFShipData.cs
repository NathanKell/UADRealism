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

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class TAFShipDataAccessor
    {
        public static TAFShipData TAFData(this Ship ship)
        {
            var sd = ship.gameObject.GetComponent<TAFShipData>();
            if (sd == null)
                sd = ship.gameObject.AddComponent<TAFShipData>();

            return sd;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class TAFShipData : MonoBehaviour
    {
        public class GunGradeData
        {
            public int grade = -1;
            public float caliber = 0;
            public bool isCasemateGun;

            public GunGradeData() { }

            public GunGradeData(Ship.TurretCaliber tc, Ship ship)
            {
                caliber = tc.turretPartData.caliber;
                isCasemateGun = tc.isCasemateGun;
                grade = ship.TechGunGrade(tc.turretPartData);
            }

            public GunGradeData(Ship.TurretCaliber.Store tcs, Ship ship)
            {
                if (G.GameData.parts.TryGetValue(tcs.turretPartDataName, out var data))
                {
                    caliber = data.caliber;
                    grade = ship.TechGunGrade(data);
                }

                isCasemateGun = tcs.isCasemateGun;
            }

            public GunGradeData(Ship.TurretCaliber.Store tcs)
            {
                string partName = tcs.turretPartDataName;

                if (tcs.turretPartDataName.Contains(';'))
                {
                    var split = partName.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    partName = split[0];
                    tcs.turretPartDataName = partName;

                    if (split.Length > 1 && int.TryParse(split[1], out var g))
                        grade = g;
                }
                if (G.GameData.parts.TryGetValue(tcs.turretPartDataName, out var turretPartData))
                    caliber = turretPartData.caliber;
            }

            public static GunGradeData ProcessTCS(Ship.TurretCaliber.Store tcs)
            {
                var ggd = new GunGradeData();

                ggd.isCasemateGun = tcs.isCasemateGun;

                string partName = tcs.turretPartDataName;

                if (tcs.turretPartDataName.Contains(';'))
                {
                    var split = partName.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    partName = split[0];
                    tcs.turretPartDataName = partName;

                    if (split.Length > 1 && int.TryParse(split[1], out var g))
                        ggd.grade = g;
                }
                if (G.GameData.parts.TryGetValue(tcs.turretPartDataName, out var turretPartData))
                    ggd.caliber = turretPartData.caliber;

                //Melon<TweaksAndFixes>.Logger.Msg($"Loaded TC grade for caliber {ggd.caliber:F0}, casemate {ggd.isCasemateGun}, loaded grade {ggd.grade}");
                return ggd;
            }
        }

        public TAFShipData(IntPtr ptr) : base(ptr) { }

        private static List<Part.Store> _TempPartStore = new List<Part.Store>();
        private List<GunGradeData> _gradeData = new List<GunGradeData>();
        private int _torpedoGrade = -1;
        private bool _allowOverrideGrade = true;

        private Ship _ship = null;

        public void ToStore(Ship.Store store, bool isPostFromStore)
        {
            // We need to reserialize atter FromStore runs, because
            // otherwise when this ship is next restored from store,
            // it'll have lost all this data.
            if (isPostFromStore)
            {
                //Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: FromStore Post. Updating store for ship {_ship?.name ?? "<NULL>"}");
                SaveTCs(store);
                SaveTorpGrade(store);

                return;
            }

            //Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: ToStore for ship {_ship?.name ?? "<NULL>"}");
            SaveTCs(store);
            SaveTorpGrade(store);
        }

        public void FromStore(Ship.Store store)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: FromStore for ship {_ship?.name ?? "<NULL>"}");
            LoadTCs(store);
            LoadTorpGrade(store);
        }

        public void OnChangeHullPre(PartData hull)
        {
        }

        public void OnClonePost(TAFShipData from)
        {
        }

        public void OnRefit(Ship design)
        {
            CopyGradeData(design.TAFData());
        }

        private void Awake()
        {
            _ship = gameObject.GetComponent<Ship>();
            if (_ship.hull != null && _ship.hull.data != null)
                OnChangeHullPre(_ship.hull.data);
        }


        [HideFromIl2Cpp]
        private GunGradeData FindGGD(Ship.TurretCaliber tc)
            => FindGGD(tc.turretPartData.caliber, tc.isCasemateGun);

        [HideFromIl2Cpp]
        private GunGradeData FindGGD(float caliber, bool isCasemateGun)
        {
            foreach (var ggd in _gradeData)
            {
                if (ggd.caliber < 1)
                    continue;
                if (ggd.isCasemateGun != isCasemateGun)
                    continue;
                if (ggd.caliber != caliber)
                    continue;

                return ggd;
            }

            return null;
        }

        public int GunGrade(PartData data, int defaultGrade)
            => GunGrade(data.caliber, Ship.IsCasemateGun(data), defaultGrade);

        public int GunGrade(Ship.TurretCaliber tc, int defaultGrade)
            => GunGrade(tc.turretPartData.caliber, tc.isCasemateGun, defaultGrade);

        public int GunGrade(float caliber, bool casemate, int defaultGrade)
        {
            if (!_allowOverrideGrade)
                return defaultGrade;

            var ggd = FindGGD(caliber, casemate);
            if (ggd == null)
                return defaultGrade;

            if (ggd.grade == -1)
                ggd.grade = defaultGrade;
            //else
            //    Melon<TweaksAndFixes>.Logger.Msg($"For caliber {ggd.calInch:F0}, casemate {ggd.isCasemateGun}, found grade {ggd.grade}");

            return ggd.grade;
        }

        public int GetTrueGunGrade(PartData data)
        {
            bool old = _allowOverrideGrade;
            _allowOverrideGrade = false;
            var grade = _ship.TechGunGrade(data);
            _allowOverrideGrade = old;
            return grade;
        }

        public int TorpedoGrade(int defaultGrade)
        {
            if (!_allowOverrideGrade)
                return defaultGrade;

            if (_torpedoGrade == -1)
                _torpedoGrade = defaultGrade;
            
            return _torpedoGrade;
        }

        public int GetTrueTorpGrade(PartData data)
        {
            bool old = _allowOverrideGrade;
            _allowOverrideGrade = false;
            var grade = _ship.TechTorpedoGrade(data);
            _allowOverrideGrade = old;
            return grade;
        }

        public bool IsTorpGradeOverridden()
        {
            foreach (var p in _ship.parts)
                if (p != null && p.data != null && p.data.isTorpedo)
                    return IsGradeOverridden(p.data);

            return false;
        }

        public bool IsGradeOverridden(PartData data)
        {
            if (data == null)
                return false;

            if (data.isGun)
            {
                var trueGrade = GetTrueGunGrade(data);
                var ggd = FindGGD(data.caliber, Ship.IsCasemateGun(data));
                if (ggd != null && ggd.grade != -1 && ggd.grade != trueGrade)
                    return true;
            }
            else if (data.isTorpedo)
            {
                return _torpedoGrade != -1 && _torpedoGrade != GetTrueTorpGrade(data);
            }

            return false;
        }

        public void ResetAllGrades()
        {
            ResetAllGunGrades();
            ResetTorpGrade();
        }

        public void ResetAllGunGrades()
        {
            // Annoyingly we don't save partdatas
            // so we have to trawl TCs for each GGD.
            for(int i = _gradeData.Count; i-- > 0;)
            {
                var ggd = _gradeData[i];
                if (ggd.grade < 0)
                    continue;

                for(int j = _ship.shipGunCaliber.Count; j-- > 0;)
                {
                    var tc = _ship.shipGunCaliber[j];
                    if (tc.isCasemateGun != ggd.isCasemateGun || tc.turretPartData.caliber != ggd.caliber)
                        continue;

                    var trueGrade = GetTrueGunGrade(tc.turretPartData);
                    if (ggd.grade != trueGrade)
                        ResetGunGrade(ggd);

                    break;
                }
            }
        }

        public void ResetGunGrade(Ship.TurretCaliber tc)
            => ResetGunGrade(tc.turretPartData.caliber, tc.isCasemateGun);

        public void ResetGunGrade(float caliber, bool casemate)
        {
            var ggd = FindGGD(caliber, casemate);
            if (ggd == null)
                return;

            ResetGunGrade(ggd);
        }

        [HideFromIl2Cpp]
        private void ResetGunGrade(GunGradeData ggd)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"For caliber {ggd.caliber:F0}, casemate {ggd.isCasemateGun}, reset grade (was {ggd.grade})");
            ggd.grade = -1; // will be updated next call to TechGunGrade.

            Ship.TurretCaliber tc = ShipM.FindMatchingTurretCaliber(_ship, ggd.caliber, ggd.isCasemateGun);
            Ship.TurretArmor ta = ShipM.FindMatchingTurretArmor(_ship, ggd.caliber, ggd.isCasemateGun);

            // We need to now replace all parts using this TC. Otherwise
            // they'll all be using the old model. We could do something
            // smarter where we actually switch the models in place but
            // this is much safer, and also solves the case where changing
            // the model removes mount points (RmeovePart is called with
            // the optional bool for erasing those).
            for(int i = _ship.parts.Count; i-- > 0;)
            {
                var p = _ship.parts[i];
                if (!p.data.isGun || p.data.caliber != ggd.caliber || Ship.IsCasemateGun(p.data) != ggd.isCasemateGun)
                    continue;

                if (tc == null)
                    tc = ShipM.FindMatchingTurretCaliber(_ship, p.data);
                if (ta == null)
                    ta = ShipM.FindMatchingTurretArmor(_ship, p.data);

                _TempPartStore.Add(p.ToStore());
                _ship.RemovePart(p, true, true);
            }
            for(int i = _TempPartStore.Count; i-- > 0;)
            {
                var p = Part.CreateFromStore(_TempPartStore[i], _ship, _ship.partsCont);
                if (p != null)
                {
                    p.SetActiveX(true);
                    _ship.AddPart(p);
                    p.LoadModel(_ship, true);
                }
            }
            if (_TempPartStore.Count > 0)
            {
                _ship.CheckCaliberOnShip(_ship);
                var tcnew = ShipM.FindMatchingTurretCaliber(_ship, ggd.caliber, ggd.isCasemateGun);
                if (tcnew != null && tc != null)
                    tcnew.CloneFrom(tc);
                var tanew = ShipM.FindMatchingTurretArmor(_ship, ggd.caliber, ggd.isCasemateGun);
                if (tanew != null && ta != null)
                    tanew.CloneFrom(ta);

                _TempPartStore.Clear();
                _ship.Init();
                _ship.CalcInstability(true);
            }
            G.ui.Refresh(true);
        }

        public void ResetTorpGrade()
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"For torps, reset grade (was {_torpedoGrade})");
            _torpedoGrade = -1; // will be updated next call to TechTorpedoGrade.

            // We need to now replace all parts that are torpedoes. Otherwise
            // they'll all be using the old model. We could do something
            // smarter where we actually switch the models in place but
            // this is much safer, and also solves the case where changing
            // the model removes mount points (RmeovePart is called with
            // the optional bool for erasing those).
            for (int i = _ship.parts.Count; i-- > 0;)
            {
                var p = _ship.parts[i];
                if (!p.data.isTorpedo)
                    continue;

                _TempPartStore.Add(p.ToStore());
                _ship.RemovePart(p, true, true);
            }
            for (int i = _TempPartStore.Count; i-- > 0;)
            {
                var p = Part.CreateFromStore(_TempPartStore[i], _ship, _ship.partsCont);
                if (p != null)
                {
                    p.SetActiveX(true);
                    _ship.AddPart(p);
                    p.LoadModel(_ship, true);
                }
            }
            _TempPartStore.Clear();
            _ship.Init();
            _ship.CalcInstability(true);
            G.ui.Refresh(true);
        }

        [HideFromIl2Cpp]
        private void CopyGradeData(TAFShipData other)
        {
            _torpedoGrade = other._torpedoGrade;
            _gradeData.Clear();
            foreach (var gdO in other._gradeData)
            {
                var gd = new GunGradeData();
                gd.grade = gdO.grade;
                gd.caliber = gdO.caliber;
                gd.isCasemateGun = gdO.isCasemateGun;
            }
        }

        private void LoadTCs(Ship.Store store)
        {
            _gradeData.Clear();
            foreach (var tcs in store.turretCalibers)
                _gradeData.Add(GunGradeData.ProcessTCS(tcs));
        }

        private void SaveTCs(Ship.Store store)
        {
            foreach (var tcs in store.turretCalibers)
            {
                string pName = tcs.turretPartDataName;
                if (tcs.turretPartDataName.Contains(';'))
                {
                    var split = tcs.turretPartDataName.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    pName = split[0];
                }

                var data = G.GameData.parts[pName];
                var ggd = FindGGD(data.caliber, tcs.isCasemateGun);
                if (ggd == null)
                    ggd = new GunGradeData(tcs, _ship);
                else if (ggd.grade < 0)
                    ggd.grade = _ship.TechGunGrade(data);

                //Melon<TweaksAndFixes>.Logger.Msg($"For caliber {ggd.caliber:F0}, casemate {ggd.isCasemateGun}, saved grade {ggd.grade}");
                tcs.turretPartDataName = pName + ";" + ggd.grade.ToString();
            }
        }

        private void LoadTorpGrade(Ship.Store store)
        {
            if (store.hullName.Contains(';'))
            {
                var split = store.hullName.Split(';', StringSplitOptions.RemoveEmptyEntries);
                store.hullName = split[0];
                if (split.Length > 1 && int.TryParse(split[1], out var g))
                    _torpedoGrade = g;
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Loaded torpedo grade {_torpedoGrade}");
        }

        private void SaveTorpGrade(Ship.Store store)
        {
            PartData torpData = null;
            foreach (var p in _ship.parts)
            {
                if (p == null || p.data == null || !p.data.isTorpedo)
                    continue;
                torpData = p.data;
                break;
            }
            if (torpData == null)
                return;

            string hName = store.hullName;
            if (hName.Contains(';'))
            {
                var split = hName.Split(';', StringSplitOptions.RemoveEmptyEntries);
                hName = split[0];
            }
            if (_torpedoGrade < 0)
                _torpedoGrade = _ship.TechTorpedoGrade(torpData);

            //Melon<TweaksAndFixes>.Logger.Msg($"Saved torpedo grade {_torpedoGrade}");
            store.hullName = hName + ";" + _torpedoGrade;
        }
    }
}
