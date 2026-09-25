using System.Reflection;
using BSG.CameraEffects;
using C11_TN4_Client.Core;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace C11_TN4_Client.patches
{
    internal class NightVisionMaskApply : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(NightVision), nameof(NightVision.ApplySettings));
        }[PatchPrefix]
        private static bool PatchPrefix(NightVision __instance)
        {
            if (__instance.TextureMask == null) return true;

            //  If a custom half-mask active, we temporarily overwrite the 
            // base NightVision.Mask variable right before the game reads it.
            if (NvgPodRotator.ActiveMask != null)
            {
                if (NvgPodRotator.ManagedInstance == null || __instance == NvgPodRotator.ManagedInstance)
                {
                    __instance.Mask = NvgPodRotator.ActiveMask;
                    
                    // Optional: uncomment this if you want to verify it in the logs
                    C11Plugin.DebugLog($"[C11-TN4-Client] ApplySettings Prefix — injecting mask '{__instance.Mask.name}' natively.");
                }
            }

            //  Return true to let the original ApplySettings run

            return true; 
        }
    }
}