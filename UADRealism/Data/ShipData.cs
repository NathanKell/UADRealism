//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;

#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace UADRealism
{
    public static class ShipDataAccessor
    {
        public static ShipData ModData(this Ship ship)
        {
            var sd = ship.gameObject.GetComponent<ShipData>();
            if (sd == null)
                sd = ship.gameObject.AddComponent<ShipData>();

            return sd;
        }
    }

    [RegisterTypeInIl2Cpp]
    public class ShipData : MonoBehaviour
    {
        public const float _MinFineness = 0f;
        public const float _MaxFineness = 100f;
        public const float _MinFreeboard = -30f;
        public const float _MaxFreeboard = 45f;

        public ShipData(IntPtr ptr) : base(ptr) { }

        private float _freeboard = 0f;
        public float Freeboard => _freeboard;
        private float _fineness = _MinFineness + (_MaxFineness - _MinFineness) * 0.5f;
        public float Fineness => _fineness;
        private bool _ignoreNextPartYChange = false;
        public bool IgnoreNextPartYChange => _ignoreNextPartYChange;
        private Ship _ship = null;

        public void SetFreeboard(float fb) => _freeboard = fb;
        public void SetFineness(float fn) => _fineness = fn;
        public void SetIgnoreNextPartYChange(bool val) => _ignoreNextPartYChange = val;

        public int SectionsFromFineness()
        {
            return Mathf.RoundToInt(Mathf.Lerp(_ship.hull.data.sectionsMin, _ship.hull.data.sectionsMax, 1f - _fineness * 0.01f));
        }

        public void ToStore(Ship.Store store)
        {
            store.hullPartSizeZ = _fineness;
            store.hullPartSizeY = _freeboard;
        }

        public void FromStore(Ship.Store store)
        {
            _fineness = store.hullPartSizeZ;
            _freeboard = store.hullPartSizeY;

            store.hullPartSizeZ = 0f;
            store.hullPartSizeY = 0f;
            store.hullPartMaxZ = 0f;
            store.hullPartMinZ = 0f;

            // Let's also set beam and draught so when ChangeHull
            // calls SetTonnage which calls RefreshHull (since it
            // calls SetBeam/Draught without model updating) the
            // hull will be correct _before_ FromStore adds parts.
            _ship.beam = store.beam;
            _ship.draught = store.draught;
        }

        private void Awake()
        {
            _ship = gameObject.GetComponent<Ship>();
        }
    }
}
