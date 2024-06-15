using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

#pragma warning disable CS8603

namespace UADRealism
{
    public static class Database
    {
        private static readonly string[,] _GunGradeTechs = new string[21,6];
        private static readonly int[,] _GunGradeYears = new int[21, 6];
        private static readonly Dictionary<string, int> _PartYears = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _PartTechs = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _ComponentTechs = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _ComponentYears = new Dictionary<string, int>();
        private static readonly Dictionary<string, HashSet<string>> _ParamToHulls = new Dictionary<string, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<string>> _PartToHulls = new Dictionary<string, HashSet<string>>();
        private static readonly List<string> _AllHulls = new List<string>();

        private static void FillTechData()
        {
            foreach (var kvpT in G.GameData.technologies)
            {
                foreach (var kvpE in kvpT.Value.effects)
                {
                    switch (kvpE.Key)
                    {
                        case "unlock":
                        case "unlockpart":
                            foreach (var effList in kvpE.Value)
                            {
                                foreach (var v in effList)
                                {
                                    if (G.GameData.parts.TryGetValue(v, out var pData))
                                    {
                                        //Melon<UADRealismMod>.Logger.Msg($"Part {pData.name} needs tech {kvpT.key} of year {kvpT.Value.year}");
                                        _PartTechs[pData.name] = kvpT.Key;
                                        _PartYears[pData.name] = kvpT.Value.year;
                                    }
                                }
                            }
                            break;
                        case "gun":
                            foreach (var effList in kvpE.Value)
                            {
                                if (effList.Count < 2 || !float.TryParse(effList[0], out var calF))
                                    continue;
                                if (!int.TryParse(effList[1], out var grade))
                                    continue;
                                int cal = Mathf.RoundToInt(calF);
                                //Melon<UADRealismMod>.Logger.Msg($"Gun of {cal}in, grade {grade} needs tech {kvpT.key} of year {kvpT.Value.year}");
                                _GunGradeTechs[cal, grade] = kvpT.Key;
                                _GunGradeYears[cal, grade] = kvpT.Value.year;
                            }
                            break;
                    }
                }
                if (kvpT.Value.componentx != null)
                {
                    //Melon<UADRealismMod>.Logger.Msg($"Component {kvpT.Value.componentx.name} needs tech {kvpT.key} of year {kvpT.Value.year}");
                    _ComponentTechs[kvpT.Value.componentx.name] = kvpT.Key;
                    _ComponentYears[kvpT.Value.componentx.name] = kvpT.Value.year;
                }
            }
        }

        private static void FillPartDatabase()
        {
            // Pass 1: Fill hull structures
            foreach (var kvp in G.GameData.parts)
            {
                var hull = kvp.Value;
                if (!hull.isHull)
                    continue;

                _AllHulls.Add(hull.name);

                foreach (var key in hull.paramx.Keys)
                {
                    if (!_ParamToHulls.TryGetValue(key, out var set))
                    {
                        set = new HashSet<string>();
                        _ParamToHulls.Add(key, set);
                    }
                    set.Add(hull.name);
                }
            }

            // Pass 2: fill part->hull structures
            foreach (var kvp in G.GameData.parts)
            {
                var part = kvp.Value;
                if (part.isHull)
                    continue;

                var possibleHulls = new HashSet<string>();
                bool foundTags = false;
                // There will be zero or more hashsets in needTags.
                // A hull has to have its params intersect with every hashset.
                foreach (var needSet in part.needTags)
                {
                    foreach (var s in needSet)
                    {
                        if (!_ParamToHulls.TryGetValue(s, out var set))
                            continue;

                        if (foundTags)
                        {
                            possibleHulls.IntersectWith(set);
                        }
                        else
                        {
                            foreach (var h in set)
                                possibleHulls.Add(h);
                        }
                    }
                    if (needSet.Count > 0)
                        foundTags = true;
                }
                if (!foundTags)
                {
                    foreach (var h in _AllHulls)
                        possibleHulls.Add(h);
                }
                else if (possibleHulls.Count == 0)
                {
                    _PartToHulls.Add(part.name, possibleHulls);
                    continue;
                }
                
                foreach (var excludeSet in part.excludeTags)
                {
                    foreach (var s in excludeSet)
                    {
                        if (!_ParamToHulls.TryGetValue(s, out var set))
                            continue;
                        possibleHulls.ExceptWith(set);
                    }
                }

                if (possibleHulls.Count == 0)
                {
                    _PartToHulls.Add(part.name, possibleHulls);
                    continue;
                }

                if (part.isGun)
                {
                    var calInch = part.GetCaliberInch();
                    possibleHulls.RemoveWhere(h =>
                    {
                        var hull = G.GameData.parts[h];
                        var sType = hull.shipType;
                        if ((calInch < sType.mainFrom || sType.mainTo < calInch) && (calInch < sType.secFrom || sType.secTo < calInch))
                            return true;

                        if (hull.maxAllowedCaliber > 0 && calInch > hull.maxAllowedCaliber)
                            return true;

                        if (hull.paramx.ContainsKey("unique_guns"))
                        {
                            return !part.paramx.ContainsKey("unique");
                        }
                        else
                        {
                            // Ignore max caliber for secondary guns from tech
                            // Ignore barrel limits from tech
                            return false;
                        }
                    });
                }

                // No need to check torpedo, since all paths lead to true.
                //if (part.isTorpedo)
                //{
                //    if (part.paramx.ContainsKey("sub_torpedo"))
                //        return true;
                //    if (part.paramx.ContainsKey("deck_torpedo"))
                //        return true;

                //    // Ignore barrel limit by tech
                //}

                _PartToHulls.Add(part.name, possibleHulls);
            }
        }

        public static void FillDatabase()
        {
            FillTechData();
            FillPartDatabase();
        }

        public static int GetYear(PartData data)
        {
            if (!_PartYears.TryGetValue(data.name, out var year))
                return -1;

            return year;
        }

        public static int GetGunYear(int caliberInch, int grade)
        {
            if (caliberInch < 0 || caliberInch >= _GunGradeYears.GetLength(0) || grade < 0 || grade >= _GunGradeYears.GetLength(1))
            {
                Melon<UADRealismMod>.Logger.Error($"Tried to get Gun Grade Year for caliber {caliberInch}, grade {grade}");
                return -1;
            }
            return _GunGradeYears[caliberInch, grade];
        }

        public static bool CanHullMountPart(PartData part, PartData hull)
        {
            if (!_PartToHulls.TryGetValue(part.name, out var hulls))
                return false;

            return hulls.Contains(hull.name);
        }

        public static List<PartData> GetHullsForPart(PartData part)
        {
            var lst = new List<PartData>();
            if (!_PartToHulls.TryGetValue(part.name, out var hulls))
                return lst;

            foreach (var h in hulls)
                lst.Add(G.GameData.parts[h]);

            return lst;
        }

        public static HashSet<string> GetHullNamesForPart(PartData part)
        {
            _PartToHulls.TryGetValue(part.name, out var hulls);
            return hulls;
        }
    }
}
