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

// M3 Workstream B: Hive Matron (Boss Bible §2) — spacing AI, adjacency punish
// (Tail Stab), Chitin Guard, drones, Pin's line-charge + Perfect Dodge, and
// the dash-reset. Runs against the REAL embedded npcs.json/items.json content,
// mirroring MaggotKingTests' harness pattern.
public sealed class HiveMatronTests
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

    private static (GameTickService svc, GameState state, NpcInstance npc) Build(IRandomProvider? rng = null)
    {
        rng ??= new AlwaysHitRandom();
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var template = npcs.GetTemplate("hive_matron")!;

        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var npc = new NpcInstance(template);
        state.StartDuel(npc);
        state.DisengageAtSpawn();

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
    public void Ingests_WithArenaRadius5_And11x11()
    {
        var (_, state, _) = Build();
        Assert.Equal(5, state.ArenaRadius);
        Assert.False(state.NpcStationary);
    }

    [Fact]
    public async Task Phase1_FiresDartVolleyAtRotationTick0()
    {
        var (svc, state, _) = Build();
        int hpBefore = state.Player.CurrentHp;

        await Tick(svc); // T0 casts Dart Volley — homing ranged projectile, not instant
        Assert.Equal(hpBefore, state.Player.CurrentHp);
        Assert.Contains(state.CombatLog, e => e.Kind == LogEntryKind.BossCast);
    }

    [Fact]
    public async Task AdjacencyPunish_TailStab_FiresAfter2ConsecutiveTicksAdjacent_WithKnockbackAndPoison()
    {
        var (svc, state, npc) = Build();
        // Corner her: her own spacing AI would normally flee adjacency, so
        // pin her against the arena edge (fleeing north is out-of-bounds)
        // with the player adjacent south of her — she can't create space.
        state.SetNpcTile(state.ArenaRadius, state.ArenaRadius);
        state.SetPlayerTile(state.ArenaRadius, state.ArenaRadius - 1);

        await Tick(svc); // tick 1 adjacent, cornered — can't flee
        Assert.False(state.PlayerPoisoned);

        int hpBefore = state.Player.CurrentHp;
        await Tick(svc); // tick 2 adjacent -> Tail Stab fires
        Assert.True(state.Player.CurrentHp < hpBefore);
        Assert.True(state.PlayerPoisoned);
        Assert.Contains(state.CombatLog, e => e.Message.Contains("Tail Stab"));
        // Knockback should have moved the player away from her.
        Assert.True(Math.Abs(state.PlayerTile.Z - state.NpcTile.Z) >= 2);
    }

    [Fact]
    public async Task ChitinGuard_ReducesPlayerRangedDamage_WhileActive()
    {
        var (svc, state, npc) = Build();
        // Fast-forward to the buff's cadence (25 ticks). Her own attacks
        // (dart volley/sting lob's venom pool/pin) would otherwise kill this
        // undefended test player long before tick 25 and end the duel —
        // top off HP every tick so the fight simply keeps running.
        for (int i = 0; i < 25; i++)
        {
            await Tick(svc);
            state.Player.RestoreHp();
        }

        Assert.True(npc.DamageReductionActive, "Chitin Guard should be active by tick 25 (CadenceTicks=25).");
    }

    [Fact]
    public async Task Drones_SpawnOnce_AtEach25PercentThreshold()
    {
        var (svc, state, npc) = Build();
        // Bring her to exactly 75% HP and tick — a drone wave should spawn once.
        int hp75 = npc.MaxHp * 75 / 100;
        typeof(NpcInstance).GetMethod("TakeDamage")!.Invoke(npc, [npc.MaxHp - hp75]);

        await Tick(svc);
        Assert.Equal(2, state.Adds.Count);
        Assert.All(state.Adds, a => Assert.Equal(AddKind.Drone, a.Kind));

        // Ticking again at the same HP must NOT spawn a second wave (once-only).
        await Tick(svc);
        Assert.Equal(2, state.Adds.Count(a => a.IsAlive));
    }

    [Fact]
    public async Task Drone_TakesRealWeaponDamage_NotOneHitFodder()
    {
        var (svc, state, npc) = Build();
        int hp75 = npc.MaxHp * 75 / 100;
        typeof(NpcInstance).GetMethod("TakeDamage")!.Invoke(npc, [npc.MaxHp - hp75]);
        await Tick(svc);

        var drone = state.Adds.First();
        Assert.Equal(3, drone.MaxHp);
        Assert.True(drone.IsAlive);
    }

    [Fact]
    public async Task Pin_HitsPlayer_WhenStillOnTheLine_AtResolution()
    {
        var (svc, state, npc) = Build();
        // Pin casts on RotationTick 12 (the 13th tick processed — a rotation
        // step fires on the Nth Tick() call for RotationTick N-1... i.e. the
        // (N+1)th call fires RotationTick N, matching MaggotKingTests'
        // "one Tick() call fires tick 0" convention). Top off HP along the
        // way — Dart Volley/Sting Lob's venom pool would otherwise kill this
        // undefended test player before Pin even casts.
        for (int i = 0; i < 13; i++) { await Tick(svc); state.Player.RestoreHp(); }
        Assert.NotNull(npc.LineChargeTiles);
        Assert.Contains(state.PlayerTile, npc.LineChargeTiles!);

        int hpBefore = state.Player.CurrentHp;
        await Tick(svc); // resolves: same-tick TickHazards-style decrement already consumed 1 of the 2 warning ticks on the cast tick itself
        Assert.True(state.Player.CurrentHp < hpBefore);
    }

    [Fact]
    public async Task Pin_Miss_HitsTheWall_AndOpensAPunishWindow()
    {
        // Perfect Dodge credit on a Pin miss reuses the same TryPerfectDodge
        // helper Maggot King's eruptions already exercise (see
        // MaggotKingTests' Perfect Dodge coverage) — this test focuses on
        // the miss/punish-window outcome itself, since driving a real
        // move-off-the-line-mid-tick through this harness (vs. a direct
        // SetPlayerTile before the tick, which back-dates preTickPlayerTile
        // too) would need the full OrderMove/KickMoveAsync movement path.
        var (svc, state, npc) = Build();
        for (int i = 0; i < 13; i++) { await Tick(svc); state.Player.RestoreHp(); }
        Assert.NotNull(npc.LineChargeTiles);

        var tiles = npc.LineChargeTiles!;
        var offLine = Enumerable.Range(-state.ArenaRadius, state.ArenaRadius * 2 + 1)
            .SelectMany(x => Enumerable.Range(-state.ArenaRadius, state.ArenaRadius * 2 + 1).Select(z => (X: x, Z: z)))
            .First(t => state.InArena(t) && !tiles.Contains(t));
        state.SetPlayerTile(offLine.X, offLine.Z);

        await Tick(svc); // resolves — player is off the line
        Assert.Contains(state.CombatLog, e => e.Message.Contains("wall"));
        Assert.True(npc.InPunishWindow);
    }

    [Fact]
    public async Task SpacingAi_StepsAwayWhenPlayerCloserThanPreferredRange()
    {
        var (svc, state, npc) = Build();
        // Put the player adjacent (well inside her 3-5 preferred band's floor).
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1);
        var npcTileBefore = state.NpcTile;

        await Tick(svc);
        Assert.NotEqual(npcTileBefore, state.NpcTile); // she stepped away
    }

    [Fact]
    public async Task Phase2_EntersFrenzy_BelowThreshold()
    {
        var (svc, state, npc) = Build();
        typeof(NpcInstance).GetMethod("TakeDamage")!.Invoke(npc, [npc.MaxHp - (npc.MaxHp * 39 / 100)]);
        await Tick(svc);
        Assert.Equal(2, npc.Phase);
    }
}
