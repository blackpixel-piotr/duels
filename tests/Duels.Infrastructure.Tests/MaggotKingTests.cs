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

    // Always hits, but rolls the FLOOR of any range (Next => min): a boss auto's
    // 60–100% band roll lands at 60%, the opposite extreme from AlwaysHitRandom's
    // full band — used to bracket the band's lower bound.
    private sealed class MinRollRandom : IRandomProvider
    {
        public int Next(int min, int max) => min;
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

    private static (GameTickService svc, GameState state, NpcInstance npc) Build(IRandomProvider? rng = null)
    {
        rng ??= new AlwaysHitRandom();
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var template = npcs.GetTemplate("maggot_king")!;

        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var npc = new NpcInstance(template);
        state.StartDuel(npc);
        state.HoldPositionAtSpawn(); // stand still unless a test moves the player

        var damage = new DamageModel(rng);
        var svc = new GameTickService(
            new InMemoryStateRepo(state), damage, rng,
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

        await Tick(svc); // RotationTick 0 CASTS Bile Spit — a 2-tick magic projectile, not instant damage

        Assert.Equal(hpBefore, state.Player.CurrentHp); // still in flight, no damage yet
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.BossCast && e.Message == "magic:2");

        await Tick(svc); // in flight
        await Tick(svc); // impact — exactly 2 ticks after cast

        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.NpcHit && e.Message.Contains("Bile Spit"));
        Assert.Equal(hpBefore - 18, state.Player.CurrentHp); // Medium band, no prayer/gear mitigation
        // Unprayed hit: "normal" tier, not "blocked" — a real hitsplat.
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.HitsplatNpc && e.Message == "18:normal:magic");
    }

    [Fact]
    public async Task Phase1_BossAuto_RollsBottomOfBand_WhenRngRollsLow()
    {
        // Standard autos roll 60–100% of their listed band (items doc §1). The
        // full-band ceiling is covered by every other test (AlwaysHitRandom
        // rolls the top); this pins the floor: 60% of Bile Spit's 18 = 11.
        // Mechanics/DoTs never roll — see the fixed-value eruption/Rot Burst
        // tests, which stay exact.
        var (svc, state, _) = Build(new MinRollRandom());
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // T0 casts Bile Spit — a 2-tick magic projectile
        await Tick(svc); // in flight
        await Tick(svc); // impact

        Assert.Equal(hpBefore - 11, state.Player.CurrentHp);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.HitsplatNpc && e.Message == "11:normal:magic");
    }

    [Fact]
    public async Task Phase1_BileSpit_FullyNegatedByMatchingPrayer()
    {
        var (svc, state, _) = Build();
        state.Player.ToggleProtection(ProtectionPrayer.Magic);
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // T0 cast — no damage yet
        await Tick(svc); // in flight
        await Tick(svc); // impact — Magic prayer still up: 100% block, not a mitigation

        Assert.Equal(hpBefore, state.Player.CurrentHp);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.NpcHit && e.Message.Contains("Bile Spit") && e.Message.Contains("(prayed)"));
        // A prayer-negated hit gets its own "blocked" tier (renders as a
        // doctrine-colored slashed ring, not a "0" numeral) — distinct from
        // a real 0-damage hit or a miss.
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.HitsplatNpc && e.Message == "0:blocked:magic");
    }

    [Fact]
    public async Task ImpactResolutionPrayer_RaisedAfterCastButBeforeImpact_StillBlocksDamage()
    {
        // Global Combat Grammar: "Protection prayers are evaluated on the
        // impact tick, never the cast tick." Prove it by NOT praying at
        // cast time and only raising the matching prayer partway through
        // the projectile's flight — it should still fully block on impact.
        var (svc, state, _) = Build();

        await Tick(svc); // T0 Bile Spit casts with no prayer up at all

        state.Player.ToggleProtection(ProtectionPrayer.Magic); // raised mid-flight
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // still in flight
        await Tick(svc); // impact — prayer active NOW is what matters

        Assert.Equal(hpBefore, state.Player.CurrentHp);
    }

    [Fact]
    public async Task ProtectionPrayerDrain_FiresOnceEveryNineTicks_NotEveryTick()
    {
        // Playtest revision, twice now: drain rate cut to a ninth of the
        // original (2 points per protection-drain event, unchanged
        // throughout) by only firing every 9th tick instead of every tick
        // (first cut was every 3rd; this doubles down on the same feedback).
        var (svc, state, _) = Build();
        state.Player.ToggleProtection(ProtectionPrayer.Magic);
        int startPoints = state.Player.PrayerPoints;

        for (int i = 0; i < 8; i++)
        {
            await Tick(svc); // ticks 1..8 of the cadence — no drain yet
            Assert.Equal(startPoints, state.Player.PrayerPoints);
        }

        await Tick(svc); // tick 9 — drains now
        Assert.Equal(startPoints - 2, state.Player.PrayerPoints);
    }

    [Fact]
    public async Task Phase1_StyleTelegraphAtTick7_SetsForecast()
    {
        // Tier-1 baseline (Boss Bible Global Combat Grammar): style-shift
        // telegraphs warn 3 ticks ahead, not 2 — the telegraph tick moved
        // from T8 to T7 so it still lands exactly 3 ticks before the T10
        // Lash/Grub Volley it announces (T7 + 3 = T10).
        var (svc, state, npc) = Build();

        for (int i = 0; i < 8; i++) await Tick(svc); // ticks resolve T0..T7 inclusive

        Assert.NotNull(npc.ForecastAttackId);
        Assert.Equal(3, npc.ForecastTicksLeft);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.BossSpecial && e.Message.Contains("mandibles glow"));
    }

    [Fact]
    public async Task Phase1_SecondStyleTelegraph_GivesExactlyThreeTicksBeforeTheWrappedBileSpit()
    {
        // Regression test for the bug this pass was asked to verify/fix:
        // the SECOND telegraph used to fire at T16, promising "N ticks
        // warning" but the wrapped Bile Spit (rotation restarts at T0)
        // didn't actually land until 4 ticks later (T16 -> T18/T19 idle ->
        // T0), because the two "free damage window" idle ticks sat between
        // the telegraph and the wrap with nothing accounting for them. It
        // now fires at T17, so T17 + 3 = T20 (== T0 of the next loop) lines
        // up exactly, idle ticks included.
        var (svc, state, npc) = Build();

        for (int i = 0; i < 18; i++) await Tick(svc); // ticks resolve T0..T17 inclusive

        Assert.Equal("bile_spit", npc.ForecastAttackId);
        Assert.Equal(3, npc.ForecastTicksLeft);
    }

    [Fact]
    public async Task MechanicToggle_DisablingEruptions_SuppressesTheWave()
    {
        // Dev per-mechanic kill switch: with Eruptions off, a wave that is due
        // this tick does not spawn (contrast Eruption_FiresOnCooldown, same
        // setup minus the toggle, which spawns 3).
        var (svc, state, npc) = Build();
        state.ToggleMechanic(BossMechanic.Eruptions);
        npc.ResetEruptionCooldown(1); // would be due next tick

        await Tick(svc);

        Assert.False(state.IsMechanicEnabled(BossMechanic.Eruptions));
        Assert.Empty(state.Hazards);
    }

    [Fact]
    public async Task DamageSourceDeathLogging_BleedKill_NamesBleed()
    {
        // Every player-damage source now sets KilledBy, so a death to a DoT
        // reads as the DoT, not a stale boss auto. Bleed the player to death
        // and confirm the summary + the "Slain by" log line both name it.
        var (svc, state, _) = Build();
        state.Player.TakeDamage(state.Player.MaxHp - 3); // 3 HP left
        state.ApplyBleed(1, 5);                           // one lethal 5-dmg bleed tick

        await Tick(svc);

        // (HandleDefeat restores HP for the next fight, so assert on the duel
        // summary + log line, not the now-revived player.)
        Assert.NotNull(state.LastDuelSummary);
        Assert.False(state.LastDuelSummary!.Won);
        Assert.Equal("Bleed", state.LastDuelSummary.KilledBy);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.System && e.Message == "Slain by: Bleed");
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
    public async Task Eruption_StaggersByOneTick_WhenItWouldCoincideWithStyleTelegraph()
    {
        // Playtest revision: Eruption's cooldown is independent of the
        // rotation script by design, but nothing used to stop it from
        // landing on the exact same tick as a style-shift telegraph —
        // forcing a prayer flick and a tile relocation in the same single
        // reaction window. Prime it to be due on T7, the telegraph tick.
        var (svc, state, npc) = Build();
        for (int i = 0; i < 7; i++) await Tick(svc); // ticks 0..6 resolve; T7 is next

        npc.ResetEruptionCooldown(1); // due on the SAME tick as the T7 telegraph, absent the stagger

        await Tick(svc); // T7: telegraph fires; eruption must be nudged, not pile on

        Assert.NotNull(npc.ForecastAttackId); // the telegraph itself still fired on schedule
        Assert.Empty(state.Hazards); // eruption did not

        await Tick(svc); // T8: eruption fires on its own, exactly one tick later

        Assert.Equal(3, state.Hazards.Count);
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
    public async Task Phase2_TriggersAtHalfHp_BecomesMasterScript_WithRoar()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2);

        await Tick(svc); // detects the threshold, flips to the P2 master script

        Assert.Equal(2, npc.Phase);
        Assert.True(npc.UsesMasterScript);
        Assert.Equal(28, npc.ActivePhaseDef.LoopLength);
        Assert.Equal(3, npc.RoarTicksLeft); // 3-tick transition roar
        Assert.Equal(0, npc.RotationTick);
    }

    [Fact]
    public async Task Phase2_TransitionSpawnsFirstSwarmPair_At1Hp_AndContactBleeds()
    {
        var (svc, state, npc) = Build();
        npc.TakeDamage(npc.MaxHp / 2);

        await Tick(svc); // enter P2 — the transition spawns the first pair

        Assert.Equal(2, state.Adds.Count);
        Assert.All(state.Adds, a => Assert.Equal(1, a.MaxHp)); // 1 HP each in P2

        // They crawl in from the corners; contact applies a bleed stack.
        for (int i = 0; i < 30 && state.BleedTicksLeft == 0; i++) await Tick(svc);
        Assert.True(state.BleedTicksLeft > 0);
    }

    // Isolate the Rot Burst mechanic from the other master-script mechanics
    // (they'd add incidental damage across the ticks these tests span).
    private static void SoloRotBurst(GameState state)
    {
        state.ToggleMechanic(BossMechanic.Swarms);
        state.ToggleMechanic(BossMechanic.Eruptions);
        state.ToggleMechanic(BossMechanic.Dots);
        state.ToggleMechanic(BossMechanic.BossAutos);
    }

    [Fact]
    public async Task RotBurst_DealsSevereUnprayableDamage_AndOpensPunishWindow()
    {
        var (svc, state, npc) = Build();
        SoloRotBurst(state);
        npc.TakeDamage(npc.MaxHp / 2);
        await Tick(svc); // enter P2 (Rot Burst only exists there)
        Assert.Equal(2, npc.Phase);
        for (int i = 0; i < 3; i++) await Tick(svc); // tick past the transition roar

        state.ClearPendingAttacks();
        npc.StartRotBurstInhale(1); // resolves on the next master tick
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
        SoloRotBurst(state);
        npc.TakeDamage(npc.MaxHp / 2);
        await Tick(svc); // enter P2
        for (int i = 0; i < 3; i++) await Tick(svc); // past the roar

        // Build a scorch tile under the player (its own permanent scorch).
        state.AddHazardWave([state.PlayerTile], warningTicks: 1, poolTicks: 1);
        await Tick(svc); // warning -> pool
        await Tick(svc); // pool -> scorch
        Assert.True(state.IsScorch(state.PlayerTile));

        state.ClearPendingAttacks();
        npc.StartRotBurstInhale(1);
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc);

        Assert.Equal(hpBefore, state.Player.CurrentHp); // sheltered
        Assert.True(npc.InPunishWindow); // the boss still slumps regardless
    }

    [Fact]
    public async Task RotBurstInhale_BurnsActivePoolsToScorch()
    {
        // The inhale's signature safety guarantee: every active pool instantly
        // dries into scorch, so safe ground exists from the inhale's first tick.
        var (svc, state, npc) = Build();
        SoloRotBurst(state);
        npc.TakeDamage(npc.MaxHp / 2);
        await Tick(svc); // enter P2
        for (int i = 0; i < 3; i++) await Tick(svc); // past the roar

        // Make a pool away from the player.
        var poolTile = (state.PlayerTile.X + 2, state.PlayerTile.Z);
        state.AddHazardWave([poolTile], warningTicks: 1, poolTicks: 20, scorchTicks: 40);
        await Tick(svc); // warning -> pool
        Assert.True(state.IsPool(poolTile));

        // BeginRotBurstInhale performs this the instant the inhale starts.
        state.BurnPoolsToScorch();
        Assert.True(state.IsScorch(poolTile));
        Assert.False(state.IsPool(poolTile));
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
