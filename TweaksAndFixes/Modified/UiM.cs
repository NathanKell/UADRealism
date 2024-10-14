using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

#pragma warning disable CS8600
#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public class UiM
    {
        public static void CheckForPeace(Ui _this)
        {
            int monthsForLowVPWarEnd = Config.Param("taf_war_max_months_for_low_vp_war", 12);
            int monthsForEconCollapse = Config.Param("taf_war_min_months_for_econ_collapse_peace", 24);
            float lowVPThreshold = Config.Param("taf_war_low_vp_threshold", 1000f);

            float peace_min_vp_difference = MonoBehaviourExt.Param("    peace_min_vp_difference", 10000f);
            float peace_enemy_vp_ratio = MonoBehaviourExt.Param("peace_enemy_vp_ratio", 2f);
            float peace_vp_sum_prolonged_war = MonoBehaviourExt.Param("peace_vp_sum_prolonged_war", 150000f);

            var CD = CampaignController.Instance.CampaignData;

            foreach (var rel in CD.Relations.Values)
            {
                if (!rel.isWar)
                    continue;

                Player a, b;
                float vpA, vpB;
                bool hasHuman;
                if (!rel.b.isAi)
                {
                    a = rel.b;
                    b = rel.a;
                    vpA = rel.victoryPointsB;
                    vpB = rel.victoryPointsA;
                    hasHuman = true;
                }
                else
                {
                    a = rel.a;
                    b = rel.b;
                    vpA = rel.victoryPointsA;
                    vpB = rel.victoryPointsB;
                    hasHuman = a.isAi;
                }

                // Early check: If the war has gone on for too long with no VP, just call for peace
                int turnsSinceStart = CampaignController.Instance.CurrentDate.MonthsPassedSince(rel.recentWarStartDate);

                if (vpA + vpB < lowVPThreshold && turnsSinceStart > monthsForLowVPWarEnd)
                {
                    _this.AskForPeace(hasHuman, rel, PlayerController.Instance, LocalizeManager.Localize("$TAF_Ui_War_WhitePeace"), vpA >= vpB);
                    continue;
                }

                int turnsSinceCheck = CampaignController.Instance.CurrentDate.MonthsPassedSince(rel.LastTreatyCheckDate);
                var checkThresh = rel.TreatyCheckMonthTreashold;
                if (checkThresh == 0)
                {
                    checkThresh = Config.Param("war_min_duration", 5);
                    rel.TreatyCheckMonthTreashold = checkThresh;
                }
                if (turnsSinceCheck < checkThresh)
                    continue;

                Player loserPlayer = null;
                if (Mathf.Abs(vpB - vpA) >= peace_min_vp_difference && Mathf.Max((vpB + 1f) / (vpA + 1f), (vpB + 1f) / (vpA + 1f)) >= peace_enemy_vp_ratio && vpA + vpB >= peace_vp_sum_prolonged_war)
                {
                    loserPlayer = vpB > vpA ? a : b;
                }
                else if (turnsSinceStart >= monthsForEconCollapse)
                {
                    var wgeA = a.WealthGrowthEffective();
                    var wgeB = b.WealthGrowthEffective();
                    if (wgeA <= 0)
                    {
                        if (wgeB <= 0)
                        {
                            var aRel = CampaignControllerM.GetAllianceRelation(a, b);

                            float vpAA, vpAB;
                            if (aRel.A.Players.Contains(a.data))
                            {
                                vpAA = aRel.vpA;
                                vpAB = aRel.vpB;
                            }
                            else
                            {
                                vpAA = aRel.vpB;
                                vpAB = aRel.vpA;
                            }
                            if (vpAA > vpAB)
                                loserPlayer = b;
                            else if (vpAA < vpAB)
                                loserPlayer = a;
                            else if (vpA > vpB)
                                loserPlayer = b;
                            else if (vpA < vpB)
                                loserPlayer = a;
                            else
                                loserPlayer = UnityEngine.Random.Range(0, 1) == 0 ? a : b;
                        }

                        if (vpB > vpA
                            || (CampaignControllerM.GetAllianceRelation(a, b) is AllianceRelation alRel
                                && (alRel.A.Players.Contains(a.data) ?
                                    (alRel.vpB > alRel.vpA)
                                    : (alRel.vpA > alRel.vpB))))
                        {
                            loserPlayer = a;
                        }
                    }
                    else if (wgeB <= 0)
                    {
                        if (vpA > vpB
                            || (CampaignControllerM.GetAllianceRelation(a, b) is AllianceRelation alRel
                                && (alRel.A.Players.Contains(a.data) ?
                                    (alRel.vpA > alRel.vpB)
                                    : (alRel.vpB > alRel.vpA))))
                        {
                            loserPlayer = a;
                        }
                    }
                }

                if (loserPlayer != null)
                {
                    if (loserPlayer == a)
                        _this.AskForPeace(hasHuman, rel, PlayerController.Instance, LocalizeManager.Localize("$Ui_World_TheWarIsNotGoingWellThe") + "{0} {1}" + LocalizeManager.Localize("$Ui_World_asksYouShouldAskUnfPeace"), false);
                    else
                        _this.AskForPeace(hasHuman, rel, PlayerController.Instance, LocalizeManager.Localize("$Ui_World_WeAreWinningSnThe") + "{0} {1}" + LocalizeManager.Localize("$Ui_World_desperAsksPeaceTreaty"), true);
                }
            }
        }
    }
}