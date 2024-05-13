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

        public static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private struct ObjectStack
        {
            public GameObject obj;
            public int depth;

            public ObjectStack(GameObject go, int d)
            {
                obj = go;
                depth = d;
            }
        }

        private static readonly List<ObjectStack> _ObjectHierarchyStack = new List<ObjectStack>();

        public static string DumpHierarchy(GameObject obj)
        {

            _ObjectHierarchyStack.Add(new ObjectStack(obj, 0));
            string hierarchy = "hierarchy:";
            while (_ObjectHierarchyStack.Count > 0)
            {
                int max = _ObjectHierarchyStack.Count - 1;
                var tuple = _ObjectHierarchyStack[max];
                var go = tuple.obj;
                int depth = tuple.depth;
                _ObjectHierarchyStack.RemoveAt(max);
                hierarchy += "\n";
                for (int i = 0; i < depth; ++i)
                {
                    hierarchy += "--";
                }
                hierarchy += " " + go.name;

                var rends = go.GetComponentsInChildren<Renderer>();
                Bounds goBounds = new Bounds();
                bool needBounds = true;
                for (int i = 0; i < rends.Length; ++i)
                {
                    if (rends[i] == null || !rends[i].enabled)
                        continue;

                    if (needBounds)
                    {
                        goBounds = rends[i].bounds;
                        needBounds = false;
                    }
                    else
                    {
                        goBounds.Encapsulate(rends[i].bounds);
                    }
                }

                if (!needBounds)
                    hierarchy += ": " + goBounds.size + " @ " + goBounds.center;

                int rCount = go.GetComponents<Renderer>().Length;
                int mCount = go.GetComponents<MeshFilter>().Length;
                hierarchy += $". R={rCount}, M={mCount}";

                ++depth;
                for (int i = 0; i < go.transform.childCount; ++i)
                {
                    var subT = go.transform.GetChild(i);
                    if (subT == null)
                        continue;
                    var sub = subT.gameObject;
                    if (sub == null)
                        continue;
                    if (sub.activeSelf)
                        _ObjectHierarchyStack.Add(new ObjectStack(sub, depth));
                }
            }

            return hierarchy;
        }
    }
}
