using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using TweaksAndFixes;

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
            public float _calInch;
            public float _length;
            public int _year;
            public GunRemainInService _remainInService;
        }

        private static readonly Dictionary<string, List<GunInfo>[,]> _NationGunInfos = new Dictionary<string, List<GunInfo>[,]>();

        private static GunInfo InfoInch(float calIn, float calLen, int year, GunRemainInService remain = GunRemainInService.No)
        {
            return new GunInfo()
            {
                _calInch = calIn,
                _length = calLen,
                _year = year,
                _remainInService = remain
            };
        }

        private static GunInfo InfoMM(float calmm, float calLen, int year, GunRemainInService remain = GunRemainInService.No)
        {
            var gi = new GunInfo();
            gi._calInch = calmm * (1f / 25.4f);
            // catch stuff like 380mm needing to be represented as 15in.
            var ceil = Mathf.Ceil(gi._calInch);
            if (gi._calInch > 0.98f * ceil)
                gi._calInch = ceil;
            gi._length = calLen;
            gi._year = year;
            gi._remainInService = remain;

            return gi;
        }

        private static void AddGun(string nation, GunInfo info, int grade = -1)
        {
            if (!_NationGunInfos.TryGetValue(nation, out var gunArr))
            {
                gunArr = new List<GunInfo>[21, 6];
                _NationGunInfos[nation] = gunArr;
            }
            int calIn = (int)info._calInch;
            if (grade == -1)
            {
                for (int i = 1; grade < 5; ++i)
                {
                    int newerYear = Database.GetGunYear(calIn, i + 1);
                    if (newerYear < info._year)
                        continue;
                    int olderYear = Database.GetGunYear(calIn, i);
                    if (info._year - olderYear < newerYear - info._year)
                        grade = i;
                    else
                        grade = i + 1;
                    break;
                }
            }
            if (gunArr[calIn, grade] == null)
                gunArr[calIn, grade] = new List<GunInfo>();

            foreach (var gi in gunArr[calIn, grade])
            {
                if (gi._calInch == info._calInch && gi._length == info._length)
                {
                    if (info._year < gi._year)
                        gi._year = info._year;
                    return;
                }
            }
            gunArr[calIn, grade].Add(info);
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
                            if (gi2._calInch == gi._calInch)
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

        private struct GradeInfo
        {
            public int _calInch;
            public int _grade;
            public List<GunInfo> _infos;
        }

        private static readonly List<GradeInfo> _TempGradeInfo = new List<GradeInfo>();
        private static readonly int[] _TempGunGrades = new int[21];
        public static GunInfo GetInfoForOptions(Ship ship, GenerateShip.GunCal cal, HashSet<int> calOptions, int numInCal, GunInfo existing = null)
        {
            if (!_NationGunInfos.TryGetValue(ship.player.data.name, out var gunArr))
                return null;

            int calStart, calEnd;
            switch (cal)
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

            int foundCalStart = int.MaxValue;
            int foundCalEnd = int.MinValue;

            bool isMonitor = false;
            bool isVirginia = false;
            if (ship.hull.name.StartsWith("ironclad"))
            {
                if (ship.hull.name == "ironclad_monitor")
                    isMonitor = true;
                isVirginia = !isMonitor;
            }
            for (int iCal = 2; iCal < 21; ++iCal)
            {
                _TempGunGrades[iCal] = -1;
                if (iCal > calEnd || iCal < calStart || !calOptions.Contains(iCal))
                    continue;

                PartData example;
                if ((isMonitor && G.GameData.parts.TryGetValue($"monitor_gun_{iCal}_x1", out example))
                    || (isVirginia && (G.GameData.parts.TryGetValue($"virginia_casemate_{iCal}", out example) || G.GameData.parts.TryGetValue($"virginia_gun_{iCal}_x1", out example)))
                    || G.GameData.parts.TryGetValue($"gun_{iCal}_x1", out example))
                {
                    int grade = ship.TechGunGrade(example);
                    var infos = gunArr[iCal, grade];
                    if (infos == null)
                        continue;

                    _TempGradeInfo.Add(new GradeInfo() { _calInch = iCal, _grade = grade, _infos = infos });
                    _TempGunGrades[iCal] = grade;
                    if (iCal > foundCalEnd)
                        foundCalEnd = iCal;
                    if (iCal < foundCalStart)
                        foundCalStart = iCal;
                }
            }
            if (_TempGradeInfo.Count == 0)
                return null;

            //_TempGradeInfo.Sort((a, b) => b._grade.CompareTo(a._grade));

            if (cal == GenerateShip.GunCal.Main)
            {
                switch (ship.shipType.name)
                {
                    case "tb":

                        break;
                }
            }

            return null;
        }

        public static void FillData()
        {
            // Generic
            var qf47_40 = InfoMM(47, 40, 1885);
            var qf57_40 = InfoMM(57, 40, 1883);
            var bofors = InfoMM(40, 56.3f, 1934);
            var rf3_50 = InfoInch(3, 50, 1945);

            // UK
            AddGun("britain", qf47_40);
            AddGun("britain", qf57_40);
            AddGun("britain", InfoMM(47, 50, 1900, GunRemainInService.UntilReplaceSameInch)); // QF
            AddGun("britain", bofors);

            AddGun("britain", InfoInch(3, 28, 1885)); // BL
            AddGun("britain", InfoInch(3, 40, 1893)); // QF
            AddGun("britain", InfoInch(3, 50, 1899)); // QF
            AddGun("britain", InfoInch(3, 45, 1910, GunRemainInService.UntilReplaceSameInch)); // HA
            AddGun("britain", rf3_50);

            AddGun("britain", InfoInch(4, 28, 1880)); // BL
            AddGun("britain", InfoInch(4, 40, 1894)); // QF
            AddGun("britain", InfoInch(4, 40, 1908)); // QF
            AddGun("britain", InfoInch(4, 40, 1919)); // QF
            AddGun("britain", InfoInch(4, 40, 1934)); // QF
            AddGun("britain", InfoInch(4, 45, 1913)); // QF HA
            AddGun("britain", InfoInch(4, 45, 1930)); // QF HA
            AddGun("britain", InfoInch(4.5f, 45, 1935)); // QF
            AddGun("britain", InfoInch(4.5f, 45, 1944)); // QF
            AddGun("britain", InfoInch(4.7f, 40, 1885)); // QF
            AddGun("britain", InfoInch(4.7f, 40, 1896)); // QF
            AddGun("britain", InfoInch(4.7f, 45, 1918)); // BL
            AddGun("britain", InfoInch(4.7f, 43, 1918)); // QF
            AddGun("britain", InfoInch(4.7f, 45, 1928)); // BL
            AddGun("britain", InfoInch(4.7f, 50, 1938)); // BL

            AddGun("britain", InfoInch(5.5f, 50, 1913, GunRemainInService.UntilReplaceSameInch)); // BL
            AddGun("britain", InfoInch(5.25f, 50, 1934)); // QF

            AddGun("britain", InfoInch(6, 40, 1888)); // QF
            AddGun("britain", InfoInch(6, 45, 1899)); // BL
            AddGun("britain", InfoInch(6, 50, 1905)); // BL
            AddGun("britain", InfoInch(6, 45, 1913)); // BL
            AddGun("britain", InfoInch(6, 50, 1921)); // BL
            AddGun("britain", InfoInch(6, 50, 1930)); // BL

            AddGun("britain", InfoInch(7.5f, 45, 1902)); // should be 1903 but for distribution purposes...
            AddGun("britain", InfoInch(7.5f, 50, 1905, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", InfoInch(7.5f, 45, 1915, GunRemainInService.UntilReplaceSameOrGreaterCal));

            AddGun("britain", InfoInch(8, 30, 1888)); // guess
            AddGun("britain", InfoInch(8, 50, 1923));
            AddGun("britain", InfoInch(8, 50, 1941));

            AddGun("britain", InfoInch(9.2f, 31.5f, 1880));
            AddGun("britain", InfoInch(9.2f, 40, 1895));
            AddGun("britain", InfoInch(9.2f, 47, 1895));
            AddGun("britain", InfoInch(9.2f, 50, 1901));
            AddGun("britain", InfoInch(9.2f, 51, 1913));

            // Skip Swiftsure's 10"/45

            AddGun("britain", InfoInch(12, 35, 1890));
            AddGun("britain", InfoInch(12, 40, 1898));
            AddGun("britain", InfoInch(12, 45, 1903));
            AddGun("britain", InfoInch(12, 50, 1906));
            AddGun("britain", InfoInch(12, 50, 1909, GunRemainInService.Yes));

            AddGun("britain", InfoInch(13.5f, 30, 1880));
            AddGun("britain", InfoInch(13.5f, 45, 1909));

            // Skip other countries' 14" guns
            AddGun("britain", InfoInch(14, 45, 1937));

            AddGun("britain", InfoInch(15, 42, 1912, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", InfoInch(15, 45, 1935));

            AddGun("britain", InfoInch(16, 45, 1922, GunRemainInService.UntilReplaceSameCal));
            AddGun("britain", InfoInch(16, 45, 1938));
            AddGun("britain", InfoInch(16, 45, 1943));

            AddGun("britain", InfoInch(18, 40, 1915));
            AddGun("britain", InfoInch(18, 45, 1922, GunRemainInService.UntilReplaceSameCal));
        }
    }
}
