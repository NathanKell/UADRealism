using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using Il2CppSystem.Linq;

#pragma warning disable CS8602

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignController))]
    internal class Patch_CampaignController
    {
        // AI Scrapping patch
        // We replace the _entirety_ of the scrap logic by:
        // 1. Prefixing/postfixing AI Manage Fleet's MoveNext to set a bool that we're in that method
        // 2. Changing the predicate method that determines whether to scrap a ship to always report
        //      a single true, all others false (using the bool in 1, such that the scrap loop runs
        //      only once)
        // 3. Prefixing CampaignController.ScrapShip such that, when it runs, if the bool in 1 is set,
        //      our new scrap logic runs on the entire fleet of the player in question. To do that,
        //      we have to add a passthrough bool so we can still call scrap from that method.

        internal static bool _IsInManageFleet = false;
        internal static int _ManageFleetScrapShipsHit = 0;
        internal static bool _PassthroughScrap = false;

        [HarmonyPatch(nameof(CampaignController.GetSharedDesign))]
        [HarmonyPrefix]
        internal static bool Prefix_GetSharedDesign(CampaignController __instance, Player player, ShipType shipType, int year, bool checkTech, bool isEarlySavedShip, ref Ship __result)
        {
            __result = CampaignControllerM.GetSharedDesign(__instance, player, shipType, year, checkTech, isEarlySavedShip);
            return false;
        }

        [HarmonyPatch(nameof(CampaignController.ScrapShip))]
        [HarmonyPrefix]
        internal static bool Prefix_ScrapShip(CampaignController __instance, Ship ship, bool addCashAndCrew)
        {
            if (!Config.ScrappingChange || _PassthroughScrap || !_IsInManageFleet)
                return true;

            _PassthroughScrap = true;
            CampaignControllerM.HandleScrapping(__instance, ship.player);
            _PassthroughScrap = false;
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
    }

    [HarmonyPatch(typeof(CampaignController._AiManageFleet_d__169))]
    internal class Patch_CampaignController_co169
    {
        [HarmonyPatch(nameof(CampaignController._AiManageFleet_d__169.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(CampaignController._AiManageFleet_d__169 __instance)
        {
            Patch_CampaignController._IsInManageFleet = true;
            Patch_CampaignController._ManageFleetScrapShipsHit = 0;
        }

        [HarmonyPatch(nameof(CampaignController._AiManageFleet_d__169.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(CampaignController._AiManageFleet_d__169 __instance)
        {
            Patch_CampaignController._IsInManageFleet = false;
        }
    }
    
    [HarmonyPatch(typeof(CampaignController.__c__DisplayClass169_0))]
    internal class Patch_CampaignController_169
    {
        [HarmonyPatch(nameof(CampaignController.__c__DisplayClass169_0._AiManageFleet_b__3))]
        [HarmonyPrefix]
        internal static bool Prefix__AiManageFleet_b__3(CampaignController.__c__DisplayClass169_0 __instance, Ship s, ref bool __result)
        {
            if (!Config.ScrappingChange)
                return true;

            if (Patch_CampaignController._ManageFleetScrapShipsHit++ == 0)
                __result = true;
            else
                __result = false;

            return false;
        }
    }
}
