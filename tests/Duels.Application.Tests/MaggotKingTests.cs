using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Application.Services;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using System.Reflection;
using Xunit;

namespace Duels.Application.Tests;

// Tile-hazard boss mechanics (HazardProfile): telegraphed eruptions that
// ignore protection prayers, lingering poison pools, and the phase-2 frenzy.
public sealed class MaggotKingTests
{
    private sealed class AlwaysHitMaxRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0;
    }

    private sealed class StubItemRepo : IItemRepository
    {
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => null;
        public string? GetItemName(string id) => id;
        public bool IsWeapon(string id) => false;
        public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems() => [];
        public int GetFenceValue(string id) => 0;
    }

    private sealed class StubNpcRepo : INpcRepository
    {
        public NpcTemplate? GetTemplate(string id) => null;
        public IReadOnlyList<NpcTemplate> GetAll() => [];
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

    // Feeble melee hazard boss: waves every 2 ticks after a 2-tick warning,
    // spawned far enough away (and hitting for at most 1) that hazard damage
    // dominates every assertion. Big HP so nobody dies mid-test.
    private static NpcTemplate HazardBoss(int cooldown = 2, int warning = 2, int tilesPerWave = 3) =>
        new("hazard_boss", "Hazard Boss", "", new CombatStats(1, 1, 99, 500), ItemModifiers.Zero,
            AttackType.Crush, [], attackSpeedTicks: 50,
            hazards: new HazardProfile("The ground churns!", cooldown, warning, tilesPerWave,
                EruptDamage: 22, PoolTicks: 4, PoolDamage: 4));

    private static (GameTickService svc, GameState state) Build(NpcTemplate template)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(template));
        state.HoldPositionAtSpawn(); // stand still unless a test moves the player

        var combat = new CombatCalculator(new AlwaysHitMaxRandom());
        var svc = new GameTickService(
            new InMemoryStateRepo(state), combat, new AlwaysHitMaxRandom(),
            new StubItemRepo(), new StubNpcRepo(), new StubEventBus(), new StubTickSource());
        return (svc, state);
    }

    private static async Task Tick(GameTickService svc)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, ["p1"])!;
    }

    private static int Splats(GameState state, string splat) =>
        state.CombatLog.Count(e => e.Kind == LogEntryKind.HitsplatNpc && e.Message == splat);

    [Fact]
    public async Task Wave_SpawnsOnCooldown_AndMarksPlayerTile()
    {
        var (svc, state) = Build(HazardBoss(cooldown: 2));

        await Tick(svc);
        Assert.Empty(state.Hazards); // cooldown still running

        await Tick(svc); // cooldown hits 0 → wave
        Assert.NotEmpty(state.Hazards);
        Assert.All(state.Hazards, h => Assert.False(h.Pool));
        Assert.Contains(state.Hazards, h => (h.X, h.Z) == state.PlayerTile);
        Assert.Equal(3, state.Hazards.Count);
    }

    [Fact]
    public async Task Eruption_IgnoresProtectionPrayer_WhenStandingOnTile()
    {
        var (svc, state) = Build(HazardBoss(cooldown: 2, warning: 2));
        // Praying against the boss's own style must NOT reduce eruption damage.
        state.Player.ToggleProtection(ProtectionPrayer.Melee);

        await Tick(svc); await Tick(svc); // wave spawns (warning 2)
        int hpBefore = state.Player.CurrentHp;
        await Tick(svc);                  // warning 2 → 1
        await Tick(svc);                  // 1 → 0: eruption under our feet
        Assert.Equal(22, hpBefore - state.Player.CurrentHp);
        Assert.Equal(1, Splats(state, "22:hazard"));
        Assert.True(state.PlayerPoisoned);
    }

    [Fact]
    public async Task Eruption_MissesPlayerWhoMovedOff()
    {
        // Long cooldown (kicked once manually) so no second wave muddies the
        // "everything is a pool now" assertion at the end.
        var (svc, state) = Build(HazardBoss(cooldown: 20, warning: 2));
        state.ActiveNpc!.ResetHazardCooldown(2);

        await Tick(svc); await Tick(svc); // wave spawns on our tile
        // Step well clear of the whole wave footprint (Chebyshev 2 of spawn).
        state.SetPlayerTile(-3, 0);
        await Tick(svc); await Tick(svc); // eruption fires on empty ground
        Assert.Equal(0, Splats(state, "22:hazard"));
        Assert.False(state.PlayerPoisoned);
        // The blast zone is now pools.
        Assert.NotEmpty(state.Hazards);
        Assert.All(state.Hazards, h => Assert.True(h.Pool));
    }

    [Fact]
    public async Task Pools_BurnWhileStoodIn_NotAfterLeaving_AndExpire()
    {
        var (svc, state) = Build(HazardBoss(cooldown: 20, warning: 2));
        state.ActiveNpc!.ResetHazardCooldown(2);

        await Tick(svc); await Tick(svc);  // wave
        state.SetPlayerTile(-3, 0);        // dodge the eruption
        await Tick(svc); await Tick(svc);  // tiles erupt → pools (4 ticks left)
        var pool = state.Hazards.First(h => h.Pool);

        state.SetPlayerTile(pool.X, pool.Z); // wade in
        await Tick(svc);
        Assert.Equal(1, Splats(state, "4:poison"));

        state.SetPlayerTile(-3, 0);          // step out
        await Tick(svc);
        Assert.Equal(1, Splats(state, "4:poison")); // no new burn

        await Tick(svc); await Tick(svc);    // pools dry up (PoolTicks 4)
        Assert.Empty(state.Hazards);
    }

    [Fact]
    public async Task Phase2_AtHalfHp_QuickensRotation_AndWidensWaves()
    {
        var (svc, state) = Build(HazardBoss(cooldown: 4, warning: 2, tilesPerWave: 3));
        var npc = state.ActiveNpc!;
        npc.TakeDamage(npc.MaxHp / 2 + 10);
        npc.ResetHazardCooldown(1); // next tick both flips phase and fires a wave

        await Tick(svc);
        Assert.True(npc.PhaseShiftUsed);
        Assert.Equal(2, npc.AttacksPerStyleOverride);
        // Enraged wave is one tile wider.
        Assert.Equal(4, state.Hazards.Count(h => !h.Pool));
    }

    [Fact]
    public async Task HazardsClear_WhenDuelEnds()
    {
        var (svc, state) = Build(HazardBoss(cooldown: 2));
        await Tick(svc); await Tick(svc);
        Assert.NotEmpty(state.Hazards);

        state.EndDuel();
        Assert.Empty(state.Hazards);
    }
}
