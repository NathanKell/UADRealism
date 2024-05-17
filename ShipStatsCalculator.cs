using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

namespace UADRealism
{
    [RegisterTypeInIl2Cpp]
    public class ShipStatsCalculator : MonoBehaviour
    {
        private static class RenderSetup
        {
            private struct ShadowInfo
            {
                public UnityEngine.Rendering.ShadowCastingMode _mode;
                public bool _receiveShadows;
            }

            private static readonly List<MonoBehaviour> _behaviours = new List<MonoBehaviour>();
            private static readonly List<Collider> _colliders = new List<Collider>();
            private static readonly Dictionary<GameObject, int> _layers = new Dictionary<GameObject, int>();
            private static readonly List<GameObject> _decor = new List<GameObject>();
            private static readonly Dictionary<Renderer, ShadowInfo> _renderers = new Dictionary<Renderer, ShadowInfo>();
            private static readonly List<Renderer> _renderersDisabled = new List<Renderer>();
            private static readonly List<GameObject> _decorStack = new List<GameObject>();
            private static Vector3 _pos;
            private static Quaternion _rot;

            private static float _ambientIntensity;
            private static Color _ambientLight;
            private static UnityEngine.Rendering.AmbientMode _ambientMode;
            private static UnityEngine.Rendering.SphericalHarmonicsL2 _ambientProbe;
            private static bool _fog;
            private static Color _fogColor;
            private static float _fogDensity;
            private static float _fogStartDistance;
            private static float _fogEndDistance;
            private static FogMode _fogMode;

            public static void SetForRender(GameObject hullModel)
            {
                _ambientIntensity = RenderSettings.ambientIntensity;
                _ambientLight = RenderSettings.ambientLight;
                _ambientMode = RenderSettings.ambientMode;
                _ambientProbe = RenderSettings.ambientProbe;
                _fog = RenderSettings.fog;
                _fogColor = RenderSettings.fogColor;
                _fogDensity = RenderSettings.fogDensity;
                _fogStartDistance = RenderSettings.fogStartDistance;
                _fogEndDistance = RenderSettings.fogEndDistance;
                _fogMode = RenderSettings.fogMode;

                //RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                //RenderSettings.ambientIntensity = 99f;
                //RenderSettings.ambientLight = new Color(99f, 0f, 0f);

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(100f, 0f, 0f);

                var okRenderers = Part.GetVisualRenderers(hullModel);

                StoreLayerRecursive(hullModel);
                SetLayerRecursive(hullModel, _CameraLayerInt);

                _pos = hullModel.transform.localPosition;
                _rot = hullModel.transform.localRotation;
                hullModel.transform.localPosition = Vector3.zero;
                hullModel.transform.localRotation = Quaternion.identity;

                // TODO: Could optimize this to just walk through all GOs
                // and then all Components on each, rather than a bunch of
                // garbage-producing calls that recheck all GOs and all comps.

                foreach (var mb in hullModel.GetComponentsInChildren<MonoBehaviour>())
                {
                    if (!mb.enabled)
                        continue;

                    _behaviours.Add(mb);
                    mb.enabled = false;
                }

                foreach (var coll in hullModel.GetComponentsInChildren<Collider>())
                {
                    if (!coll.enabled)
                        continue;

                    _colliders.Add(coll);
                    coll.enabled = false;
                }

                _decorStack.Add(hullModel);
                while (_decorStack.Count > 0)
                {
                    int max = _decorStack.Count - 1;
                    var go = _decorStack[max];
                    _decorStack.RemoveAt(max);
                    for (int i = 0; i < go.transform.GetChildCount(); ++i)
                    {
                        var child = go.transform.GetChild(i)?.gameObject;
                        if (child == null)
                            continue;

                        if (!child.activeSelf)
                            continue;

                        if (child.name.StartsWith("Decor")
                            || child.name.StartsWith("Mount")
                            || child.name == "Deck"
                            || child.name.StartsWith("DeckSize")
                            || child.name.StartsWith("DeckWall"))
                        {
                            _decor.Add(child);
                            child.active = false;
                            continue;
                        }

                        _decorStack.Add(child);
                    }
                }

                Renderer[] rs = hullModel.GetComponentsInChildren<Renderer>();

                foreach (var mr in rs)
                {
                    if (mr != null && !okRenderers.Contains(mr))
                    {
                        if (mr.enabled)
                        {
                            _renderersDisabled.Add(mr);
                            mr.enabled = false;
                        }
                        continue;
                    }
                    _renderers[mr] = new ShadowInfo() { _mode = mr.shadowCastingMode, _receiveShadows = mr.receiveShadows };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    // TODO: Change material -- it'd be great to render to depth.
                    // That would allow for much better detection of things.
                }
            }

            public static void Restore(GameObject hullModel)
            {
                foreach (var mb in _behaviours)
                {
                    mb.enabled = true;
                }
                _behaviours.Clear();

                foreach (var coll in _colliders)
                {
                    coll.enabled = true;
                }
                _colliders.Clear();

                foreach (var decor in _decor)
                {
                    decor.active = true;
                }
                _decor.Clear();

                foreach (var kvp in _renderers)
                {
                    kvp.Key.shadowCastingMode = kvp.Value._mode;
                    kvp.Key.receiveShadows = kvp.Value._receiveShadows;
                }
                _renderers.Clear();

                foreach (var r in _renderersDisabled)
                    r.enabled = true;
                _renderersDisabled.Clear();

                hullModel.transform.localPosition = _pos;
                hullModel.transform.localRotation = _rot;

                RestoreLayerRecursive(hullModel);
                _layers.Clear();

                RenderSettings.ambientIntensity = _ambientIntensity;
                RenderSettings.ambientLight = _ambientLight;
                RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientProbe = _ambientProbe;

                RenderSettings.fog = _fog;
                RenderSettings.fogColor = _fogColor;
                RenderSettings.fogDensity = _fogDensity;
                RenderSettings.fogStartDistance = _fogStartDistance;
                RenderSettings.fogEndDistance = _fogEndDistance;
                RenderSettings.fogMode = _fogMode;
            }

