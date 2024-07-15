using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class ModUtils
    {
        // Due to an Il2Cpp interop issue, you can't actually pass null nullables, you have to pass
        // nullables _with no value_. So we're going to just store a bunch of statics here we can use
        // instead of allocating each time.
        public static Il2CppSystem.Nullable<int>  _NullableEmpty_Int = new Il2CppSystem.Nullable<int>();

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
        public static float DistributedRange(float range, int steps = 2, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            float val = 0f;
            for (int i = steps; i-- > 0;)
            {
                val += Range(-range, range, rnd, nativeRnd);
            }
            return val / steps;
        }

        public static float DistributedRange(float range, Il2CppSystem.Random rnd)
            => DistributedRange(range, 2, null, rnd);

        public static float DistributedRange(int steps, Il2CppSystem.Random rnd)
            => DistributedRange(1f, steps, null, rnd);

        // Biases a random number in the range [-1, 1]
        // so the midpoint is at the bias
        public static float BiasRange(float randomNum, float bias)
        {
            if (randomNum > 0f)
                randomNum *= 1f - bias;
            else
                randomNum *= 1f + bias;

            return randomNum + bias;
        }

        public static int RangeToInt(float input, int size)
        {
            input += 1f;
            input *= 0.5f;
            // Now in range 0-1
            int result = (int)(input * size);
            // Catch if it was -1 to 1 _inclusive_
            if (result >= size)
                result = size - 1;

            return result;
        }

        // Returns a smoothed distribution across an integer range, i.e.
        // Random.Range(-range, range) + Random.Range(-range, range)...
        // divided by steps. Note: done as float and remapped.
        public static int DistributedRange(int range, int steps = 2, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
            => RangeToInt(DistributedRange(1f, steps, rnd, nativeRnd), range * 2 + 1) - range;

        public static float DistributedRangeWithStepSize(float range, float stepSize, int steps, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            float numSteps = range / stepSize;
            int intSteps = (int)numSteps;
            if (numSteps - intSteps < 0.001f) // catch float imprecision
                ++intSteps;

            int val = DistributedRange(intSteps, steps, rnd, nativeRnd);
            return val * stepSize;
        }

        public static float Range(float a, float b, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            if (nativeRnd != null)
                return (float)nativeRnd.NextDouble() * (b - a) + a;

            if (rnd == null)
                return UnityEngine.Random.Range(a, b);

            return (float)rnd.NextDouble() * (b - a) + a;
        }

        public static int Range(int minInclusive, int maxInclusive, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            if (minInclusive == maxInclusive)
                return minInclusive;

            if (nativeRnd != null)
                return (int)(nativeRnd.NextDouble() * (maxInclusive - minInclusive + 1)) + minInclusive;

            if (rnd == null)
                return UnityEngine.Random.Range(minInclusive, maxInclusive + 1);

            return (int)(rnd.NextDouble() * (maxInclusive - minInclusive + 1)) + minInclusive;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;

            return value;
        }

        public static float ClampWithStep(float val, float stepSize, float minVal, float maxVal)
        {
            int stepCount = Mathf.RoundToInt(val / stepSize);
            float steppedVal = stepCount * stepSize;
            if (steppedVal < minVal)
                ++stepCount;
            else if (steppedVal > maxVal)
                --stepCount;
            return stepCount * stepSize;
        }

        public static T RandomOrNull<T>(this List<T> items, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null) where T : class
        {
            int iC = items.Count;
            if (iC == 0)
                return null;
            return items[Range(0, iC - 1, rnd, nativeRnd)];
        }

        public static T Random<T>(this Il2CppSystem.Collections.Generic.List<T> items, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            return items[Range(0, items.Count - 1, rnd, nativeRnd)];
        }

        public static T RandomOrNull<T>(this Il2CppSystem.Collections.Generic.List<T> items, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null) where T : class
        {
            int iC = items.Count;
            if (iC == 0)
                return null;
            return items[Range(0, iC - 1, rnd, nativeRnd)];
        }

        public static T Random<T>(this List<T> items, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null)
        {
            return items[Range(0, items.Count - 1, rnd, nativeRnd)];
        }

        public static T RandomByWeights<T>(Dictionary<T, float> dictionary, System.Random rnd = null, Il2CppSystem.Random nativeRnd = null) where T : notnull
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

            float selector = Range(0f, sum, rnd, nativeRnd);
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

        private readonly static List<int> _ShuffleIndices = new List<int>();
        private readonly static List<int> _ShuffleRemainingOptions = new List<int>();
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

        public static float RoundToStep(float val, float step)
            => Mathf.RoundToInt(val / step) * step;

        public static string ArmorString(Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> armor)
        {
            //return $"{armor[Ship.A.Belt]:F1}/{armor[Ship.A.BeltBow]:F1}/{armor[Ship.A.BeltStern]:F1}, {armor[Ship.A.Deck]:F1}/{armor[Ship.A.DeckBow]:F1}/{armor[Ship.A.DeckStern]:F1} "
            //    + $"{armor[Ship.A.ConningTower]:F1}/{armor[Ship.A.Superstructure]:F1}, {armor[Ship.A.TurretSide]:F1}/{armor[Ship.A.TurretTop]:F1}/{armor[Ship.A.Barbette]:F1}, "
            //    + $"{armor[Ship.A.InnerBelt_1st]:F1}/{armor[Ship.A.InnerBelt_2nd]:F1}/{armor[Ship.A.InnerBelt_3rd]:F1}, {armor[Ship.A.InnerDeck_1st]:F1}/{armor[Ship.A.InnerDeck_2nd]:F1}/{armor[Ship.A.InnerDeck_3rd]:F1}";
            string s = "Armor:";
            bool first = true;
            foreach (var kvp in armor)
            {
                if (first)
                    first = false;
                else
                    s += ",";
                s += $" {kvp.Key}={kvp.Value:F1}";
            }
            return s;
        }

        public static float ArmorValue(this Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float> armor, Ship.A key)
        {
            armor.TryGetValue(key, out var val);
            return val;
        }

        public static float ArmorValue(this Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<Ship.A, float>> armor, Ship.A key)
        {
            foreach (var kvp in armor)
                if (kvp.Key == key)
                    return kvp.Value;

            return 0f;
        }

        public static void SetValue(this Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<Ship.A, float>> armor, Ship.A key, float value)
        {
            for (int i = armor.Count; i-- > 0;)
            {
                if (armor[i].Key == key)
                {
                    armor[i] = new Il2CppSystem.Collections.Generic.KeyValuePair<Ship.A, float>(key, value);
                    return;
                }
            }
        }

        private static List<string> HumanModToList(string input, out string key, HashSet<string> discard)
        {
            key = null;
            input.Trim();
            int parenA = input.IndexOf('(');
            if (parenA < 0)
            {
                if (discard != null && discard.Contains(input))
                    return null;

                key = input;
                return new List<string>();
            }
            

            key = input.Substring(0, parenA);
            if (discard != null && discard.Contains(key))
                return null;

            int parenB = input.LastIndexOf(')');
            if (parenB < parenA)
                parenB = input.Length;

            ++parenA;
            string val = input.Substring(parenA, parenB - parenA);
            val.Trim();
            var split = val.Split(';');
            if (split == null || split.Length == 0)
                return new List<string>();

            var lst = new List<string>();
            foreach (var s in split)
                lst.Add(s.Trim());

            return lst;
        }

        public static Dictionary<string, List<string>> HumanModToDictionary1D(string input, HashSet<string> discard = null)
        {
            var dict = new Dictionary<string, List<string>>();
            var split = input.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in split)
            {
                var list = HumanModToList(s, out var key, discard);
                if (list == null)
                    continue;
                dict[key] = list;
            }
            return dict;
        }

        public static Dictionary<string, List<List<string>>> HumanModToDictionary2D(string input, HashSet<string> discard = null)
        {
            var dict = new Dictionary<string, List<List<string>>>();
            var split = input.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in split)
            {
                var list = HumanModToList(s, out var key, discard);
                if (list == null)
                    continue;

                if (dict.TryGetValue(key, out var l))
                {
                    l.Add(list);
                }
                else
                {
                    l = new List<List<string>>();
                    l.Add(list);
                    dict[key] = l;
                }
            }
            return dict;
        }

        public static List<T> ToManaged<T>(this Il2CppSystem.Collections.Generic.List<T> list)
        {
            var ret = new List<T>(list.Count);
            foreach (var item in list)
                ret.Add(item);

            return ret;
        }

        public static HashSet<T> ToManaged<T>(this Il2CppSystem.Collections.Generic.HashSet<T> set)
        {
            var ret = new HashSet<T>(set.Count);
            foreach (var item in set)
                ret.Add(item);

            return ret;
        }

        public static Dictionary<TKey, TValue> ToManaged<TKey, TValue>(this Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dict) where TKey : notnull
        {
            var ret = new Dictionary<TKey, TValue>(dict.Count);
            foreach (var kvp in dict)
                ret.Add(kvp.Key, kvp.Value);

            return ret;
        }

        public static bool GetValueOrNew<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, out TValue value) where TValue : class, new()
        {
            if (!dict.TryGetValue(key, out value))
            {
                value = new TValue();
                dict[key] = value;
                return false;
            }

            return true;
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
