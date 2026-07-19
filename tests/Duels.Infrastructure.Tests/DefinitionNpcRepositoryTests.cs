using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionNpcRepositoryTests
{
    // Loads the real embedded npcs.json against the real items.json — proves
    // the pipeline end to end and spot-checks fidelity against the content
    // it replaced (InMemoryNpcRepository).
    [Fact]
    public void LoadsRealNpcsJson_WithExpectedFidelity()
    {
        var items = new DefinitionItemRepository();
        var repo = new DefinitionNpcRepository(items);

        Assert.Equal(12, repo.GetAll().Count);

        var maggotKing = repo.GetTemplate("maggot_king");
        Assert.NotNull(maggotKing);
        Assert.Equal(150, maggotKing!.Stats.Hitpoints);
        Assert.Equal(new[] { AttackType.Crush, AttackType.Ranged, AttackType.Magic }, maggotKing.StyleRotation);
        Assert.Equal(3, maggotKing.AttacksPerStyle);
        Assert.NotNull(maggotKing.Hazards);
        Assert.Equal(22, maggotKing.Hazards!.EruptDamage);
        Assert.Equal(4, maggotKing.Hazards.PoolDamage);
        Assert.Equal(8, maggotKing.Hazards.PoolTicks);

        var champion = repo.GetTemplate("champion");
        Assert.NotNull(champion);
        Assert.Equal(3, champion!.AttacksPerStyle);
        Assert.Null(repo.GetTemplate("goblin")!.Hazards);
    }

    [Fact]
    public void ThrowsOnDuplicateNpcId()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("dupe", "Dupe A", "", CombatStats.Default, ItemModifiers.Zero, AttackType.Crush, []),
            new("dupe", "Dupe B", "", CombatStats.Default, ItemModifiers.Zero, AttackType.Crush, []),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionNpcRepository(templates, items));
        Assert.Contains("dupe", ex.Message);
    }

    [Fact]
    public void ThrowsWhenLootTableReferencesUnknownItem()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("ghost_npc", "Ghost", "", CombatStats.Default, ItemModifiers.Zero, AttackType.Crush,
                [new LootEntry("nonexistent_item", 1.0)]),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionNpcRepository(templates, items));
        Assert.Contains("nonexistent_item", ex.Message);
    }

    [Fact]
    public void AllowsGoldAsALootEntryWithoutValidation()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("gold_only", "Gold Only", "", CombatStats.Default, ItemModifiers.Zero, AttackType.Crush,
                [new LootEntry("gold", 1.0, MinQty: 10, MaxQty: 20)]),
        };

        var repo = new DefinitionNpcRepository(templates, items);
        Assert.Single(repo.GetAll());
    }
}
