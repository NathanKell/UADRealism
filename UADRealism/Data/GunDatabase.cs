using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using TweaksAndFixes;
using JetBrains.Annotations;
using Il2CppSystem.Runtime.InteropServices;
using Il2CppSystem.Net;

#pragma warning disable CS8603
#pragma warning disable CS8625

namespace UADRealism.Data
{
    [Flags]
    public enum GunRemainInService
    {
        No = 0,
        Yes = 1 << 0,
        RequireReplacement = 1 << 1,
        CheckUp = 1 << 2,
        CheckDown = 1 << 3,
        MatchCaliber = 1 << 4,

        UntilReplaceSameInch = Yes | RequireReplacement,
        UntilReplaceSameCal = UntilReplaceSameInch | MatchCaliber,
        UntilReplaceSameOrGreaterCal = Yes | RequireReplacement | CheckUp
    }

    public class GunInfo : Serializer.IPostProcess
    {
        [Serializer.Field] public float caliber;
        [Serializer.Field] public float length;
        [Serializer.Field] public int year;
        public int _calInch;
        [Serializer.Field] public GunRemainInService remainInService;
        [Serializer.Field] public string nations = string.Empty;
        [Serializer.Field] public int grade;
        public HashSet<string> _nations = new HashSet<string>();

        private static float _CalStepHalf = -1f;

        public void PostProcess()
        {
            if (caliber < 21f)
                caliber *= 25.4f;

            if (_CalStepHalf < 0f)
                _CalStepHalf = MonoBehaviourExt.Param("gun_diameter_step", 0.1f) * 0.5f;

            if (caliber < (3f - _CalStepHalf) * 25.4f)
            {
                _calInch = 2;
            }
            else if (caliber >= 20f * 25.4f)
            {
                _calInch = 20;
            }
            else
            {
                _calInch = (int)(caliber * 1f / 25.4f);
                if (caliber >= (Mathf.Ceil(caliber * 1f / 25.4f) - _CalStepHalf) * 25.4f)
                    ++_calInch;

            }

            if (grade < 1)
            {
                for (int i = 1; grade < GunDatabase.MaxGunGrade; ++i)
                {
                    int newerYear = Database.GetGunYear(_calInch, i + 1);
                    if (newerYear < year)
                        continue;
                    int olderYear = Database.GetGunYear(_calInch, i);
                    if (year - olderYear < newerYear - year)
                        grade = i;
                    else
                        grade = i + 1;
                    break;
                }
            }

            foreach (var nation in nations.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                _nations.Add(nation);
                GunDatabase.Add(nation, this);
            }
        }
    }

    public class GunDatabase : Serializer.IPostProcess
    {
        public const int MaxGunGrade = 5;

        private static readonly Dictionary<string, GunDatabase> _InfoByNation = new Dictionary<string, GunDatabase>();

        public string nation;
        public List<GunInfo>[] byGrade = new List<GunInfo>[MaxGunGrade + 1];
        public List<GunInfo>[,] byCalAndGrade = new List<GunInfo>[21, MaxGunGrade + 1];

        public GunDatabase(string nat)
        {
            nation = nat;
            for (int i = 1; i < byGrade.Length; ++i)
                byGrade[i] = new List<GunInfo>();

            for (int i = 2; i < byCalAndGrade.GetLength(0); ++i)
                for (int j = 1; j < byCalAndGrade.GetLength(1); ++j)
                    byCalAndGrade[i, j] = new List<GunInfo>();
        }

        public void Add(GunInfo info)
        {
            // If this nation already has an identical gun,
            // then just update its year and early-out
            foreach (var gi in byCalAndGrade[info._calInch, info.grade])
            {
                if (gi.caliber == info.caliber && gi.length == info.length)
                {
                    if (info.year < gi.year)
                        gi.year = info.year;
                    return;
                }
            }
            byCalAndGrade[info._calInch, info.grade].Add(info);
            byGrade[info.grade].Add(info);
        }

        public static void Add(string nation, GunInfo info)
        {
            if (!_InfoByNation.TryGetValue(nation, out var data))
            {
                data = new GunDatabase(nation);
                _InfoByNation[nation] = data;
            }
            data.Add(info);
        }

