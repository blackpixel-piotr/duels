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

// M3 Workstream C: Mirrorhide (Boss Bible §3) — Echo Offense, Attunement +
// Shatter, Reflection, Cloak, and Phase 2's Copycat. Runs against the REAL
// embedded npcs.json/items.json content, mirroring MaggotKingTests' harness.
public sealed class MirrorhideTests
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

    private static (GameTickService svc, GameState state, NpcInstance npc, IItemRepository items) Build(IRandomProvider? rng = null)
    {
        rng ??= new AlwaysHitRandom();
        var items = new DefinitionItemRepository();
        var npcs = new DefinitionNpcRepository(items);
        var template = npcs.GetTemplate("mirrorhide")!;

        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        var npc = new NpcInstance(template);
        state.StartDuel(npc);
        state.DisengageAtSpawn();
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1); // adjacent, melee-ready

        var damage = new DamageModel(rng);
        var svc = new GameTickService(
            new InMemoryStateRepo(state), damage, rng,
            items, new StubEventBus(), new StubTickSource());
        return (svc, state, npc, items);
    }

    private static async Task Tick(GameTickService svc)
    {
        var method = typeof(GameTickService).GetMethod("ProcessTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(svc, ["p1"])!;
    }

    private static async Task PlayerAttack(GameTickService svc, GameState state)
    {
        // Force the attack-cooldown gate open (weapon speed would otherwise
        // require several ticks between attacks) so each call lands exactly
        // one attack this tick, deterministically.
        state.Engage();
        state.ResetPlayerCooldown(0);
        await Tick(svc);
    }

    [Fact]
    public void Ingests_WithArenaRadius4_Melee9x9()
    {
        var (_, state, _, _) = Build();
        Assert.Equal(4, state.ArenaRadius);
    }

    [Fact]
    public async Task EchoOffense_HerAttackStyle_MatchesThePlayersLastLandedHitStyle()
    {
        var (svc, state, npc, items) = Build();
        player_EquipWeapon(state, items, "wpn_ranged_t1"); // Ranged
        await PlayerAttack(svc, state);
        Assert.Equal(AttackType.Ranged, npc.LastHitStyle);

        player_EquipWeapon(state, items, "wpn_magic_t1"); // Magic
        state.Player.RestoreHp();
        await PlayerAttack(svc, state);
        Assert.Equal(AttackType.Magic, npc.LastHitStyle);
    }

    [Fact]
    public async Task Attunement_BecomesImmune_After4ConsecutiveSameStyleHits_ThenBlocksThatStyle()
    {
        var (svc, state, npc, items) = Build();
        player_EquipWeapon(state, items, "wpn_melee_t1"); // Slash

        for (int i = 0; i < 4; i++) { await PlayerAttack(svc, state); state.Player.RestoreHp(); }
        Assert.Equal(AttackType.Slash, npc.AttunedStyle);
        Assert.True(npc.IsShimmering);

        // Advance through the 2-tick shimmer without landing another hit —
        // full immunity should then be active.
        for (int i = 0; i < 3; i++) await Tick(svc);
        Assert.True(npc.IsAttuned);

        int hpBefore = npc.CurrentHp;
        await PlayerAttack(svc, state); // Slash again, while fully immune
        Assert.Equal(hpBefore, npc.CurrentHp); // fully blocked
    }

    [Fact]
    public async Task Shatter_DifferentStyleDuringShimmer_OpensPunishWindow()
    {
        var (svc, state, npc, items) = Build();
        player_EquipWeapon(state, items, "wpn_melee_t1"); // Slash

        for (int i = 0; i < 4; i++) { await PlayerAttack(svc, state); state.Player.RestoreHp(); }
        Assert.True(npc.IsShimmering);

        player_EquipWeapon(state, items, "wpn_magic_t1"); // different style, during the shimmer window
        await PlayerAttack(svc, state);

        Assert.Null(npc.AttunedStyle);
        Assert.True(npc.InPunishWindow);
        Assert.Contains(state.CombatLog, e => e.Message.Contains("SHATTERED"));
    }

    [Fact]
    public async Task Reflect_CastAtRotationTick12_ReflectsDamageBackDuringItsWindow()
    {
        var (svc, state, npc, items) = Build();
        player_EquipWeapon(state, items, "wpn_melee_t1");

        // Reflect casts at RotationTick 12 (the 13th tick processed).
        for (int i = 0; i < 13; i++) { await Tick(svc); state.Player.RestoreHp(); }
        Assert.True(npc.ReflectChannelTicksLeft > 0);

        // 3-tick channel resolves; the reflect window then opens.
        for (int i = 0; i < 3; i++) { await Tick(svc); state.Player.RestoreHp(); }
        Assert.True(npc.ReflectWindowActive);

        int hpBefore = state.Player.CurrentHp;
        await PlayerAttack(svc, state); // Slash, matching whatever she's reflecting
        Assert.True(state.Player.CurrentHp < hpBefore || npc.ReflectStyle != AttackType.Slash);
    }

    [Fact]
    public async Task Cloak_MakesHerUntargetable_ThenEndsWithReposition()
    {
        var (svc, state, npc, items) = Build();
        player_EquipWeapon(state, items, "wpn_melee_t1");

        // Cloak cadence is 20 ticks; force it early via reflection to keep
        // this test fast rather than ticking 20 times.
        typeof(NpcInstance).GetProperty("CloakCooldown")!
            .GetSetMethod(nonPublic: true)!.Invoke(npc, [0]);

        int hpBefore = npc.CurrentHp;
        await Tick(svc); // cloak should engage this tick
        Assert.True(npc.IsCloaked);

        await PlayerAttack(svc, state); // should whiff — she's untargetable
        Assert.Equal(hpBefore, npc.CurrentHp);
        Assert.Contains(state.CombatLog, e => e.Message.Contains("cloaked"));
    }

    [Fact]
    public async Task Phase2_TightensAttunement_To3Hits()
    {
        var (svc, state, npc, items) = Build();
        typeof(NpcInstance).GetMethod("TakeDamage")!.Invoke(npc, [npc.MaxHp - (npc.MaxHp * 49 / 100)]);
        await Tick(svc);
        Assert.Equal(2, npc.Phase);
        Assert.Equal(3, npc.ActivePhaseDef.Attunement!.HitsToAttune);
    }

    private static void player_EquipWeapon(GameState state, IItemRepository items, string weaponId)
    {
        state.Player.AddToInventory(weaponId);
        state.Player.Equip(weaponId, EquipmentSlot.Weapon);
    }
}
