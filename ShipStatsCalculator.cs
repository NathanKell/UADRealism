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
            private static Vector3 _pos;
            private static Quaternion _rot;

            public static void SetForRender(GameObject hullModel)
            {
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

                for(int i = 0; i < hullModel.transform.GetChildCount(); ++i)
                {
                    var child = hullModel.transform.GetChild(i)?.gameObject;
                    if (child == null)
                        continue;

                    if (!child.name.Contains("Decor"))
                        continue;

                    if (!child.activeSelf)
                        continue;

                    _decor.Add(child);
                    child.active = false;
                }

                Renderer[] rs = hullModel.GetComponentsInChildren<Renderer>();

                foreach (var mr in rs)
                {
                    if (!okRenderers.Contains(mr))
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
                if(_layers.TryGetValue(obj, out int layer))
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

        private static readonly int _CameraLayerInt = LayerMask.NameToLayer("VisibilityFog_unused");
        private const int _ResSideFront = 512;

        private Camera _camera;
        //private Light _light;
        private RenderTexture _renderTexture;
        private Texture2D _texture;
        private short[] _beamPixelCounts = new short[_ResSideFront];

        public ShipStatsCalculator(IntPtr ptr) : base(ptr) { }

        public void Awake()
        {
            if (_Instance != null)
                Destroy(_Instance);

            _renderTexture = new RenderTexture(_ResSideFront, _ResSideFront, 16, RenderTextureFormat.ARGB32);
            _texture = new Texture2D(_ResSideFront, _ResSideFront, TextureFormat.ARGB32, false);

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

        public struct ShipStats
        {
            public float Lwl;
            public float B;
            public float T;
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

        public ShipStats GetStats(GameObject obj, Bounds bounds)
        {
            var stats = new ShipStats();

            RenderSetup.SetForRender(obj);

            float Awp = 0f;
            float Vd = 0f;

            float draught = -bounds.min.y;
            stats.T = draught;
            _camera.targetTexture = _renderTexture;
            RenderTexture.active = _renderTexture;
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
                _texture.ReadPixels(new Rect(0, 0, _ResSideFront, _ResSideFront), 0, 0);

                // It would be very nice to not alloc a full mb here. But
                // il2cpp/harmony doesn't support GetRawTextureData<>,
                // only GetRawTextureData (and that copies as well).
                Color32[] pixels = _texture.GetPixels32();
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

                        // Now find displacement
                        float volFactor = sizePerPixel * sizePerPixel * sizePerPixel * stats.Cm;
                        for (int col = 0; col < _ResSideFront; ++col)
                        {
                            int rowBegin = -1;
                            float dispPerPx = _beamPixelCounts[col] * volFactor;
                            for (int r = 0; r < _ResSideFront; ++r)
                            {
                                if (pixels[r * _ResSideFront + col].a == 0)
                                {
                                    if (rowBegin >= 0)
                                    {
                                        Vd += (r - rowBegin) * dispPerPx;
                                        rowBegin = -1;
                                    }
                                    continue;
                                }

                                if (rowBegin < 0)
                                    rowBegin = r;
                            }
                            if (rowBegin >= 0)
                                Vd += (_ResSideFront - rowBegin) * dispPerPx;
                        }
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
                            stats.Cm = ((float)totalMidshipsPixels) / (beamBulgePixels * (firstRow - lastRow + 1));

                        stats.Am = totalMidshipsPixels * sizePerPixel * sizePerPixel;
                        break;

                    case ShipViewDir.Bottom:
                        // This will detect the midpoints _counting the bulge_
                        // but that should be ok.
                        // We'll also record the number of beam pixels at each row.
                        int minInset = _ResSideFront / 2 + 1;
                        const int stopPoint = _ResSideFront / 2 - 1;
                        for (row = 0; row < _ResSideFront; ++row)
                        {
                            short numPx = 0;
                            for (int col = 0; col < _ResSideFront; ++col)
                            {
                                if (pixels[row * _ResSideFront + col].a == 0)
                                    continue;

                                // Assume symmetry for perf
                                if (col > stopPoint)
                                    break;
                                
                                ++numPx;

                                if(col < minInset)
                                {
                                    minInset = col;
                                }
                            }
                            numPx *= 2;
                            _beamPixelCounts[_ResSideFront - row - 1] = numPx;
                            Awp += numPx;
                        }
                        Awp *= sizePerPixel * sizePerPixel;
                        break;
                }

                //if (pics++ < 3)
                //{
                //    string filePath = "C:\\temp\\112\\screenshot_";
                //    if (Patch_GameData._IsProcessing)
                //        filePath += "p_";
                //    filePath += obj.name + "_" + view.ToString() + ".png";

                //    var bytes = ImageConversion.EncodeToPNG(_texture);
                //    Il2CppSystem.IO.File.WriteAllBytes(filePath, bytes);
                //}
            }
            RenderTexture.active = null;

            // Corrections based on inexact detection
            // First, apply the regardless-of-bulge correftions.
            Awp *= 0.98f;
            Vd *= Util.Remap(stats.B / stats.Lwl, 3f, 10f, 0.85f, 0.92f, true); // this is bad because of rudder/props
            // Bulge case:
            float beamRatio = stats.B / stats.beamBulge;
            if (beamRatio != 1f)
            {
                Awp *= Util.Remap(beamRatio, 0.85f, 1f, 0.97f, 1f, true);
                Vd *= Util.Remap(beamRatio, 0.85f, 1f, 0.9f, 1f, true);
            }

            // Set the stats struct.
            stats.Awp = Awp;
            stats.Vd = Vd;
            stats.Cb = Vd / (stats.Lwl * stats.beamBulge * stats.T);
            stats.Cwp = Awp / (stats.Lwl * stats.beamBulge);
            stats.Cp = Vd / (stats.Am * stats.Lwl);
            stats.Cvp = Vd / (Awp * draught);

            RenderSetup.Restore(obj);

            return stats;
        }
    }
}
