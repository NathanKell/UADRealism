using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;

#pragma warning disable CS8603

namespace TweaksAndFixes
{
    public class ComponentDataM
    {
        public static float GetWeight(ComponentData c, string sType)
        {
            if (!c.paramx.TryGetValue("weight_per", out var list))
                return c.weight;

            //Melon<TweaksAndFixes>.Logger.Msg($"Have weight_per, count is {list.Count}");

            // Annoyingly this is a flat list, not the multilist of a tech or part.
            // So we have to chain items.

            // Check there's an even number of entries, i.e. a bunch of key/value pairs
            int iC = list.Count;
            if (iC == 0 || (iC / 2) * 2 != iC)
                return c.weight;

            // Iterate through the pairs; if shiptype matches (key), return next item (value)
            --iC;
            for (int i = 0; i < iC; i += 2)
            {
                //Melon<TweaksAndFixes>.Logger.Msg($"Checking {list[i]} vs {sType} (weight would be {list[i + 1]}");
                if (list[i] == sType)
                {
                    var res = float.Parse(list[i + 1]);
                    //Melon<TweaksAndFixes>.Logger.Msg($"Type {list[i]}: returning {res}");
                    return res;
                }
            }

            return c.weight;
        }
    }
}