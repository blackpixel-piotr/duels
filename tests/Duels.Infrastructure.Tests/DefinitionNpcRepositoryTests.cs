using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionNpcRepositoryTests
{
    // Loads the real embedded npcs.json against the real items.json — proves
    // the pipeline end to end for M1's single boss-script-driven boss.
    [Fact]
    public void LoadsRealNpcsJson_WithExpectedFidelity()
    {
        var items = new DefinitionItemRepository();
        var repo = new DefinitionNpcRepository(items);

        Assert.Single(repo.GetAll());

        var maggotKing = repo.GetTemplate("maggot_king");
        Assert.NotNull(maggotKing);
        Assert.Equal(450, maggotKing!.Stats.Hitpoints);
        Assert.NotNull(maggotKing.Script);

        var script = maggotKing.Script!;
        Assert.Equal(50, script.PhaseTwoThresholdPercent);
        Assert.True(script.Stationary);
        Assert.Equal(2, script.Footprint.Width);
        Assert.Equal(2, script.Footprint.Height);

        Assert.Equal(20, script.Phase1.LoopLength);
        Assert.Equal(35, script.Phase1.Eruption.EruptDamage);
        Assert.Null(script.Phase1.RotBurst);

        Assert.Equal(14, script.Phase2.LoopLength);
        Assert.NotNull(script.Phase2.RotBurst);
        Assert.Equal(55, script.Phase2.RotBurst!.Damage);
        Assert.NotNull(script.Phase2.Swarms);
        Assert.Equal(2, script.Phase2.Swarms!.Count);

        Assert.True(script.Attacks.ContainsKey("bile_spit"));
        Assert.Equal(AttackType.Magic, script.Attacks["bile_spit"].Style);
    }

    [Fact]
    public void ThrowsOnDuplicateNpcId()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("dupe", "Dupe A", "", CombatStats.Default, []),
            new("dupe", "Dupe B", "", CombatStats.Default, []),
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
            new("ghost_npc", "Ghost", "", CombatStats.Default,
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
            new("gold_only", "Gold Only", "", CombatStats.Default,
                [new LootEntry("gold", 1.0, MinQty: 10, MaxQty: 20)]),
        };

        var repo = new DefinitionNpcRepository(templates, items);
        Assert.Single(repo.GetAll());
    }
}
