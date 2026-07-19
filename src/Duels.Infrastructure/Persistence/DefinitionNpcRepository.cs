using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Infrastructure.Definitions;

namespace Duels.Infrastructure.Persistence;

/// <summary>Loads all NPC/boss templates from npcs.json (an embedded definition
/// file mirroring the current roster 1:1) instead of hard-coding them in C#.
/// Cross-validates every loot-table item id against <see cref="IItemRepository"/>
/// so a typo'd drop fails at startup, not silently in a duel.</summary>
public sealed class DefinitionNpcRepository : INpcRepository
{
    private readonly Dictionary<string, NpcTemplate> _npcs;

    public DefinitionNpcRepository(IItemRepository items)
        : this(DefinitionLoader.Load<List<NpcTemplate>>("npcs.json"), items)
    {
    }

    internal DefinitionNpcRepository(List<NpcTemplate> templates, IItemRepository items)
    {
        _npcs = new Dictionary<string, NpcTemplate>();
        foreach (var npc in templates)
        {
            if (!_npcs.TryAdd(npc.Id, npc))
                throw new InvalidOperationException($"npcs.json: duplicate npc id '{npc.Id}'.");
        }

        ValidateLootReferences(items);
    }

    public NpcTemplate? GetTemplate(string npcId) => _npcs.GetValueOrDefault(npcId);
    public IReadOnlyList<NpcTemplate> GetAll() => _npcs.Values.ToList();

    private void ValidateLootReferences(IItemRepository items)
    {
        var missing = new List<string>();
        foreach (var npc in _npcs.Values)
        foreach (var entry in npc.LootTable)
        {
            if (entry.ItemId == "gold") continue;
            if (items.GetItemName(entry.ItemId) is null)
                missing.Add($"{npc.Id} -> {entry.ItemId}");
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"npcs.json: loot table references unknown item id(s): {string.Join(", ", missing)}");
    }
}
