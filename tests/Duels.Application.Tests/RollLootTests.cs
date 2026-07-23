using System.Reflection;
using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Application.Services;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

// Backlog resolution batch 1 §2: weighted-group loot rolls (economy doc §5's
// two-stage "one roll on... Slot" model) and the "gold" pseudo-item.
public sealed class RollLootTests
{
    private sealed class ScriptedRandom : IRandomProvider
    {
        private readonly Queue<double> _doubles;
        public ScriptedRandom(params double[] doubles) => _doubles = new Queue<double>(doubles);
        public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.999;
        public int Next(int min, int max) => min;
    }

    private sealed class StubItemRepo : IItemRepository
    {
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => null;
        public string? GetItemName(string id) => id;
        public bool IsWeapon(string id) => false;
        public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems() => [];
        public int? GetShopPrice(string id) => null;
        public int GetFenceValue(string id) => 0;
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

    private static List<string> RollLoot(IRandomProvider rng, NpcTemplate template, out Player player, out GameState state)
    {
        player = new Player("p1", "Hero");
        state = new GameState("p1", player);
        var svc = new GameTickService(new InMemoryStateRepo(state), new DamageModel(rng), rng,
            new StubItemRepo(), new StubEventBus(), new StubTickSource());
        var method = typeof(GameTickService).GetMethod("RollLoot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (List<string>)method.Invoke(svc, [state, player, template])!;
    }

    [Fact]
    public void GroupedRoll_BelowGroupChance_PicksWeightedMember()
    {
        var template = new NpcTemplate("t", "T", "", new CombatStats(1, 1, 1, 10),
        [
            new LootEntry("a", 0.5, GroupId: "g", Weight: 30),
            new LootEntry("b", 0.5, GroupId: "g", Weight: 70),
        ]);
        // First NextDouble (0.1) < 0.5 group chance -> hits. Second (0.5) * totalWeight(100) = 50 -> falls in "a"[0,30) or "b"[30,100)? 50 is in "b".
        var rng = new ScriptedRandom(0.1, 0.5);
        var looted = RollLoot(rng, template, out _, out _);
        Assert.Single(looted);
        Assert.Equal("b", looted[0]);
    }

    [Fact]
    public void GroupedRoll_AboveGroupChance_DropsNothing()
    {
        var template = new NpcTemplate("t", "T", "", new CombatStats(1, 1, 1, 10),
        [
            new LootEntry("a", 0.5, GroupId: "g", Weight: 100),
        ]);
        var rng = new ScriptedRandom(0.9); // >= 0.5 group chance -> miss
        var looted = RollLoot(rng, template, out _, out _);
        Assert.Empty(looted);
    }

    [Fact]
    public void GoldPseudoItem_GrantsGoldDirectly_NotAnInventoryItem()
    {
        var template = new NpcTemplate("t", "T", "", new CombatStats(1, 1, 1, 10),
        [
            new LootEntry("gold", 1.0, MinQty: 40, MaxQty: 40),
        ]);
        var rng = new ScriptedRandom(0.0);
        var looted = RollLoot(rng, template, out var player, out _);
        Assert.Empty(looted); // gold is not an inventory item
        // Player now starts with 600g (backlog resolution batch 1 §4, cold start).
        Assert.Equal(640, player.Gold);
        Assert.Empty(player.Inventory);
    }

    [Fact]
    public void UngroupedEntries_StillRollIndependently()
    {
        var template = new NpcTemplate("t", "T", "", new CombatStats(1, 1, 1, 10),
        [
            new LootEntry("x", 1.0),
            new LootEntry("y", 1.0),
        ]);
        var rng = new ScriptedRandom(0.0, 0.0);
        var looted = RollLoot(rng, template, out _, out _);
        Assert.Equal(2, looted.Count);
        Assert.Contains("x", looted);
        Assert.Contains("y", looted);
    }
}
