using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace c11_tn_4.Utilities;
// Shoutout EpicRangeTime for letting me use his code, and CJ and Drakia for creating said code

// Preload + 3: after TrueNorth (Preload + 2) has created the Vzor remote
[Injectable(TypePriority = OnLoadOrder.Preload + 3)]
public class C11Blacklister(
    ILogger<C11Blacklister> logger,
    TemplateTable templateTable   // 4.1: replaces DatabaseService
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        EditFilters(cancellationToken);
        return Task.CompletedTask;
    }

    private void EditFilters(CancellationToken cancellationToken)
    {
        var items = templateTable.Items;

        //VZOR4 REMOTE LOGIC

        // DEFINE  IDs
        MongoId vzorRemoteSwitchId = "696000ef0493e34c01446f6a";

        //WHITELIST: Handguards allowed to use this remote
        // Kept as strings: the placeholders aren't valid 24-char ids, and converting
        // them to MongoId would throw at startup.
        var allowedHandguards = new HashSet<string>()
        {
            "ALLOWED_HANDGUARD_ID_1", // <--- REPLACE WITH REAL IDs
            "ALLOWED_HANDGUARD_ID_2",
            "ALLOWED_HANDGUARD_ID_3"
        };

        // CATEGORY: scan all "Foregrips/Handguards"
        var handguardCategory = new List<MongoId>()
        {
            "55818a104bdc2db9688b4569"
        };

        //GENERATE CONFLICTS
        //Find the Remote Switch item, then blacklist every handguard not in the list above.
        if (items.TryGetValue(vzorRemoteSwitchId, out var remoteItem) && remoteItem.Properties != null)
        {
            var remoteConflicts = new HashSet<MongoId>(remoteItem.Properties.ConflictingItems ?? new HashSet<MongoId>());

            foreach (var kvp in items)
            {
                // Walks the whole item database, so this is the loop worth checking in
                cancellationToken.ThrowIfCancellationRequested();

                var item = kvp.Value;

                if (IsChildOfAny(item, handguardCategory, items))
                {
                    if (!allowedHandguards.Contains(item.Id.ToString()))
                    {
                        remoteConflicts.Add(item.Id);
                    }
                }
            }

            remoteItem.Properties.ConflictingItems = remoteConflicts;
            logger.LogDebug($"[C11] Vzor Remote Conflicts Updated. Count: {remoteConflicts.Count}");
        }
        else
        {
            if (vzorRemoteSwitchId != "696000ef0493e34c01446f6a")
            {
                logger.LogError($"[C11] Could not find Vzor Remote Switch with ID: {vzorRemoteSwitchId}");
            }
        }
    }

    // Helpers
    private bool IsChildOfAny(TemplateItem item, List<MongoId> parentIds, Dictionary<MongoId, TemplateItem> allItems)
    {
        foreach (var parentId in parentIds)
            if (IsChildOf(item, parentId, allItems)) return true;
        return false;
    }

    // 4.1: TemplateItem.Parent is a MongoId, not a string
    private bool IsChildOf(TemplateItem item, MongoId targetParentId, Dictionary<MongoId, TemplateItem> allItems)
    {
        if (item.Parent.IsEmpty) return false;
        if (item.Parent == targetParentId) return true;

        if (allItems.TryGetValue(item.Parent, out var parentItem))
            return IsChildOf(parentItem, targetParentId, allItems);
        return false;
    }

    private void ModifySlotFilters(TemplateItem item, int slotIndex, int filterIndex, List<MongoId> ids)
    {
        var slot = GetSlotAtIndex(item, slotIndex);
        var filter = GetSlotFilterAtIndex(slot, filterIndex);
        if (filter.Filter == null) filter.Filter = new HashSet<MongoId>();
        filter.Filter.UnionWith(ids);
    }

    private Slot GetSlotAtIndex(TemplateItem item, int index, bool isCartridge = false)
    {
        var slots = isCartridge ? item.Properties?.Cartridges?.ToArray() : item.Properties?.Slots?.ToArray();

        if (index >= 0 && index < slots?.Length)
        {
            return slots[index];
        }

        throw new IndexOutOfRangeException($"Index on item slot property `{item.Name}` is out of range");
    }

    private SlotFilter GetSlotFilterAtIndex(Slot slot, int index)
    {
        var slotFilter = slot.Properties?.Filters?.ToArray() ?? [];

        if (index >= 0 && index < slotFilter.Length)
        {
            return slotFilter[index];
        }

        throw new IndexOutOfRangeException($"Index on slot property `{slot.Name}` is out of range");
    }
}
