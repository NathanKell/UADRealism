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
    public class GunDatabase
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

        public class GunInfo
        {
            public float _caliber;
            public float _length;
            public int _year;
            public int _calInch;
            public GunRemainInService _remainInService;
        }

        private static readonly Dictionary<string, List<GunInfo>[,]> _NationGunInfos = new Dictionary<string, List<GunInfo>[,]>();

        private static GunInfo NewInfo(float cal, float calLen, int year, GunRemainInService remain = GunRemainInService.No)
        {
            var gi = new GunInfo()
            {
                _caliber = cal < 21 ? cal * 25.4f : cal,
                _length = calLen,
                _year = year,
                _remainInService = remain
            };
            gi._calInch = Mathf.RoundToInt(gi._caliber * 10f / 25.4f) / 10;
            if (gi._calInch < 2)
                gi._calInch = 2;
            else if (gi._calInch > 20)
                gi._calInch = 20;

            return gi;
        }

        private static void AddGun(string nation, GunInfo info, int grade = -1)
        {
            if (!_NationGunInfos.TryGetValue(nation, out var gunArr))
            {
                gunArr = new List<GunInfo>[21, 6];
                _NationGunInfos[nation] = gunArr;
            }
            if (grade == -1)
            {
                for (int i = 1; grade < 5; ++i)
                {
                    int newerYear = Database.GetGunYear(info._calInch, i + 1);
                    if (newerYear < info._year)
                        continue;
                    int olderYear = Database.GetGunYear(info._calInch, i);
                    if (info._year - olderYear < newerYear - info._year)
                        grade = i;
                    else
                        grade = i + 1;
                    break;
                }
            }
            if (gunArr[info._calInch, grade] == null)
                gunArr[info._calInch, grade] = new List<GunInfo>();

            foreach (var gi in gunArr[info._calInch, grade])
            {
                if (gi._caliber == info._caliber && gi._length == info._length)
                {
                    if (info._year < gi._year)
                        gi._year = info._year;
                    return;
                }
            }
            gunArr[info._calInch, grade].Add(info);
        }

        private static int GradeToExtendTo(GunInfo gi, List<GunInfo>[,] gunArr, int inch, int grade)
        {
            if (gi._remainInService == GunRemainInService.No)
                return -1;

            if ((gi._remainInService & GunRemainInService.RequireReplacement) == 0)
                return 5;

            int gradeToStop = -1;
            int iMin = (gi._remainInService & GunRemainInService.CheckDown) != 0 ? Math.Max(inch - 1, 2) : inch;
            int iMax = (gi._remainInService & GunRemainInService.CheckUp) != 0 ? Math.Min(inch + 1, 20) : inch;
            ++iMax;
            for (int g = grade + 1; g < 6; ++g)
            {
                bool foundNone = true;
                for (int i = iMin; i < iMax && foundNone; ++i)
                {
                    if (gunArr[i, g] == null)
                        continue;

                    if ((gi._remainInService & GunRemainInService.MatchCaliber) == 0)
                    {
                        if (gunArr[i, g].Count > 0)
                            foundNone = false;
                    }
                    else
                    {
                        foreach (var gi2 in gunArr[i, g])
                        {
                            if (gi2._caliber == gi._caliber)
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

        private static void HandleRemainInService()
        {
            foreach (var kvp in _NationGunInfos)
            {
                for (int inch = 20; inch > 1; --inch)
                {
                    for (int grade = 4; grade > 0; --grade)
                    {
                        if (kvp.Value[inch, grade] == null)
                            continue;

                        foreach (var gi in kvp.Value[inch, grade])
                        {
                            int extendGrade = GradeToExtendTo(gi, kvp.Value, inch, grade);
                            if (extendGrade == -1)
                                continue;
                            ++extendGrade;
                            for (int g = grade + 1; g < extendGrade; ++g)
                            {
                                if (kvp.Value[inch, g] == null)
                                    kvp.Value[inch, g] = new List<GunInfo>();

                                kvp.Value[inch, g].Add(gi);
                            }
                        }
                    }
                }
            }
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
            if (!_NationGunInfos.TryGetValue(ship.player.data.name, out var gunArr))
                return false;

            for (int i = cal; i < 21; ++i)
            {
                if (gunGrades[i] < 1)
                    continue;
                if (gunArr[cal, gunGrades[i]] != null && gunArr[cal, gunGrades[i]].Count > 0)
                    return true;
            }

            return false;
        }

        private struct GradeInfo
        {
            public int _grade;
            public GunInfo _info;
        }

        private static GunInfo CalFromGradeInfos(Ship ship)
        {
            if (_TempGradeInfo.Count == 1)
                return _TempGradeInfo[0]._info;

            int maxGrade = -1;
            GradeInfo maxGradeInfo = new GradeInfo();
            float minCal = float.MaxValue;
            float maxCal = float.MinValue;
            int minInch = int.MaxValue;
            int maxInch = int.MinValue;
            foreach(var ci in _TempGradeInfo)
            {
                if (ci._grade > maxGrade)
                {
                    maxGrade = ci._grade;
                    maxGradeInfo = ci;
                }
                if (ci._info._caliber < minCal)
                    minCal = ci._info._caliber;
                if (ci._info._caliber > maxCal)
                    maxCal = ci._info._caliber;
                if (ci._info._calInch < minInch)
                    minInch = ci._info._calInch;
                if (ci._info._calInch > maxInch)
                    maxInch = ci._info._calInch;
            }

            float idealCal = IdealCalForTonnage(ship, minCal, maxCal);
            float bestDelta = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < _TempGradeInfo.Count; ++i)
            {
                var ci = _TempGradeInfo[i];
                float delta = Mathf.Abs(ci._info._caliber - idealCal);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIdx = i;
                }
            }

            var closest = _TempGradeInfo[bestIdx];
            if (closest._grade == maxGrade)
                return closest._info;

            // See if we can find a gun with a better grade
            int maxDiff = Math.Max(bestIdx, _TempGradeInfo.Count - bestIdx) + 1;
            float gradeMult = 1f / (closest._info._calInch * 0.1f);
            for (int i = 1; i < maxDiff; ++i)
            {
                int test = bestIdx + i;
                if (test < _TempGradeInfo.Count)
                {
                    int gradeOffset = (int)(Mathf.Abs(_TempGradeInfo[test]._info._caliber - closest._info._caliber) * gradeMult) + 1;
                    if (closest._info._calInch != _TempGradeInfo[test]._info._calInch && _TempGradeInfo[test]._grade > closest._grade + gradeOffset)
                        return _TempGradeInfo[test]._info;
                }
                test = closest._info._calInch - i;
                if (test >= 0)
                {
                    int gradeOffset = (int)(Mathf.Abs(_TempGradeInfo[test]._info._caliber - closest._info._caliber) * gradeMult) + 1;
                    if (closest._info._calInch != _TempGradeInfo[test]._info._calInch && _TempGradeInfo[test]._grade > closest._grade + gradeOffset)
                        return _TempGradeInfo[test]._info;
                }
            }

            return closest._info;
        }

        private static int IdealCalForTonnage(Ship ship, int min, int max)
        {
            switch (ship.shipType.name)
            {
                case "tb": return Mathf.RoundToInt(Util.Remap(ship.Tonnage(), 100f, 500f, min, max, true));
                case "dd": return Mathf.RoundToInt(Util.Remap(ship.Tonnage(), 600f, 1500f, min, max, true));
                case "cl":
                    int clYear = Database.GetYear(ship.hull.data);
                    if(clYear < 1910)
                        return Mathf.RoundToInt(Util.Remap(ship.Tonnage(), 1000f, 2800f, min, max, true));
                    if (clYear < 1920)
                        return Mathf.RoundToInt(Util.Remap(ship.Tonnage(), 2000f, 4000f, min, max, true));
                    if (ship.Tonnage() > 8000f)
                        return max;
                    return ModUtils.Range(min, max);

                default:
                    return ModUtils.Range(min, max);
            }
        }

        private static readonly List<GradeInfo> _TempGradeInfo = new List<GradeInfo>();
        private static readonly HashSet<GunInfo> _TempUsedInfos = new HashSet<GunInfo>();
        private static readonly HashSet<GunInfo> _TempUsedInfosLocal = new HashSet<GunInfo>();
        public static bool GetGunInfoForShip(Ship ship, List<GenerateShip.CalInfo> info, int[] gunGrades, Il2CppSystem.Random rnd)
        {
            if (!_NationGunInfos.TryGetValue(ship.player.data.name, out var gunArr))
                return false;

            _TempUsedInfos.Clear();
            float lastCal = float.MaxValue;

            foreach(var cInfo in info)
            {
                if (!cInfo._required && ModUtils.Range(0f, 1f, null, rnd) > 0.5f)
                    continue;

                _TempGradeInfo.Clear();
                _TempUsedInfosLocal.Clear();

                int calStart, calEnd;
                float calMinStep = Mathf.Floor(cInfo._min * 10f / 25.4f) * 0.1f;
                float calMaxStep = Mathf.Ceil(cInfo._max * 10f / 25.4f) * 0.1f;
                switch (cInfo._cal)
                {
                    case GenerateShip.GunCal.Sec:
                        calStart = (int)ship.shipType.secFrom;
                        calEnd = (int)ship.shipType.secTo;
                        break;
                    case GenerateShip.GunCal.Ter:
                        calStart = (int)ship.shipType.secFrom;
                        calEnd = (int)(ship.shipType.secTo * 0.5f); // this is what stock does
                        break;
                    default:
                        calStart = (int)ship.shipType.mainFrom;
                        calEnd = (int)ship.shipType.mainTo;
                        break;
                }
                calStart = Math.Max(calStart, (int)calMinStep);
                calEnd = Math.Min(calEnd, (int)calMaxStep);

                for (int iCal = calStart; iCal <= calEnd; ++iCal)
                {
                    if (gunGrades[iCal] < 1)
                        continue;

                    var infos = gunArr[iCal, gunGrades[iCal]];
                    if (infos == null)
                        continue;

                    foreach (var gi in infos)
                    {
                        if (gi._caliber > cInfo._max || gi._caliber < cInfo._min 
                            || gi._caliber > lastCal // we always decrease in caliber through the set of guns
                            || _TempUsedInfosLocal.Contains(gi) || _TempUsedInfos.Contains(gi))
                            continue;

                        _TempUsedInfosLocal.Add(gi);
                        _TempGradeInfo.Add(new GradeInfo() { _info = gi, _grade = gunGrades[iCal] });
                    }
                }
                if (_TempGradeInfo.Count == 0)
                {
                    // TODO: check for the full shiptype range?
                    if (cInfo._required)
                        return false;

                    continue;
                }

                cInfo._info = CalFromGradeInfos(ship);
                if (cInfo._info == null)
                {
                    if (cInfo._required)
                        return false;

                    continue;
                }
                _TempUsedInfos.Add(cInfo._info);
                lastCal = cInfo._info._caliber;
            }
            return true;
        }

        public static void FillData()
        {
            // Generic
            var qf47_40 = NewInfo(47, 40, 1885);
            var qf57_40 = NewInfo(57, 40, 1883);
            var bofors = NewInfo(40, 56.3f, 1934);
            var rf3_50 = NewInfo(3, 50, 1945);

            // UK
            AddGun("britain", qf47_40);
            AddGun("britain", qf57_40);
            AddGun("britain", NewInfo(47, 50, 1900, GunRemainInService.UntilReplaceSameInch)); // QF
            AddGun("britain", bofors);

            AddGun("britain", NewInfo(3, 28, 1885)); // BL
            AddGun("britain", NewInfo(3, 40, 1893)); // QF
            AddGun("britain", NewInfo(3, 50, 1899)); // QF
            AddGun("britain", NewInfo(3, 45, 1910, GunRemainInService.UntilReplaceSameInch)); // HA
            AddGun("britain", rf3_50);

            AddGun("britain", NewInfo(4, 28, 1880)); // BL
            AddGun("britain", NewInfo(4, 40, 1894)); // QF
            AddGun("britain", NewInfo(4, 40, 1908)); // QF
            AddGun("britain", NewInfo(4, 40, 1919)); // QF
            AddGun("britain", NewInfo(4, 40, 1934)); // QF
            AddGun("britain", NewInfo(4, 45, 1913)); // QF HA
            AddGun("britain", NewInfo(4, 45, 1930)); // QF HA
            AddGun("britain", NewInfo(4.5f, 45, 1935)); // QF
            AddGun("britain", NewInfo(4.5f, 45, 1944)); // QF
            AddGun("britain", NewInfo(4.7f, 40, 1885)); // QF
            AddGun("britain", NewInfo(4.7f, 40, 1896)); // QF
            AddGun("britain", NewInfo(4.7f, 45, 1918)); // BL
            AddGun("britain", NewInfo(4.7f, 43, 1918)); // QF
            AddGun("britain", NewInfo(4.7f, 45, 1928)); // BL
            AddGun("britain", NewInfo(4.7f, 50, 1938)); // BL

            AddGun("britain", NewInfo(5.5f, 50, 1913, GunRemainInService.UntilReplaceSameInch)); // BL
            AddGun("britain", NewInfo(5.25f, 50, 1934)); // QF

            AddGun("britain", NewInfo(6, 40, 1888)); // QF
            AddGun("britain", NewInfo(6, 45, 1899)); // BL
            AddGun("britain", NewInfo(6, 50, 1905)); // BL
            AddGun("britain", NewInfo(6, 45, 1913)); // BL
            AddGun("britain", NewInfo(6, 50, 1921)); // BL
            AddGun("britain", NewInfo(6, 50, 1930)); // BL

            AddGun("britain", NewInfo(7.5f, 45, 1902)); // should be 1903 but for distribution purposes...
            AddGun("britain", NewInfo(7.5f, 50, 1905, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", NewInfo(7.5f, 45, 1915, GunRemainInService.UntilReplaceSameOrGreaterCal));

            AddGun("britain", NewInfo(8, 30, 1888)); // guess
            AddGun("britain", NewInfo(8, 50, 1923));
            AddGun("britain", NewInfo(8, 50, 1941));

            AddGun("britain", NewInfo(9.2f, 31.5f, 1880));
            AddGun("britain", NewInfo(9.2f, 40, 1895));
            AddGun("britain", NewInfo(9.2f, 47, 1895));
            AddGun("britain", NewInfo(9.2f, 50, 1901));
            AddGun("britain", NewInfo(9.2f, 51, 1913));

            // Skip Swiftsure's 10"/45

            AddGun("britain", NewInfo(12, 35, 1890));
            AddGun("britain", NewInfo(12, 40, 1898));
            AddGun("britain", NewInfo(12, 45, 1903));
            AddGun("britain", NewInfo(12, 50, 1906));
            AddGun("britain", NewInfo(12, 50, 1909, GunRemainInService.Yes));

            AddGun("britain", NewInfo(13.5f, 30, 1880));
            AddGun("britain", NewInfo(13.5f, 45, 1909));

            // Skip other countries' 14" guns
            AddGun("britain", NewInfo(14, 45, 1937));

            AddGun("britain", NewInfo(15, 42, 1912, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", NewInfo(15, 45, 1935));

            AddGun("britain", NewInfo(16, 45, 1922, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", NewInfo(16, 45, 1938));
            AddGun("britain", NewInfo(16, 45, 1943));

            AddGun("britain", NewInfo(18, 40, 1915));
            AddGun("britain", NewInfo(18, 45, 1922, GunRemainInService.UntilReplaceSameCal));
        }
    }
}
