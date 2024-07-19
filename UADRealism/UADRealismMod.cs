using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UADRealism.Data;

namespace UADRealism
{
    public class UADRealismMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
            TweaksAndFixes.Config.MaxGunGrade = GunDatabase.MaxGunGrade;
        }

        public override void OnDeinitializeMelon()
        {
            base.OnDeinitializeMelon();
        }
    }
}
