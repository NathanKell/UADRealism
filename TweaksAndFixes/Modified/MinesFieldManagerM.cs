using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using System.Security;

#pragma warning disable CS8600
#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public class MinesFieldManagerM
    {
        public static float DamageTaskForce(MinesFieldManager _this, CampaignController.TaskForce taskForce, Player mineFieldOwner, float minefieldRadiusKm, float damageMultiplier = 1f)
        {
            var rel = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, mineFieldOwner, taskForce.Controller);
            var sweepPrevent = taskForce.MineSweepingPrevention();
            var detectVal = taskForce.AverageMineDetectValue();
            var fleetFactor = MonoBehaviourExt.Param("minefield_fleet_factor", 100000f);

            float factor;
            float tonnage;
            if (rel.isWar)
            {
                factor = fleetFactor * -2f * detectVal;
                tonnage = taskForce.BattleTonnage() * 5f;
            }
            else
            {
                factor = fleetFactor * -2800f * detectVal;
                tonnage = taskForce.BattleTonnage() * 0.04f;
            }

            float randFactor = ModUtils.Range(factor, tonnage * minefieldRadiusKm / detectVal);
            if (MonoBehaviourExt.Param("minefield_chance_factor", 150000f) >= randFactor)
                return 0f;
            float remappedRand = Util.Remap(randFactor, 0f, 300000f, 0f, 10f, false);
            int shipsDamaged = Mathf.RoundToInt(remappedRand);
            taskForce.DamagedWithMinesTurn = CampaignController.Instance.CurrentDate.turn;

            float shipDmgFactor = MonoBehaviourExt.Param("minefield_dmg_factor_ships", 0.1f);
            float crewDmgFactor = MonoBehaviourExt.Param("minefield_dmg_factor_crew", 0.1f);

            float totalDisp = 0f;
            for (int i = taskForce.Vessels.Count; i-- > 0 && shipsDamaged > 0;)
            {
                var curV = taskForce.Vessels[i];
                float antimine = curV.TechR("antimine");

                float clampedAnti = Mathf.Clamp(antimine, 0.05f, 10f);
                float damageShip = ModUtils.Range(1f, 200f) * shipDmgFactor * clampedAnti * damageMultiplier;
                float damageCrew = ModUtils.Range(0.05f, 0.35f) * crewDmgFactor * clampedAnti * damageMultiplier;

                var name = curV.Name(false, false, false, false, false);
                Debug.LogFormat("[Damage] Mines damage vessel {0} damage {1}", name, damageShip.ToString("F2"));
                curV.HP -= damageShip;
                curV.CrewPercents -= damageCrew;

                totalDisp += curV.Weight();

                if (curV.HP <= 0f || curV.CrewPercents <= 0f)
                {
                    curV.SetStatus(VesselEntity.Status.Sunk);
                    curV.CrewPercents = 0f;
                    curV.CrewTrainingAmount = 0f;
                    CampaignController.Instance.CampaignData.RemoveVesselFromTaskForce(curV);
                    damageShip = 100f;
                    damageCrew = 1f;
                }

                _this.AddInfo(rel.isWar ? _this.enemyMineFieldDamage : _this.friendlyMineFieldDamage, curV, damageShip, damageCrew);
            }
            return 0f;
        }

        public static void MineHitSpenting(MinesFieldManager _this, float displacement, MinesField minefield)
        {
            float param = MonoBehaviourExt.Param("minefield_hit_spenting", 0.02f);
            float remapped = Util.Remap(param * displacement, 0f, 7000f, 0f, 400f, false);
            minefield.ChangeRadius(-remapped, 1f);
        }

        public static void Minesweep(MinesFieldManager _this, MinesField minefield, CampaignController.TaskForce taskForce)
        {
            var rel = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, minefield.GetPlayer(), taskForce.Controller);
            if (rel == null || !rel.isWar)
                return;

            minefield.ChangeRadius(-TaskForceM.GetMinesweepingCapacity(taskForce), 1f);

        }
    }
}