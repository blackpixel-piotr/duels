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

public sealed class RangeAndMovementTests
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

    // Beefy dummy so nobody dies while we watch positioning.
    private static NpcTemplate Tank(AttackType style, IReadOnlyList<AttackType>? rotation = null) =>
        new("dummy", "Dummy", "", new CombatStats(1, 1, 99, 500), ItemModifiers.Zero, style,
            [], styleRotation: rotation);

    private static (GameTickService svc, GameState state) Build(NpcTemplate template)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(template));

        var combat = new CombatCalculator(new AlwaysHitMaxRandom());
        var svc = new GameTickService(
            new InMemoryStateRepo(state), combat, new AlwaysHitMaxRandom(),
            new StubItemRepo(), new StubNpcRepo(), new StubEventBus());
        return (svc, state);
    }

    private static async Task Tick(GameTickService svc)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, ["p1"])!;
    }

    private static int Hitsplats(GameState state, LogEntryKind kind) =>
        state.CombatLog.Count(e => e.Kind == kind);

    [Fact]
    public async Task MeleeVsMelee_NoHitsWhileApproaching_FirstContactAfterWalkIn()
    {
        var (svc, state) = Build(Tank(AttackType.Crush));
        Assert.Equal(6, state.DistanceToNpc); // (0,3) vs (1,-3)

        // Tick 1-2: both walk (closure 2/tick), nobody can hit.
        await Tick(svc);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatNpc));
        await Tick(svc);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));

        // Tick 3: adjacency reached — both attacks land this tick.
        await Tick(svc);
        Assert.Equal(1, state.DistanceToNpc);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > 0);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatNpc) > 0);
    }

    [Fact]
    public async Task RangedNpc_HitsFromSpawnDistance_WhilePlayerStillWalking()
    {
        var (svc, state) = Build(Tank(AttackType.Ranged));

        await Tick(svc);

        // NPC fired from across the arena; melee player landed nothing yet.
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatNpc) > 0);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));
        // Ranged NPC stands its ground; the player walked.
        Assert.Equal((1, -3), state.NpcTile);
        Assert.NotEqual((0, 3), state.PlayerTile);
    }

    [Fact]
    public async Task Chase_ConvergesToAdjacency_NeverSharesTile()
    {
        var (svc, state) = Build(Tank(AttackType.Slash));

        for (int i = 0; i < 10; i++)
        {
            await Tick(svc);
            Assert.True(state.DistanceToNpc >= 1, $"tick {i}: combatants share a tile");
        }
        Assert.Equal(1, state.DistanceToNpc); // settled adjacent, both melee
    }

    [Fact]
    public async Task QueuedSpec_HeldDuringApproach_NotDiscarded()
    {
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.SetQueuedAction("spec");

        await Tick(svc); // out of range — action must survive the walk tick
        Assert.Equal("spec", state.QueuedAction);
    }

    [Fact]
    public void NpcInRange_TracksStyle()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Magic)));

        Assert.Equal(6, state.DistanceToNpc);
        Assert.True(state.NpcInRange); // magic covers the arena

        state.StartDuel(new NpcInstance(Tank(AttackType.Stab)));
        Assert.False(state.NpcInRange); // melee must close first
    }
}
