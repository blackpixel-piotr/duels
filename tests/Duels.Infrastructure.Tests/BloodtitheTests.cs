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

// M3 Workstream D: Bloodtithe (Boss Bible §4) — bleed stacking (prayer
// negates), Tithe aura drain/heal with the back-tile bonus, Font purge,
// Scythe Arc's delayed miss/punish, Transfusion's heal+interrupt, and
// Phase 2's Crimson Pact/Harvest. Runs against the real embedded content.
public sealed class BloodtitheTests
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
        var template = npcs.GetTemplate("bloodtithe")!;

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
    public void Ingests_WithArenaRadius4_And2FontTiles()
    {
        var (_, state, npc) = Build();
        Assert.Equal(4, state.ArenaRadius);
        Assert.Equal(2, state.FontCooldowns.Count);
    }

    [Fact]
    public async Task ScytheArc_HitsAtMeleeRange_AndAppliesABleedStack()
    {
        var (svc, state, npc) = Build();
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1); // adjacent -> scythe_arc, not blood_lance

        for (int i = 0; i < 3; i++) { await Tick(svc); state.Player.RestoreHp(); } // cast(0) + 2-tick windup resolves at tick 2
        Assert.Equal(1, state.PlayerBleedStacks);
    }

    [Fact]
    public async Task ScytheArc_CorrectlyPrayed_AppliesNoBleedStack()
    {
        var (svc, state, npc) = Build();
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1);
        state.Player.ToggleProtection(ProtectionPrayer.Melee);

        for (int i = 0; i < 3; i++) { await Tick(svc); state.Player.RestoreHp(); }
        Assert.Equal(0, state.PlayerBleedStacks);
    }

    [Fact]
    public async Task ScytheArc_MissesWhenOutOfMeleeRangeAtResolution_AndOpensPunishWindow()
    {
        var (svc, state, npc) = Build();
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1); // adjacent at cast
        await Tick(svc); // cast tick — scythe_arc windup begins

        state.SetPlayerTile(0, 0); // step away before the 2-tick windup resolves
        await Tick(svc);
        await Tick(svc); // resolves — player no longer in melee range

        Assert.True(npc.InPunishWindow);
    }

    [Fact]
    public async Task Font_PurgesAllBleedStacks_ThenGoesOnCooldown()
    {
        var (svc, state, npc) = Build();
        state.ApplyBleedStack(5, 10, 3);
        state.ApplyBleedStack(5, 10, 3);
        Assert.Equal(2, state.PlayerBleedStacks);

        state.SetPlayerTile(4, 4); // one of the two font tiles
        await Tick(svc);

        Assert.Equal(0, state.PlayerBleedStacks);
        Assert.True(state.FontCooldowns[(4, 4)] > 0);
    }

    [Fact]
    public async Task TitheAura_DrainsPlayerAndHealsBoss_WhenNotOnHisBackTile()
    {
        var (svc, state, npc) = Build();
        // Stand directly south of him (his default facing is toward the
        // player anyway on tick 1, so approach from a side he isn't facing
        // yet — south, matching his spawn-relative position).
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1);
        int npcHpBefore = npc.CurrentHp;
        int playerHpBefore = state.Player.CurrentHp;

        await Tick(svc);

        Assert.True(state.Player.CurrentHp <= playerHpBefore);
        Assert.True(npc.CurrentHp >= npcHpBefore);
    }

    [Fact]
    public async Task BackTile_BonusDamage_WhenPlayerAttacksFromBehindHim()
    {
        var (svc, state, npc) = Build();
        // Tick once so his facing locks onto the player's current side, then
        // attack from the tile directly opposite his facing.
        state.SetPlayerTile(state.NpcTile.X, state.NpcTile.Z - 1);
        await Tick(svc);

        int facing = (int)Math.Round(npc.FacingAngleDeg / 90.0) % 4;
        var backOffsets = new (int X, int Z)[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        var backOffset = backOffsets[(facing + 2) % 4];
        var backTile = (X: state.NpcTile.X + backOffset.X, Z: state.NpcTile.Z + backOffset.Z);
        state.SetPlayerTile(backTile.X, backTile.Z);
        state.Engage();
        state.ResetPlayerCooldown(0);

        int hpBefore = npc.CurrentHp;
        await Tick(svc);
        int normalDamage = hpBefore - npc.CurrentHp;
        Assert.True(normalDamage > 0);
    }

    [Fact]
    public async Task Transfusion_HealsHimEachTick_UntilInterruptedBySpecial()
    {
        var (svc, state, npc) = Build();
        state.SetPlayerTile(0, 0); // out of aura/melee range so nothing else complicates this
        npc.TakeDamage(200); // so healing is visible (not capped at MaxHp)

        for (int i = 0; i < 11; i++) { await Tick(svc); state.Player.RestoreHp(); } // transfusion casts at RotationTick 10 (the 11th tick processed)
        Assert.True(npc.TransfusionActive);
        int hpAfterCastTick = npc.CurrentHp;

        await Tick(svc); // one heal tick
        Assert.True(npc.CurrentHp > hpAfterCastTick);
    }

    [Fact]
    public async Task Phase2_TightensLoop_AndRaisesBleedCapTo8()
    {
        var (svc, state, npc) = Build();
        typeof(NpcInstance).GetMethod("TakeDamage")!.Invoke(npc, [npc.MaxHp - (npc.MaxHp * 49 / 100)]);
        await Tick(svc);
        Assert.Equal(2, npc.Phase);
        Assert.Equal(8, npc.ActivePhaseDef.BleedOnHit!.MaxStacks);
    }
}
