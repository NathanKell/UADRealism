using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(BattleManager._UpdateLoadingMissionBuild_d__113))]
    internal class Patch_BattleManager_d115
    {

        // For some reason we can't access native nullables
        // so we have to cache off these custom and limit values
        // for armor and speed so they'll be accessible to our
        // patched AdjustHullStats method (see Ship GenerateRandomShip
        // coroutine patch).
        internal class BattleShipGenerationInfo
        {
            public bool isActive = false;
            public float limitArmor = -1f;
            public float limitSpeed = -1f;
            public float customSpeed = -1f;
            public float customArmor = -1f;

            public void Reset()
            {
                isActive = false;
                limitArmor = -1f;
                limitSpeed = -1f;
                customArmor = -1f;
                customSpeed = -1f;
            }
        }

        internal static readonly BattleShipGenerationInfo _ShipGenInfo = new BattleShipGenerationInfo();

        [HarmonyPatch(nameof(BattleManager._UpdateLoadingMissionBuild_d__113.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(BattleManager._UpdateLoadingMissionBuild_d__113 __instance, out int __state)
        {
            __state = __instance.__1__state;
            if (__state == 3 || __state == 5)
            {
                _ShipGenInfo.isActive = true;
                var cm = __instance.__4__this.CurrentAcademyMission;
                if (__instance._isEnemy_5__5)
                {
                    _ShipGenInfo.limitArmor = cm.easyArmor;
                    if (_ShipGenInfo.limitArmor < 0f)
                        _ShipGenInfo.limitArmor = cm.normalArmor;

                    _ShipGenInfo.limitSpeed = cm.easySpeed;
                    if (_ShipGenInfo.limitSpeed < 0f)
                        _ShipGenInfo.limitSpeed = cm.normalSpeed;
                    if (_ShipGenInfo.limitSpeed > 0f)
                        _ShipGenInfo.limitSpeed *= ShipM.KnotsToMS;

                    if (cm.paramx.TryGetValue("armor", out var cArm))
                        _ShipGenInfo.customArmor = float.Parse(cArm[0], ModUtils._InvariantCulture);
                    else
                        _ShipGenInfo.customArmor = -1f;

                    if (cm.paramx.TryGetValue("speed", out var cSpd))
                        _ShipGenInfo.customSpeed = float.Parse(cSpd[0], ModUtils._InvariantCulture) * ShipM.KnotsToMS;
                    else
                        _ShipGenInfo.customSpeed = -1f;
                }
            }
        }

        [HarmonyPatch(nameof(BattleManager._UpdateLoadingMissionBuild_d__113.MoveNext))]
        [HarmonyPostfix]
        internal static void Postfix_MoveNext(BattleManager._UpdateLoadingMissionBuild_d__113 __instance, int __state)
        {
            _ShipGenInfo.Reset();
        }
    }
}
