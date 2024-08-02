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
using Il2CppInterop.Runtime.Attributes;

#pragma warning disable CS0649
#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    public class MapData
    {
        public class PortElement : Serializer.IPostProcess
        {
            //@name,enabled,nameUi,province,#provinceName,latitude,longitude,#posLink,portСapacity,#baseportcapacity,#balancer,seaControlDistanceMultiplier,inBeingDistanceMultiplier,#changesea
            [Serializer.Field] string name;
            [Serializer.Field] string nameUi;
            [Serializer.Field] string province;
            [Serializer.Field] int portCapacity = -1;
            [Serializer.Field] float seaControlDistanceMultiplier = -1;
            [Serializer.Field] float inBeingDistanceMultiplier = -1;

            public void PostProcess()
            {
                Il2Cpp.PortElement old = null;
                foreach (var p in _Instance._map.Ports.Ports)
                {
                    if (p.Id == name)
                    {
                        old = p;
                        break;
                    }
                }
                if (old == null)
                    return;

                old.Name = nameUi;
                old.ProvinceId = province;
                old.PortCapacity = portCapacity;
                old.SeaControlDistanceMultiplier = seaControlDistanceMultiplier;
                old.InBeingDistanceMultiplier = inBeingDistanceMultiplier;
            }
        }

        public class Province : Serializer.IPostProcess
        {
            //@name,enabled,nameUi,area,controller,controller_1890,controller_1900,controller_1910,controller_1920,controller_1930,controller_1940,claim,isHome,
            ///type,latitude,longitude,#posLink,development,port,population,oilDiscoveryYear,oilDiscoveryBaseChance,oilCapacity,oilReservesInTurns,revoltChance,provinceArmyPercentage,
            ///ProvinceDefenderBonus,neighbour_provinces,#Naval_Defense_Level,#Enemies,#Protectors,#Rebellion_Chance,#Naval_Invasion_Chance,#Min_Naval_Invasion_Displacement,#AverageFleetStrength,#comment,#comment2
            [Serializer.Field] string name;
            [Serializer.Field] string nameUi;
            [Serializer.Field] string area;
            [Serializer.Field] string controller;
            [Serializer.Field] string controller_1890;
            [Serializer.Field] string controller_1900;
            [Serializer.Field] string controller_1910;
            [Serializer.Field] string controller_1920;
            [Serializer.Field] string controller_1930;
            [Serializer.Field] string controller_1940;
            [Serializer.Field] string claim;
            [Serializer.Field] int isHome;
            [Serializer.Field] string type;
            [Serializer.Field] float development;
            [Serializer.Field] float port;
            [Serializer.Field] float population;
            [Serializer.Field] int oilDiscoveryYear;
            [Serializer.Field] float oilDiscoveryBaseChance;
            [Serializer.Field] float oilCapacity;
            [Serializer.Field] float oilReservesInTurns;
            [Serializer.Field] float revoltChance;
            [Serializer.Field] float provinceArmyPercentage;
            [Serializer.Field] float ProvinceDefenderBonus;
            [Serializer.Field] string neighbour_provinces;

            public void PostProcess()
            {
                Il2Cpp.Province old = null;
                foreach (var p in _Instance._map.Provinces.Provinces)
                {
                    if (p.Id == name)
                    {
                        old = p;
                        break;
                    }
                }
                if (old == null)
                    return;

                old.Name = nameUi;
                old.AreaId = area;
                old.Controller = controller;
                old.Controller_1890 = controller_1890;
                old.Controller_1900 = controller_1900;
                old.Controller_1910 = controller_1910;
                old.Controller_1920 = controller_1920;
                old.Controller_1930 = controller_1930;
                old.Controller_1940 = controller_1940;
                old.Claim = claim;
                old.IsHome = isHome > 0;
                old.Type = type;
                old.Development = development;
                old.Port = port;
                old.population = population;
                old.oilDiscoveryYear = oilDiscoveryYear;
                old.oilDiscoveryBaseChance = oilDiscoveryBaseChance;
                old.oilCapacity = oilCapacity;
                old.oilReservesInTurns = oilReservesInTurns;
                old.RevoltChance = revoltChance;
                old.ProvinceArmyPercentage = provinceArmyPercentage;
                old.ProvinceDefenderBonus = ProvinceDefenderBonus;
                old.NeighbourProvincesString = neighbour_provinces;
            }
        }

        static MapData _Instance = null;

        CampaignMap _map;

        public static void LoadMapData(CampaignMap map)
        {
            _Instance = new MapData(map);
            _Instance.LoadData();
            _Instance = null;
        }

        public MapData(CampaignMap map) { _map = map; }

        private void LoadData()
        {
            bool success = true;
            success &= Load<PortElement>("ports");
            success &= Load<Province>("provinces");
            if(success)
                Melon<TweaksAndFixes>.Logger.Msg($"Loaded map data successfully`");
            else
                Melon<TweaksAndFixes>.Logger.Error($"Failed to load overriding map data");
        }

        private bool Load<T>(string assetName)
        {
            var text = Util.ResourcesLoad<TextAsset>(assetName, false);
            if (text == null)
            {
                Melon<TweaksAndFixes>.Logger.Error($"Could not find asset `{assetName}`");
                return false;
            }
            List<T> items = new List<T>();
            var input = text.text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (input.Length < 2)
            {
                Melon<TweaksAndFixes>.Logger.Error($"Fewer than 2 lines of text in asset `{assetName}`");
                return false;
            }
            return Serializer.CSV.Read<List<T>, T>(input, items, true, true);
        }
    }
}
