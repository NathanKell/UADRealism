using MelonLoader;
using System.Runtime.InteropServices;
using System.Reflection;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(TweaksAndFixes.TweaksAndFixes), "TweaksAndFixes", "1.0.0", "NathanKell")]
namespace TweaksAndFixes
{
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
