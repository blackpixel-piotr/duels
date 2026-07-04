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

public sealed class GameTickServiceLootTests
{
    // Always hits, always deals max damage — drives the combat roll deterministically
    private sealed class AlwaysHitMaxRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0;
    }

    // Every roll succeeds (NextDouble returns 0, always < any positive drop chance)
    private sealed class AlwaysSucceedRandom : IRandomProvider
    {
        public int Next(int min, int max) => min;
        public double NextDouble() => 0.0;
    }

    private sealed class StubItemRepo : IItemRepository
    {
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => null;
        public string? GetItemName(string id) => id;
        public bool IsWeapon(string id) => false;
        public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems() => [];
        public int GetFenceValue(string id) => 777;
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

    private static (GameTickService svc, GameState state) Build(NpcTemplate template)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(template));
        state.SetQueuedAction("attack");

        // These tests exercise loot, not movement — start in melee range.
        state.SetPlayerTile(0, 0);
        state.SetNpcTile(1, 0);

        var combat = new CombatCalculator(new AlwaysHitMaxRandom());
        var svc = new GameTickService(
            new InMemoryStateRepo(state), combat, new AlwaysSucceedRandom(),
            new StubItemRepo(), new StubNpcRepo(), new StubEventBus());

        return (svc, state);
    }

    private static async Task RunOneTick(GameTickService svc, string playerId)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, [playerId])!;
    }

    [Fact]
    public async Task Victory_RollsGoldAndItemDrops()
    {
        var template = new NpcTemplate("dummy", "Dummy", "", new CombatStats(1, 1, 1, 1), ItemModifiers.Zero, AttackType.Crush,
            [
                new LootEntry("gold", 1.0, MinQty: 10, MaxQty: 10),
                new LootEntry("trinket", 1.0),
            ]);
        var (svc, state) = Build(template);

        await RunOneTick(svc, "p1");

        Assert.Equal(10_010, state.Player.Gold); // 10,000 starting + 10 loot gold
        Assert.Contains("trinket", state.Player.Inventory);
    }

    [Fact]
    public async Task Victory_OnceOnlyDrop_SkippedIfAlreadyOwned()
    {
        var template = new NpcTemplate("dummy", "Dummy", "", new CombatStats(1, 1, 1, 1), ItemModifiers.Zero, AttackType.Crush,
            [
                new LootEntry("rare_item", 1.0, OnceOnly: true),
            ]);
        var (svc, state) = Build(template);
        state.Player.AddToInventory("rare_item");

        await RunOneTick(svc, "p1");

        Assert.Equal(1, state.Player.Inventory.Count(i => i == "rare_item"));
    }

    [Fact]
    public async Task Victory_FullInventory_FencesInsteadOfAdding()
    {
        var template = new NpcTemplate("dummy", "Dummy", "", new CombatStats(1, 1, 1, 1), ItemModifiers.Zero, AttackType.Crush,
            [
                new LootEntry("overflow_item", 1.0),
            ]);
        var (svc, state) = Build(template);
        for (int i = 0; i < 28; i++)
            state.Player.AddToInventory("filler");

        int goldBefore = state.Player.Gold;
        await RunOneTick(svc, "p1");

        Assert.Equal(28, state.Player.Inventory.Count);
        Assert.DoesNotContain("overflow_item", state.Player.Inventory);
        Assert.Equal(goldBefore + 777, state.Player.Gold);
    }

    [Fact]
    public async Task Victory_PaysBountyAlways_EvenWithoutWager()
    {
        var template = new NpcTemplate("dummy", "Dummy", "", new CombatStats(1, 1, 1, 1), ItemModifiers.Zero, AttackType.Crush,
            [], goldReward: 500);
        var (svc, state) = Build(template);

        await RunOneTick(svc, "p1");

        Assert.Equal(10_500, state.Player.Gold);
    }
}