            private static void StoreLayerRecursive(GameObject obj)
            {
                for (int i = 0; i < obj.transform.childCount; ++i)
                    StoreLayerRecursive(obj.transform.GetChild(i).gameObject);

                _layers[obj] = obj.layer;
            }

            private static void SetLayerRecursive(GameObject obj, int newLayer)
            {
                obj.layer = newLayer;

                for (int i = 0; i < obj.transform.childCount; ++i)
                    SetLayerRecursive(obj.transform.GetChild(i).gameObject, newLayer);
            }

            private static void RestoreLayerRecursive(GameObject obj)
            {
                if (_layers.TryGetValue(obj, out int layer))
                    obj.layer = layer;

                for (int i = 0; i < obj.transform.childCount; ++i)
                    RestoreLayerRecursive(obj.transform.GetChild(i).gameObject);
            }
        }

        private static ShipStatsCalculator _Instance = null;
        public static ShipStatsCalculator Instance
        {
            get
            {
                if (_Instance == null)
                {
                    var gameObject = new GameObject("StatsCalculator");
                    gameObject.AddComponent<ShipStatsCalculator>();
                }
                return _Instance;
            }
        }

        // To avoid allocs. Compared to instantiation though it's probably not noticeable.
        private static readonly List<string> _foundVars = new List<string>();
        private static readonly List<string> _matchedVars = new List<string>();
        private static readonly List<Bounds> _sectionBounds = new List<Bounds>();

        private static readonly int _CameraLayerInt = LayerMask.NameToLayer("VisibilityFog_unused");
        private const int _TextureRes = 512;

        private Camera _camera;
        private RenderTexture _renderTexture;
        private Texture2D _texture;
        private short[] _beamPixelCounts = new short[_TextureRes];
        private float[] _displacementsPerPx = new float[_TextureRes];

        public ShipStatsCalculator(IntPtr ptr) : base(ptr) { }

        public void Awake()
        {
            if (_Instance != null)
                Destroy(_Instance);

            _renderTexture = new RenderTexture(_TextureRes, _TextureRes, 16, RenderTextureFormat.ARGB32);
            _texture = new Texture2D(_TextureRes, _TextureRes, TextureFormat.ARGB32, false);

            _camera = gameObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 1f;
            _camera.aspect = 1f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 500f;
            _camera.enabled = false;
            _camera.cullingMask = 1 << _CameraLayerInt;
            _camera.targetTexture = _renderTexture;

            _Instance = this;
        }

        public void OnDestroy()
        {
            _renderTexture.Release();
        }

        enum ShipViewDir
        {
            Front = 0,
            Bottom,
            Side,

            MAX
        }

        private static bool IsFogged(Color32 px)
        {
            return px.r > 200 && px.g < 100 && px.b < 100;
        }

