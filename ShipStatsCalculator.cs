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
            private static Vector3 _pos;
            private static Quaternion _rot;

            public static void SetForRender(GameObject hullInfo)
            {
                // TODO: Could optimize this to just walk through all GOs
                // and then all Components on each, rather than a bunch of
                // garbage-producing calls that recheck all GOs and all comps.

                foreach (var mb in hullInfo.GetComponentsInChildren<MonoBehaviour>())
                {
                    if (!mb.enabled)
                        continue;

                    _behaviours.Add(mb);
                    mb.enabled = false;
                }

                foreach (var coll in hullInfo.GetComponentsInChildren<Collider>())
                {
                    if (!coll.enabled)
                        continue;

                    _colliders.Add(coll);
                    coll.enabled = false;
                }

                SetLayerRecursive(hullInfo, _CameraLayerInt);

                var children = hullInfo.GetChildren();
                foreach (var child in children)
                {
                    if (child == null)
                        continue;

                    if (!child.name.Contains("Decor"))
                        continue;

                    if (!child.activeSelf)
                        continue;

                    _decor.Add(child);
                    child.active = false;
                }

                _pos = hullInfo.transform.localPosition;
                _rot = hullInfo.transform.localRotation;
                hullInfo.transform.localPosition = Vector3.zero;
                hullInfo.transform.localRotation = Quaternion.identity;

                Renderer[] rs = hullInfo.GetComponentsInChildren<Renderer>();
                for (int i = rs.Length; i-- > 0;)
                {
                    Renderer mr = rs[i];
                    _renderers[mr] = new ShadowInfo() { _mode = mr.shadowCastingMode, _receiveShadows = mr.receiveShadows };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    // TODO: Change material?
                }
            }

            public static void Restore(GameObject hullInfo)
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

                RestoreLayerRecursive(hullInfo);
                _layers.Clear();

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

                hullInfo.transform.localPosition = _pos;
                hullInfo.transform.localRotation = _rot;
            }

            private static void SetLayerRecursive(GameObject obj, int newLayer)
            {
                _layers[obj] = obj.layer;

                obj.SetLayer(newLayer);
                for (int i = 0; i < obj.transform.childCount; ++i)
                    SetLayerRecursive(obj.transform.GetChild(i).gameObject, newLayer);
            }

            private static void RestoreLayerRecursive(GameObject obj)
            {
                if(_layers.TryGetValue(obj, out int layer))
                    obj.SetLayer(layer);

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

        private static readonly int _CameraLayerInt = LayerMask.NameToLayer("VisibilityFog_unused");
        private const int _ResSideFront = 512;
        private const int _ResFrame = 128;
        private const int _FrameSteps = 256;

        private Camera _camera;
        private RenderTexture _sideFrontRT;
        private Texture2D _sideFrontTex;
        private RenderTexture _frameRT;
        private Texture2D _frameTex;
        private float[] _areas = new float[_FrameSteps];
        private float[] _beams = new float[_FrameSteps];

        public ShipStatsCalculator(IntPtr ptr) : base(ptr) { }

        public void Awake()
        {
            if (_Instance != null)
                Destroy(_Instance);

            _sideFrontRT = new RenderTexture(_ResSideFront, _ResSideFront, 16, RenderTextureFormat.ARGB4444);
            _sideFrontTex = new Texture2D(_ResSideFront, _ResSideFront, TextureFormat.ARGB4444, false);

            _frameRT = new RenderTexture(_ResFrame, _ResFrame, 16, RenderTextureFormat.ARGB32);
            _frameTex = new Texture2D(_ResFrame, _ResFrame, TextureFormat.Alpha8, false);

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
            _camera.targetTexture = _sideFrontRT;

            _Instance = this;
        }

        public void OnDestroy()
        {
            _sideFrontRT.Release();
            _frameRT.Release();
        }

        enum ShipViewDir
        {
            Front = 0,
            Bottom,
            Side,

            MAX
        }

        public struct ShipStats
        {
            public float Lwl;
            public float B;
            public float D;
            public float beamBulge;
            public float bulgeDepth;
            public float Awp;
            public float Am;
            public float Vd;
            public float Cb;
            public float Cm;
            public float Cwp;
            public float Cp;
            public float Cvp;
        }

        public ShipStats GetStats(Ship ship)
        {
            var stats = new ShipStats();

            if (ship == null || ship.hull == null || ship.hull.hullInfo == null)
                return stats;

            var obj = ship.hull.hullInfo.gameObject;
            RenderSetup.SetForRender(obj);

            Bounds bounds = new Bounds();
            bool boundsSet = false;

            Renderer[] rs = obj.GetComponentsInChildren<Renderer>();
            for (int i = rs.Length; i-- > 0;)
            {
                if (boundsSet)
                {
                    bounds.Encapsulate(rs[i].bounds);
                }
                else
                {
                    bounds = rs[i].bounds;
                    boundsSet = true;
                }
            }

            float draught = -ship.hullSize.min.y;
            stats.D = draught;
            _camera.targetTexture = _sideFrontRT;
            float bowMidpoint = 0f;
            float sternMidpoint = bounds.size.z;
            RenderTexture.active = _sideFrontRT;
            for (ShipViewDir view = (ShipViewDir)0; view < ShipViewDir.MAX; ++view)
            {
                float size;
                float depth;
                Vector3 dir;

                switch (view)
                {
                    default:
                    case ShipViewDir.Side:
                        size = Mathf.Max(draught, bounds.size.z);
                        depth = bounds.size.x;
                        dir = Vector3.left;
                        _camera.transform.position = new Vector3(bounds.max.x + 1f, -size * 0.5f, bounds.center.z);
                        break;

                    case ShipViewDir.Front:
                        size = Mathf.Max(draught, bounds.size.x);
                        depth = bounds.size.z;
                        dir = Vector3.back;
                        _camera.transform.position = new Vector3(bounds.center.x, -size * 0.5f, bounds.max.z + 1f);
                        break;

                    case ShipViewDir.Bottom:
                        size = Mathf.Max(bounds.size.x, bounds.size.z);
                        dir = Vector3.up;
                        depth = draught;
                        _camera.transform.position = new Vector3(bounds.center.x, bounds.min.y - 1f, bounds.center.z);
                        break;
                }
                _camera.transform.rotation = Quaternion.LookRotation(dir);
                _camera.orthographicSize = size * 0.5f;
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = depth + 1f;

                _camera.Render();
                _sideFrontTex.ReadPixels(new Rect(0, 0, _ResSideFront, _ResSideFront), 0, 0);

                // It would be very nice to not alloc a full mb here. But
                // il2cpp/harmony doesn't support GetRawTextureData<>,
                // only GetRawTextureData (and that copies as well).
                Color32[] pixels = _sideFrontTex.GetPixels32();
                int waterlinePixels = 0;
                int row;

                float sizePerPixel = size / _ResSideFront;
                switch (view)
                {
                    default:
                    case ShipViewDir.Side:
                        for (row = _ResSideFront - 1; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                ++waterlinePixels;
                            }
                            // Sanity check, just in case there's some garbage here
                            if (waterlinePixels > _ResSideFront / 4)
                                break;
                        }
                        stats.Lwl = waterlinePixels * sizePerPixel;
                        break;

                    case ShipViewDir.Front:
                        int firstRow = _ResSideFront - 1;
                        for (row = firstRow; row >= 0; --row)
                        {
                            waterlinePixels = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
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
                        const int midPoint = _ResSideFront / 2 + 1;
                        int lastRow = row;
                        for (row = firstRow - 1; row >= 0; --row)
                        {
                            int pixelsInCurrentRow = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                // Check if we're below hull bottom
                                if (col > midPoint && pixelsInCurrentRow == 0)
                                    break;

                                if (pixels[row * _ResSideFront + col].a == 0)
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
                        stats.bulgeDepth = (_ResSideFront - (maxWidthRow + 1)) * sizePerPixel;

                        // Midship coefficient
                        if (waterlinePixels == 0)
                            stats.Cm = 0f;
                        else
                            // This maybe needs to be beamBulgePixels not waterlinePixels to ensure Cm <= 1
                            stats.Cm = ((float)totalMidshipsPixels) / (waterlinePixels * (firstRow - lastRow + 1));

                        stats.Am = stats.Cm * (stats.B * draught);
                        break;

                    case ShipViewDir.Bottom:
                        // This will detect the midpoints _counting the bulge_
                        // but that should be ok.
                        int bowMidpointPx = 0;
                        int sternMidpointPx = _ResSideFront - 1;
                        int minInset = _ResSideFront / 2 + 1;
                        for (row = 0; row < _ResSideFront; ++row)
                        {
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                if (col > minInset)
                                    break;

                                if(col < minInset)
                                {
                                    minInset = col;
                                    bowMidpointPx = row;
                                    sternMidpointPx = row;
                                }
                                else if (col == minInset)
                                {
                                    sternMidpointPx = row;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        bowMidpoint = bowMidpointPx * sizePerPixel; // treat the midpoint as at the forward end of the pixel
                        sternMidpoint = (sternMidpointPx + 1) * sizePerPixel; // treat the midpoint as at the sternward end of the pixel
                        break;
                }

                //Melon<UADRealismMod>.Logger.Msg($"For direction {view.ToString()}, pixel count at row {row} is {pxCount} so dimension = {dimension:F2}");

                //string filePath = "C:\\temp\\112\\screenshot_" + obj.name + "_" + view.ToString() + ".png";

                //var bytes = ImageConversion.EncodeToPNG(_sideFrontTex);
                //Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
            }
            RenderTexture.active = null;

            _camera.targetTexture = _frameRT;
            float lenPerStep = bounds.size.z / (_FrameSteps - 1);
            float dimension = Mathf.Max(draught, bounds.size.x);
            float orthoSize = dimension * 0.5f;
            float lenPerPixel = dimension / _ResFrame;
            float areaPerPixel = lenPerPixel * lenPerPixel;
            _camera.orthographicSize = orthoSize;
            
            _camera.nearClipPlane = 0.1f;
            int mpIndex = -1;
            RenderTexture.active = _frameRT;
            for (int frame = 0; frame < _FrameSteps; ++frame)
            {
                float frameStart = frame * lenPerStep;
                if (frameStart >= bowMidpoint && frameStart <= sternMidpoint)
                {
                    if (mpIndex >= 0)
                    {
                        _beams[frame] = _beams[mpIndex];
                        _areas[frame] = _areas[mpIndex];
                        continue;
                    }
                    mpIndex = frame;
                }
                else if (frameStart > sternMidpoint)
                {
                    frameStart = (_FrameSteps - frame - 1) * lenPerStep;
                    _camera.transform.position = new Vector3(bounds.center.x, -orthoSize, bounds.min.z - 1f);
                    _camera.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                }
                else
                {
                    _camera.transform.position = new Vector3(bounds.center.x, -orthoSize, bounds.max.z + 1f);
                    _camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
                }

                _camera.farClipPlane = frameStart + 1f;

                _camera.Render();
                _frameTex.ReadPixels(new Rect(0, 0, _ResFrame, _ResFrame), 0, 0);

                // It would be very nice to not alloc a full mb here. But
                // il2cpp/harmony doesn't support GetRawTextureData<>,
                // only GetRawTextureData (and that copies as well).
                //Color32[] pixels = _frameTex.GetPixels32();
                var bytes = _frameTex.GetRawTextureData();
                int waterlinePixels = 0;
                int totalPx = 0;
                for (int row = _ResFrame - 1; row >= 0; --row)
                {
                    int pxCount = 0;
                    for (int col = 0; col < _ResFrame; ++col)
                    {
                        //if (pixels[row * _ResFrame + col].a == 0)
                        //if (_frameTex.GetPixel(col, row).a == 0f)
                        if (bytes[row * _ResFrame + col] == 0)
                            continue;

                        ++pxCount;
                    }
                    totalPx += pxCount;

                    if (waterlinePixels == 0)
                        waterlinePixels = pxCount;
                }
                _beams[frame] = waterlinePixels * lenPerPixel;
                _areas[frame] = totalPx * areaPerPixel;

                //if (ship.shipType.name == "cl")
                //{
                //    string filePath = "C:\\temp\\112\\screenshot_" + obj.name + "_frame_" + frame + "_" + totalPx + ".png";

                //    var bytes = ImageConversion.EncodeToPNG(_sideFrontTex);
                //    Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
                //}
            }
            RenderTexture.active = null;

            float Awp = 0f;
            float Vd = 0f;
            lenPerStep = bounds.size.z / _FrameSteps; // now it's all steps, not one less step.
            for (int i = 1; i < _FrameSteps; ++i)
            {
                // we'll assume some curvature to the hull, which means
                // that we more heavily weight the wider portion.
                int min = i, max = i;
                if (_beams[i] > _beams[i - 1])
                    --min;
                else
                    --max;

                Awp += (_beams[min] * 0.4f + _beams[max] * 0.6f) * lenPerStep;

                // We could probably assume that beam and area are ordered the same
                // but we'll double-check.
                min = i;
                max = i;
                if (_areas[i] > _areas[i - 1])
                    --min;
                else
                    --max;

                Vd += (_areas[min] * 0.4f + _areas[max] * 0.6f) * lenPerStep;
            }
            stats.Awp = Awp;
            stats.Vd = Vd;
            stats.Cb = Vd / (stats.Lwl * stats.B * stats.D);
            stats.Cwp = Awp / (stats.Lwl * stats.B);
            stats.Cp = Vd / (stats.Am * stats.Lwl);
            stats.Cvp = Vd / (Awp * draught);

            RenderSetup.Restore(obj);

            return stats;
        }
    }
}
