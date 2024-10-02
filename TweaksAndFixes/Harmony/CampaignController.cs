using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using Il2CppSystem.Linq;

#pragma warning disable CS8602
#pragma warning disable CS8604

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignController))]
    internal class Patch_CampaignController
    {
        internal static bool _IsInManageFleet = false;

        [HarmonyPatch(nameof(CampaignController.GetSharedDesign))]
        [HarmonyPrefix]
        internal static bool Prefix_GetSharedDesign(CampaignController __instance, Player player, ShipType shipType, int year, bool checkTech, bool isEarlySavedShip, ref Ship __result)
        {
            __result = CampaignControllerM.GetSharedDesign(__instance, player, shipType, year, checkTech, isEarlySavedShip);
            return false;
        }

        // We're going to cache off relations before the adjustment
        // and then check for changes.
        internal struct RelationInfo
        {
            public bool isWar;
            public bool isAlliance;
            public float attitude;
            public bool isValid;
            public List<Player>? alliesA;
            public List<Player>? alliesB;

            public RelationInfo(Relation old)
            {
                isValid = true;

                isWar = old.isWar;
                isAlliance = old.isAlliance;
                attitude = old.attitude;

                // Hopefully the perf hit of the GC alloc is balanced
                // by doing it native (we could avoid the alloc by finding
                // these players, but it'd be in managed code)
                alliesA = new List<Player>();
                foreach (var p in old.a.InAllianceWith().ToList())
                    alliesA.Add(p);
                alliesB = new List<Player>();
                foreach (var p in old.b.InAllianceWith().ToList())
                    alliesB.Add(p);
            }

            public RelationInfo()
            {
                isValid = false;
                isWar = isAlliance = false;
                attitude = 0;
                alliesA = alliesB = null;
            }
        }
        private static bool _PassThroughAdjustAttitude = false;
        [HarmonyPatch(nameof(CampaignController.AdjustAttitude))]
        [HarmonyPrefix]
        internal static void Prefix_AdjustAttitude(CampaignController __instance, Relation relation, float attitudeDelta, bool canFullyAdjust, bool init, string info, bool raiseEvents, bool force, bool fromCommonEnemy, out RelationInfo __state)
        {
            if (init || _PassThroughAdjustAttitude || !Config.AllianceTweaks)
            {
                __state = new RelationInfo();
                return;
            }

            __state = new RelationInfo(relation);
        }

        [HarmonyPatch(nameof(CampaignController.AdjustAttitude))]
        [HarmonyPostfix]
        internal static void Postfix_AdjustAttitude(CampaignController __instance, Relation relation, float attitudeDelta, bool canFullyAdjust, bool init, string info, bool raiseEvents, bool force, bool fromCommonEnemy, RelationInfo __state)
        {
            if (init || !__state.isValid)
                return;

            // Don't cascade. AdjustAttitude calls itself a bunch of times.
            // If we're applying relation-change events, don't rerun for each
            // sub-call of this.
            _PassThroughAdjustAttitude = true;
            if (relation.isWar != __state.isWar)
            {
                if (__state.isWar)
                {
                    // at peace now
                    // *** Commented out for now.
                    // Eventually want to have alliance leaders make peace
                    // (except for the human player). But until that code's
                    // written, no point in removing the player from alliances
                    // because the game already does that.

                    // check if the human is allied to either
                    // and is at war too. If so, break the alliance.
                    // (We don't force the player into peace.)
                    //for (int i = __state.alliesA.Count; i-- > 0;)
                    //{
                    //    Player p = __state.alliesA[i];
                    //    if (!p.isAi)
                    //    {
                    //        var relA = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                    //        var relB = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                    //        if (relA.isAlliance && relB.isWar) // had better be true
                    //        {
                    //            __instance.AdjustAttitude(relA, -relA.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                    //            __state.alliesA.RemoveAt(i);
                    //        }
                    //        break;
                    //    }
                    //}
                    //for (int i = __state.alliesB.Count; i-- > 0;)
                    //{
                    //    Player p = __state.alliesB[i];
                    //    if (!p.isAi)
                    //    {
                    //        var relA = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                    //        var relB = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                    //        if (relB.isAlliance && relA.isWar) // had better be true
                    //        {
                    //            __instance.AdjustAttitude(relB, -relB.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                    //            __state.alliesB.RemoveAt(i);
                    //        }
                    //        break;
                    //    }
                    //}

                    // TODO: Do we want to have strongest nations sign for all others?
                }
                else
                {
                    // at war now

                    // First, find overlapping allies. They break
                    // both alliances.
                    for (int i = __state.alliesA.Count; i-- > 0;)
                    {
                        Player p = __state.alliesA[i];
                        for (int j = __state.alliesB.Count; j-- > 0;)
                        {
                            if (__state.alliesB[j] == p)
                            {
                                __state.alliesA.RemoveAt(i);
                                __state.alliesB.RemoveAt(j);
                                var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                                if (rel.isAlliance) // had better be true
                                    __instance.AdjustAttitude(rel, -rel.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                                rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                                if (rel.isAlliance)
                                    __instance.AdjustAttitude(rel, -rel.attitude, true, false, info, raiseEvents, true, fromCommonEnemy);
                                break;
                            }
                        }
                    }

                    // All other allies declare war
                    foreach (var p in __state.alliesA)
                    {
                        var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.b);
                        if (!rel.isWar)
                            __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                    }
                    foreach (var p in __state.alliesB)
                    {
                        var rel = RelationExt.Between(__instance.CampaignData.Relations, p, relation.a);
                        if (!rel.isWar)
                            __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                    }
                    // Allies declare war on each other
                    for (int i = __state.alliesA.Count; i-- > 0;)
                    {
                        Player a = __state.alliesA[i];
                        for (int j = __state.alliesB.Count; j-- > 0;)
                        {
                            Player b = __state.alliesB[i];
                            var rel = RelationExt.Between(__instance.CampaignData.Relations, a, b);
                            if (!rel.isWar)
                                __instance.AdjustAttitude(rel, -200f, true, false, info, raiseEvents, true, fromCommonEnemy);
                        }
                    }
                }
            }

            _PassThroughAdjustAttitude = false;
        }

        [HarmonyPatch(nameof(CampaignController.AiManageFleet))]
        [HarmonyPrefix]
        internal static void Prefix_AiManageFleet(CampaignController __instance, Player player, out float __state)
        {
            _IsInManageFleet = true;
            if (Config.ScrappingChange && !player.isDisabled && player.isAi)
            {
                CampaignControllerM.HandleScrapping(__instance, player);
                // we need to disable stock scrapping
                __state = Config.Param("min_fleet_tonnage_for_scrap", 1f);
                G.GameData.parms["min_fleet_tonnage_for_scrap"] = float.MaxValue;
            }
            else
            {
                __state = 0f;
            }
        }

        [HarmonyPatch(nameof(CampaignController.AiManageFleet))]
        [HarmonyPostfix]
        internal static void Postfix_AiManageFleet(CampaignController __instance, Player player, float __state)
        {
            _IsInManageFleet = false;
            if (Config.ScrappingChange && !player.isDisabled && player.isAi)
            {
                G.GameData.parms["min_fleet_tonnage_for_scrap"] = __state;
            }
        }

        [HarmonyPatch(nameof(CampaignController.CheckPredefinedDesigns))]
        [HarmonyPrefix]
        internal static void Prefix_CheckPredefinedDesigns(CampaignController __instance)
        {
            if (__instance._currentDesigns != null)
                return;

            string path = Path.Combine(Config._BasePath, Config._PredefinedDesignsFile);
            if (!File.Exists(path))
                return;

            var bytes = File.ReadAllBytes(path);
            var store = Util.DeserializeObjectByte<CampaignDesigns.Store>(bytes);
            int dCount = 0;
            bool isValid = true;
            if (store.shipsPerYear == null)
            {
                isValid = false;
                Debug.LogError("spy");
            }
            else
            {
                foreach (var spy in store.shipsPerYear)
                {
                    if (spy.Value.shipsPerPlayer == null)
                    {
                        isValid = false;
                        Debug.LogError(spy.Key + ": spp");
                    }
                    else
                    {
                        foreach (var spp in spy.Value.shipsPerPlayer)
                        {
                            if (spp.Value.shipsPerType == null)
                            {
                                isValid = false;
                                Debug.LogError(spp.Key + ": spp");
                            }
                            else
                            {
                                foreach (var spt in spp.Value.shipsPerType)
                                {
                                    if (spt.Value == null)
                                    {
                                        isValid = false;
                                        Debug.LogError(spt.Key + ": spt");
                                    }
                                    else
                                        dCount += spt.Value.Count;
                                }
                            }
                        }
                    }
                }
            }

            if (isValid)
            {
                __instance._currentDesigns = CampaignDesigns.FromStore(store);
                Melon<TweaksAndFixes>.Logger.Msg($"Overrode predefined designs by loading {dCount} ships from {Config._PredefinedDesignsFile}");
            }
            else
            {
                Melon<TweaksAndFixes>.Logger.Error($"Tried to override predefined designs ");
            }
        }
    }
}
