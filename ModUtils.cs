using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    public static class ModUtils
    {
        // Reimplementation of stock function
        public static void FindChildrenStartsWith(GameObject obj, string str, List<GameObject> list)
        {
            if (obj.name.StartsWith(str))
                list.Add(obj);
            for (int i = 0; i < obj.transform.childCount; ++i)
                FindChildrenStartsWith(obj.transform.GetChild(i).gameObject, str, list);
        }

        public static double Lerp(double a, double b, double t, bool clamp = true)
        {
            if (clamp)
            {
                if (t <= 0)
                    return a;
                if (t >= 1)
                    return b;
            }

            return a + (b - a) * t;
        }

        public static double InverseLerp(double a, double b, double value, bool clamp = true)
        {
            if (clamp)
            {
                if (value <= a)
                    return 0d;
                if (value >= b)
                    return 1d;
            }

            return (value - a) / (b - a);
        }
    }
}
