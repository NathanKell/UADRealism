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
    public class GunDatabase : Serializer.IPostProcess
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

            public void PostProcess()
            {
                if (caliber < 21f)
                    caliber *= 25.4f;

                _calInch = Mathf.Clamp(Mathf.RoundToInt(caliber * 10f / 25.4f) / 10, 2, 20);

                if (grade < 1)
                {
                    for (int i = 1; grade < 5; ++i)
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

        private static readonly Dictionary<string, GunDatabase> _Info = new Dictionary<string, GunDatabase>();

        public string nation;
        public List<GunInfo>[] byGrade = new List<GunInfo>[6];
        public List<GunInfo>[,] byCalAndGrade = new List<GunInfo>[21, 6];

        public GunDatabase(string nat)
        {
            nation = nat;
            for (int i = 1; i < byGrade.Length; ++i)
                byGrade[i] = new List<GunInfo>();

            for (int i = 1; i < byGrade.Length; ++i)
                for (int j = 2; j < 21; ++j)
                    byCalAndGrade[j, i] = new List<GunInfo>();
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
            if (!_Info.TryGetValue(nation, out var data))
            {
                data = new GunDatabase(nation);
                _Info[nation] = data;
            }
            data.Add(info);
        }

        private int GradeToExtendTo(GunInfo gi, int inch, int grade)
        {
            if (gi.remainInService == GunRemainInService.No)
                return -1;

            if ((gi.remainInService & GunRemainInService.RequireReplacement) == 0)
                return 5;

            int gradeToStop = -1;
            int iMin = (gi.remainInService & GunRemainInService.CheckDown) != 0 ? Math.Max(inch - 1, 2) : inch;
            int iMax = (gi.remainInService & GunRemainInService.CheckUp) != 0 ? Math.Min(inch + 1, 20) : inch;
            ++iMax;
            for (int g = grade + 1; g < 6; ++g)
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

            for (int inch = 20; inch > 1; --inch)
            {
                for (int grade = 4; grade > 0; --grade)
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
                list.Sort((a, b) => a.caliber.CompareTo(b.caliber));
            foreach (var list in byGrade)
                list.Sort((a, b) => a.caliber.CompareTo(b.caliber));
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
            if (!_Info.TryGetValue(ship.player.data.name, out var gunDB))
                return false;

            for (int i = cal; i < 21; ++i)
            {
                if (gunGrades[i] < 1)
                    continue;
                if (gunDB.byCalAndGrade[cal, gunGrades[i]].Count > 0)
                    return true;
            }

            return false;
        }

        public static GunInfo GetGunForRPI(Ship ship, RandPartInfo rp, int[] gunGrades, Dictionary<GunDatabase.GunInfo, int> badGunTries)
        {
            return null;
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
                if (ci._info.caliber < minCal)
                    minCal = ci._info.caliber;
                if (ci._info.caliber > maxCal)
                    maxCal = ci._info.caliber;
                if (ci._info._calInch < minInch)
                    minInch = ci._info._calInch;
                if (ci._info._calInch > maxInch)
                    maxInch = ci._info._calInch;
            }

            int bestIdx = -1;
            float idealCal = IdealCalForTonnage(ship, minCal, maxCal);
            if (idealCal < 0f)
            {
                bestIdx = ModUtils.Range(0, _TempGradeInfo.Count - 1);
            }
            else
            {
                float bestDelta = float.MaxValue;
                for (int i = 0; i < _TempGradeInfo.Count; ++i)
                {
                    var ci = _TempGradeInfo[i];
                    float delta = Mathf.Abs(ci._info.caliber - idealCal);
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestIdx = i;
                    }
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
                    int gradeOffset = (int)(Mathf.Abs(_TempGradeInfo[test]._info.caliber - closest._info.caliber) * gradeMult) + 1;
                    if (closest._info._calInch != _TempGradeInfo[test]._info._calInch && _TempGradeInfo[test]._grade > closest._grade + gradeOffset)
                        return _TempGradeInfo[test]._info;
                }
                test = closest._info._calInch - i;
                if (test >= 0)
                {
                    int gradeOffset = (int)(Mathf.Abs(_TempGradeInfo[test]._info.caliber - closest._info.caliber) * gradeMult) + 1;
                    if (closest._info._calInch != _TempGradeInfo[test]._info._calInch && _TempGradeInfo[test]._grade > closest._grade + gradeOffset)
                        return _TempGradeInfo[test]._info;
                }
            }

            return closest._info;
        }

        private static float IdealCalForTonnage(Ship ship, float min, float max)
        {
            int year = Database.GetYear(ship.hull.data);
            if (max < 60 && max >= 40 && year > 1920)
                return 40f;

            float tng = ship.Tonnage();
            tng *= 1f + ModUtils.DistributedRange(0.1f, 4);

            switch (ship.shipType.name)
            {
                case "tb": return Util.Remap(tng, 100f, 500f, min, max, true);
                case "dd": return Util.Remap(tng, 600f, 1500f, min, max, true);
                case "cl":
                    if(year < 1910)
                        return Util.Remap(tng, 1000f, 2800f, min, max, true);
                    if (year < 1920)
                        return Util.Remap(tng, 2500f, 4000f, min, max, true);

                    if (tng > 8000f)
                        return max;
                    else
                        return -1f;

                case "ca":
                    if (year < 1915 && ship.player.data.name == "usa")
                        return Util.Remap(tng, 8000f, 15000f, min, max, true);
                    else
                        return -1f;

                case "bb":
                case "bc":
                    if (ship.shipType.name == "bc")
                        tng *= 0.9f;
                    if (year < 1905)
                    {
                        if (max >= 279 && min < 279)
                            return Util.Remap(tng, 12000f, 14000f, min, max, true);
                        else
                            return -1f;
                    }
                    if (year < 1915)
                    {
                        if (max < 320)
                            return Util.Remap(tng, 20000f, 23000f, min, max, true);
                        if (min > 200)
                            return Util.Remap(tng, 20000f, 28000f, min, max, true);
                        
                        return -1f;
                    }
                    if (min > 270)
                    {
                        return Util.Remap(tng, 25000f, 45000f + Math.Max(0f, (max - 406) * (1f / 25.4f)) * 5000f, min, max, true);
                    }
                    return -1f;

                default:
                    return -1f;
            }
        }

        private static readonly List<GradeInfo> _TempGradeInfo = new List<GradeInfo>();
        private static readonly HashSet<GunInfo> _TempUsedInfos = new HashSet<GunInfo>();
        private static readonly HashSet<GunInfo> _TempUsedInfosLocal = new HashSet<GunInfo>();
        public static bool GetGunInfoForShip(Ship ship, List<RandPartInfo.Battery> info, int[] gunGrades, Il2CppSystem.Random rnd)
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
                        if (gi.caliber > cInfo._max || gi.caliber < cInfo._min 
                            || gi.caliber > lastCal // we always decrease in caliber through the set of guns
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
                lastCal = cInfo._info.caliber;
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
