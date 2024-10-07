using MelonLoader;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(TweaksAndFixes.TweaksAndFixes), "TweaksAndFixes", "3.14.0", "NathanKell")]
[assembly: MelonColor(255, 220, 220, 0)]
namespace TweaksAndFixes
{
    public class TweaksAndFixes : MelonMod
    {
        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
        }

        public override void OnLateInitializeMelon()
        {
            try
            {
                bool hasUnityExplorer = false;
                foreach (var mod in MelonMod.RegisteredMelons)
                {
                    if (mod.Info.Name == "UnityExplorer")
                    {
                        hasUnityExplorer = true;
                        break;
                    }
                }
                if (!hasUnityExplorer)
                    Application.add_logMessageReceived(new Action<string, string, LogType>(Application_logMessageReceived));
            }
            catch (Exception ex)
            {
                Melon<TweaksAndFixes>.Logger.Warning("Exception setting up Unity log listener, make sure Unity libraries have been unstripped!\n" + ex);
            }
        }

        public override void OnDeinitializeMelon()
        {
            base.OnDeinitializeMelon();
        }

        private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
        {
            string logStr = $"[Unity]: {condition ?? string.Empty}";
            switch (type)
            {
                case LogType.Log: Melon<TweaksAndFixes>.Logger.Msg(logStr); break;
                case LogType.Warning: Melon<TweaksAndFixes>.Logger.Warning(logStr); break;
                case LogType.Error:
                case LogType.Exception:
                    Melon<TweaksAndFixes>.Logger.Error($"[Unity]: {condition}\n{stackTrace}");
                    //Melon<TweaksAndFixes>.Logger.Error(logStr);
                    break;
            }
        }
    }
}
