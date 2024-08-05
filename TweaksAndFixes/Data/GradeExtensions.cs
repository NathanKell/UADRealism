﻿//#define LOGHULLSTATS
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
    public static class GradeExtensions
    {
        public class GunDataExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.guns.TryGetValue(name, out var gd))
                    return;

                Serializer.Human.FillIndexedDicts(gd, param, true);

                int max = GetMaxGrade(gd);
                Config.MaxGunGrade = Math.Max(Config.MaxGunGrade, max);
            }

            public static int GetMaxGrade(GunData gd)
            {
                int max = 1;
                for (; ; ++max)
                {
                    if (gd.accuracies.ContainsKey(max)
                        && gd.barrelWeights.ContainsKey(max)
                        && gd.firerates.ContainsKey(max)
                        && gd.penetrations.ContainsKey(max)
                        && gd.ranges.ContainsKey(max)
                        && gd.shellVelocities.ContainsKey(max)
                        && gd.shellWeights.ContainsKey(max)
                        && gd.accuracies.ContainsKey(max))
                        continue;

                    break;
                }
                return max - 1;
            }

            public static void LoadData()
            {
                var lines = Serializer.CSV.TextAssetToLines("guns");
                if (lines != null)
                {
                    List<GunDataExtension> list = new List<GunDataExtension>();
                    Serializer.CSV.Read<List<GunDataExtension>, GunDataExtension>(lines, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var gd in G.GameData.guns.Values)
                {
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.barrelWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.firerates, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.penetrations, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.ranges, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellVelocities, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.shellWeights, Config.MaxGunGrade);
                    ModUtils.FillGradeData(gd.accuracies, Config.MaxGunGrade);
                }
                GunData.gradesTotal = Config.MaxGunGrade;
            }
        }

        public class PartModelExtension : Serializer.IPostProcess
        {
            [Serializer.Field] string name;
            [Serializer.Field] string param;

            public void PostProcess()
            {
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(param))
                    return;

                if (!G.GameData.partModels.TryGetValue(name, out var pm))
                    return;

                Serializer.Human.FillIndexedDicts(pm, param, true);
            }

            public static void LoadData()
            {
                var lines = Serializer.CSV.TextAssetToLines("partModels");
                if (lines != null)
                {
                    List<PartModelExtension> list = new List<PartModelExtension>();
                    Serializer.CSV.Read<List<PartModelExtension>, PartModelExtension>(lines, list, true, true);
                }

                // We need to ensure that everything goes up to the max grade.
                foreach (var pm in G.GameData.partModels.Values)
                {
                    // Skip partmodels that aren't guns
                    if (pm.caliberLengthModifiers.count < 5)
                        continue;

                    ModUtils.FillGradeData(pm.caliberLengthModifiers, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.maxScales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.models, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.scales, Config.MaxGunGrade);
                    ModUtils.FillGradeData(pm.weightModifiers, Config.MaxGunGrade);
                }
            }
        }

        public static void LoadData()
        {
            GunDataExtension.LoadData();
            PartModelExtension.LoadData();
        }
    }
}