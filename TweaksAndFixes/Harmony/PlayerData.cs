using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(PlayerData))]
    internal class Patch_PlayerData
    {
        // Eventually patch here rather than prefix PostProcessAll?
        //[HarmonyPatch(nameof(PlayerData.PostProcess))]
        //[HarmonyPostfix]
        //internal static void Postfix_Flag(PlayerData __instance)
        //{
        //}

        internal static void PatchPlayerMaterials()
        {
            var gameData = G.GameData;
            if (!gameData.players.TryGetValue("britain", out var refData) || refData.type != "major")
            {
                foreach (var pd in gameData.players.Values)
                {
                    if (pd.type == "major" && pd.PlayerMaterial != null)
                    {
                        refData = pd;
                        break;
                    }
                }
            }
            if (refData != null)
            {
                foreach (var pd in gameData.players.Values)
                {
                    if (pd.type == "major" && pd.PlayerMaterial == null)
                    {
                        Melon<TweaksAndFixes>.Logger.Msg($"Applying major-player material to {pd.name}");
                        pd.PlayerMaterial = new Material(refData.PlayerMaterial);
                        var col = pd.highlightColor.ChangeA(0.25f);
                        pd.PlayerMaterial.color = col;
                    }
                }
            }
        }
    }
}
