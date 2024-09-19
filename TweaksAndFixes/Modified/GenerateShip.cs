using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using MelonLoader;
using MelonLoader.CoreClrUtils;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625
#pragma warning disable CS8618

namespace TweaksAndFixes
{
    public class GenerateShip
    {
        public struct BeamDraughtData
        {
            const string _BDLParamName = "ai_beamdraughtlimits";

            public bool isValid;
            public float beamMin;
            public float beamMax;
            public float draughtMin;
            public float draughtMax;

            public BeamDraughtData()
            {
                beamMin = beamMax = draughtMin = draughtMax = 0f;
                isValid = false;
            }

            public BeamDraughtData(PartData hullData)
            {
                isValid = true;

                beamMin = hullData.beamMin;
                beamMax = hullData.beamMax;
                draughtMin = hullData.draughtMin;
                draughtMax = hullData.draughtMax;
            }

            public BeamDraughtData(Il2CppSystem.Collections.Generic.List<string> bdlParam, string hName)
            {
                beamMin = beamMax = draughtMin = draughtMax = 0f;
                isValid = true;
                int iC = bdlParam.Count;
                for (int i = 0; i < iC; ++i)
                {
                    if (!float.TryParse(bdlParam[i], out var val))
                    {
                        Melon<TweaksAndFixes>.Logger.Error($"For ship {hName}, {_BDLParamName} failed to parse {bdlParam[i]}");
                        continue;
                    }
                    if (val != 0)
                    {
                        switch (i)
                        {
                            case 0: beamMin = val; break;
                            case 1: beamMax = val; break;
                            case 2: draughtMin = val; break;
                            default: draughtMax = val; break;
                        }
                    }
                }
            }

            public static BeamDraughtData FromParam(Ship ship)
            {
                var bdl = new BeamDraughtData();
                var hd = ship.hull.data;
                if (!hd.paramx.TryGetValue(_BDLParamName, out var bdlParam))
                    return bdl;

                if (bdlParam.Count != 4)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"For ship {hd.name}, {_BDLParamName} doesn't have 4 elements. The elements should be beamMin, beamMax, draughtMin, draughtMax. Values of 0 mean keep existing non-AI values.");
                    return bdl;
                }

                return new BeamDraughtData(bdlParam, hd.name);
            }

            public void Apply(Ship ship)
            {
                var hd = ship.hull.data;
                hd.beamMin = beamMin;
                hd.beamMax = beamMax;
                hd.draughtMin = draughtMin;
                hd.draughtMax = draughtMax;
            }

