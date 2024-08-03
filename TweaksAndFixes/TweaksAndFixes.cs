﻿using MelonLoader;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(TweaksAndFixes.TweaksAndFixes), "TweaksAndFixes", "2.4.1", "NathanKell")]
[assembly: MelonColor(255, 220, 220, 0)]
namespace TweaksAndFixes
{
    public static class Config
    {
        public static int MaxGunGrade = 5;
    }

    public class TweaksAndFixes : MelonMod
    {
        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
        }

        public override void OnDeinitializeMelon()
        {
            base.OnDeinitializeMelon();
        }
    }
}
