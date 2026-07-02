using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Xunit;

namespace Duels.Application.Tests;

public sealed class CollectionLogTests
{
    private static GameState Build() => new("p1", new Player("p1", "Hero"));

    [Fact]
    public void RecordLoot_DoesNotDuplicate()
    {
        var state = Build();
        state.RecordLoot("warlords_bulwark");
        state.RecordLoot("warlords_bulwark");
        Assert.Single(state.CollectionLog);
    }

    [Fact]
    public void RecordDefeat_DoesNotDuplicate()
    {
        var state = Build();
        state.RecordDefeat("champion");
        state.RecordDefeat("champion");
        Assert.Single(state.DefeatedNpcs);
    }

    [Fact]
    public void Reset_KeepsCollectionLogAndDefeatedNpcs()
    {
        var state = Build();
        state.RecordLoot("champions_cape");
        state.RecordDefeat("champion");

        state.Reset();

        Assert.Contains("champions_cape", state.CollectionLog);
        Assert.Contains("champion", state.DefeatedNpcs);
    }

    [Fact]
    public void RestoreFromSave_RoundTripsCollectionLog()
    {
        var state = Build();
        state.RestoreFromSave(3, 12, ["swashbuckler"], ["lucky_doubloon", "berserker_ring"], ["swashbuckler", "barbarian"]);

        Assert.Equal(2, state.CollectionLog.Count);
        Assert.Contains("lucky_doubloon", state.CollectionLog);
        Assert.Equal(2, state.DefeatedNpcs.Count);
        Assert.Contains("barbarian", state.DefeatedNpcs);
    }

    [Fact]
    public void RestoreFromSave_NullCollections_DefaultToEmpty()
    {
        var state = Build();
        state.RestoreFromSave(0, 0, []);
        Assert.Empty(state.CollectionLog);
        Assert.Empty(state.DefeatedNpcs);
    }

    [Fact]
    public void StartDuel_ResetsDamageTakenCounter()
    {
        var state = Build();
        var npc = new NpcTemplate("goblin", "Goblin", "", new Duels.Domain.ValueObjects.CombatStats(1, 1, 1, 10),
            Duels.Domain.ValueObjects.ItemModifiers.Zero, Duels.Domain.ValueObjects.AttackType.Crush, []);
        state.StartDuel(new NpcInstance(npc));
        state.RecordDamageTaken(15);
        Assert.Equal(15, state.DamageTakenThisDuel);

        state.StartDuel(new NpcInstance(npc));
        Assert.Equal(0, state.DamageTakenThisDuel);
    }
}