        private int GradeToExtendTo(GunInfo gi, int inch, int grade)
        {
            if (gi.remainInService == GunRemainInService.No)
                return -1;

            if ((gi.remainInService & GunRemainInService.RequireReplacement) == 0)
                return MaxGunGrade;

            int gradeToStop = -1;
            int iMin = (gi.remainInService & GunRemainInService.CheckDown) != 0 ? Math.Max(inch - 1, 2) : inch;
            int iMax = (gi.remainInService & GunRemainInService.CheckUp) != 0 ? Math.Min(inch + 1, 20) : inch;
            ++iMax;
            for (int g = grade + 1; g < MaxGunGrade + 1; ++g)
            {
                bool foundNone = true;
                for (int i = iMin; i < iMax && foundNone; ++i)
                {
                    if ((gi.remainInService & GunRemainInService.MatchCaliber) == 0)
                    {
                        if (byCalAndGrade[i, g].Count > 0)
                            foundNone = false;
                    }
                    else
                    {
                        foreach (var gi2 in byCalAndGrade[i, g])
                        {
                            if (gi2.caliber == gi.caliber)
                            {
                                foundNone = false;
                                break;
                            }
                        }
                    }
                }
                if (!foundNone)
                    break;

                gradeToStop = g;
            }

            return gradeToStop;
        }

        public void PostProcess()
        {
            for (int inch = byCalAndGrade.GetLength(0) - 1; inch > 1; --inch)
            {
                for (int grade = MaxGunGrade - 1; grade > 0; --grade)
                {
                    foreach (var gi in byCalAndGrade[inch, grade])
                    {
                        int extendGrade = GradeToExtendTo(gi, inch, grade);
                        if (extendGrade == -1)
                            continue;
                        ++extendGrade;
                        for (int g = grade + 1; g < extendGrade; ++g)
                        {
                            byCalAndGrade[inch, g].Add(gi);
                            byGrade[g].Add(gi);
                        }
                    }
                }
            }

            foreach (var list in byCalAndGrade)
                list.Sort((a, b) => { int cmp = a.caliber.CompareTo(b.caliber); return cmp == 0 ? a.length.CompareTo(b.length) : cmp; });
            foreach (var list in byGrade)
                list.Sort((a, b) => { int cmp = a.caliber.CompareTo(b.caliber); return cmp == 0 ? a.length.CompareTo(b.length) : cmp; });
        }

        public static int TechGunGrade(Ship ship, int cal)
        {
            bool isMonitor = false;
            bool isVirginia = false;
            if (ship.hull.name.StartsWith("ironclad"))
            {
                if (ship.hull.name == "ironclad_monitor")
                    isMonitor = true;
                isVirginia = !isMonitor;
            }

            return TechGunGrade(ship, cal, isMonitor, isVirginia);
        }

        private static int TechGunGrade(Ship ship, int cal, bool isMonitor, bool isVirginia)
        {
            PartData example;
            if ((isMonitor && G.GameData.parts.TryGetValue($"monitor_gun_{cal}_x1", out example))
                || (isVirginia && (G.GameData.parts.TryGetValue($"virginia_casemate_{cal}", out example) || G.GameData.parts.TryGetValue($"virginia_gun_{cal}_x1", out example)))
                || G.GameData.parts.TryGetValue($"gun_{cal}_x1", out example))
            {
                return ship.TechGunGrade(example);
            }

            return -1;
        }

        public static void TechGunGrades(Ship ship, int[] gunGrades)
        {
            bool isMonitor = false;
            bool isVirginia = false;
            if (ship.hull.name.StartsWith("ironclad"))
            {
                if (ship.hull.name == "ironclad_monitor")
                    isMonitor = true;
                isVirginia = !isMonitor;
            }

            gunGrades[0] = gunGrades[1] = -1;

            for (int i = 2; i < 21; ++i)
                gunGrades[i] = TechGunGrade(ship, i, isMonitor, isVirginia);
        }

        public static bool HasGunOrGreaterThan(Ship ship, int cal, int[] gunGrades)
        {
            if (!_InfoByNation.TryGetValue(ship.player.data.name, out var gunDB))
                return false;

            for (; cal < gunDB.byCalAndGrade.GetLength(0); ++cal)
            {
                if (gunGrades[cal] < 1)
                    continue;
                if (gunDB.byCalAndGrade[cal, gunGrades[cal]].Count > 0)
                    return true;
            }

            return false;
        }

        public static GunDatabase GetGunDB(string nation)
            => _InfoByNation.GetValueOrDefault(nation);

        //public static void FillData()
        //{
        //    // Generic
        //    var qf47_40 = NewInfo(47, 40, 1885);
        //    var qf57_40 = NewInfo(57, 40, 1883);
        //    var bofors = NewInfo(40, 56.3f, 1934);
        //    var rf3_50 = NewInfo(3, 50, 1945);

        //    // UK
        //    AddGun("britain", qf47_40);
        //    AddGun("britain", qf57_40);
        //    AddGun("britain", NewInfo(47, 50, 1900, GunRemainInService.UntilReplaceSameInch)); // QF
        //    AddGun("britain", bofors);

