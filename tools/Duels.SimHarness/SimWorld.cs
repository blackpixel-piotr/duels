using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Application.Services;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Persistence;

namespace Duels.SimHarness;

/// <summary>
/// A single headless, deterministic combat instance: one player vs. one boss,
/// wired to the REAL <see cref="GameTickService"/> and the REAL embedded
/// npcs.json / items.json content, but with instant (non-real-time) infra
/// stubs so a whole fight runs in milliseconds.
///
/// This is the backend of the dev arena: no Blazor, no Three.js, no browser —
/// exactly the "quick arena to test boss script / player movement" the team
/// wanted. A <see cref="IPlayerBrain"/> supplies the player's inputs each tick;
/// <see cref="TraceRecorder"/> captures what happened.
/// </summary>
public sealed class SimWorld
{
    public const string PlayerId = "sim";

    public GameState State { get; }
    public Player Player => State.Player;
    public NpcInstance Npc => State.ActiveNpc!;

    private readonly GameTickService _svc;

    public SimWorld(string bossId, IRandomProvider rng, IReadOnlyList<string>? loadout = null)
    {
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var template = npcs.GetTemplate(bossId)
            ?? throw new ArgumentException(
                $"No boss template '{bossId}' — is it implemented yet? " +
                $"(GetTemplate returned null; locked/unbuilt bosses do that by design.)");

        var player = new Player(PlayerId, "Duelist");
        // Give the player their kit up-front so weapon swaps in a brain work.
        foreach (var itemId in loadout ?? DefaultLoadout)
            player.AddToInventory(itemId);
        if ((loadout ?? DefaultLoadout).Count > 0)
            player.Equip((loadout ?? DefaultLoadout)[0], EquipmentSlot.Weapon);

        State = new GameState(PlayerId, player);
        State.StartDuel(new NpcInstance(template));
        // StartDuel engages the approach-lock; disengage so the brain owns all
        // movement (matches how the real pre-fight screen hands off control).
        State.DisengageAtSpawn();

        _svc = new GameTickService(
            new InMemoryStateRepo(State),
            new DamageModel(rng),
            rng, items,
            new NullEventBus(),
            new InstantTickSource());
    }

    /// <summary>A sensible ranged-primary T2 kit: swap slot 0 is the equipped
    /// weapon, the rest sit in the bag ready for a brain to swap to.</summary>
    public static readonly IReadOnlyList<string> DefaultLoadout =
        new[] { "wpn_ranged_t2", "wpn_melee_t2", "wpn_magic_t2" };

    /// <summary>Advance the fight one 0.6s tick. The brain has already written
    /// this tick's inputs (prayer, move order, queued action) into State.</summary>
    public Task TickAsync() => _svc.TickOnceAsync(PlayerId);

    public bool FightOver => !State.InDuel || !Player.IsAlive;

    // ── deterministic infra stubs ──────────────────────────────────────────

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default) where TEvent : DomainEvent => Task.CompletedTask;
        public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> h) where TEvent : DomainEvent { }
    }

    private sealed class InstantTickSource : ITickSource
    {
        public long ElapsedMsIntoCurrentTick => 0;
        public void Reset() { }
        public Task WaitForNextTickAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