        public HullStats GetStats(GameObject obj, Bounds bounds, bool isDDHull)
        {
            var stats = new HullStats();

            RenderSetup.SetForRender(obj);

            float Awp = 0f;
            float Vd = 0f;
            int maxBeamPx = 0;
            int midFwd = _TextureRes;
            int midAft = 0;
            int bowRow = _TextureRes;
            int sternRow = 0;
            bool hasBulge = false;
            int sternCol = 0;
            int transomCol = -1;
            int firstPropCol = -1;
            int firstEndPropCol = -1;
            var part = obj.GetParent().GetComponent<Part>();
            int sec = part == null || part.middles == null ? 0 : part.middles.Count;

            float draught = -bounds.min.y;
            _camera.targetTexture = _renderTexture;
            RenderTexture.active = _renderTexture;
            for (ShipViewDir view = (ShipViewDir)0; view < ShipViewDir.MAX; ++view)
            {
                float size;
                float depth;
                Vector3 dir;

                switch (view)
                {
                    case ShipViewDir.Front:
                        size = Mathf.Max(draught, bounds.size.x);
                        //depth = bounds.size.z - GetSectionBounds(part.stern, null).size.z + 5f;
                        depth = GetSectionBounds(part.bow, null).size.z;
                        dir = Vector3.back;
                        _camera.transform.position = new Vector3(bounds.center.x, -size * 0.5f, bounds.max.z + 1f);
                        break;

                    case ShipViewDir.Bottom:
                        size = Mathf.Max(bounds.size.x, bounds.size.z);
                        dir = Vector3.up;
                        depth = draught;
                        _camera.transform.position = new Vector3(bounds.center.x, bounds.min.y - 1f, bounds.center.z);
                        break;

                    default:
                    case ShipViewDir.Side:
                        size = Mathf.Max(draught, bounds.size.z);
                        depth = bounds.size.x * 0.5f;
                        dir = Vector3.left;
                        _camera.transform.position = new Vector3(bounds.max.x + 1f, -size * 0.5f, bounds.center.z);
                        break;
                }
                _camera.transform.rotation = Quaternion.LookRotation(dir);
                _camera.orthographicSize = size * 0.5f;
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = depth + 1f;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = depth;

                _camera.Render();
                _texture.ReadPixels(new Rect(0, 0, _TextureRes, _TextureRes), 0, 0);

                // It would be very nice to not alloc a full mb here. But
                // il2cpp/harmony doesn't support GetRawTextureData<>,
                // only GetRawTextureData (and that copies as well).
                Color32[] pixels = _texture.GetPixels32();
                int waterlinePixels = 0;
                int row;

                float sizePerPixel = size / _TextureRes;
                switch (view)
                {
                    case ShipViewDir.Front:
                        int firstRow = _TextureRes - 1;
                        for (row = firstRow; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _TextureRes; ++col)
                            {
                                if (pixels[row * _TextureRes + col].a == 0)
                                    continue;

                                ++waterlinePixels;
                            }
                            if (waterlinePixels > 0)
                            {
                                firstRow = row;
                                break;
                            }
                        }
                        stats.B = waterlinePixels * sizePerPixel;

                        int totalMidshipsPixels = waterlinePixels;

                        int maxWidthRow = row;
                        int beamBulgePixels = waterlinePixels;
                        const int midPoint = _TextureRes / 2 + 1;
                        int lastRow = row;
                        for (row = firstRow - 1; row >= 0; --row)
                        {
                            int pixelsInCurrentRow = 0;
                            for (int col = 0; col < _TextureRes; ++col)
                            {
                                // Check if we're below hull bottom
                                if (col > midPoint && pixelsInCurrentRow == 0)
                                    break;

                                if (pixels[row * _TextureRes + col].a == 0)
                                    continue;

                                ++pixelsInCurrentRow;
                            }

                            if (pixelsInCurrentRow > 0)
                            {
                                lastRow = row;
                                totalMidshipsPixels += pixelsInCurrentRow;

                                // We found something wider than the waterline beam
                                if (pixelsInCurrentRow > beamBulgePixels)
                                {
                                    beamBulgePixels = pixelsInCurrentRow;
                                    maxWidthRow = row;
                                }
                            }
                        }
                        stats.beamBulge = beamBulgePixels * sizePerPixel;
                        stats.bulgeDepth = (_TextureRes - (maxWidthRow + 1)) * sizePerPixel;
                        // Verify draught. It's possible something might have messed up the bounds (like the props).
                        float draughtCalc = (firstRow - lastRow + 1) * sizePerPixel;
                        if (draughtCalc < 0.99f * draught || draughtCalc > 1.01f * draught)
                            draught = draughtCalc;

                        stats.T = draught;

                        float Am = totalMidshipsPixels * sizePerPixel * sizePerPixel;

                        // Midship coefficient
                        // Using wl beam, not bulge beam
                        if (waterlinePixels == 0)
                            stats.Cm = 0f;
                        else
                            stats.Cm = Am / (stats.beamBulge * draught);

                        if (beamBulgePixels != waterlinePixels)
                        {
                            hasBulge = true;

                            // Correct Cm - take average of beam and bulge cm
                            stats.Cm = Am / (stats.B * draught) * 0.67f + stats.Cm * 0.33f;
                        }

                        break;

                    case ShipViewDir.Bottom:
                        // This will detect the midpoints _counting the bulge_
                        // but that should be ok.
                        // We'll also record the number of beam pixels at each row.
                        int lastNonPropCol = -1;
                        
                        for (row = 0; row < _TextureRes; ++row)
                        {
                            short numPx = 0;
                            int sideCol = _TextureRes - row - 1;
                            for (int col = 0; col < _TextureRes; ++col)
                            {
                                if (pixels[row * _TextureRes + col].a == 0)
                                {
                                    // Account for the case where there's prop shafts
                                    // outside the hull. Discard pixels if on left side and
                                    // we hit a gap; stop when we hit a gap if on right side.
                                    if (col < _TextureRes / 2 - 1)
                                    {
                                        if (numPx > 0)
                                            numPx = 0;

                                        continue;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }

                                ++numPx;
                            }

                            // Prop detection
                            if (row > _TextureRes * 3 / 4)
                            {
                                int lastSideCol = sideCol + 1;
                                short lastPx = _beamPixelCounts[lastSideCol];
                                if (numPx > lastPx)
                                {
                                    if (lastNonPropCol < 0)
                                    {
                                        lastNonPropCol = lastSideCol;
                                        if (firstPropCol < 0)
                                            firstPropCol = sideCol;
                                    }
                                }

                                if (lastNonPropCol > 0)
                                {
                                    // attempt to continue angle - this will reverse the rate
                                    // of change of angle, but it's better than continuing straight back
                                    // and local curvature will be low for the few pixels of propeller
                                    int delta = lastNonPropCol - sideCol;
                                    short deltaPx = _beamPixelCounts[lastNonPropCol + delta];
                                    short lastPropPx = _beamPixelCounts[lastNonPropCol];
                                    short predictedPx = (short)(lastPropPx - (deltaPx - lastPropPx));

                                    if (delta > 6 || (numPx < predictedPx + 2 && numPx <= lastPropPx))
                                    {
                                        lastNonPropCol = -1;
                                        if (firstEndPropCol < 0)
                                            firstEndPropCol = sideCol;
                                    }
                                    else
                                    {
                                        numPx = predictedPx;
                                        // handle output
                                        //int pxStart = (_ResSideFront - numPx) / 2;
                                        //int pxEnd = pxStart + numPx - 1;
                                        //for (int i = 0; i < _ResSideFront; ++i)
                                        //{
                                        //    pixels[row * _ResSideFront + i].a = (i < pxStart || i > pxEnd) ? (byte)0 : (byte)255;
                                        //}
                                    }
                                }
                            }

                            Awp += numPx;
                            _beamPixelCounts[sideCol] = numPx;

                            if (numPx > 0)
                            {
                                sternRow = row;

                                if (row < bowRow)
                                    bowRow = row;

                                if (numPx > maxBeamPx)
                                {
                                    midFwd = row;
                                    maxBeamPx = numPx;
                                }
                                // account for noise in the hull side:
                                // require 3+ pixels inwards to end the midsection
                                if (numPx > maxBeamPx - 3)
                                    midAft = 0;
                                else if (midAft == 0)
                                    midAft = row;
                            }
                        }
                        Awp *= sizePerPixel * sizePerPixel;
                        stats.bowLength = (midFwd - bowRow + 1) * sizePerPixel;
                        stats.iE = Mathf.Atan2(stats.B * 0.5f, stats.bowLength) * Mathf.Rad2Deg;
                        stats.LrPct = (sternRow - midAft + 1) * sizePerPixel; // will divide by Lwl once that's found.

                        // Try to scale Cm based on fineness of hull which we'll guess from
                        // the bow length vs the beam.
                        // This is because the Cm values ingame are kinda bad for the hulls, rather too low.
                        // For destroyers (or TBs), though, they are broadly correct.
                        if (!isDDHull)
                        {
                            stats.Cm = Mathf.Pow(stats.Cm, Util.Remap(stats.bowLength / stats.B, 1.5f, 2.5f, 0.25f, 0.5f, true));
                        }

                        // Detect transom
                        float minTransomWidth = stats.beamBulge * 0.25f; // otherwise can detect small cruiser sterns
                        sternCol = _TextureRes - sternRow - 1;

                        for (int col = -1; col < _TextureRes * 1 / 5; ++col)
                        {
                            int curCol = sternCol + col;
                            int curPx = curCol < 0 ? 0 : _beamPixelCounts[curCol];
                            const float angleDelta = 45f;
                            const int yDelta = 3;
                            int nextCol = curCol + yDelta;
                            int nextPx = _beamPixelCounts[nextCol];
                            int farCol = nextCol + yDelta;
                            float angle1 = Mathf.Atan2(yDelta * 2, nextPx - curPx);
                            float angle2 = Mathf.Atan2(yDelta * 2, _beamPixelCounts[farCol] - nextPx);
                            if (angle2 - angle1 > Mathf.Deg2Rad * angleDelta)
                            {
                                if (transomCol < nextCol && nextPx * sizePerPixel > minTransomWidth)
                                {
                                    // We have to skip props here, they can stick out and represent
                                    // sharp changes in beam
                                    bool isProp = false;
                                    for (int i = nextCol + 1; i < nextCol + 5; ++i)
                                        if (_beamPixelCounts[i] < nextPx)
                                            isProp = true;

                                    if (!isProp)
                                        transomCol = nextCol;
                                }
                            }
                            // we've passed the transom. But wait until a few pixels down in case
                            // we got noise early.
                            else if (col > 4)
                            {
                                break;
                            }
                        }

                        //if (transomCol >= 0)
                        //{
                        //if (transomCol >= 0)
                        //{
                        //Debug.Log($"{(part.data == null ? obj.name : ShipStats.GetHullModelKey(part.data))} ({sec}): Transom at col {transomCol}: {(_beamPixelCounts[transomCol] / (float)maxBeamPx):P2}");
                        //}
                        //Patch_GameData._WrittenModels.Add(ShipStats.GetHullModelKey(p.data) + "_");
                        //string hierarchy = $"{(part.data == null ? obj.name : ShipStats.GetHullModelKey(part.data))} ({sec}): {ModUtils.DumpHierarchy(obj)}";
                        //Debug.Log("---------\n" + hierarchy);
                        //}
                        //{
                        //    string filePath = "C:\\temp\\112\\uad\\nar\\5.0\\shots\\";
                        //    int bbPx = Mathf.RoundToInt(stats.beamBulge / sizePerPixel);
                        //    filePath += $"{ShipStats.GetHullModelKey(part.data).Replace(";", "+")}_{sec}_{view.ToString()}_b{maxBeamPx}_f{bbPx}.png";

                        //    var bytes = ImageConversion.EncodeToPNG(_texture);
                        //    Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
                        //}

                        break;

                    default:
                    case ShipViewDir.Side:
                        // The ones we found in Bottom can have
                        // below-waterline aft extensions, so we
                        // need to re-find these.
                        int bowCol = 0;
                        sternCol = _TextureRes;
                        for (row = _TextureRes - 1; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _TextureRes; ++col)
                            {
                                if (pixels[row * _TextureRes + col].a == 0)
                                    continue;

                                ++waterlinePixels;
                                bowCol = col;
                                if (col < sternCol)
                                    sternCol = col;
                            }
                            if (waterlinePixels > 0)
                                break;
                        }
                        stats.Lwl = waterlinePixels * sizePerPixel;
                        stats.LrPct /= stats.Lwl; // we now know our LwL so we can divide by it.

                        // Now find displacement
                        float volFactor = sizePerPixel * sizePerPixel * sizePerPixel;
                        float bowCm = Mathf.Min(0.55f, stats.Cm);
                        int startCol = -1;
                        int lastCol = -1;
                        midFwd = _TextureRes - midFwd - 1;
                        midAft = _TextureRes - midAft - 1;
                        int maxDepthPx = 0;

                        int bulgeFirst = Math.Min(midAft, Mathf.RoundToInt(waterlinePixels * 0.333f));
                        int bulgeLast = Math.Max(midFwd, Mathf.RoundToInt(waterlinePixels * 0.667f));
                        float nonBulgeMult = hasBulge ? stats.B / stats.beamBulge : 1f;
                        int lastDepth = 0;
                        // go bow->stern
                        for (int col = _TextureRes - 1; col >= 0; --col)
                        {
                            bool hasPx = false;
                            float bowT = bowCol == midFwd ? (col == bowCol ? 0f : 1f) : ((float)(bowCol - col) / (bowCol - midFwd));
                            float dispPerPx = _beamPixelCounts[col] * volFactor * Mathf.Lerp(bowCm, stats.Cm, bowT * bowT);
                            // Assume bulge is along 2/3 of the length, so correct beam in re: Cm
                            // (don't worry about the bulge slowly increasing or decreasing in width)
                            if (hasBulge && col >= bulgeFirst && col <= bulgeLast)
                                dispPerPx *= nonBulgeMult;

                            bool firstRowFog = IsFogged(pixels[(_TextureRes - 1) * _TextureRes + col]);
                            int numFog = 0;
                            int startRow = -1;
                            // Note: we don't have to cover the case where we don't hit an empty pixel
                            // before the end of the column, because we know the ship is going to be longer
                            // (wider in columns) than it is deep (tall in rows), and this is a square ortho
                            // projection.
                            for (int r = _TextureRes - 1; r >= 0; --r)
                            {
                                var px = pixels[r * _TextureRes + col];
                                if (hasPx)
                                {
                                    // Stop when we hit a gap (no holes in the hull! Otherwise we'll count
                                    // prop shafts). If we're aft of midships, we also keep track of the last depth
                                    // and stop if we go beyond it. This is to try not to count rudders. Note we can't
                                    // just use pure midships, since the max depth might be slightly aft of that.
                                    bool depthLimit = col < _TextureRes * 2 / 3 && r < lastDepth;

                                    bool isFogged = IsFogged(px);
                                    // If we're going back deeper and we're aft, or it's an empty pixel (a hole, or below the hull),
                                    // _or_ we're aft and it's some kind of narrow rudder / keel thing (which we detect via fog), stop here.
                                    // Note: we need to make sure this whole column isn't fogged, and we need to make sure this isn't the
                                    // last row of a normal column (where the row after this will be empty, which means the fog just means
                                    // that the beam is zeroing here).
                                    // TODO: Do the same thing for rudders we do for props in bottom view: if we go below last depth,
                                    // instead of just continuing at last depth, continue the _slope_.
                                    //if (depthLimit || px.a == 0 || (!firstRowRed && col < _ResSideFront / 2 && IsFogged(px) && (r == 0 || pixels[(r - 1) * _ResSideFront + col].a > 0)))
                                    if (depthLimit || px.a == 0 || (!firstRowFog && col < _TextureRes / 2 && numFog > 0))
                                    {
                                        Vd += (startRow - r) * dispPerPx;
                                        if (!depthLimit)
                                            lastDepth = r + 1;

                                        // handle output
                                        //for (int r2 = r; r2 >= 0; --r2)
                                        //    pixels[r2 * _ResSideFront + col].a = 0;

                                        break;
                                    }
                                    if (isFogged)
                                        ++numFog;
                                }
                                // we don't care about fog in this case
                                if (px.a == 0)
                                    continue;

                                hasPx = true;
                                if (startRow < 0)
                                    startRow = r;
                                int curDepth = _TextureRes - r;
                                if (curDepth > maxDepthPx)
                                    maxDepthPx = curDepth;
                            }

                            if (hasPx)
                            {
                                if (startCol < 0)
                                    startCol = col;

                                lastCol = col;
                            }
                            _displacementsPerPx[col] = Vd;

                            // Handle transom
                            if (col == transomCol)
                            {
                                stats.Catr = hasPx ? (_beamPixelCounts[col] * (_TextureRes - lastDepth)) / (float)(maxBeamPx * maxDepthPx) : 0;
                                //Debug.Log($"Transom col {transomCol}, {_beamPixelCounts[col]}x{(_ResSideFront - lastDepth)} / midships {maxBeamPx}x{maxDepthPx} = {stats.Catr:F3}");
                            }
                        }

                        float halfDisp = Vd * 0.5f;
                        stats.lcbPct = stats.Lwl * 0.5f;
                        for (int col = startCol; col >= lastCol; --col)
                        {
                            float dispAtColEnd = _displacementsPerPx[col];
                            if (dispAtColEnd > halfDisp)
                            {
                                float sternToCB = col - lastCol;
                                // assume linear change in transverse area, which will be wrong if draught is changing too
                                if (col == startCol)
                                    sternToCB += halfDisp / dispAtColEnd;
                                else
                                    sternToCB += Mathf.InverseLerp(_displacementsPerPx[col + 1], dispAtColEnd, halfDisp);

                                stats.lcbPct = (sternToCB * sizePerPixel) / stats.Lwl - 0.5f;
                                break;
                            }
                        }
                        break;
                }

                //if(view != ShipViewDir.Front || sec == 0)
                //{
                //    string filePath = "C:\\temp\\112\\uad\\nar\\5.0\\shots\\";
                //    filePath += $"{ShipStats.GetHullModelKey(part.data).Replace(";", "+")}_{sec}_{view.ToString()}";
                //    if (view == ShipViewDir.Bottom && firstPropCol > 0)
                //        filePath += $"ps{firstPropCol}_pe{firstEndPropCol}";

                //    var bytes = ImageConversion.EncodeToPNG(_texture);
                //    Il2CppSystem.IO.File.WriteAllBytes(filePath + ".png", bytes);

                //    if (view == ShipViewDir.Side)
                //    {
                //        _texture.SetPixels32(pixels);
                //        bytes = ImageConversion.EncodeToPNG(_texture);
                //        Il2CppSystem.IO.File.WriteAllBytes(filePath + "_out.png", bytes);
                //    }
                //}
            }
            RenderTexture.active = null;