        //    AddGun("britain", NewInfo(3, 28, 1885)); // BL
        //    AddGun("britain", NewInfo(3, 40, 1893)); // QF
        //    AddGun("britain", NewInfo(3, 50, 1899)); // QF
        //    AddGun("britain", NewInfo(3, 45, 1910, GunRemainInService.UntilReplaceSameInch)); // HA
        //    AddGun("britain", rf3_50);

        //    AddGun("britain", NewInfo(4, 28, 1880)); // BL
        //    AddGun("britain", NewInfo(4, 40, 1894)); // QF
        //    AddGun("britain", NewInfo(4, 40, 1908)); // QF
        //    AddGun("britain", NewInfo(4, 40, 1919)); // QF
        //    AddGun("britain", NewInfo(4, 40, 1934)); // QF
        //    AddGun("britain", NewInfo(4, 45, 1913)); // QF HA
        //    AddGun("britain", NewInfo(4, 45, 1930)); // QF HA
        //    AddGun("britain", NewInfo(4.5f, 45, 1935)); // QF
        //    AddGun("britain", NewInfo(4.5f, 45, 1944)); // QF
        //    AddGun("britain", NewInfo(4.7f, 40, 1885)); // QF
        //    AddGun("britain", NewInfo(4.7f, 40, 1896)); // QF
        //    AddGun("britain", NewInfo(4.7f, 45, 1918)); // BL
        //    AddGun("britain", NewInfo(4.7f, 43, 1918)); // QF
        //    AddGun("britain", NewInfo(4.7f, 45, 1928)); // BL
        //    AddGun("britain", NewInfo(4.7f, 50, 1938)); // BL

        //    AddGun("britain", NewInfo(5.5f, 50, 1913, GunRemainInService.UntilReplaceSameInch)); // BL
        //    AddGun("britain", NewInfo(5.25f, 50, 1934)); // QF

        //    AddGun("britain", NewInfo(6, 40, 1888)); // QF
        //    AddGun("britain", NewInfo(6, 45, 1899)); // BL
        //    AddGun("britain", NewInfo(6, 50, 1905)); // BL
        //    AddGun("britain", NewInfo(6, 45, 1913)); // BL
        //    AddGun("britain", NewInfo(6, 50, 1921)); // BL
        //    AddGun("britain", NewInfo(6, 50, 1930)); // BL

        //    AddGun("britain", NewInfo(7.5f, 45, 1902)); // should be 1903 but for distribution purposes...
        //    AddGun("britain", NewInfo(7.5f, 50, 1905, GunRemainInService.UntilReplaceSameCal));
        //    AddGun("britain", NewInfo(7.5f, 45, 1915, GunRemainInService.UntilReplaceSameOrGreaterCal));

        //    AddGun("britain", NewInfo(8, 30, 1888)); // guess
        //    AddGun("britain", NewInfo(8, 50, 1923));
        //    AddGun("britain", NewInfo(8, 50, 1941));

        //    AddGun("britain", NewInfo(9.2f, 31.5f, 1880));
        //    AddGun("britain", NewInfo(9.2f, 40, 1895));
        //    AddGun("britain", NewInfo(9.2f, 47, 1895));
        //    AddGun("britain", NewInfo(9.2f, 50, 1901));
        //    AddGun("britain", NewInfo(9.2f, 51, 1913));

        //    // Skip Swiftsure's 10"/45

        //    AddGun("britain", NewInfo(12, 35, 1890));
        //    AddGun("britain", NewInfo(12, 40, 1898));
        //    AddGun("britain", NewInfo(12, 45, 1903));
        //    AddGun("britain", NewInfo(12, 50, 1906));
        //    AddGun("britain", NewInfo(12, 50, 1909, GunRemainInService.Yes));

        //    AddGun("britain", NewInfo(13.5f, 30, 1880));
        //    AddGun("britain", NewInfo(13.5f, 45, 1909));

        //    // Skip other countries' 14" guns
        //    AddGun("britain", NewInfo(14, 45, 1937));

        //    AddGun("britain", NewInfo(15, 42, 1912, GunRemainInService.UntilReplaceSameCal));
        //    AddGun("britain", NewInfo(15, 45, 1935));

        //    AddGun("britain", NewInfo(16, 45, 1922, GunRemainInService.UntilReplaceSameCal));
        //    AddGun("britain", NewInfo(16, 45, 1938));
        //    AddGun("britain", NewInfo(16, 45, 1943));

        //    AddGun("britain", NewInfo(18, 40, 1915));
        //    AddGun("britain", NewInfo(18, 45, 1922, GunRemainInService.UntilReplaceSameCal));
        //}
    }
}
