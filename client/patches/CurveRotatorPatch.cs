using System.Reflection;
using C11_TN4_Client.Core;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace C11_TN4_Client.patches
{
    internal class CurveRotatorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CurveRotator), nameof(CurveRotator.Set));
        }[PatchPostfix]
        private static void PatchPostfix(CurveRotator __instance, bool isOn, bool initial)
        {
            NvgDeviceProfile profile = null;
            
            foreach (var kvp in C11Plugin.Profiles)
            {
                var p = kvp.Value;
                if (__instance.gameObject.name.Contains(p.PrefabNameContains))
                {
                    if (string.IsNullOrEmpty(p.ChildNvgName))
                    {
                        profile = p;
                        break;
                    }
                    else if (NvgPodRotator.FindChildContainingStatic(__instance.transform, p.ChildNvgName) != null)
                    {
                        profile = p;
                        break;
                    }
                }
            }

            if (profile == null) return;

            var rotator = __instance.gameObject.GetComponent<NvgPodRotator>();
            if (rotator == null)
            {
                rotator = __instance.gameObject.AddComponent<NvgPodRotator>();
                rotator.Init(__instance, profile);
            }

            rotator.OnMountStateChanged(isOn, initial);
        }
    }
}