            // Corrections based on inexact detection
            // First, apply the regardless-of-bulge corrections.
            Awp *= 0.98f;
            Vd *= Util.Remap(stats.Lwl / stats.B, 3f, 10f, 0.95f, 0.97f, true); // this is bad because of rudder/props, and overestimating stern fullness
            
            // Bulge case:
            if (hasBulge)
            {
                float beamRatio = stats.B / stats.beamBulge;
                Vd *= Util.Remap(beamRatio, 0.85f, 1f, 0.95f, 1f, true); // we assume there's only a bit of missing volume,
                // considering that fore and aft of the bulge the hull does extend all the way out, and even along the bulge there's
                // not that much missing.

                // Assume bulge is along 2/3 of the length, so correct beam in re: Cm
                // (don't worry about the bulge slowly increasing or decreasing in width)
                Awp *= 0.333f + 0.667f * beamRatio;
            }

            // Set the remaining stats
            stats.Vd = Vd;
            // The rest of these we'll recalculate after all section-counts are rendered.
            stats.Cb = Vd / (stats.Lwl * stats.B * draught);
            stats.Cwp = Awp / (stats.Lwl * stats.B);
            stats.Cp = stats.Cb / stats.Cm;
            stats.Cvp = stats.Cb / stats.Cwp;
            stats.scaleFactor = 1f;
            stats.Cv = 100f * Vd / (stats.Lwl * stats.Lwl * stats.Lwl);

