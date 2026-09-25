using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using C11_TN4_Client.amp_arms;
using UnityEngine;

namespace C11_TN4_Client.Patches
{
    internal class AmpArmsEquipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Found by signature instead of by name: the obfuscated method_N numbers
            // can shift between game builds, but "takes a PlayerBody and a GameObject"
            // is what makes this the model-setup method.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var all = typeof(PlayerBody.SlotView).GetMethods(flags);
            var matches = all.Where(m =>
            {
                var p = m.GetParameters();
                return p.Length == 2
                    && p[0].ParameterType == typeof(PlayerBody)
                    && p[1].ParameterType == typeof(GameObject);
            }).ToList();

            if (matches.Count == 1)
            {
                C11Plugin.DebugLog($"[AmpArmsEquipPatch] Target: {matches[0].Name}");
                return matches[0];
            }

            // Zero or several matches: dump every method so the right one can be picked by name.
            C11Plugin.Log.LogWarning($"[AmpArmsEquipPatch] Found {matches.Count} (PlayerBody, GameObject) methods on SlotView, expected 1. All methods:");
            foreach (var m in all)
                C11Plugin.Log.LogWarning($"    {m}");
            return null;
        }

        [PatchPostfix]
        // __1 = the method's second argument (the GameObject). Binding by position means a
        // renamed parameter in a future build can't break the patch.
        private static void PatchPostfix(PlayerBody.SlotView __instance, GameObject __1)
        {
            var model = __1;
            if (__instance.EquipmentSlot != EquipmentSlot.Headwear) return;

            var containedItem = __instance.ContainedItem?.Value;
            if (containedItem == null) return;
            
            string helmetTemplateId = containedItem.TemplateId;
            if (!C11Plugin.HelmetConfigs.TryGetValue(helmetTemplateId, out var cfg)) return;

            var helmet = containedItem as CompoundItem;
            if (helmet == null) return;

            // Ensure we don't attach duplicate watchers
            var manager = model.GetComponent<HelmetMountManager>();
            if (manager == null) 
            {
                manager = model.AddComponent<HelmetMountManager>();
            }
            
            manager.Init(helmet, cfg);
            C11Plugin.DebugLog($"[AmpArmsEquipPatch] Attached dynamic Mount Manager to {helmetTemplateId}");
        }
    }
}