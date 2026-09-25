using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using c11_tn_4.Utilities;
using Path = System.IO.Path;

namespace c11_tn_4.Traders;

// Preload + 1: 4.1 wants new database content (items, traders) added at Preload,
// so profiles referencing Trudy never load before she exists.
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class Trudy(
    ModHelper modHelper,
    ImageRouter imageRouter,
    TraderConfig traderConfig,   // 4.1: configs are injected directly, ConfigServer is gone
    RagfairConfig ragfairConfig,
    TimeUtil timeUtil,
    C11TraderHelper traderHelper
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A path to the mods files we use below
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        // A relative path to the trader icon to show
        var traderImagePath = Path.Combine(pathToMod, "db/trudy/trudy.png");

        // The base JSON containing trader settings we will add to the server
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "db/trudy/base.json");

        // Register the trader's image/icon + set its stock refresh time
        imageRouter.AddRoute(traderBase.Avatar!.Replace(".png", ""), traderImagePath);
        traderHelper.SetTraderUpdateTime(traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));

        // Add our trader to the config file, this lets it be seen by the flea market
        ragfairConfig.Traders.TryAdd(traderBase.Id, true);

        // Add our trader (with no items yet) to the server database
        // An 'assort' is the term used to describe the offers a trader sells, it has 3 parts to an assort
        // 1: The item
        // 2: The barter scheme, cost of the item (money or barter)
        // 3: The Loyalty level, what rep level is required to buy the item from trader
        traderHelper.AddTraderWithEmptyAssortToDb(traderBase);

        // Add localization text for our trader to the database so it shows to people playing in different languages
        traderHelper.AddTraderToLocales(traderBase, "Trudy", "An obsessively polite westerner with suspiciously pristine hair, poorly disguised behind a pair of thick-rimmed glasses and a clearly synthetic mustache. His business in the Norvinsk region involves liquidating massive shipments of freshly prohibited civilian firearms");

        cancellationToken.ThrowIfCancellationRequested();

        // Get the assort data from JSON and save it into the trader we've made
        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "db/trudy/assort.json");
        traderHelper.OverwriteTraderAssort(traderBase.Id, assort);

        return Task.CompletedTask;
    }
}
