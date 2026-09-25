using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using WTTServerCommonLib.Models;

namespace c11_tn_4;

[Injectable(TypePriority = OnLoadOrder.Preload + 2), UsedImplicitly]
public class TrueNorth(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    ILogger<TrueNorth> log
) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            log.LogDebug("[True North] Embedded resource: {Res}", name);
        }

        TraderIds.Add("trudy", "699f89c757994beece5cf7e1");

        // WTT ingestion
        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
        await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly);
        await wttCommon.CustomBotLoadoutService.CreateCustomBotLoadouts(assembly);
        await wttCommon.CustomWeaponPresetService.CreateCustomWeaponPresets(assembly);
        wttCommon.CustomRigLayoutService.CreateRigLayouts(assembly);
        wttCommon.CustomSlotImageService.CreateSlotImages(assembly);
        await wttCommon.CustomQuestService.CreateCustomQuests(assembly);
        await wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly);
        await wttCommon.CustomClothingService.CreateCustomClothing(assembly);

        // DEBUG: list what the server actually registered
        var layouts = wttCommon.CustomRigLayoutService.GetLayoutManifest();
        log.LogDebug("[True North] Rig layouts registered: {Layouts}", string.Join(", ", layouts));

        log.LogInformation("Welcome to the True North");
    }
}
