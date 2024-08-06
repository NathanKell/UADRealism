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
using TweaksAndFixes;

#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public static class ShipM
    {
        public static Ship.TurretArmor FindMatchingTurretArmor(Ship ship, PartData data)
            => FindMatchingTurretArmor(ship, data.caliber, Ship.IsCasemateGun(data));

        public static Ship.TurretArmor FindMatchingTurretArmor(Ship ship, float caliber, bool isCasemate)
        {
            foreach (var ta in ship.shipTurretArmor)
                if (ta.turretPartData.caliber == caliber && ta.isCasemateGun == isCasemate)
                    return ta;

            return null;
        }

        public static void CloneFrom(this Ship.TurretArmor dest, Ship.TurretArmor src)
        {
            dest.barbetteArmor = src.barbetteArmor;
            dest.isCasemateGun = src.isCasemateGun;
            dest.sideTurretArmor = src.sideTurretArmor;
            dest.topTurretArmor = src.topTurretArmor;
        }

        public static Ship.TurretCaliber FindMatchingTurretCaliber(Ship ship, PartData data)
            => FindMatchingTurretCaliber(ship, data.caliber, Ship.IsCasemateGun(data));

        public static Ship.TurretCaliber FindMatchingTurretCaliber(Ship ship, float caliber, bool isCasemate)
        {
            foreach (var tc in ship.shipGunCaliber)
                if (tc.turretPartData.caliber == caliber && isCasemate == tc.isCasemateGun)
                    return tc;

            return null;
        }

        public static void CloneFrom(this Ship.TurretCaliber dest, Ship.TurretCaliber src)
        {
            dest.diameter = src.diameter;
            dest.isCasemateGun = src.isCasemateGun;
            dest.length = src.length;
        }

        public static bool ExistsMount(Ship ship, PartData part, Il2CppSystem.Collections.Generic.List<string> demandMounts = null, Il2CppSystem.Collections.Generic.List<string> excludeMounts = null, bool allowUsed = true)
        {
            foreach (var m in ship.mounts)
                if ((allowUsed || m.employedPart == null) && m.Fits(part, demandMounts, excludeMounts))
                    return true;

            return false;
        }
    }
}