            RenderSetup.Restore(obj);

            return stats;
        }

        public static System.Collections.IEnumerator ProcessHullData(HullData hData)
        {
            var data = hData._hulls[0];
            var part = Instance.SpawnPart(data);
            //part.gameObject.AddComponent<LogMB>();
            part.enabled = false;
            hData.yPos = part.bow.transform.localPosition.y;

            int count = hData._sectionsMax + 1;
            hData._statsSet = new HullStats[count];

            // do 0 sections in addition to all others.
            float sec0Draught = 1f;
            float sec0Beam = 1f;
            float sec0BulgeDepth = 1f;
            for (int secCount = 0; secCount < count; ++secCount)
            {
                // we're going to set min to 0 later
                //if (secCount == 1 && secCount < hData._sectionsMin)
                //    secCount = hData._sectionsMin;

                // Ship.RefreshHull (only the bits we need)
                Instance.CreateMiddles(part, secCount);
                // It's annoying to rerun this every time, but it's not exactly expensive.
                Instance.ApplyVariations(part);

                Instance.PositionSections(part);

                // Calc the stats for this layout. Note that scaling dimensions will only
                // linearly change stats, it won't require a recalc.
                var shipBounds = Instance.GetShipBounds(part.model.gameObject);
                var stats = Instance.GetStats(part.model.gameObject, shipBounds, hData._isDDHull);

                if (stats.Vd == 0f)
                    stats.Vd = 1f;

                float powCm = 1f;
                float powCwp = 1f;
                float powCb = 1f;
                if (hData._isDDHull)
                {
                    float year = ShipStats.GetAverageYear(hData);

                    powCm = Util.Remap(year, 1900, 1925, 1f, 0.7f, true);
                    powCb = Util.Remap(year, 1900, 1925, 1.5f, 1f, true);
                    powCwp = Util.Remap(year, 1890, 1930, 2f, 1.3f, true);

                    hData._year = year;
                }

                // Recompute. For zero-section case, check if destroyer and fudge numbers.
                // If not, reset based on sec0 which has best resolution for beam/draught/Cm/etc
                if (secCount == 0)
                {
                    if (hData._isDDHull)
                    {
                        stats.Cm = Mathf.Pow(stats.Cm, powCm);
                    }
                    sec0Draught = stats.T;
                    sec0Beam = stats.B;
                    sec0BulgeDepth = stats.bulgeDepth;
                }
                else
                {
                    stats.Cb = stats.Vd / (stats.Lwl * sec0Beam * sec0Draught);
                    stats.Cm = hData._statsSet[0].Cm;
                    stats.Cwp *= stats.B / sec0Beam;
                    stats.Cp = stats.Cb / stats.Cm;
                    stats.Cvp = stats.Cb / stats.Cwp;
                    stats.Catr = hData._statsSet[0].Catr;
                    stats.B = sec0Beam;
                    stats.T = sec0Draught;
                    stats.beamBulge = hData._statsSet[0].beamBulge;
                    stats.bulgeDepth = sec0BulgeDepth;
                    stats.LrPct = hData._statsSet[0].LrPct * hData._statsSet[0].Lwl / stats.Lwl;
                    stats.iE = hData._statsSet[0].iE;
                    stats.bowLength = hData._statsSet[0].bowLength;
                }

                if (hData._isDDHull)
                {
                    float oldCb = stats.Cb;
                    stats.Cb = Mathf.Pow(stats.Cb, powCb);
                    stats.Cwp = Mathf.Pow(stats.Cwp, powCwp);
                    stats.Cp = stats.Cb / stats.Cm;
                    stats.Cvp = stats.Cb / stats.Cwp;
                    float vMult = stats.Cb / oldCb;
                    stats.Vd *= vMult;
                    stats.Cv *= vMult;
                }

                // Correct for really weird high-draught hull models
                // (and, in Monitor's case, too low draught)
                float BdivT = stats.B / stats.T;
                float tMult = 1f;
                if (BdivT < 2.25f)
                {
                    if (BdivT < 1.6f)
                        tMult = (1f / 2f);
                    else
                        tMult = (1f / 1.5f);
                }
                else if(BdivT > 4f)
                {
                    tMult = (1f / 0.8f);
                }
                if (tMult != 1f)
                {
                    stats.bulgeDepth *= tMult;
                    stats.Cv *= tMult;
                    stats.T *= tMult;
                    stats.Vd *= tMult;
                }

                hData._statsSet[secCount] = stats;

#if LOGSTATS
                string beam = stats.B.ToString("F2");
                if (stats.beamBulge != stats.B)
                    beam += $"({stats.beamBulge:F2})";
                Debug.Log($"{hData._key}@{secCount}: {(stats.Lwl):F2}x{beam}x{(stats.T):F2} ({(stats.Lwl/stats.B):F2},{(stats.B/stats.T):F2}), LCB={stats.lcbPct:P2}, {stats.Vd:F0}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}, Catr={stats.Catr:F3}, Cv={stats.Cv:F3}, DD={(hData._isDDHull)}");
#endif
                // Early-out if the hull is getting too blocky
                // (but keep going if it's a special ship)
                if (stats.Cp > 0.7f)
                {
                    int minSec = Math.Max(hData._sectionsMin, secCount);
                    bool stop = true;
                    foreach (var hull in hData._hulls)
                    {
                        switch (hull.shipType.name)
                        {
                            case "amc":
                            case "tr":
                            case "ic":
                            case "ss":
                                stop = false;
                                continue;
                        }
                        if (hull.sectionsMax > minSec)
                            hull.sectionsMax = minSec;
                    }
                    if (stop)
                        break;
                }
                yield return null;
            }

            string varStr = hData._key.Substring(data.model.Length);
            if (varStr != string.Empty)
                varStr = $"({varStr.Substring(1)}) ";
            //Melon<UADRealismMod>.Logger.Msg($"Calculated stats for {data.model} {varStr}for {hData._sectionsMin} to {hData._sectionsMax} sections");

            //Melon<UADRealismMod>.Logger.Msg($"{data.model}: {(stats.Lwl * scaleFactor):F2}x{beamStr}x{(stats.D * scaleFactor):F2}, {(stats.Vd * tRatio)}t. Cb={stats.Cb:F3}, Cm={stats.Cm:F3}, Cwp={stats.Cwp:F3}, Cp={stats.Cp:F3}, Cvp={stats.Cvp:F3}");
            Destroy(part.gameObject);
            yield return null;
            Part.CleanPartsStorage();
        }

