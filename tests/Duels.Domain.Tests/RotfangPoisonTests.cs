using Duels.Domain.Entities;
using Xunit;

namespace Duels.Domain.Tests;

// Items doc §3, backlog resolution batch 1: Rotfang on-hit poison.
public sealed class RotfangPoisonTests
{
    [Fact]
    public void ApplyRotfangPoison_AddsStackAndSetsFiveTickDuration()
    {
        var npc = MakeMaggotKing();
        npc.ApplyRotfangPoison();
        Assert.Equal(1, npc.PoisonStacks);
        Assert.Equal(2, npc.PoisonDamagePerTick);
    }

    [Fact]
    public void ApplyRotfangPoison_CapsAtThreeStacks()
    {
        var npc = MakeMaggotKing();
        for (int i = 0; i < 5; i++) npc.ApplyRotfangPoison();
        Assert.Equal(3, npc.PoisonStacks);
        Assert.Equal(6, npc.PoisonDamagePerTick);
    }

    [Fact]
    public void Reapplication_RefreshesDuration_DoesNotResetStacks()
    {
        var npc = MakeMaggotKing();
        npc.ApplyRotfangPoison();
        npc.ApplyRotfangPoison(); // 2 stacks, duration refreshed to 5
        for (int i = 0; i < 4; i++) npc.TickPoison(); // duration 5 -> 1
        Assert.Equal(2, npc.PoisonStacks); // still alive, not expired yet
        npc.ApplyRotfangPoison(); // refresh again before expiry
        Assert.Equal(3, npc.PoisonStacks);
    }

    [Fact]
    public void TickPoison_DealsStackDamageForFiveTicks_ThenStops()
    {
        var npc = MakeMaggotKing();
        npc.ApplyRotfangPoison(); // 1 stack, 5 ticks
        int hits = 0;
        for (int i = 0; i < 5; i++) if (npc.TickPoison()) hits++;
        Assert.Equal(5, hits);
        Assert.False(npc.TickPoison()); // expired
        Assert.Equal(0, npc.PoisonStacks);
    }

    private static NpcInstance MakeMaggotKing()
    {
        var template = new NpcTemplate("maggot_king", "The Maggot King", "",
            new Duels.Domain.ValueObjects.CombatStats(1, 1, 1, 450), []);
        return new NpcInstance(template);
    }
}
