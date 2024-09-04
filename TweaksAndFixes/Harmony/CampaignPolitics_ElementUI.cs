using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using System.Collections.Generic;
using UnityEngine.UI;

namespace TweaksAndFixes
{
    [HarmonyPatch(typeof(CampaignPolitics_ElementUI))]
    internal class Patch_CampaignPolitics_ElementUI
    {
        [HarmonyPatch(nameof(CampaignPolitics_ElementUI.Init))]
        [HarmonyPostfix]
        internal static void Postfix_Init(CampaignPolitics_ElementUI __instance)
        {
            var rt = __instance.RelationsRoot.GetComponent<RectTransform>();
            MelonCoroutines.Start(FixAnchor(rt));
        }

        internal static System.Collections.IEnumerator FixAnchor(RectTransform rt)
        {
            // For some reason we have to wait 2 frames.
            // Presumably the anchor is getting reset after
            // Init, somewhere.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (rt == null)
                yield break;
            rt.anchorMin = new Vector2(-0.03f, 1f);
        }
    }
}
