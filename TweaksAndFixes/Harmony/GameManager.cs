using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using UnityEngine.UI;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(GameManager))]
    internal class Patch_GameManager
    {
        private static bool _IsRefreshSharedDesign = false;
        
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameManager.RefreshSharedDesign))]
        internal static void Prefix_RefreshSharedDesign()
        {
            _IsRefreshSharedDesign = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameManager.RefreshSharedDesign))]
        internal static void Postfix_RefreshSharedDesign()
        {
            _IsRefreshSharedDesign = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameManager.GetTechYear))]
        internal static bool Prefix_GetTechYear(TechnologyData t, ref int __result)
        {
            if (_IsRefreshSharedDesign && G.ui.sharedDesignYear == Config.StartingYear && !t.effects.ContainsKey("start"))
            {
                __result = 9999;
                return false;
            }

            return true;
        }


        internal static GameObject? _Batcher = null;
        [HarmonyPatch(nameof(GameManager.EnterMainMenu))]
        [HarmonyPostfix]
        internal static void Postfix_EnterMainMenu()
        {
            //if (_Batcher != null)
            //{
            //    GameObject.DestroyImmediate(_Batcher);
            //    _Batcher = null;
            //}

            //_Batcher = new GameObject("BatchShipGenerator");
            //var rootRect = _Batcher.AddComponent<RectTransform>();
            //var rootVLG = _Batcher.AddComponent<VerticalLayoutGroup>();
            //var bsg = _Batcher.AddComponent<BatchShipGenerator>();
            
            /*
             * public Button yearsButton { get; set; }
        public Toggle yearTemplate { get; set; }
        public Button closeYearsPanel { get; set; }
        public Transform yearToggleParent { get; set; }
        public GameObject yearsPanel { get; set; }
        public Button startButton { get; set; }
        public int startYear { get; set; }
        public TMP_InputField shipsAmount { get; set; }
        public TMP_Dropdown shipTypeDropdown { get; set; }
        public GameObject InitRoot { get; set; }
        public GameObject UIRoot { get; set; }
        public GameObject levelMainMenu { get; set; }
        public int maxYear { get; set; }
        public List<Toggle> yearsSelected { get; set; }
        public List<string> info { get; set; }
        public List<string> errors { get; set; }
        public TMP_Dropdown nationDropdown { get; set; }
        public TMP_Text progress { get; set; }
             */
        }
    }

    [HarmonyPatch(typeof(GameManager._LoadCampaign_d__98))]
    internal class Patch_GameManager_LoadCampaigndCoroutine
    {
        // This method calls CampaignController.PrepareProvinces *before* CampaignMap.PreInit
        // So we patch here and skip the preinit patch.
        [HarmonyPatch(nameof(GameManager._LoadCampaign_d__98.MoveNext))]
        [HarmonyPrefix]
        internal static void Prefix_MoveNext(GameManager._LoadCampaign_d__98 __instance)
        {
            if (__instance.__1__state == 6 && (Config.OverrideMap != Config.OverrideMapOptions.Disabled))
                MapData.LoadMapData();
            Patch_CampaignMap._SkipNextMapPatch = true;
        }
    }
}
