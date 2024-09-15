﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using MelonLoader;

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
        public class BeamDraughtData
        {
            const string _BDLParamName = "ai_beamdraughtlimits";

            public float beamMin;
            public float beamMax;
            public float draughtMin;
            public float draughtMax;

            public BeamDraughtData(PartData hullData)
            {
                beamMin = hullData.beamMin;
                beamMax = hullData.beamMax;
                draughtMin = hullData.draughtMin;
                draughtMax = hullData.draughtMax;
            }

            public BeamDraughtData(Il2CppSystem.Collections.Generic.List<string> bdlParam, string hName)
            {
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
                var hd = ship.hull.data;
                if (!hd.paramx.TryGetValue(_BDLParamName, out var bdlParam))
                    return null;

                if (bdlParam.Count != 4)
                {
                    Melon<TweaksAndFixes>.Logger.Error($"For ship {hd.name}, {_BDLParamName} doesn't have 4 elements. The elements should be beamMin, beamMax, draughtMin, draughtMax. Values of 0 mean keep existing non-AI values.");
                    return null;
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
        }

        static Dictionary<ComponentData, float> _CompWeights = new Dictionary<ComponentData, float>();

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
        BeamDraughtData origBDL = null;
        BeamDraughtData newBDL = null;

        float _limitArmor = -1f;
        float _limitSpeed = -1f;
        float _customSpeed = -1f;
        float _customArmor = -1f;

        RandPart _LastRandPart = null;
        bool _LastRPIsGun = false;
        ShipM.BatteryType _LastBattery = ShipM.BatteryType.main;
        ShipM.GenGunInfo _GenGunInfo = new ShipM.GenGunInfo();
        GenArmorData.GenArmorInfo _gaInfo;
        Ship._AddRandomPartsNew_d__579 _AddRandomPartsRoutine = null;

        List<PartData> _availableParts = new List<PartData>();

        HashSet<string> _seenCompTypes = new HashSet<string>();

        int[] _gunGrades = new int[21];

        float _tonnage;
        float _baseHullWeight;
        float _prePartsWeight;
        float _armRatio;
        float _prePartsFreeTngNoPayload;
        float _hullPartsTonnage = 0;
        float _lastWeight = 0;

        float _payloadBaseWeight;
        float _payloadTotalWeight;
        float _maxPayloadWeight;
        bool _doneAddingParts = false;
        bool _doneAddingFunnels = false;

        public GenerateShip(Ship._GenerateRandomShip_d__562 coroutine)
        {
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

            newBDL = BeamDraughtData.FromParam(_ship);
            if (newBDL != null)
            {
                origBDL = new BeamDraughtData(_ship.hull.data);
            }

            if (Patch_BattleManager_d114._ShipGenInfo.isActive)
            {
                _limitSpeed = Patch_BattleManager_d114._ShipGenInfo.limitSpeed;
                _customSpeed = Patch_BattleManager_d114._ShipGenInfo.customSpeed;
                _limitArmor = Patch_BattleManager_d114._ShipGenInfo.limitArmor;
                _customArmor = Patch_BattleManager_d114._ShipGenInfo.customArmor;
            }

            InitParts();

            _gaInfo = GenArmorData.GetInfoFor(this._ship);

            _armRatio = GetArmamentRatio(_avgYear);
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

        private bool UpdateRPGunCacheOrSkip(RandPart rp)
        {
            if (rp != _LastRandPart)
            {
                _LastRandPart = rp;
                _LastRPIsGun = rp.type == "gun";
                if (_LastRPIsGun)
                    _LastBattery = rp.condition.Contains("main_cal") ? ShipM.BatteryType.main : (rp.condition.Contains("sec_cal") ? ShipM.BatteryType.sec : ShipM.BatteryType.ter);
            }
            return !_LastRPIsGun;
        }

        public void PostTonnageUpdate()
        {
            _tonnage = _ship.Tonnage();
            _tngRatio = Mathf.InverseLerp(_ship.TonnageMin(), Math.Min(_ship.TonnageMax(), _ship.player.TonnageLimit(_ship.shipType)), _tonnage);
        }

        public void OnPrefix()
        {
            if (newBDL != null)
                newBDL.Apply(_ship);

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
                          Patch_BattleManager_d114._ShipGenInfo.customSpeed <= 0f,
                          Patch_BattleManager_d114._ShipGenInfo.customArmor <= 0f,
                          true,
                          true,
                          true,
                          _this.__8__1.rnd,
                          Patch_BattleManager_d114._ShipGenInfo.limitArmor,
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
                          Patch_BattleManager_d114._ShipGenInfo.customSpeed <= 0f,
                          Patch_BattleManager_d114._ShipGenInfo.customArmor <= 0f,
                          true,
                          true,
                          true,
                          _this.__8__1.rnd,
                          Patch_BattleManager_d114._ShipGenInfo.limitArmor,
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
            switch (_this.__1__state)
            {
                case 0:
                    EndInitialStep();
                    break;

                case 3:
                    PostTonnageUpdate();
                    break;
            }

            if (newBDL != null)
                origBDL.Apply(_ship);
        }

        public void OnGenerateEnd()
        {
        }

        private void OnStartARPN(Ship._AddRandomPartsNew_d__579 coroutine)
        {
            _AddRandomPartsRoutine = coroutine;
            _doneAddingParts = !StartSelectParts();
        }

        public void OnARPNPrefix(Ship._AddRandomPartsNew_d__579 coroutine)
        {
            if (_AddRandomPartsRoutine == null)
                OnStartARPN(coroutine);

            _lastWeight = _ship.Weight();
            switch (coroutine.__1__state)
            {
                case 2: // pick a part and place it
                    if (_doneAddingParts)
                    {
                        // if we've created only one of a pair, we have to create the other too.
                        // Otherwise, we're done picking for this (or any) RP. So force this RP
                        // to finish. Note that we will be slightly overweight from whatever the
                        // last part was.
                        if (coroutine._firstPairCreated_5__12 == null)
                            coroutine._desiredAmount_5__10 = 0;
                    }
                    else
                    {
                        if (!_ship.statsValid)
                            _ship.CStats();
                        if (_ship.stats.TryGetValue(G.GameData.stats["smoke_exhaust"], out var eff))
                        {
                            if (eff.total >= Config.Param("taf_generate_funnel_maxefficiency", 150f))
                            {
                                foreach (var p in G.GameData.parts.Values)
                                {
                                    if (p.type == "funnel")
                                        _ship.badData.Add(p);
                                }
                            }
                        }
                    }
                    break;
            }
        }

        public void OnARPNPostfix(int state)
        {
            switch (state)
            {
                case 2:
                    // Assume the last part in the parts list is the most recently added.
                    int idx = _ship.parts.Count - 1;
                    if (idx >= 0)
                        OnAddPart(_lastWeight, _ship.parts[idx].data);
                    // TODO: Remove this part and mark bad if we are over mass (or if paired, if
                    // second would push us over.

                    if (_doneAddingParts)
                    {
                        // if we've created only one of a pair, we have to create the other too.
                        // Otherwise, we're done picking for this (or any) RP. So force this RP
                        // to finish. Note that we will be slightly overweight from whatever the
                        // last part was.
                        if (_AddRandomPartsRoutine._firstPairCreated_5__12 == null)
                            _AddRandomPartsRoutine._desiredAmount_5__10 = 0;
                    }
                    break;
            }
        }

        public void OnARPNEnd()
        {
            _AddRandomPartsRoutine = null;
        }

        private void EndInitialStep()
        {
            if (Config.ShipGenTweaks)
            {
                _GenGunInfo.FillFor(_ship);

                if (!G.ui.isConstructorRefitMode)
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
            float delta = speedKtsMax = speedKtsMin;
            float limitDeltaPct = (speedLimiter - (speedKtsMin + delta * 0.5f)) / delta;
            float bias = limitDeltaPct * 0.5f;

            float paramMin = speedLimiter * ShipM.GetParamSpeedMultMax(_ship, _this.__8__1.rnd);
            if (paramMin > 0f && paramMin > speedKtsMin)
                speedKtsMin = paramMin;
            float paramMax = speedLimiter * ShipM.GetParamSpeedMultMax(_ship, _this.__8__1.rnd);
            if (paramMax > 0 && paramMax < speedKtsMax)
                speedKtsMax = paramMax;
            float speedKts = Mathf.Lerp(speedKtsMin, speedKtsMax, (ModUtils.BiasRange(0.5f, bias) + ModUtils.DistributedRange(0.5f, _this.__8__1.rnd, bias)));
            speedKts = Mathf.Clamp(_ship.hull.data.shipType.speedMin, _ship.hull.data.shipType.speedMax, speedKts);
            speedKts = ShipM.RoundSpeedToStep(speedKts);

            // Figure out a reasoanble beam
            if (paramMin > 0f)
                speedKtsMin = paramMin;
            if (paramMax > 0f)
                speedKtsMax = paramMax;
            float speedInRange = Mathf.InverseLerp(speedKtsMin, speedKtsMax, speedKts);
            float beamVal = Mathf.Lerp(_ship.hull.data.beamMin, _ship.hull.data.beamMax, speedInRange + ModUtils.DistributedRange(0.3f, 3, null, _this.__8__1.rnd));

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
                    _ship.SetDraught(Mathf.Clamp(_ship.draught + ModUtils.Range(0f, (_ship.hull.data.draughtMax - _ship.hull.data.draughtMin) * 0.25f, null, _this.__8__1.rnd), _ship.hull.data.draughtMin, _ship.hull.data.draughtMax), false);
                    needRefresh = true;
                }
            }
            else
            {
                _ship.SetSpeedMax(speedKts * ShipM.KnotsToMS);
                _this._savedSpeedMinValue_5__3 = -1f;

                _ship.SetBeam(beamVal, false);
                _ship.SetDraught(Mathf.Lerp(_ship.hull.data.draughtMin, _ship.hull.data.draughtMax, ModUtils.DistributedRange(0.5f, 3, null, _this.__8__1.rnd) + 0.5f), false);
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

            if (!GameManager.IsCampaign)
                _ship.CrewTrainingAmount = ModUtils.Range(17f, 100f, null, _this.__8__1.rnd);
        }

        public enum ComponentSelectionPass
        {
            Initial,
            Armor,
            PostParts
        }

        public void SelectComponents(ComponentSelectionPass pass)
        {
            List<CompType> compTypes = new List<CompType>();
            if (pass == ComponentSelectionPass.PostParts)
            {
                foreach (var ct in _ship.components.Keys)
                {
                    if (!_ship.IsComponentTypeAvailable(ct) || !Ui.NeedComponentsActive(ct, null, _ship, true, false))
                        compTypes.Add(ct);
                }
                foreach (var ct in compTypes)
                    _ship.UninstallComponent(ct);

                return;
            }

            
            Part gun = null;
            PartData gunData = null;
            Part torp = null;
            PartData torpData = null;
            if (pass == ComponentSelectionPass.Initial)
            {
                _seenCompTypes.Clear();

                // Add temporary gun and torp
                bool needGun = _isCombatant && !_this.isSimpleRefit;
                bool needTorp = _canUseTorps && !_this.isSimpleRefit;
                foreach (var p in _ship.parts)
                {
                    if (p.data.isGun)
                        needGun = false;
                    else if (p.data.isTorpedo)
                        needTorp = false;
                }
                if (needGun || needTorp)
                {
                    foreach (var data in _availableParts)
                    {
                        if (needGun && data.isGun)
                        {
                            gunData = data;
                            gun = Part.Create(data, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int);
                            _ship.AddPart(gun);
                            needGun = false;
                        }
                        else if (needTorp && data.isTorpedo)
                        {
                            torpData = data;
                            torp = Part.Create(data, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int);
                            _ship.AddPart(gun);
                            needTorp = false;
                        }
                        if (!needTorp && !needGun)
                            break;
                    }
                }
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
                if (gun != null)
                {
                    _ship.RemovePart(gun);
                    gun = null;
                    CleanTAs();
                    CleanTCs();
                }
                if (torp != null)
                {
                    _ship.RemovePart(torp);
                    torp = null;
                }

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

                if (gunData != null)
                {
                    gun = Part.Create(gunData, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int);
                    _ship.AddPart(gun);
                }
                if (torpData != null)
                {
                    torp = Part.Create(torpData, _ship, _ship.partsCont, ModUtils._NullableEmpty_Int);
                    _ship.AddPart(torp);
                }

                SelectComponents(ComponentSelectionPass.Armor);

                if (gun != null)
                {
                    _ship.RemovePart(gun);
                    CleanTAs();
                    CleanTCs();
                }
                if (torp != null)
                    _ship.RemovePart(torp);
            }

            _ship.RefreshHullStats();
            _ship.NeedRecalcCache();
        }

        private void InstallRandomComponentForType(CompType ct)
        {
            foreach (var comp in G.GameData.components.Values)
            {
                if (_ship.IsComponentAvailable(comp, out _) && Ui.NeedComponentsActive(ct, comp, _ship, true, false))
                {
                    _CompWeights[comp] = ComponentDataM.GetWeight(comp, _ship.shipType);
                }
            }
            if (_CompWeights.Count == 0)
                return;
            var newComp = ModUtils.RandomByWeights(_CompWeights, null, _this.__8__1.rnd);
            if (newComp != null)
                _ship.InstallComponent(newComp, true);
            _CompWeights.Clear();
        }

        private bool CheckSetMaxPayloadWeight()
        {
            float payloadAndArmorTng = _prePartsFreeTngNoPayload - _hullPartsTonnage;
            if (_payloadBaseWeight > payloadAndArmorTng)
                return false;

            _maxPayloadWeight = payloadAndArmorTng * _armRatio;
            _maxPayloadWeight -= _payloadBaseWeight;

            if (_maxPayloadWeight < 0)
                _maxPayloadWeight = 0.05f * (payloadAndArmorTng - _payloadBaseWeight);
            if (_maxPayloadWeight < 0)
                return false;

            return true;
        }

        public bool IsRPAllowed(RandPart rp)
        {
            if (rp.paramx.ContainsKey("delete_unmounted") || rp.paramx.ContainsKey("delete_refit"))
                return true;

            if (rp.type == "funnel" && _doneAddingFunnels)
                return false;

            return !_doneAddingParts;
        }

        private bool StartSelectParts()
        {
            if (_this.isSimpleRefit)
                return true;

            _prePartsFreeTngNoPayload = _tonnage - _baseHullWeight;
            _payloadBaseWeight = _prePartsWeight - _baseHullWeight; // mines/DC/etc
            // If we can't even add towers/funnels, bail
            if (!CheckSetMaxPayloadWeight())
                return false;


            // this will increase when towers/funnels are added
            _hullPartsTonnage = 0;

            _payloadTotalWeight = 0;

            return true;
        }

        public bool OnAddPart(float preWeight, PartData data)
        {
            // Verify we can have _any_ payload,
            // i.e. the towers/funnels we added aren't
            // already putting us over budget.
            if (!CheckSetMaxPayloadWeight())
                return false;

            if (_payloadTotalWeight >= _maxPayloadWeight)
                return false;

            float delta = _ship.Weight() - preWeight;
            bool isHull = true;
            switch (data.type)
            {
                case "funnel":
                case "tower_main":
                case "tower_sec":
                case "special":
                    _hullPartsTonnage += delta;
                    break;

                default:
                    isHull = false;
                    _payloadTotalWeight += delta;
                    break;
            }


            if (isHull)
            {
                if (!CheckSetMaxPayloadWeight())
                    return false;
            }
            else if (_payloadTotalWeight >= _maxPayloadWeight && _AddRandomPartsRoutine._firstPairCreated_5__12 == null)
            {
                return false;
            }

            return true;
        }

        private static List<PartData> _TempDatas = new List<PartData>();
        public void OnAddTurretArmor(Part part)
        {
            if (_AddRandomPartsRoutine == null)
                return;

            // We need to fix the armor levels if this is a new TA.
            if (Config.ShipGenTweaks)
            {
                // we know we jsut did AdjustHullStats, so this is safe
                // (i.e. there was a lerpval set that we can reapply)
                if (_gaInfo != null)
                    _gaInfo.ReapplyArmor(_ship);
            }

            if (!_GenGunInfo.isLimited || UpdateRPGunCacheOrSkip(_AddRandomPartsRoutine.__8__1.randPart))
                return;

            // Register reports true iff we're at the count limit
            if (_GenGunInfo.RegisterCaliber(_LastBattery, part.data))
            {
                // Ideally we'd do RemoveAll, but we can't use a managed predicate
                // on the native list. We could reimplement RemoveAll, but I don't trust
                // calling RuntimeHelpers across the boundary. This should still be faster
                // than the O(n^2) of doing RemoveAts, because we don't have to copy
                // back to compress the array each time.
                for (int i = _AddRandomPartsRoutine._chooseFromParts_5__11.Count; i-- > 0;)
                    if (_GenGunInfo.CaliberOK(_LastBattery, _AddRandomPartsRoutine._chooseFromParts_5__11[i]))
                        _TempDatas.Add(_AddRandomPartsRoutine._chooseFromParts_5__11[i]);

                _AddRandomPartsRoutine._chooseFromParts_5__11.Clear();
                for (int i = _TempDatas.Count; i-- > 0;)
                    _AddRandomPartsRoutine._chooseFromParts_5__11.Add(_TempDatas[i]);

                _TempDatas.Clear();
            }
        }

        public bool OnGetParts_ShouldSkip(Ship.__c__DisplayClass578_0 coroutineData, PartData data)
        {
            if (!_GenGunInfo.isLimited || UpdateRPGunCacheOrSkip(coroutineData.randPart))
                return false;

            int partCal = (int)((data.caliber + 1f) * (1f / 25.4f));
            return !_GenGunInfo.CaliberOK(_LastBattery, partCal);
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