            public void Reset()
            {
                beamMin = beamMax = draughtMin = draughtMax = 0f;
                isValid = false;
            }
        }

        static Dictionary<ComponentData, float> _CompWeights = new Dictionary<ComponentData, float>();

        bool _isValid = false;
        public bool IsValid => _isValid;

        float _hullYear;
        float _designYear;
        float _avgYear;
        string _sType;
        string _country;
        bool _isLight;
        bool _isCombatant;
        bool _isIC;
        float _desiredSpeed;
        Ship._GenerateRandomShip_d__562 _this;
        internal Ship _ship;
        bool _isMissionMainShip;
        int _gen;
        float _tngRatio;
        bool _canUseTorps;
        BeamDraughtData _origBDL = new BeamDraughtData();
        BeamDraughtData _newBDL = new BeamDraughtData();

        float _limitArmor = -1f;
        float _limitSpeed = -1f;
        float _customSpeed = -1f;
        float _customArmor = -1f;

        RandPart _curRP = null;
        bool _rpIsGun = false;
        ShipM.BatteryType _rpBattery = ShipM.BatteryType.main;
        ShipM.GenGunInfo _gunInfo = new ShipM.GenGunInfo();
        GenArmorData.GenArmorInfo _armorInfo = null;
        Ship._AddRandomPartsNew_d__579 _arpnRoutine = null;
        public bool AddingParts => _arpnRoutine != null;

        List<PartData> _availableParts = new List<PartData>();

        HashSet<string> _seenCompTypes = new HashSet<string>();

        float _tonnage;
        float _baseHullWeight;
        float _prePartsWeight;
        float _armRatio;
        float _prePartsFreeTngNoPayload;
        float _hullPartsTonnage = 0;

        float _payloadBaseWeight;
        float _payloadTotalWeight;
        float _maxPayloadWeight;
        bool _doneAddingParts = false;
        bool _doneAddingFunnels = false;
        int _rpDesiredAmount = 0;
        float _maxFunnelEff;

        public void Bind(Ship._GenerateRandomShip_d__562 coroutine)
        {
            _isValid = true;

            _this = coroutine;
            _ship = _this.__4__this;
            _hullYear = Database.GetYear(_ship.hull.data);
            _designYear = _ship.GetYear(_ship);
            _avgYear = (_hullYear + _designYear) * 0.5f;
            _sType = _ship.shipType.name;
            _country = _ship.player.data.name;
            _isLight = _sType == "dd" || _sType == "tb";
            _isCombatant = _sType != "tr" && _sType != "amc";
            _isIC = _sType == "ic";
            _gen = _ship.hull.data.Generation;
            _isMissionMainShip = GameManager.IsMission && this._ship.player.isMain && BattleManager.Instance.MissionMainShip == this._ship;

            _maxFunnelEff = Config.Param("taf_generate_funnel_maxefficiency", 150f);

            _newBDL = BeamDraughtData.FromParam(_ship);
            if (_newBDL.isValid)
                _origBDL = new BeamDraughtData(_ship.hull.data);

            if (Patch_BattleManager_d115._ShipGenInfo.isActive)
            {
                _limitSpeed = Patch_BattleManager_d115._ShipGenInfo.limitSpeed;
                _customSpeed = Patch_BattleManager_d115._ShipGenInfo.customSpeed;
                _limitArmor = Patch_BattleManager_d115._ShipGenInfo.limitArmor;
                _customArmor = Patch_BattleManager_d115._ShipGenInfo.customArmor;
            }
            else
            {
                _limitArmor = -1f;
                _limitSpeed = -1f;
                _customSpeed = -1f;
                _customArmor = -1f;
            }

            InitParts();

            _armorInfo = GenArmorData.GetInfoFor(this._ship);

            _armRatio = GetArmamentRatio(_avgYear);
        }

        public void Reset()
        {
            _isValid = false;
            _CompWeights.Clear();
            _this = null;
            _ship = null;
            _origBDL.Reset();
            _newBDL.Reset();
            _curRP = null;
            _rpIsGun = false;
            _gunInfo.Reset();
            _armorInfo = null;
            _arpnRoutine = null;
            _availableParts.Clear();
            _seenCompTypes.Clear();

            _hullPartsTonnage = 0;
            _doneAddingFunnels = false;
            _doneAddingParts = false;

            _limitArmor = -1f;
            _limitSpeed = -1f;
            _customSpeed = -1f;
            _customArmor = -1f;
        }

        private void CleanTCs()
        {
            for (int i = _ship.shipGunCaliber.Count; i-- > 0;)
            {
                var tc = _ship.shipGunCaliber[i];
                if (!IsTCorTAUsed(tc.turretPartData.caliber, tc.isCasemateGun))
                    _ship.shipGunCaliber.RemoveAt(i);
            }
        }

        private bool IsTCorTAUsed(float caliber, bool isCasemate)
        {
            foreach (var part in _ship.parts)
            {
                var data = part.data;
                if (!data.isGun)
                    continue;

                if (caliber == data.caliber && isCasemate == Ship.IsCasemateGun(data))
                    return true;
            }
            return false;
        }

        private void CleanTAs()
        {
            for (int i = _ship.shipTurretArmor.Count; i-- > 0;)
            {
                var ta = _ship.shipTurretArmor[i];
                if (!IsTCorTAUsed(ta.turretPartData.caliber, ta.isCasemateGun))
                    _ship.shipTurretArmor.RemoveAt(i);
            }
        }

        public static VesselEntity.OpRange GetDesiredOpRange(string sType, float year)
            => sType switch
            {
                "dd" => year < 1910 ? VesselEntity.OpRange.VeryHigh : year < 1925 ? VesselEntity.OpRange.High : VesselEntity.OpRange.Medium,
                "tb" => year < 1900 ? VesselEntity.OpRange.VeryLow : year < 1910 ? VesselEntity.OpRange.Low : VesselEntity.OpRange.Medium,
                "ca" or "cl" => VesselEntity.OpRange.Medium,
                _ => year < 1925 ? VesselEntity.OpRange.VeryLow : VesselEntity.OpRange.Low
            };

        private void InitParts()
        {
            if (_this.isSimpleRefit)
                return;

            foreach (var data in G.GameData.parts.Values)
                if (_ship.IsPartAvailable(data))
                    _availableParts.Add(data);

            _canUseTorps = false;
            if (_isCombatant && !_isIC)
            {
                var randParts = _this._isRefitMode_5__2 ? _ship.shipType.randPartsRefit : _ship.shipType.randParts;
                foreach (var rp in randParts)
                {
                    if (rp.type == "torpedo" && _ship.CheckOperations(rp))
                    {
                        _canUseTorps = true;
                        break;
                    }
                }
            }
        }

        private bool UpdateRPCacheOrSkipGun(RandPart rp)
        {
            if (rp != _curRP)
            {
                _curRP = rp;
                _rpIsGun = rp.type == "gun";
                if (_arpnRoutine._desiredAmount_5__10 == 0)
                    Melon<TweaksAndFixes>.Logger.Error($"Error: Desired amount already 0 for {rp.name} on {_ship.shipType.name} {_ship.hull.data.name} {_ship.vesselName}");
                _rpDesiredAmount = _arpnRoutine._desiredAmount_5__10;
                if (_rpIsGun)
                    _rpBattery = rp.condition.Contains("main_cal") ? ShipM.BatteryType.main : (rp.condition.Contains("sec_cal") ? ShipM.BatteryType.sec : ShipM.BatteryType.ter);
            }
            return !_rpIsGun;
        }

        public void PostTonnageUpdate()
        {
            _tonnage = _ship.Tonnage();
            _tngRatio = Mathf.InverseLerp(_ship.TonnageMin(), Math.Min(_ship.TonnageMax(), _ship.player.TonnageLimit(_ship.shipType)), _tonnage);
            //Melon<TweaksAndFixes>.Logger.Msg($"Set tonnage to {_tonnage:N0}, ratio {_tngRatio:F2}");
        }

        public void OnPrefix()
        {
            if (_newBDL.isValid)
                _newBDL.Apply(_ship);

            int state = _this.__1__state;
            switch (state)
            {
                case 0:
                    _ship.TAFData().ResetAllGrades();
                    break;

                case 2:
                    if (Config.ShipGenReorder)
                    {
                        SetInitialStats();
                        _this.__1__state = 3;
                    }
                    break;

                case 6:
                    if (Config.ShipGenReorder)
                    {
                        // If we are reordering, select components _first_.
                        // We will have selected initial stats above, so no
                        // need to run AdjustHullStats.
                        SelectComponents(ComponentSelectionPass.Initial);
                    }
                    else
                    {
                        float weightTargetRand = Util.Range(0.875f, 1.075f, _this.__8__1.rnd);
                        float clampHigh = 0.65f;
                        float clampLow = 0.45f;
                        float yearRemapToFreeTng = Util.Remap(_designYear, 1890f, 1940f, 0.6f, 0.4f, true);
                        float freeTngPortionStarting = 1f - Mathf.Clamp(weightTargetRand * yearRemapToFreeTng, clampLow, clampHigh);
                        var stopFunc = new System.Func<bool>(() =>
                        {
                            float targetRand = Util.Range(0.875f, 1.075f, _this.__8__1.rnd);
                            return (_ship.Weight() / _ship.Tonnage()) <= (1.0f - Mathf.Clamp(targetRand * yearRemapToFreeTng, clampLow, clampHigh));
                        });

                        // We can't access the nullable floats on this object
                        // so we cache off their values at the callsite (the
                        // only one that sets them).

                        ShipM.AdjustHullStats(
                          _ship,
                          -1,
                          freeTngPortionStarting,
                          stopFunc,
                          Patch_BattleManager_d115._ShipGenInfo.customSpeed <= 0f,
                          Patch_BattleManager_d115._ShipGenInfo.customArmor <= 0f,
                          true,
                          true,
                          true,
                          _this.__8__1.rnd,
                          Patch_BattleManager_d115._ShipGenInfo.limitArmor,
                          _this._savedSpeedMinValue_5__3);
                    }

                    // We can't do the frame-wait thing easily, let's just advance straight-away
                    _this.__1__state = 7;
                    break;

                case 10:
                    if (!Config.ShipGenReorder)
                    {
                        // We can't access the nullable floats on this object
                        // so we cache off their values at the callsite (the
                        // only one that sets them).

                        ShipM.AdjustHullStats(
                          _ship,
                          1,
                          1f,
                          null,
                          Patch_BattleManager_d115._ShipGenInfo.customSpeed <= 0f,
                          Patch_BattleManager_d115._ShipGenInfo.customArmor <= 0f,
                          true,
                          true,
                          true,
                          _this.__8__1.rnd,
                          Patch_BattleManager_d115._ShipGenInfo.limitArmor,
                          _this._savedSpeedMinValue_5__3);
                    }

                    _ship.UpdateHullStats();

                    foreach (var p in _ship.parts)
                        p.UpdateCollidersSize(_ship);

                    foreach (var p in _ship.parts)
                        Part.GunBarrelLength(p.data, _ship, true);

                    // We can't do the frame-wait thing easily, let's just advance straightaway
                    _this.__1__state = 11;
                    break;

                case 12:
                    if (Config.ShipGenReorder)
                    {
                        SelectComponents(ComponentSelectionPass.PostParts);
                        // Skip to next
                        _this.__1__state = 13;
                    }
                    break;
            }
        }

        public void OnPostfix(int origState)
        {
            // For now, we're going to reset all grades regardless.
            //if (__state == 1 && (!__instance._isRefitMode_5__2 || !__instance.isSimpleRefit))
            //    __instance.__4__this.TAFData().ResetAllGrades();
            switch (origState)
            {
                case 0:
                    EndInitialStep();
                    break;

                case 2:
                case 3:
                    PostTonnageUpdate();
                    break;
            }

            if (_newBDL.isValid)
                _origBDL.Apply(_ship);
        }

        public void OnGenerateEnd()
        {
        }

        private void OnStartARPN(Ship._AddRandomPartsNew_d__579 coroutine)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"Start finding parts");
            _arpnRoutine = coroutine;
            _doneAddingParts = !StartSelectParts();
        }

        public void OnARPNPrefix(Ship._AddRandomPartsNew_d__579 coroutine)
        {
            if (_arpnRoutine == null)
                OnStartARPN(coroutine);

            // this coroutine yields after a bunch of steps, not after
            // each part, so it doesn't do us any good to hook here right now
        }

        public void OnARPNPostfix(int state)
        {
            // this coroutine yields after a bunch of steps, not after
            // each part, so it doesn't do us any good to hook here right now
        }

        public void OnARPNEnd()
        {
            _arpnRoutine = null;
        }

        private void EndInitialStep()
        {
            if (Config.ShipGenTweaks)
            {
                _gunInfo.FillFor(_ship);

                if (!_this._isRefitMode_5__2)
                {
                    // For now, let each method handle it.
                    _this._savedSpeedMinValue_5__3 = -1f;
                }
            }
        }

        private void SetInitialStats()
        {
            _this.__8__1.rnd = new Il2CppSystem.Random(); // hilariously, the game seeds this with a random number (!)

            float speedKtsMin;
            float speedKtsMax;
            float speedLimiter = _ship.hull.data.speedLimiter;
            switch (_sType)
            {
                case "tb":
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1935f, 21f, 32f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1935f, 26f, 38f, true);
                    break;
                case "dd":
                    speedKtsMin = Util.Remap(_avgYear, 1895f, 1935f, 24f, 33f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1895f, 1935f, 29f, 38f, true);
                    break;
                case "cl":
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1930f, 17f, 28f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1930f, 24f, 37f, true);
                    break;
                case "ca":
                    speedKtsMin = Util.Remap(_avgYear, 1890f, 1930f, 16f, 28f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1890f, 1930f, 23f, 35f, true);
                    break;
                case "bc":
                    speedKtsMin = Util.Remap(_avgYear, 1900f, 1930f, 23f, 27f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1900f, 1930f, 27f, 34f, true);
                    break;
                case "bb":
                    speedKtsMin = Util.Remap(_avgYear, 1895f, 1935f, 15f, 21f, true);
                    speedKtsMax = Util.Remap(_avgYear, 1895f, 1935f, 22f, 34f, true);
                    break;
                case "ic":
                    speedKtsMin = 5f;
                    speedKtsMax = 11f;
                    break;
                default: // tr, amc
                    speedKtsMin = 8f;
                    speedKtsMax = 14f;
                    break;
            }
            float delta = speedKtsMax - speedKtsMin;
            float limitDeltaPct = (speedLimiter - (speedKtsMin + delta * 0.5f)) / delta;
            float bias = limitDeltaPct * 0.5f;

            float paramMin = speedLimiter * ShipM.GetParamSpeedMultMax(_ship, _this.__8__1.rnd);
            if (paramMin > 0f && paramMin > speedKtsMin)
                speedKtsMin = paramMin;
            float paramMax = speedLimiter * ShipM.GetParamSpeedMultMax(_ship, _this.__8__1.rnd);
            if (paramMax > 0 && paramMax < speedKtsMax)
                speedKtsMax = paramMax;
            float speedKts = Mathf.Lerp(speedKtsMin, speedKtsMax, (ModUtils.BiasRange(0.5f, bias) + ModUtils.DistributedRange(0.5f, _this.__8__1.rnd, bias)));
            //Melon<TweaksAndFixes>.Logger.Msg($"Speed: {speedKtsMin:F1}->{speedKtsMax:F1}={speedKts:F1}. ST {_ship.hull.data.shipType.speedMin:F1}->{_ship.hull.data.shipType.speedMax:F1}");
            speedKts = Mathf.Clamp(speedKts, _ship.hull.data.shipType.speedMin, _ship.hull.data.shipType.speedMax);
            speedKts = ModUtils.RoundToStep(speedKts, 0.1f);

            // Figure out a reasoanble beam
            float hullSpeedMin = paramMin > 0f ? paramMin : speedKtsMin;
            float hullSpeedMax = paramMax > 0f ? paramMax : speedKtsMax;
            float speedInRange = Mathf.InverseLerp(hullSpeedMin, hullSpeedMax, speedKts);
            float bT = Mathf.Clamp01(speedInRange + ModUtils.DistributedRange(0.3f, 3, null, _this.__8__1.rnd));
            float beamVal = ModUtils.LerpCentered(_ship.hull.data.beamMin, _ship.hull.data.beamMax, bT);

            bool needRefresh = false;
            // if this is a refit, we'll use this as our goal speed but maybe not hit it.
            if (_this._isRefitMode_5__2)
            {
                _this._savedSpeedMinValue_5__3 = Mathf.Max(_ship.speedMax / ShipM.KnotsToMS - 2f, (_ship.speedMax * 0.9f) / ShipM.KnotsToMS) * ShipM.KnotsToMS;
                if (ModUtils.Range(0f, 1f, null, _this.__8__1.rnd) > 0.75f)
                {
                    float speedMS = Mathf.Clamp(speedKts * ShipM.KnotsToMS, _ship.speedMax - 2f * ShipM.KnotsToMS, _ship.speedMax + 2f * ShipM.KnotsToMS);
                    _ship.SetSpeedMax(speedMS);
                }

                if (ModUtils.Range(0f, 1f, null, _this.__8__1.rnd) > 0.75f)
                {
                    if (beamVal > _ship.beam)
                    {
                        beamVal = Mathf.Clamp(Mathf.Min(beamVal, _ship.beam + 0.1f * (_ship.hull.data.beamMax - _ship.hull.data.beamMin)), _ship.hull.data.beamMin, _ship.hull.data.beamMax);
                        _ship.SetBeam(beamVal, false);
                        needRefresh = true;
                    }
                }
                if (ModUtils.Range(0f, 1f, null, _this.__8__1.rnd) > 0.5f)
                {
                    _ship.SetDraught(Mathf.Clamp(_ship.draught + ModUtils.DistributedRange((_ship.hull.data.draughtMax - _ship.hull.data.draughtMin) * 0.25f, _this.__8__1.rnd), _ship.hull.data.draughtMin, _ship.hull.data.draughtMax), false);
                    needRefresh = true;
                }
            }
            else
            {
                _ship.SetSpeedMax(speedKts * ShipM.KnotsToMS);
                _this._savedSpeedMinValue_5__3 = -1f;

                _ship.SetBeam(beamVal, false);
                _ship.SetDraught(ModUtils.LerpCentered(_ship.hull.data.draughtMin, _ship.hull.data.draughtMax, ModUtils.DistributedRange(0.5f, 3, null, _this.__8__1.rnd) + 0.5f), false);
                needRefresh = true;
            }
            if (needRefresh)
                _ship.RefreshHull(false);

            _desiredSpeed = _ship.speedMax;

            if (_this._isRefitMode_5__2)
            {
                if (!_this.isSimpleRefit)
                {
                    if (_ship.CurrentCrewQuarters < Ship.CrewQuarters.Standard && ModUtils.Range(0f, 1f, null, _this.__8__1.rnd) > 0.5f)
                        _ship.CurrentCrewQuarters = Ship.CrewQuarters.Standard;
                }
            }
            else
            {
                _ship.CurrentCrewQuarters = (Ship.CrewQuarters)ModUtils.RangeToInt(ModUtils.BiasRange(ModUtils.DistributedRange(1f, 2, null, _this.__8__1.rnd), _isLight ? -0.33f : 0f), 3);

                // We can't check _this.customRange, it's nullable.
                // But it's only ever set for missions, and there it's High (3).
                _ship.SetOpRange(GameManager.IsMission ?  VesselEntity.OpRange.High :
                    (VesselEntity.OpRange)ModUtils.Clamp(
                        (int)GetDesiredOpRange(_sType, _hullYear) + ModUtils.RangeToInt(ModUtils.DistributedRange(1f, 4, null, _this.__8__1.rnd), 3) - 1,
                        (int)ShipM.MinOpRange(_ship, VesselEntity.OpRange.Low),
                        (int)VesselEntity.OpRange.VeryHigh), true);

                // We can't check _this.customSurv, it's nullable. But it's never used.
                _ship.SetSurvivability((Ship.Survivability)ModUtils.Clamp(
                    ModUtils.RangeToInt(ModUtils.BiasRange(ModUtils.DistributedRange(1f, 4, null, _this.__8__1.rnd), _isLight ? 0f : 0.7f), 3) + 2,
                    (int)ShipM.MinSurv(_ship, Ship.Survivability.High),
                    (int)Ship.Survivability.VeryHigh));

                if (_ship.armor == null)
                    _ship.armor = new Il2CppSystem.Collections.Generic.Dictionary<Ship.A, float>();
                for (Ship.A armor = Ship.A.Belt; armor <= Ship.A.InnerDeck_3rd; armor = (Ship.A)((int)(armor + 1)))
                    _ship.armor[armor] = 0f;
                _ship.SetArmor(_ship.armor);
                // Yes, this is illegal. But we need the ship weight with no armor.

                CleanTAs();
                CleanTCs();
            }

            //Melon<TweaksAndFixes>.Logger.Msg($"Initial setup: kts {speedKtsMin:F1}->{speedKtsMax:F1}={speedKts:F1} (param {paramMin:F1}->{paramMax:F1}), b/t {beamVal:F1}/{_ship.draught:F1}. {_ship.CurrentCrewQuarters}/{_ship.opRange}/{_ship.survivability}");

            if (!GameManager.IsCampaign)
                _ship.CrewTrainingAmount = ModUtils.Range(17f, 100f, null, _this.__8__1.rnd);
        }

        public bool SkipNDI(NeedsDifferentItems ndi)
        {
            if (_this._isRefitMode_5__2)
                return false;

            return (_isCombatant && ndi.m_RequiredType == RequiredType.needs_Part && ndi.needCompOne == "gun")
                    || (_isCombatant && ndi.m_RequiredType == RequiredType.needs_Caliber && _ship.shipType.mainTo * 25.4f >= ndi.floatValueNeed)
                    || (_isCombatant && ndi.m_RequiredType == RequiredType.needs_Part && ndi.needCompOne == "tower_main")
                    || (_canUseTorps && ndi.m_RequiredType == RequiredType.needs_Part && ndi.needCompOne == "torpedo");
        }

        public enum ComponentSelectionPass
        {
            Initial,
            Armor,
            PostParts
        }

        public void SelectComponents(ComponentSelectionPass pass)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"Select component pass {pass}");

            List <CompType> compTypes = new List<CompType>();
            if (pass == ComponentSelectionPass.PostParts)
            {
                foreach (var ct in _ship.components.Keys)
                {
                    if (!_ship.IsComponentTypeAvailable(ct) || !Ui.NeedComponentsActive(ct, null, _ship, true, false))
                        compTypes.Add(ct);
                }
                //Melon<TweaksAndFixes>.Logger.Msg("Uninstalling components: " + string.Join(", ", compTypes.Select(c => ", " + c.name).ToList()));
                foreach (var ct in compTypes)
                    _ship.UninstallComponent(ct);

                return;
            }

            if (pass == ComponentSelectionPass.Initial)
            {
                // This starts null???
                if (_ship.parts == null)
                    _ship.parts = new Il2CppSystem.Collections.Generic.List<Part>();
                if (_ship.shipGunCaliber == null)
                    _ship.shipGunCaliber = new Il2CppSystem.Collections.Generic.List<Ship.TurretCaliber>();
                if (_ship.shipTurretArmor == null)
                    _ship.shipTurretArmor = new Il2CppSystem.Collections.Generic.List<Ship.TurretArmor>();

                _seenCompTypes.Clear();
            }

            CompType mines = null;
            CompType dc = null;
            CompType sweep = null;

            foreach (var ct in G.GameData.compTypes.Values)
            {
                if (pass != ComponentSelectionPass.PostParts)
                {
                    // Skip protection components for now.
                    switch (ct.name)
                    {
                        case "armor":
                        case "barbette":
                        case "torpedo_belt":
                        case "citadel":
                            if (pass == ComponentSelectionPass.Initial)
                                continue;
                            else
                                break;

                        default:
                            if (pass == ComponentSelectionPass.Armor)
                                continue;
                            else
                                break;
                    }
                }

                // If we're in post-parts, don't change existing components.
                // Just handle new ones.
                if (_seenCompTypes.Contains(ct.name))
                    continue;

                if (_ship.IsComponentTypeAvailable(ct) && Ui.NeedComponentsActive(ct, null, _ship, true, false))
                {
                    // These count as part of armament so need to get installed later.
                    //if (!_this._isRefitMode_5__2)
                    //{
                        if (ct.name == "mines")
                        {
                            mines = ct;
                            continue;
                        }
                        else if (ct.name == "depthcharge")
                        {
                            dc = ct;
                            continue;
                        }
                        else if (ct.name == "minesweep")
                        {
                            sweep = ct;
                            continue;
                        }
                    //}

                    compTypes.Add(ct);
                    _seenCompTypes.Add(ct.name);
                }
            }

            // This check is because we have multiple passes
            if (compTypes.Count == 0 && pass != ComponentSelectionPass.Initial)
                return;

            foreach (var ct in compTypes)
                InstallRandomComponentForType(ct);

            if (pass == ComponentSelectionPass.Initial)
            {
                _ship.RefreshHullStats();
                _ship.NeedRecalcCache();

                // record weight before adding protection components.
                // The portion of turret type or torpedo type components
                // that affect hull weight will show up here.
                _baseHullWeight = _ship.Weight();

                //if (!_this._isRefitMode_5__2)
                //{
                if (mines != null)
                    InstallRandomComponentForType(mines);
                if (dc != null)
                    InstallRandomComponentForType(dc);
                if (sweep != null)
                    InstallRandomComponentForType(sweep);
                //}
                _ship.RefreshHullStats();
                _ship.NeedRecalcCache();
                _prePartsWeight = _ship.Weight();

                SelectComponents(ComponentSelectionPass.Armor);
            }

            _ship.RefreshHullStats();
            _ship.NeedRecalcCache();
        }

        private void InstallRandomComponentForType(CompType ct)
        {
            foreach (var comp in G.GameData.components.Values)
            {
                if (comp.typex == ct && _ship.IsComponentAvailable(comp, out _) && Ui.NeedComponentsActive(ct, comp, _ship, true, false))
                {
                    _CompWeights[comp] = ComponentDataM.GetWeight(comp, _ship.shipType);
                }
            }
            if (_CompWeights.Count == 0)
            {
                //Melon<TweaksAndFixes>.Logger.Msg($"Tried to find comp for {ct.name} but 0 weight!");
                return;
            }
            var newComp = ModUtils.RandomByWeights(_CompWeights, null, _this.__8__1.rnd);
            if (newComp != null)
            {
                _ship.InstallComponent(newComp, true);
                //Melon<TweaksAndFixes>.Logger.Msg($"Selected {newComp.name} for {ct.name}");
            }
            _CompWeights.Clear();
        }

        private bool CheckSetMaxPayloadWeight()
        {
            float payloadAndArmorTng = _prePartsFreeTngNoPayload - _hullPartsTonnage;
            if (_payloadBaseWeight <= payloadAndArmorTng)
            {
                _maxPayloadWeight = payloadAndArmorTng * _armRatio;
                _maxPayloadWeight -= _payloadBaseWeight;

                if (_maxPayloadWeight < 0)
                    _maxPayloadWeight = 0.05f * (payloadAndArmorTng - _payloadBaseWeight);

                //Melon<TweaksAndFixes>.Logger.Msg($"Setpayload. Armor {payloadAndArmorTng:N0}, max payload {_maxPayloadWeight:N0}, ratio {_armRatio:F2}");

                return _maxPayloadWeight >= 0;
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Setpayload, already over base");
            return false;
        }

        public bool IsRPAllowed(RandPart rp)
        {
            //Melon<TweaksAndFixes>.Logger.Msg($"Check RP {rp.name}: {rp.type}. Done? F={_doneAddingFunnels}, P={_doneAddingParts}");
            if (rp.paramx.ContainsKey("delete_unmounted") || rp.paramx.ContainsKey("delete_refit"))
                return true;

            if (_doneAddingParts)
                return false;

            if (_doneAddingFunnels && rp.type == "funnel")
                return false;

            return true;
        }

        private bool StartSelectParts()
        {
            if (_this.isSimpleRefit)
                return true;

            _prePartsFreeTngNoPayload = _tonnage - _baseHullWeight;
            _payloadBaseWeight = _prePartsWeight - _baseHullWeight; // mines/DC/etc
            //Melon<TweaksAndFixes>.Logger.Msg($"tonnage {_tonnage:N0}, baseHull {_baseHullWeight:N0}, preparts {_prePartsFreeTngNoPayload:N0}, payload {_payloadBaseWeight:N0}");
            // If we can't even add towers/funnels, bail
            if (!CheckSetMaxPayloadWeight())
                return false;

            // this will increase when towers/funnels are added
            _hullPartsTonnage = 0;

            _payloadTotalWeight = 0;

            return true;
        }

        private void SetOverweight()
        {
            if (_doneAddingParts)
                return;
            _doneAddingParts = true;

            UpdateRPCacheOrSkipGun(_arpnRoutine.__8__1.randPart);
            _arpnRoutine._desiredAmount_5__10 = 0;
        }

        private void UnsetOverweight()
        {
            if (!_doneAddingParts)
                return;
            _doneAddingParts = false;

            if (!_doneAddingFunnels)
            {
                UpdateRPCacheOrSkipGun(_arpnRoutine.__8__1.randPart);
                _arpnRoutine._desiredAmount_5__10 = _rpDesiredAmount;
            }
        }

        private void SetEnoughFunnels()
        {
            if (_doneAddingFunnels)
                return;
            _doneAddingFunnels = true;

            UpdateRPCacheOrSkipGun(_arpnRoutine.__8__1.randPart);
            _arpnRoutine._desiredAmount_5__10 = 0;
        }

        private void UnsetEnoughFunnels()
        {
            if (!_doneAddingFunnels || _arpnRoutine._desiredAmount_5__10 != 0)
                return;
            _doneAddingParts = false;

            if (!_doneAddingParts)
            {
                UpdateRPCacheOrSkipGun(_arpnRoutine.__8__1.randPart);
                _arpnRoutine._desiredAmount_5__10 = _rpDesiredAmount;
            }
        }

        private bool EnoughFunnels()
        {
            if (!_ship.statsValid)
                _ship.CStats();
            if (_ship.stats.TryGetValue(G.GameData.stats["smoke_exhaust"], out var eff))
            {
                if (eff.total >= _maxFunnelEff)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateStoredWeight(float preWeight, Part part)
        {
            float delta = _ship.Weight() - preWeight;
            //bool isHull;
            switch (part.data.type)
            {
                case "funnel":
                case "tower_main":
                case "tower_sec":
                case "special":
                    //isHull = true;
                    _hullPartsTonnage += delta;
                    break;

                default:
                    _payloadTotalWeight += delta;
                    //isHull = false;
                    break;
            }
            //Melon<TweaksAndFixes>.Logger.Msg($"Changed part {part.data.name}, weight delta {delta:F1}, is hull? {isHull}");
        }

        public void OnAddPart(float preWeight, Part part)
        {
            if (!Config.ShipGenReorder)
                return;

            if (part.data.isFunnel)
            {
                if (EnoughFunnels())
                    SetEnoughFunnels();
                else
                    UnsetEnoughFunnels();
            }

            UpdateStoredWeight(preWeight, part);

            if (!CheckSetMaxPayloadWeight() || _payloadTotalWeight >= _maxPayloadWeight)
                SetOverweight();
        }

        public void OnRemovePart(float preWeight, Part part)
        {
            if (!Config.ShipGenReorder)
                return;

            if (part.data.isFunnel)
            {
                if (EnoughFunnels())
                    SetEnoughFunnels();
                else
                    UnsetEnoughFunnels();
            }

            UpdateStoredWeight(preWeight, part);

            if (CheckSetMaxPayloadWeight() && _payloadTotalWeight < _maxPayloadWeight)
                UnsetOverweight();
        }

        private static List<PartData> _TempDatas = new List<PartData>();
        public void OnAddTurretArmor(Part part)
        {
            if (_arpnRoutine == null)
                return;

            // We need to fix the armor levels if this is a new TA.
            if (Config.ShipGenTweaks)
            {
                // we know we jsut did AdjustHullStats, so this is safe
                // (i.e. there was a lerpval set that we can reapply)
                if (_armorInfo != null)
                    _armorInfo.ReapplyArmor(_ship);
            }

            if (!_gunInfo.isLimited || UpdateRPCacheOrSkipGun(_arpnRoutine.__8__1.randPart))
                return;

            // Register reports true iff we're at the count limit
            if (_gunInfo.RegisterCaliber(_rpBattery, part.data))
            {
                // Ideally we'd do RemoveAll, but we can't use a managed predicate
                // on the native list. We could reimplement RemoveAll, but I don't trust
                // calling RuntimeHelpers across the boundary. This should still be faster
                // than the O(n^2) of doing RemoveAts, because we don't have to copy
                // back to compress the array each time.
                for (int i = _arpnRoutine._chooseFromParts_5__11.Count; i-- > 0;)
                    if (_gunInfo.CaliberOK(_rpBattery, _arpnRoutine._chooseFromParts_5__11[i]))
                        _TempDatas.Add(_arpnRoutine._chooseFromParts_5__11[i]);

                _arpnRoutine._chooseFromParts_5__11.Clear();
                for (int i = _TempDatas.Count; i-- > 0;)
                    _arpnRoutine._chooseFromParts_5__11.Add(_TempDatas[i]);

                _TempDatas.Clear();
            }
        }

        public bool OnGetParts_ShouldSkip(Ship.__c__DisplayClass578_0 coroutineData, PartData data)
        {
            if (!_gunInfo.isLimited || UpdateRPCacheOrSkipGun(coroutineData.randPart))
                return false;

            int partCal = (int)((data.caliber + 1f) * (1f / 25.4f));
            return !_gunInfo.CaliberOK(_rpBattery, partCal);
        }

        private bool IsPlacementValid(Part part)
        {
            if (!part.CanPlace())
                return false;

            if (part.data.isGun || part.data.isTorpedo)
            {
                if (!part.CanPlaceSoft())
                    return false;

                if (!Ship.IsCasemateGun(part.data) && !part.CanPlaceSoftLight())
                    return false;
            }

            foreach (var p in _ship.parts)
            {
                if (p == part)
                    continue;

                if (p.data.isTorpedo || (p.data.isGun && !Ship.IsCasemateGun(p.data)))
                {
                    if (!p.CanPlaceSoftLight())
                        return false;
                }
            }

            return true;
        }

        private bool IsAllowedMount(Mount m, Part part, Il2CppSystem.Collections.Generic.List<string> demandMounts, Il2CppSystem.Collections.Generic.List<string> excludeMounts)
        {
            if (!m.Fits(part, demandMounts, excludeMounts))
                return false;
            if (_ship.mountsUsed.TryGetValue(m, out var otherPart) && otherPart != part)
                return false;
            if (m.parentPart == part)
                return false;

            return true;
        }

        private float GetArmamentRatio(float year)
        {
            return _sType switch
            {
                "dd" => Util.Remap(year, 1900f, 1940f, 0.9f, 0.85f, true),
                "cl" => Util.Remap(year, 1890f, 1940f, 0.18f, 0.25f, true),
                "ca" => Util.Remap(year, 1890f, 1940f, 0.15f, 0.18f, true),
                "bc" => Util.Remap(year, 1915f, 1935f, 0.21f, 0.15f, true),
                "bb" => Util.Remap(year, 1895f, 1935f, 0.18f, 0.15f, true),
                "ic" => 0.15f,
                _ => 1f
            };
        }
    }
}