        private Part SpawnPart(PartData data)
        {
            var partGO = new GameObject(data.name);
            var part = partGO.AddComponent<Part>();
            part.data = data;
            partGO.active = true;

            // Do what we need from Ship.ChangeHull and Part.LoadModel
            var model = Resources.Load<GameObject>(data.model);
            var instModel = Util.AttachInst(partGO, model);
            instModel.active = true;
            instModel.transform.localScale = Vector3.one;
            part.model = instModel.GetComponent<PartModel>();
            part.isShown = true;

            var visual = part.model.GetChild("Visual", false);
            var sections = visual.GetChild("Sections", false);
            part.bow = sections.GetChild("Bow", false);
            part.stern = sections.GetChild("Stern", false);
            var middles = new List<GameObject>();
            ModUtils.FindChildrenStartsWith(sections, "Middle", middles);
            middles.Sort((a, b) => a.name.CompareTo(b.name));
            part.middlesBase = new Il2CppSystem.Collections.Generic.List<GameObject>();
            foreach (var m in middles)
            {
                part.middlesBase.Add(m);
                m.active = false;
            }

            part.middles = new Il2CppSystem.Collections.Generic.List<GameObject>();
            part.hullInfo = part.model.GetComponent<HullInfo>();
            //part.RegrabDeckSizes(true);
            //part.RecalcVisualSize();

            foreach (var go in part.middlesBase)
                Util.SetActiveX(go, false);

            return part;
        }

