using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

public sealed class GameStateDotTests
{
    private static GameState BuildInDuel()
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var npc = new NpcTemplate("goblin", "Goblin", "", new CombatStats(1, 1, 1, 50), [], DummyStyle: AttackType.Crush);
        state.StartDuel(new NpcInstance(npc));
        return state;
    }

    [Fact]
    public void ApplyBleed_SetsTicksAndAmount()
    {
        var state = BuildInDuel();
        state.ApplyBleed(4, 2);
        Assert.Equal(4, state.BleedTicksLeft);
        Assert.Equal(2, state.BleedPerTick);
    }

    [Fact]
    public void TickBleed_DecrementsUntilZero()
    {
        var state = BuildInDuel();
        state.ApplyBleed(2, 3);
        state.TickBleed();
        Assert.Equal(1, state.BleedTicksLeft);
        state.TickBleed();
        Assert.Equal(0, state.BleedTicksLeft);
        state.TickBleed();
        Assert.Equal(0, state.BleedTicksLeft);
    }

    [Fact]
    public void Poison_TicksEveryFourthCall()
    {
        var state = BuildInDuel();
        state.ApplyPoison();
        Assert.True(state.PlayerPoisoned);
        Assert.False(state.TickPoison());
        Assert.False(state.TickPoison());
        Assert.False(state.TickPoison());
        Assert.True(state.TickPoison()); // 4th call fires
    }

    [Fact]
    public void CurePoison_StopsFutureTicks()
    {
        var state = BuildInDuel();
        state.ApplyPoison();
        state.CurePoison();
        Assert.False(state.PlayerPoisoned);
        for (int i = 0; i < 8; i++)
            Assert.False(state.TickPoison());
    }

    [Fact]
    public void StartDuel_ClearsPreviousDots()
    {
        var state = BuildInDuel();
        state.ApplyBleed(5, 2);
        state.ApplyPoison();

        var npc2 = new NpcTemplate("goblin2", "Goblin2", "", new CombatStats(1, 1, 1, 20), [], DummyStyle: AttackType.Crush);
        state.StartDuel(new NpcInstance(npc2));

        Assert.Equal(0, state.BleedTicksLeft);
        Assert.False(state.PlayerPoisoned);
    }

    [Fact]
    public void EndDuel_ClearsDots()
    {
        var state = BuildInDuel();
        state.ApplyBleed(5, 2);
        state.ApplyPoison();
        state.EndDuel();
        Assert.Equal(0, state.BleedTicksLeft);
        Assert.False(state.PlayerPoisoned);
    }
}
