using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace c11_tn_4.Utilities
{
    /// <summary>
    /// Helper for adding Trudy into the server
    /// </summary>
    // Plain [Injectable]: this isn't an IOnLoad, so a TypePriority does nothing here.
    [Injectable]
    public class C11TraderHelper(
        ISptLogger<C11TraderHelper> logger,   // 4.1: ISptLogger moved to SPTarkov.Common.Models.Logging
        ICloner cloner,
        TradersTable tradersTable,            // 4.1: replaces databaseService.GetTables().Traders
        LocaleTable localeTable)              // 4.1: replaces databaseService.GetTables().Locales
    {
        /// <summary>
        /// Add the traders update time for when their offers refresh
        /// </summary>
        public void SetTraderUpdateTime(TraderConfig traderConfig, TraderBase baseJson, int refreshTimeSecondsMin, int refreshTimeSecondsMax)
        {
            var traderRefreshRecord = new UpdateTime
            {
                TraderId = baseJson.Id,
                Seconds = new MinMax<int>(refreshTimeSecondsMin, refreshTimeSecondsMax)
            };

            traderConfig.UpdateTime.Add(traderRefreshRecord);
        }

        /// <summary>
        /// Add a traders base data to the server, no assort items
        /// </summary>
        public void AddTraderWithEmptyAssortToDb(TraderBase traderDetailsToAdd)
        {
            // Create an empty assort ready for our items
            var emptyTraderItemAssortObject = new TraderAssort
            {
                Items = [],
                BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
                LoyalLevelItems = new Dictionary<MongoId, int>()
            };

            // Create trader data ready to add to database
            var traderDataToAdd = new Trader
            {
                Assort = emptyTraderItemAssortObject,
                Base = cloner.Clone(traderDetailsToAdd)!,
                QuestAssort = new() // one empty set per quest status
                {
                    { "Started", new() },
                    { "Success", new() },
                    { "Fail", new() }
                },
                Dialogue = []
            };

            // TradersTable is a Dictionary<MongoId, Trader> in 4.1
            if (!tradersTable.TryAdd(traderDetailsToAdd.Id, traderDataToAdd))
            {
                logger.Warning($"Trader {traderDetailsToAdd.Id} already exists in the database, not added");
            }
        }

        /// <summary>
        /// Add traders name/location/description to all locales (e.g. German/French/English)
        /// </summary>
        public void AddTraderToLocales(TraderBase baseJson, string firstName, string description)
        {
            var newTraderId = baseJson.Id;
            var fullName = baseJson.Name;
            var nickName = baseJson.Nickname;
            var location = baseJson.Location;

            foreach (var (_, lazyLocale) in localeTable.Global)
            {
                // Locales are lazy loaded, so a transformer re-applies our entries every time they load.
                // Indexer instead of .Add so a reload can't throw on duplicate keys (same pattern SPT uses).
                lazyLocale.AddTransformer(lazyloadedLocaleData =>
                {
                    lazyloadedLocaleData![$"{newTraderId} FullName"] = fullName!;
                    lazyloadedLocaleData[$"{newTraderId} FirstName"] = firstName;
                    lazyloadedLocaleData[$"{newTraderId} Nickname"] = nickName!;
                    lazyloadedLocaleData[$"{newTraderId} Location"] = location!;
                    lazyloadedLocaleData[$"{newTraderId} Description"] = description;
                    return lazyloadedLocaleData;
                });
            }
        }

        /// <summary>
        /// Overwrite the desired traders assorts with the ones provided
        /// </summary>
        public void OverwriteTraderAssort(MongoId traderId, TraderAssort newAssorts)
        {
            var traderToEdit = tradersTable.GetTrader(traderId);
            if (traderToEdit is null)
            {
                logger.Warning($"Unable to update assorts for trader: {traderId}, they couldn't be found on the server");
                return;
            }

            traderToEdit.Assort = newAssorts;
        }
    }
}
