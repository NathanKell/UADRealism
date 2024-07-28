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
            public float calInch = 0;
            public bool isCasemateGun;

            public GunGradeData() { }

            public GunGradeData(Ship.TurretCaliber tc, Ship ship)
            {
                calInch = tc.turretPartData.GetCaliberInch();
                isCasemateGun = tc.isCasemateGun;
                grade = ship.TechGunGrade(tc.turretPartData);
            }

            public GunGradeData(Ship.TurretCaliber.Store tcs, Ship ship)
            {
                if (G.GameData.parts.TryGetValue(tcs.turretPartDataName, out var data))
                {
                    calInch = data.GetCaliberInch();
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
                    calInch = turretPartData.GetCaliberInch();
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
                    ggd.calInch = turretPartData.GetCaliberInch();

                Melon<TweaksAndFixes>.Logger.Msg($"Loaded TC grade for caliber {ggd.calInch:F0}, casemate {ggd.isCasemateGun}, loaded grade {ggd.grade}");
                return ggd;
            }
        }

        public TAFShipData(IntPtr ptr) : base(ptr) { }

        private List<GunGradeData> _gradeData = new List<GunGradeData>();
        private bool _allowOverrideGrade = true;

        private Ship _ship = null;

        public void ToStore(Ship.Store store, bool isPostLoad)
        {
            if (isPostLoad)
            {
                Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: FromStore Post. Updating store for ship {_ship?.name ?? "<NULL>"}");
                SaveTCs(store);

                return;
            }

            Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: ToStore for ship {_ship?.name ?? "<NULL>"}");
            SaveTCs(store);
        }

        public void FromStore(Ship.Store store)
        {
            Melon<TweaksAndFixes>.Logger.Msg($"TAFShipData: FromStore for ship {_ship?.name ?? "<NULL>"}");
            LoadTCs(store);
        }

        public void OnChangeHullPre(PartData hull)
        {
        }

        public void OnClonePost(TAFShipData from)
        {
        }

        private void Awake()
        {
            _ship = gameObject.GetComponent<Ship>();
            if (_ship.hull != null && _ship.hull.data != null)
                OnChangeHullPre(_ship.hull.data);
        }


        [HideFromIl2Cpp]
        private GunGradeData FindGGD(Ship.TurretCaliber tc)
            => FindGGD(tc.turretPartData.GetCaliberInch(), tc.isCasemateGun);

        [HideFromIl2Cpp]
        private GunGradeData FindGGD(float calInch, bool isCasemateGun)
        {
            foreach (var ggd in _gradeData)
            {
                if (ggd.calInch < 1)
                    continue;
                if (ggd.isCasemateGun != isCasemateGun)
                    continue;
                if (ggd.calInch != calInch)
                    continue;

                return ggd;
            }

            return null;
        }

        public int GunGrade(PartData data, int defaultGrade)
            => GunGrade(data.GetCaliberInch(), Ship.IsCasemateGun(data), defaultGrade);

        public int GunGrade(Ship.TurretCaliber tc, int defaultGrade)
            => GunGrade(tc.turretPartData.GetCaliberInch(), tc.isCasemateGun, defaultGrade);

        public int GunGrade(float calInch, bool casemate, int defaultGrade)
        {
            if (!_allowOverrideGrade)
                return defaultGrade;

            var ggd = FindGGD(calInch, casemate);
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

        public bool IsGradeOverridden(PartData data)
        {
            if (data == null)
                return false;

            var trueGrade = GetTrueGunGrade(data);
            var ggd = FindGGD(data.GetCaliberInch(), Ship.IsCasemateGun(data));
            if (ggd != null && ggd.grade != -1 && ggd.grade != trueGrade)
                return true;

            return false;
        }

        public void ResetGrade(Ship.TurretCaliber tc)
            => ResetGrade(tc.turretPartData.GetCaliberInch(), tc.isCasemateGun);

        static List<Part.Store> _TempPartStore = new List<Part.Store>();
        public void ResetGrade(float calInch, bool casemate)
        {
            var ggd = FindGGD(calInch, casemate);
            if (ggd == null)
                return;

            Melon<TweaksAndFixes>.Logger.Msg($"For caliber {ggd.calInch:F0}, casemate {ggd.isCasemateGun}, reset grade (was {ggd.grade})");
            ggd.grade = -1; // will be updated next call to TechGunGrade.

            // We need to now replace all parts using this TC. Otherwise
            // they'll all be using the old model. We could do something
            // smarter where we actually switch the models in place but
            // this is much safer, and also solves the case where changing
            // the model removes mount points (RmeovePart is called with
            // the optional bool for erasing those).
            for(int i = _ship.parts.Count; i-- > 0;)
            {
                var p = _ship.parts[i];
                if (!p.data.isGun || p.data.GetCaliberInch() != ggd.calInch || Ship.IsCasemateGun(p.data) != ggd.isCasemateGun)
                    continue;

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
            _TempPartStore.Clear();
            _ship.Init();
            _ship.CalcInstability(true);
            G.ui.Refresh(true);
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
                var ggd = FindGGD(data.GetCaliberInch(), tcs.isCasemateGun);
                if (ggd == null)
                    ggd = new GunGradeData(tcs, _ship);
                else if (ggd.grade < 0)
                    ggd.grade = _ship.TechGunGrade(data);

                Melon<TweaksAndFixes>.Logger.Msg($"For caliber {ggd.calInch:F0}, casemate {ggd.isCasemateGun}, saved grade {ggd.grade}");
                tcs.turretPartDataName = pName + ";" + ggd.grade.ToString();
            }
        }
    }
}
