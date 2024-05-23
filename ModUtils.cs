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
                    hierarchy += ": " + goBounds.min + "-" + goBounds.max;

                hierarchy += $" {go.transform.position}x{go.transform.localScale}";

                int rCount = go.GetComponents<Renderer>().Length;
                int mCount = go.GetComponents<MeshFilter>().Length;
                if (rCount > 0 || mCount > 0)
                    hierarchy += ". R.";
                //hierarchy += $". R={rCount}, M={mCount}";

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

        public static GameObject FindDeepChild(this GameObject obj, string name)
        {
            if (obj.name == name)
                return obj;

            for (int i = 0; i < obj.transform.childCount; ++i)
            {
                var test = obj.transform.GetChild(i).gameObject.FindDeepChild(name);
                if (test != null)
                    return test;
            }

            return null;
        }

        // Returns a smoothed distribution, i.e.
        // Random.Range(-range, range) + Random.Range(-range, range)...
        // divided by steps
        public static float DistributedRange(float range, int steps = 2)
        {
            float val = 0f;
            for (int i = steps; i-- > 0;)
            {
                val += UnityEngine.Random.Range(-range, range);
            }
            return val / steps;
        }

        // Returns a smoothed distribution, i.e.
        // Random.Range(-range, range) + Random.Range(-range, range)...
        // divided by steps
        public static int DistributedRange(int range, int steps = 2)
        {
            int val = 0;
            for (int i = steps; i-- > 0;)
            {
                val += UnityEngine.Random.Range(-range, range);
            }
            return val / steps;
        }

        public static float Range(float a, float b, System.Random rnd = null)
        {
            if (rnd == null)
                return UnityEngine.Random.Range(a, b);

            return (float)rnd.NextDouble() * (b - a) + a;
        }

        public static T RandomByWeights<T>(Dictionary<T, float> dictionary, System.Random rnd = null)
        {
            if (dictionary.Count == 0)
                return default(T);
            float sum = 0f;
            foreach (var kvp in dictionary)
            {
                if (kvp.Value < 0f)
                    continue;

                sum += kvp.Value;
            }
            if (sum == 0f)
                return default(T);

            float selector = Range(0f, sum, rnd);
            float curSum = 0f;
            foreach (var kvp in dictionary)
            {
                float val = kvp.Value;
                if (val < 0f)
                    val = 0f;
                curSum += val;

                if (selector > curSum)
                    continue;

                return kvp.Key;
            }

            // will never hit this, because selector can't be > sum.
            // But VS complains not all paths return a value without it, heh.
            return default(T);
        }

        private static List<int> _ShuffleIndices = new List<int>();
        private static List<int> _ShuffleRemainingOptions = new List<int>();
        public static void Shuffle<T>(this List<T> list)
        {
            int iC = list.Count;
            for (int i = 0; i < iC; ++i)
                _ShuffleRemainingOptions.Add(i);

            for (int i = 0; i < iC; ++i)
            {
                int idx = UnityEngine.Random.Range(0, _ShuffleRemainingOptions.Count - 1);
                _ShuffleIndices.Add(_ShuffleRemainingOptions[idx]);
                _ShuffleRemainingOptions.RemoveAt(idx);
            }

            // Slightly wasteful, but this ensures
            // we hit all elements.
            for (int i = 0; i < iC; ++i)
                ShuffleEx(list, i);

            _ShuffleIndices.Clear();
            _ShuffleRemainingOptions.Clear();
        }

        private static void ShuffleEx<T>(List<T> list, int idx)
        {
            if (_ShuffleIndices[idx] == -1)
                return;

            if (_ShuffleIndices[idx] == idx)
            {
                _ShuffleIndices[idx] = -1;
                return;
            }

            int desired = _ShuffleIndices[idx];
            _ShuffleIndices[idx] = -1;
            T elem = list[idx];
            ShuffleEx(list, desired);
            list[desired] = elem;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class LogMB : MonoBehaviour
    {
        public LogMB(IntPtr ptr) : base(ptr) { }

        public void OnDestroy()
        {
            Debug.Log($"$$$$ Destroying {gameObject.name}. Stack trace:\n{Environment.StackTrace}");
        }
    }
}