        private Bounds GetShipBounds(GameObject obj)
        {
            var allVRs = Part.GetVisualRenderers(obj);
            Bounds shipBounds = new Bounds();
            bool foundShipBounds = false;
            foreach (var r in allVRs)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                if (foundShipBounds)
                {
                    shipBounds.Encapsulate(r.bounds);
                }
                else
                {
                    foundShipBounds = true;
                    shipBounds = r.bounds;
                }
            }

            return shipBounds;
        }

        private void CreateMiddles(Part part, int numTotal)
        {
            // Spawn middle sections
            for (int i = part.middles.Count; i < numTotal; ++i)
            {
                var mPrefab = part.middlesBase[i % part.middlesBase._size];
                var cloned = Util.CloneGameObject(mPrefab);
                part.middles.Add(cloned);
                //if (cloned.transform.parent != part.bow.transform.parent)
                //{
                //    Melon<UADRealismMod>.Logger.BigError($"New middle {cloned.name} has parent {cloned.transform.parent.gameObject.name} not {part.bow.transform.parent.gameObject.name}! Reparenting.");
                //    cloned.transform.parent = part.bow.transform.parent;
                //}
                Util.SetActiveX(cloned, true);
            }
        }

        private void ApplyVariations(Part part)
        {
            part.data.paramx.TryGetValue("var", out var desiredVars);
            var varComps = part.model.gameObject.GetComponentsInChildren<Variation>(true);
            for (int i = 0; i < varComps.Length; ++i)
            {
                var varComp = varComps[i];
                string varName = string.Empty;
                if (desiredVars != null)
                {
                    foreach (var s in varComp.variations)
                        if (desiredVars.Contains(s))
                            _matchedVars.Add(s);
                }

                _foundVars.AddRange(_matchedVars);

                if (_matchedVars.Count > 1)
                    Melon<UADRealismMod>.Logger.BigError("Found multiple variations on partdata " + part.data.name + ": " + string.Join(", ", _matchedVars));
                if (_matchedVars.Count > 0)
                    varName = _matchedVars[0];

                _matchedVars.Clear();

                // This is a reimplementation of Variation.Init, since that needs a Ship
                var childObjs = Util.GetChildren(varComp);
                if (childObjs.Count == 0)
                {
                    Melon<UADRealismMod>.Logger.BigError("For part " + part.data.name + ", no variations!");
                    continue;
                }
                if (varComp.variations.Count != childObjs.Count)
                {
                    Melon<UADRealismMod>.Logger.BigError("For part " + part.data.name + ", var count / child count mismatch!");
                    continue;
                }
                int varIdx = varComp.variations.IndexOf(varName);
                if ((varIdx < 0 && varName != string.Empty))
                {
                    Melon<UADRealismMod>.Logger.BigError("For part " + part.data.name + ", can't find variation " + varName);
                }
                int selectedIdx;
                if (varIdx >= 0)
                {
                    selectedIdx = varIdx;
                }
                else if (varComp.random)
                {
                    selectedIdx = UnityEngine.Random.Range(0, varComp.variations.Count - 1);
                }
                else
                {
                    selectedIdx = varComp.variations.IndexOf(varComp.@default);
                    if (selectedIdx < 0)
                    {
                        if (varComp.@default != string.Empty)
                        {
                            Melon<UADRealismMod>.Logger.BigError("For part " + part.data.name + ", can't find default variation " + varComp.@default);
                        }
                        selectedIdx = 0;
                    }
                }
                for (int v = childObjs.Count; v-- > 0;)
                    Util.SetActiveX(childObjs[v], v == selectedIdx);
            }

            // This check is not quite the same as the game's
            // but it's fine.
            if (desiredVars != null)
            {
                foreach (var desV in desiredVars)
                {
                    for (int v = _foundVars.Count; v-- > 0;)
                    {
                        if (_foundVars[v] == desV)
                            _foundVars.RemoveAt(v);
                    }
                }
                if (_foundVars.Count > 0)
                    Melon<UADRealismMod>.Logger.BigError("On partdata " + part.data.name + ": Missing vars: " + string.Join(", ", _foundVars));
            }

            _foundVars.Clear();
        }

        private static readonly Bounds _EmptyBounds = new Bounds();

        private void PositionSections(Part part)
        {
            if (part == null)
            {
                Melon<UADRealismMod>.Logger.BigError("Part null!\n" + Environment.StackTrace);
                return;
            }
            // Get reversed list of sections so we can position them
            var sectionsReverse = new Il2CppSystem.Collections.Generic.List<GameObject>();
            sectionsReverse.AddRange(part.hullSections);
            for (int i = 0; i < sectionsReverse.Count / 2; ++i)
            {
                int opposite = sectionsReverse.Count - 1 - i;
                if (i == opposite)
                    break;

                var tmp = sectionsReverse[i];
                sectionsReverse[i] = sectionsReverse[opposite];
                sectionsReverse[opposite] = tmp;
            }

            // Calculate bounds for all sections
            int mCount = part.middles == null ? -1 : part.middles.Count;
            if (part.bow == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: bow null!");
                return;
            }
            var sectionsGO = Util.GetParent(part.bow);
            if (sectionsGO == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: Sections null!");
                return;
            }
            var sectionsTrf = sectionsGO.transform;
            float shipLength = 0f;

            if (_sectionBounds == null)
            {
                Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: _SectionBounds is null!");
                return;
            }

            for (int i = 0; i < sectionsReverse.Count; ++i)
            {
                var sec = sectionsReverse[i];
                if (sec == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: Section {i} is null!");
                    return;
                }
                var secBounds = GetSectionBounds(sec, sectionsTrf);
                _sectionBounds.Add(secBounds);
                shipLength += secBounds.size.z;
            }

            // Reposition/rescale sections
            float lengthOffset = shipLength * -0.5f;
            for (int i = 0; i < sectionsReverse.Count; ++i)
            {
                var sec = sectionsReverse[i];
                if (sec == null)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: Section {i} is null!");
                    return;
                }
                if(_sectionBounds[i] == _EmptyBounds)
                {
                    Melon<UADRealismMod>.Logger.BigError($"Part {part.name}@{mCount}: Section {(sec == null ? "NULL" : sec.name)} ({i}) has empty bounds!");
                    return;
                }
                var secBounds = _sectionBounds[i];
                Util.SetLocalZ(sec.transform, secBounds.size.z * 0.5f - secBounds.center.z + lengthOffset + sec.transform.localPosition.z);
                Util.SetScaleX(sec.transform, 1f); // beam
                Util.SetScaleY(sec.transform, 1f); // draught
                lengthOffset += secBounds.size.z;
            }

            _sectionBounds.Clear();
        }

        private Bounds GetSectionBounds(GameObject sec, Transform space)
        {
            var vr = Part.GetVisualRenderers(sec);
            Bounds secBounds = new Bounds();
            bool foundBounds = false;
            int rCount = 0;
            foreach (var r in vr)
            {
                if (!r.gameObject.activeInHierarchy)
                    continue;

                ++rCount;

                var trf = r.transform;
                var mesh = r.GetComponent<MeshFilter>();
                var sharedMesh = mesh.sharedMesh;
                var meshBounds = sharedMesh.bounds;
                // transform bounds to 'Sections' space
                meshBounds = Util.TransformBounds(trf, meshBounds);
                var invBounds = space == null ? meshBounds : Util.InverseTransformBounds(space, meshBounds);

                if (foundBounds)
                {
                    secBounds.Encapsulate(invBounds);
                }
                else
                {
                    secBounds = invBounds;
                    foundBounds = true;
                }
            }
            var secInfo = sec.GetComponent<SectionInfo>() ?? sec.GetComponentInChildren<SectionInfo>();
            if (secInfo != null)
            {
                //Debug.Log($"For part {data.name}, sec {sec.name}, size mods {secInfo.sizeFrontMod} + {secInfo.sizeBackMod}");
                var bSize = secBounds.size;
                bSize.z += secInfo.sizeFrontMod + secInfo.sizeBackMod;
                secBounds.size = bSize;
                var bCent = secBounds.center;
                bCent.z += (secInfo.sizeFrontMod - secInfo.sizeBackMod) * 0.5f;
                secBounds.center = bCent;
            }
            return secBounds;
        }
    }
}
