using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Application.Services;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Persistence;
using System.Reflection;
using Xunit;

namespace Duels.Infrastructure.Tests;

// Choreography tests for the M1 boss (m1-plan Workstream D): rotation
// timeline, eruption cadence, pool->scorch conversion, Rot Burst safe-tile
// negation + punish window, swarm spawn thresholds + contact bleed, and
// Perfect Dodge. Runs against the REAL embedded npcs.json/items.json content,
// not a synthetic stand-in, so it breaks if the shipped choreography drifts.
public sealed class MaggotKingTests
{
    private sealed class AlwaysHitRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0;
    }

    private sealed class StubEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default) where TEvent : DomainEvent => Task.CompletedTask;
        public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> h) where TEvent : DomainEvent { }
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTickSource : ITickSource
    {
        public long ElapsedMsIntoCurrentTick => 0;
        public void Reset() { }
        public Task WaitForNextTickAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (GameTickService svc, GameState state, NpcInstance npc) Build()
    {
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var template = npcs.GetTemplate("maggot_king")!;

        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var npc = new NpcInstance(template);
        state.StartDuel(npc);
        state.HoldPositionAtSpawn(); // stand still unless a test moves the player

        var damage = new DamageModel(new AlwaysHitRandom());
        var svc = new GameTickService(
            new InMemoryStateRepo(state), damage, new AlwaysHitRandom(),
            items, new StubEventBus(), new StubTickSource());
        return (svc, state, npc);
    }

    private static async Task Tick(GameTickService svc)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, ["p1"])!;
    }

    [Fact]
    public async Task Phase1_FiresBileSpitAtRotationTick0()
    {
        var (svc, state, _) = Build();
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // RotationTick 0 resolves before advancing

        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.NpcHit && e.Message.Contains("Bile Spit"));
        Assert.Equal(hpBefore - 18, state.Player.CurrentHp); // Medium band, no prayer/gear mitigation
    }

    [Fact]
    public async Task Phase1_StyleTelegraphAtTick8_SetsForecast()
    {
        var (svc, state, npc) = Build();

        for (int i = 0; i < 9; i++) await Tick(svc); // ticks resolve T0..T8 inclusive

        Assert.NotNull(npc.ForecastAttackId);
        Assert.Equal(2, npc.ForecastTicksLeft);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.BossSpecial && e.Message.Contains("mandibles glow"));
    }

    [Fact]
    public async Task Eruption_FiresOnCooldown_MarkingPlayerTilePlusExtras()
    {
        var (svc, state, npc) = Build();
        npc.ResetEruptionCooldown(1);

        await Tick(svc);

        Assert.Equal(3, state.Hazards.Count); // Phase 1 TilesPerWave = 3 (player tile + 2 random)
        Assert.Contains(state.Hazards, h => (h.X, h.Z) == state.PlayerTile);
        Assert.All(state.Hazards, h => Assert.Equal(HazardState.Warning, h.State));
    }

    [Fact]
    public async Task Eruption_DealsUnprayableDamage_AndPoisonsOnLandedTile()
    {
        var (svc, state, _) = Build();
        state.Player.ToggleProtection(ProtectionPrayer.Magic); // must not matter — unprayable
        state.AddHazardWave([state.PlayerTile], warningTicks: 1, poolTicks: 20);

        await Tick(svc); // this tick is also RotationTick 0 — Bile Spit fires concurrently

        // Assert on the specific hazard hitsplat rather than total HP delta:
        // the eruption shares this tick with the boss's own scripted T0
        // Bile Spit (reduced by the Magic prayer this test raised), so the
        // net HP change isn't just the eruption's 35.
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.HitsplatNpc && e.Message == "35:hazard");
        Assert.True(state.PlayerPoisoned);
    }

    [Fact]
    public void HazardTile_Lifecycle_WarningToPoolToScorch()
    {
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(npcs.GetTemplate("maggot_king")!));

        state.AddHazardWave([(2, 2)], warningTicks: 2, poolTicks: 2);
        Assert.Empty(state.TickHazards());          // fuse 2 -> 1
        var erupted = state.TickHazards();           // fuse 1 -> 0: erupts
        Assert.Contains((2, 2), erupted);
        Assert.True(state.IsPool((2, 2)));

        state.TickHazards();                          // pool 2 -> 1
        Assert.True(state.IsPool((2, 2)));
        state.TickHazards();                          // pool 1 -> 0: dries to scorch
        Assert.True(state.IsScorch((2, 2)));
        Assert.False(state.IsPool((2, 2)));
    }

    [Fact]
    public async Task PerfectDodge_GrantsSpecialEnergy_WhenVacatingFinalFuseTileInTime()
    {
        var (svc, state, _) = Build();
        state.Player.DrainSpecialEnergy(100);
        var standingTile = state.PlayerTile;
        state.AddHazardWave([standingTile], warningTicks: 1, poolTicks: 5);
        state.OrderMove(standingTile.X + 3, standingTile.Z); // moves 2 tiles this tick — clears the tile

        await Tick(svc);

        // Perfect Dodge's +15 plus the unconditional +1/tick base regen that
        // also fires this same tick.
        Assert.Equal(16, state.Player.SpecialEnergy);
        Assert.NotEqual(standingTile, state.PlayerTile);
    }

    [Fact]
    public async Task Phase2_TriggersAtHalfHp_AndCompressesLoop()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2);

        await Tick(svc); // detects the threshold, flips phase, resets the cursor

        Assert.Equal(2, npc.Phase);
        Assert.Equal(0, npc.RotationTick);
        Assert.Equal(14, npc.ActivePhaseDef.LoopLength);
    }

    [Fact]
    public async Task Swarms_SpawnOncePhase2Begins_AndContactAppliesBleed()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2); // exactly the 50% swarm threshold

        await Tick(svc);

        Assert.Equal(2, state.Adds.Count);
        Assert.All(state.Adds, a => Assert.Equal(2, a.MaxHp));

        for (int i = 0; i < 20 && state.BleedTicksLeft == 0; i++) await Tick(svc);
        Assert.True(state.BleedTicksLeft > 0);
    }

    [Fact]
    public async Task RotBurst_DealsSevereUnprayableDamage_AndOpensPunishWindow()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2);
        await Tick(svc); // enter Phase 2 (Rot Burst only exists there)
        Assert.Equal(2, npc.Phase);

        npc.StartRotBurstInhale(1); // resolves on the very next tick
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc);

        Assert.False(npc.RotBurstInhaling);
        Assert.Equal(hpBefore - 55, state.Player.CurrentHp); // Severe band, unprayable
        Assert.True(npc.InPunishWindow);
    }

    [Fact]
    public async Task RotBurst_NegatedForPlayerStandingOnScorch()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2);
        await Tick(svc); // also crosses the 50% swarm threshold

        // Swarm adds are incidental to this test (which is only about Rot
        // Burst vs. scorch shelter) but crawl toward the player every tick
        // and can land a contact bleed by coincidence of timing. Neutralize
        // them so their movement can't add noise to the HP assertion below.
        foreach (var add in state.Adds) add.TakeDamage(add.MaxHp);
        state.RemoveDeadAdds();

        state.AddHazardWave([state.PlayerTile], warningTicks: 1, poolTicks: 1);
        await Tick(svc); // warning -> pool
        await Tick(svc); // pool -> scorch
        Assert.True(state.IsScorch(state.PlayerTile));

        npc.StartRotBurstInhale(1);
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc);

        Assert.Equal(hpBefore, state.Player.CurrentHp); // sheltered
        Assert.True(npc.InPunishWindow); // the boss still slumps regardless
    }

    [Fact]
    public async Task PunishWindow_BossCannotAct_TakesExtraDamageFromPlayer()
    {
        var (svc, state, npc) = Build();
        npc.StartSlump(3);
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // boss skips its rotation entirely while slumped

        Assert.Equal(hpBefore, state.Player.CurrentHp); // no boss attack landed
        Assert.True(npc.InPunishWindow);
    }
}
