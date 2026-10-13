// ReSharper disable InconsistentNaming
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using C11_TN4_Client.amp_arms;
using EFT;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using UnityEngine;

namespace C11_TN4_Client.Patches
{
    /// <summary>
    /// Gives inspect / weapon-modding preview models the same AMP-arm tuning as the helmet
    /// on your head. Those windows build their model through ObjectsFactory.CreateCleanLootPrefab
    /// (seen in the stack trace from the PPSh preview crash), so this attaches a
    /// HelmetMountManager to whatever that returns when it's one of the registered helmets.
    /// Both managers read the same F12 settings, so a change in one shows in the other.
    ///
    /// Not enabled at startup: C11Plugin.SetAmpPreviewEnabled applies and removes it from the
    /// advanced "Tune Preview Models" switch.
    /// </summary>
    internal class AmpArmsPreviewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // The synchronous version; the async one (used for icon rendering) returns a Task.
            var method = typeof(ObjectsFactory)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "CreateCleanLootPrefab"
                                     && !typeof(Task).IsAssignableFrom(m.ReturnType)
                                     && m.GetParameters().Length > 0
                                     && m.GetParameters()[0].ParameterType == typeof(Item));

            if (method == null)
                C11Plugin.Log.LogWarning("[AmpArmsPreviewPatch] ObjectsFactory.CreateCleanLootPrefab(Item, ...) not found - " +
                                         "AMP arms won't be tuned in preview windows.");
            return method;
        }

        [PatchPostfix]
        private static void PatchPostfix(object __result, Item __0)
        {
            // Backstop for the master switch: the patch is normally removed when the
            // switch is off, but if unpatching ever fails this keeps it inert.
            if (!C11Plugin.AmpPreviewTuning.Value) return;

            try
            {
                var helmet = __0 as CompoundItem;
                if (helmet == null) return;
                if (!C11Plugin.HelmetConfigs.TryGetValue(helmet.TemplateId.ToString(), out var cfg)) return;

                GameObject model = __result as GameObject ?? (__result as Component)?.gameObject;
                if (model == null) return;

                var manager = model.GetComponent<HelmetMountManager>() ?? model.AddComponent<HelmetMountManager>();
                manager.IsPreview = true;
                manager.Init(helmet, cfg);
                C11Plugin.DebugLog($"[AmpArmsPreviewPatch] Preview mount manager on {helmet.TemplateId}");
            }
            catch (System.Exception e)
            {
                // Never let a preview model's setup fail because of us
                C11Plugin.Log.LogError($"[AmpArmsPreviewPatch] {e}");
            }
        }
    }
}