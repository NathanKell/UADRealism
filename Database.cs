using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

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
                                _GunGradeTechs[cal, grade] = kvpT.Key;
                                _GunGradeYears[cal, grade] = kvpT.Value.year;
                            }
                            break;
                    }
                }
                if (kvpT.Value.componentx != null)
                {
                    _ComponentTechs[kvpT.Value.componentx.name] = kvpT.Key;
                    _ComponentYears[kvpT.Value.componentx.name] = kvpT.Value.year;
                }
            }
        }

        public static void FillDatabase()
        {
            FillTechData();
        }

        public static int GetYear(PartData data)
        {
            if (!_PartYears.TryGetValue(data.name, out var year))
                return -1;

            return year;
        }

        public static int GetGunYear(int caliberInch, int grade)
        {
            return _GunGradeYears[caliberInch, grade];
        }
    }
}
