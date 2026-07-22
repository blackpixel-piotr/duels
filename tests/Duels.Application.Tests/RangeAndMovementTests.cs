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
    private sealed class AlwaysHitRandom : IRandomProvider
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

    // Beefy, unscripted (Script=null) dummy — exercises the generic mover and
    // player-vs-NPC range/pathfinding logic without a boss rotation.
    private static NpcTemplate Tank(AttackType style) =>
        new("dummy", "Dummy", "", new CombatStats(1, 1, 99, 500), [], DummyStyle: style);

    private static (GameTickService svc, GameState state) Build(NpcTemplate template)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        state.StartDuel(new NpcInstance(template));

        var damage = new DamageModel(new AlwaysHitRandom());
        var svc = new GameTickService(
            new InMemoryStateRepo(state), damage, new AlwaysHitRandom(),
            new StubItemRepo(), new StubEventBus(), new StubTickSource());
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

        // Tick 1: player runs 2 tiles, NPC walks 1 (closure 3) — no reach yet.
        await Tick(svc);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));

        // Tick 2: adjacency reached — but persistent-lock rule 2 ("moving on
        // a tick defers the attack, never cancels it") means the tick that
        // closes the gap doesn't also land the hit; the player was still
        // moving this same tick.
        await Tick(svc);
        Assert.Equal(1, state.DistanceToNpc);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));

        // Tick 3: the first fully-stationary tick in range — attack lands here.
        await Tick(svc);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > 0);
    }

    [Fact]
    public async Task RangedNpc_StandsGround_WhilePlayerWalksIn()
    {
        var (svc, state) = Build(Tank(AttackType.Ranged));

        await Tick(svc);

        // Ranged dummy stands its ground; the player walked.
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
    public async Task Chase_AlongAnAxis_SettlesCardinalAdjacent_NotDiagonal()
    {
        // Player due south of the NPC on the same column — a dead-straight
        // approach. ApproachSlot must not cut diagonal on the final step.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.SetPlayerTile(0, 5);
        state.SetNpcTile(0, -5);

        for (int i = 0; i < 15 && state.DistanceToNpc > 1; i++)
            await Tick(svc);

        Assert.Equal(1, state.DistanceToNpc);
        Assert.Equal(state.NpcTile.X, state.PlayerTile.X); // still the same column — no diagonal cut
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
    public async Task MoveOrder_NeverBreaksLock_AttacksResumeAutomaticallyOnRegainingRange()
    {
        // Persistent target lock (M1 revision): OrderMove is a pure
        // movement action — it never disengages. Walking away retires the
        // auto-chase-assist (no auto-drag-back, kiting works), but combat
        // resumes the moment range is regained, with no re-engage tap.
        var (svc, state) = Build(Tank(AttackType.Crush));

        // Walk-in first: get adjacent, then order a retreat to a far corner.
        for (int i = 0; i < 4; i++) await Tick(svc);
        Assert.Equal(1, state.DistanceToNpc);
        int hitsBefore = Hitsplats(state, LogEntryKind.HitsplatPlayer);

        state.OrderMove(-4, 4);
        while (state.PlayerMoveTarget is not null)
        {
            await Tick(svc);
            Assert.Equal(hitsBefore, Hitsplats(state, LogEntryKind.HitsplatPlayer)); // never attacks mid-walk
        }
        Assert.True(Math.Max(Math.Abs(state.PlayerTile.X - -4), Math.Abs(state.PlayerTile.Z - 4)) <= 1);
        Assert.True(state.Engaged); // lock persists through the whole walk
        var held = state.PlayerTile;
        await Tick(svc);
        Assert.Equal(held, state.PlayerTile); // no auto-chase after a manual walk — assist retired by OrderMove

        // The (non-stationary) dummy chases the held player on its own —
        // once it closes back to adjacency, attacks resume with NO
        // Engage() call anywhere: the lock was never broken, and the
        // player was stationary the whole time it happened.
        for (int i = 0; i < 8 && state.DistanceToNpc > 1; i++) await Tick(svc);
        Assert.Equal(1, state.DistanceToNpc);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > hitsBefore);
        Assert.Equal(held, state.PlayerTile); // the player itself never moved back — only the NPC did
    }

    [Fact]
    public async Task Engage_FromDisengaged_ResumesChase()
    {
        // Engage() is the only thing that (re-)arms the auto-chase-assist —
        // exercised here from a genuinely disengaged state (DisengageAtSpawn),
        // since OrderMove no longer produces one to resume from.
        var (svc, state) = Build(Tank(AttackType.Ranged)); // NPC stands off at spawn
        state.DisengageAtSpawn();
        Assert.False(state.Engaged);

        state.Engage();
        Assert.True(state.Engaged);
        var before = state.DistanceToNpc;
        await Tick(svc);
        Assert.True(state.DistanceToNpc < before); // walking back in
    }

    [Fact]
    public void OrderMove_ClampsToArena()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Crush)));

        state.OrderMove(99, -99);
        var t = state.PlayerMoveTarget!.Value;
        Assert.True(Math.Abs(t.X) <= GameState.ArenaRadius);
        Assert.True(Math.Abs(t.Z) <= GameState.ArenaRadius);
        Assert.True(GameState.InArena(t));
    }

    // ── Pathfinding (straight line unless blocked, around solid obstacles) ──

    private static (int X, int Z) NextStep(GameState state, (int X, int Z) from,
                                           (int X, int Z) goal, (int X, int Z) avoid)
    {
        var m = typeof(GameTickService).GetMethod("NextStepToward",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((int X, int Z))m.Invoke(null, [state, from, goal, avoid])!;
    }

    private static List<(int X, int Z)> WalkPath(GameState state, (int X, int Z) from,
                                                 (int X, int Z) goal, (int X, int Z) avoid)
    {
        var path = new List<(int X, int Z)> { from };
        var cur = from;
        for (int i = 0; i < 40 && cur != goal; i++)
        {
            var next = NextStep(state, cur, goal, avoid);
            if (next == cur) break;
            cur = next;
            path.Add(cur);
        }
        return path;
    }

    private static List<(int X, int Z)> Bresenham((int X, int Z) a, (int X, int Z) b)
    {
        var pts = new List<(int X, int Z)>();
        int x0 = a.X, z0 = a.Z, dx = Math.Abs(b.X - x0), dz = Math.Abs(b.Z - z0);
        int sx = x0 < b.X ? 1 : -1, sz = z0 < b.Z ? 1 : -1, err = dx - dz;
        while (true)
        {
            pts.Add((x0, z0));
            if (x0 == b.X && z0 == b.Z) break;
            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 < dx) { err += dx; z0 += sz; }
        }
        return pts;
    }

    // NOTE: M1's arena carries no obstacles (Maggot King's arena has none per
    // the Boss Bible) — ObstacleLayout is empty, so this suite only verifies
    // the straight-line case and the never-share-a-tile guarantee.
    [Fact]
    public void Path_IsStraightLine_WhenClear()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        var from = (0, 3); var goal = (4, 1);

        var path = WalkPath(state, from, goal, (99, 99));
        Assert.Equal(goal, path[^1]);
        Assert.Equal(Bresenham(from, goal), path);
    }

    [Fact]
    public void Path_NeverEntersOpponentTile()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        var opponent = (0, 0);
        var path = WalkPath(state, (0, 3), (0, -3), opponent);
        Assert.DoesNotContain(opponent, path);
    }

    [Fact]
    public void Obstacles_DoNotDisconnectArena()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Crush)));

        var start = state.PlayerTile;
        var seen = new HashSet<(int X, int Z)> { start };
        var q = new Queue<(int X, int Z)>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var n = (c.X + dx, c.Z + dz);
                    if (seen.Contains(n) || !GameState.InArena(n) || state.IsObstacle(n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
        }
        for (int x = -GameState.ArenaRadius; x <= GameState.ArenaRadius; x++)
            for (int z = -GameState.ArenaRadius; z <= GameState.ArenaRadius; z++)
            {
                var t = (x, z);
                if (GameState.InArena(t) && !state.IsObstacle(t))
                    Assert.Contains(t, seen);
            }
    }

    [Fact]
    public async Task MeleeApproach_StraightWhenAligned_NoDiagonalSidestep()
    {
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.FreezeEnemy(true); // isolate the player's approach (NPC won't move)
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 4);

        for (int i = 0; i < 6 && state.DistanceToNpc > 1; i++)
        {
            await Tick(svc);
            Assert.Equal(0, state.PlayerTile.X);
        }
        Assert.Equal(1, state.DistanceToNpc);
        Assert.Equal(0, state.PlayerTile.X);
    }

    [Fact]
    public async Task MeleeApproach_TwoTilesStraight_LiveEnemy_ClosesCardinal()
    {
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 2);

        await Tick(svc);

        Assert.Equal(0, state.PlayerTile.X);
        Assert.Equal(0, state.NpcTile.X);
        Assert.Equal(1, state.DistanceToNpc);
    }

    [Fact]
    public async Task Melee_FromDiagonal_SquaresUpToCardinal_NeverAttacksAcrossCorner()
    {
        // OSRS melee rule: diagonal (corner) tiles are OUT of melee range.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.FreezeEnemy(true);
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(1, 1);

        Assert.False(state.InAttackRange(AttackRange.Melee));

        await Tick(svc);

        var manhattan = Math.Abs(state.PlayerTile.X) + Math.Abs(state.PlayerTile.Z);
        Assert.Equal(1, manhattan);
        Assert.True(state.InAttackRange(AttackRange.Melee));
    }

    [Fact]
    public void MeleeRange_CardinalOnly_RangedKeepsChebyshev()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        state.SetNpcTile(0, 0);

        state.SetPlayerTile(0, 1);
        Assert.True(state.InAttackRange(AttackRange.Melee));
        state.SetPlayerTile(1, 1);
        Assert.False(state.InAttackRange(AttackRange.Melee));
        Assert.True(state.InAttackRange(AttackRange.Distant));
        state.SetPlayerTile(0, 0 + AttackRange.Distant);
        Assert.True(state.InAttackRange(AttackRange.Distant));
    }

    [Fact]
    public async Task MoveOrder_EvenATrivialOne_NeverBlocksTheAttackGate()
    {
        // Persistent target lock: OrderMove is purely a movement action, so
        // even ordering a "move" to the tile the player is already
        // standing on (a same-tick, zero-distance order — PlayerTile never
        // actually changes) doesn't block the very next attack. No
        // Engage() call anywhere in this test.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.FreezeEnemy(true);
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 1);
        state.OrderMove(0, 1);

        await Tick(svc);

        Assert.True(state.Engaged);
        Assert.Null(state.PlayerMoveTarget);
        Assert.Equal(1, state.DistanceToNpc);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > 0);
    }
}
