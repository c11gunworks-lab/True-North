using System.Reflection;
using JetBrains.Annotations;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace c11_tn_4.Utilities;   

/*
 * Why this exists
 * ---------------
 * The flea only lists a default preset if its item is a weapon, or "equipment"
 * as SPT defines it: Headwear, Vest or Armor (ItemHelper.ArmorItemCanHoldMods).
 * Headset parts are Headphones, and the AXL link is a Mount, so their presets
 * are silently skipped and the bare item is listed instead - which for Amp Arms,
 * the AXL and the 3M adapter means an item with an empty required slot.
 *
 * This postfix adds those presets to the list the flea builds from. The base
 * game has no default presets on Headphones or Mount items, so only modded
 * content is affected. Once a preset is listed, SPT stops listing the bare item.
 */
public class HeadsetPresetFleaPatch : AbstractPatch
{
    internal static PresetHelper? Presets;
    internal static ItemHelper? Items;

    protected override MethodBase? GetTargetMethod()
    {
        return typeof(RagfairAssortGenerator).GetMethod(
            "GetPresetsToAdd", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    [PatchPostfix]
    public static void Postfix(ref List<Preset> __result)
    {
        if (Presets is null || Items is null)
        {
            return;
        }

        var all = Presets.GetAllPresets();
        if (all is null)
        {
            return;
        }

        var listed = new HashSet<MongoId>(__result.Select(p => p.Id));

        foreach (var preset in all)
        {
            // default presets only - the one marked with _encyclopedia
            if (preset.Encyclopedia is not { } tpl || listed.Contains(preset.Id))
            {
                continue;
            }

            if (!Items.IsOfBaseclass(tpl, BaseClasses.HEADPHONES) &&
                !Items.IsOfBaseclass(tpl, BaseClasses.MOUNT))
            {
                continue;
            }

            __result.Add(preset);
            listed.Add(preset.Id);
        }
    }
}


[Injectable(TypePriority = OnLoadOrder.Preload + 5), UsedImplicitly]
public class HeadsetPresetFleaLoader(
    PresetHelper presetHelper,
    ItemHelper itemHelper,
    ISptLogger<HeadsetPresetFleaLoader> logger
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            HeadsetPresetFleaPatch.Presets = presetHelper;
            HeadsetPresetFleaPatch.Items = itemHelper;
            new HeadsetPresetFleaPatch().Enable();

            logger.Debug("[Headsets] Headset presets enabled on the flea market");
            return Task.FromResult(Task.CompletedTask);
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}