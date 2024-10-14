using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(PlayerController))]
    internal class Patch_PlayerController
    {
        [HarmonyPatch(nameof(PlayerController.CloneShipRaw))]
        [HarmonyPostfix]
        internal static void Postfix_CloneShipRaw(Ship from, ref Ship __result)
        {
            __result.TAFData().OnClonePost(from.TAFData());
        }

        //[HarmonyPatch(nameof(PlayerController.CloneDesign), new Type[] { typeof(Ship) })]
        //[HarmonyPostfix]
        internal static void Postfix_CloneDesign(Ship ship)
        {
            if (PlayerController.Instance == null || PlayerController.Instance.Ship == null)
                return;

            var clone = PlayerController.Instance.Ship;
            clone.NeedRecalcCache();
            if (clone.matsCache != null)
                clone.matsCache.Clear();
            clone.statsValid = false;
            clone.techCache.Clear();

            ship.NeedRecalcCache();
            if (ship.matsCache != null)
                ship.matsCache.Clear();
            ship.statsValid = false;
            ship.techCache.Clear();

            string log = $"Cloned {ship.Name()}.\nOrig weight {ship.Weight():N0}t from {ship.tonnage:N0} ({ship.Tonnage():N0})\nNew weight {clone.Weight():N0}t.from {clone.tonnage:N0} ({clone.Tonnage():N0}).\nMissing techs:";
            foreach (var t in ship.techs)
                if (!clone.techs.Contains(t))
                    log += "\n" + t.name;
            foreach (var t in clone.techs)
                if (!ship.techs.Contains(t))
                    log += "\n" + t.name;

            log += "\nMissing TechsActual:";
            foreach (var t in ship.techsActual)
                if (!clone.techsActual.Contains(t))
                    log += "\n" + t.name;
            foreach (var t in clone.techsActual)
                if (!ship.techsActual.Contains(t))
                    log += "\n" + t.name;

            log += "\nMissing techsList:";
            foreach (var t in ship.techsList)
                if (!clone.techsList.Contains(t))
                    log += "\n" + t.name;
            foreach (var t in clone.techsList)
                if (!ship.techsList.Contains(t))
                    log += "\n" + t.name;

            log += "\nTechLevels";
            foreach (var kvp in ship.techLevels)
                if (!clone.techLevels.TryGetValue(kvp.Key, out var other))
                    log += $"\nClone lacks {kvp.Key}";
                else if (other != kvp.Value)
                    log += $"\nMismatch: {kvp.Key}: {kvp.Value} vs {other}";
            foreach (var kvp in clone.techLevels)
                if (!ship.techLevels.TryGetValue(kvp.Key, out var other))
                    log += $"\nOrig lacks {kvp.Key}";
                else if (other != kvp.Value)
                    log += $"\nMismatch: {kvp.Key}: {other} vs {kvp.Value}";

            log += "\nComponents";
            foreach (var kvp in ship.components)
                if (!clone.components.TryGetValue(kvp.Key, out var other))
                    log += $"\nClone lacks {kvp.Key.name}";
                else if (other != kvp.Value)
                    log += $"\nMismatch: {kvp.Value.name} vs {other.name}";
            foreach (var kvp in clone.components)
                if (!ship.components.TryGetValue(kvp.Key, out var other))
                    log += $"\nOrig lacks {kvp.Key.name}";
                else if (other != kvp.Value)
                    log += $"\nMismatch: {other.name} vs {kvp.Value.name}";

            log += "\nArmor";
            foreach (var kvp in ship.armor)
                if (!clone.armor.TryGetValue(kvp.Key, out var other))
                    log += $"\nClone lacks {kvp.Key}";
                else if (Mathf.RoundToInt(other * 100) != Mathf.RoundToInt(100 * kvp.Value))
                    log += $"\nMismatch: {kvp.Key}: {kvp.Value:F3} vs {other:F3}";
            foreach (var kvp in clone.armor)
                if (!ship.armor.TryGetValue(kvp.Key, out var other))
                    log += $"\nOrig lacks {kvp.Key}";
                else if (Mathf.RoundToInt(100 * other) != Mathf.RoundToInt(100 * kvp.Value))
                    log += $"\nMismatch: {kvp.Key}: {other:F3} vs {kvp.Value:F3}";

            if (ship.hull.isShown != clone.hull.isShown)
                log += $"\nHull shown mismatch: {ship.hull.isShown} vs {clone.hull.isShown}";

            if (ship.parts.Count != clone.parts.Count)
            {
                log += $"\nPart countmismatch: {ship.parts.Count} vs {clone.parts.Count}";
            }
            else
            {
                for (int i = 0; i < ship.parts.Count; ++i)
                {
                    var ps = ship.parts[i];
                    var pc = clone.parts[i];
                    if (ps.isShown != pc.isShown)
                    {
                        log += $"\nPart shown mismatch on {ps.data.name}: {ps.isShown} vs {pc.isShown}";
                    }
                }
            }
            log += $"\nTechR hull: {ship.TechR("hull"):F2}, {clone.TechR("hull"):F2}. Sum {ship.TechSum("hull"):F2}, {clone.TechSum("hull"):F2}\nCit length {(ship.GetDynamicCitadelMaxZ(false, false) - ship.GetDynamicCitadelMinZ(false, false)):N1}, {(clone.GetDynamicCitadelMaxZ(false, false) - clone.GetDynamicCitadelMinZ(false, false)):N1}";
            Melon<TweaksAndFixes>.Logger.Msg(log);
        }
    }
}
