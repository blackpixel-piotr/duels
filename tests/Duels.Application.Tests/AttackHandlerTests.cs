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
using Xunit;

namespace Duels.Application.Tests;

public sealed class AttackHandlerTests
{
    // Always hits, always deals max damage
    private sealed class AlwaysHitRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0; // triggers hit (very low number)
    }

    // Always misses
    private sealed class AlwaysMissRandom : IRandomProvider
    {
        public int Next(int min, int max) => min;
        public double NextDouble() => 0.999; // triggers miss (very high number, but attacker roll=0)
    }

    private sealed class StubItemRepo : IItemRepository
    {
        private readonly Weapon _weapon = new("sword", "Sword", AttackType.Slash,
            new ItemModifiers(SlashAttack: 20, StrengthBonus: 20));
        public GearPiece? GetGear(string id) => _weapon.AsGearPiece();
        public Weapon? GetWeapon(string id) => _weapon;
        public string? GetItemName(string id) => "Sword";
        public bool IsWeapon(string id) => true;
    }

    private sealed class StubEventBus : IEventBus
    {
        public List<DomainEvent> Published { get; } = new();
        public Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default) where TEvent : DomainEvent { Published.Add(e); return Task.CompletedTask; }
        public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> h) where TEvent : DomainEvent { }
    }

    private static (AttackHandler handler, GameState state, StubEventBus events) Build(IRandomProvider random)
    {
        var player = new Player("p1", "Hero");
        var goblin = new NpcTemplate("goblin", "Goblin", "It's a goblin.",
            new CombatStats(1, 1, 1, 2), ItemModifiers.Zero, AttackType.Crush,
            [], goldReward: 5);

        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(goblin));

        var stateRepo = new InMemoryStateRepo(state);
        var npcRepo = new StubNpcRepo(goblin);
        var itemRepo = new StubItemRepo();
        var events = new StubEventBus();
        var calc = new CombatCalculator(random);
        var unlockSvc = new ItemUnlockService(random, itemRepo);

        var handler = new AttackHandler(stateRepo, itemRepo, calc, events, unlockSvc);
        return (handler, state, events);
    }

    [Fact]
    public async Task Attack_WhenAlwaysHits_PublishesAttackLandedEvent()
    {
        var (handler, _, events) = Build(new AlwaysHitRandom());
        await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate));
        Assert.Contains(events.Published, e => e is AttackLanded { AttackerId: "p1" });
    }

    [Fact]
    public async Task Attack_KillsNpc_PublishesDuelWonEvent()
    {
        // Goblin has very low HP — always-hit should kill it quickly
        var (handler, state, events) = Build(new AlwaysHitRandom());
        for (int i = 0; i < 30; i++)
        {
            if (!state.InDuel) break;
            await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Aggressive));
        }
        Assert.Contains(events.Published, e => e is DuelWon);
    }

    [Fact]
    public async Task Attack_WithNoActiveDuel_ReturnsFail()
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var stateRepo = new InMemoryStateRepo(state);
        var events = new StubEventBus();
        var random = new AlwaysHitRandom();
        var itemRepo = new StubItemRepo();
        var handler = new AttackHandler(stateRepo, itemRepo, new CombatCalculator(random), events, new ItemUnlockService(random, itemRepo));

        var result = await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate));

        Assert.False(result.Success);
    }

    // ── Minimal stubs ──────────────────────────────────────────────────────────

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubNpcRepo : INpcRepository
    {
        private readonly NpcTemplate _npc;
        public StubNpcRepo(NpcTemplate npc) => _npc = npc;
        public NpcTemplate? GetTemplate(string id) => _npc;
        public IReadOnlyList<NpcTemplate> GetAll() => [_npc];
    }
}
