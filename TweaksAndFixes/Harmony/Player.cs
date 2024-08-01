using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(Player))]
    internal class Patch_Player
    {
        [HarmonyPatch(nameof(Player.Flag), new Type[] { typeof(PlayerData), typeof(bool), typeof(Player), typeof(int) })]
        [HarmonyPrefix]
        internal static bool Prefix_Flag(PlayerData data, bool naval, Player player, int newYear, ref Sprite __result)
        {
            var newSprite = FlagDatabase.Instance.GetFlag(data, naval, player, newYear);
            if (newSprite != null)
            {
                __result = newSprite;
                return false;
            }

            return true;
        }
    }
}
