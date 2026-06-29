using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.Interfaces;

namespace Duels.Application.Services;

public sealed class ItemUnlockService
{
    private readonly IRandomProvider _random;
    private readonly IItemRepository _itemRepo;

    public ItemUnlockService(IRandomProvider random, IItemRepository itemRepo)
    {
        _random = random;
        _itemRepo = itemRepo;
    }

    public IReadOnlyList<(string ItemId, string ItemName)> RollDrops(NpcTemplate npc, Player player)
    {
        var results = new List<(string, string)>();
        foreach (var entry in npc.LootTable)
        {
            if (_random.NextDouble() <= entry.DropChance && !player.HasItem(entry.ItemId))
            {
                var name = _itemRepo.GetItemName(entry.ItemId) ?? entry.ItemId;
                results.Add((entry.ItemId, name));
            }
        }
        return results;
    }
}
