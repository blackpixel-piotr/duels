using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Application.Services;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using System.Reflection;
using Xunit;

namespace Duels.Application.Tests;

// UI bible §3.2: "Tap = swap (resolves same tick, max one swap per tick;
// extra taps buffer)". Verifies the M0 input-buffer mechanic added to
// GameState/WeaponShortcutHandler/GameTickService.
public sealed class WeaponSwapBufferTests
{
    private static readonly Weapon SwordA = new("sword_a", "Sword A", AttackType.Slash, ItemModifiers.Zero);
    private static readonly Weapon SwordB = new("sword_b", "Sword B", AttackType.Slash, ItemModifiers.Zero);
    private static readonly Weapon SwordC = new("sword_c", "Sword C", AttackType.Slash, ItemModifiers.Zero);

    private sealed class StubItemRepo : IItemRepository
    {
        private readonly Dictionary<string, Weapon> _weapons = new()
        {
            [SwordA.Id] = SwordA, [SwordB.Id] = SwordB, [SwordC.Id] = SwordC,
        };
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => _weapons.GetValueOrDefault(id);
        public string? GetItemName(string id) => _weapons.GetValueOrDefault(id)?.Name ?? id;
        public bool IsWeapon(string id) => _weapons.ContainsKey(id);
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

    private sealed class AlwaysHitMaxRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0;
    }

    private static NpcTemplate Tank() =>
        new("dummy", "Dummy", "", new CombatStats(1, 1, 99, 500), ItemModifiers.Zero, AttackType.Crush, []);

    private static (GameTickService svc, GameState state, WeaponShortcutHandler handler) Build()
    {
        var player = new Player("p1", "Hero");
        player.AddToInventory("sword_a");
        player.AddToInventory("sword_b");
        player.AddToInventory("sword_c");
        player.Equip("sword_a", EquipmentSlot.Weapon);

        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(Tank()));
        state.SetTestScene(true); // bypass the Attack-level wield gate
        state.HoldPositionAtSpawn();

        var items = new StubItemRepo();
        var stateRepo = new InMemoryStateRepo(state);
        var combat = new CombatCalculator(new AlwaysHitMaxRandom());
        var svc = new GameTickService(
            stateRepo, combat, new AlwaysHitMaxRandom(),
            items, new StubNpcRepo(), new StubEventBus(), new StubTickSource());
        var handler = new WeaponShortcutHandler(stateRepo, items);

        return (svc, state, handler);
    }

    private static async Task Tick(GameTickService svc)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, ["p1"])!;
    }

    [Fact]
    public async Task FirstSwapInATick_ResolvesImmediately()
    {
        var (_, state, handler) = Build();

        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_b"));

        Assert.Equal("sword_b", state.Player.GetEquippedWeaponId());
        Assert.Null(state.PendingWeaponSwapId);
    }

    [Fact]
    public async Task SecondSwapInSameTick_BuffersInsteadOfResolvingImmediately()
    {
        var (_, state, handler) = Build();

        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_b"));
        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_c"));

        // Slot already claimed by the sword_b tap — sword_c buffers instead
        // of equipping now.
        Assert.Equal("sword_b", state.Player.GetEquippedWeaponId());
        Assert.Equal("sword_c", state.PendingWeaponSwapId);
    }

    [Fact]
    public async Task BufferedSwap_AppliesAtTopOfNextTick()
    {
        var (svc, state, handler) = Build();

        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_b"));
        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_c")); // buffers

        await Tick(svc);

        Assert.Equal("sword_c", state.Player.GetEquippedWeaponId());
        Assert.Null(state.PendingWeaponSwapId);
    }

    [Fact]
    public async Task SwapGateReopens_EachTick()
    {
        var (svc, state, handler) = Build();

        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_b"));
        await Tick(svc); // consumes this tick's slot, gate resets for the new tick

        await handler.HandleAsync(new WeaponShortcutCommand("p1", "sword_c"));

        // A fresh tick means a fresh slot — this resolves immediately, no buffering.
        Assert.Equal("sword_c", state.Player.GetEquippedWeaponId());
        Assert.Null(state.PendingWeaponSwapId);
    }
}
