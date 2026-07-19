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

    private sealed class StubTickSource : ITickSource
    {
        public long ElapsedMsIntoCurrentTick => 0;
        public void Reset() { }
        public Task WaitForNextTickAsync(CancellationToken ct) => Task.CompletedTask;
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
            new StubItemRepo(), new StubNpcRepo(), new StubEventBus(), new StubTickSource());
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
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatNpc));

        // Tick 2: adjacency reached — both attacks land this tick.
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
    public async Task MoveOrder_WalksThere_ThenHoldsPosition_NoAutoRetaliate_UntilReengaged()
    {
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
        // Arrived — or stopped adjacent when the chasing NPC sits on the tile.
        Assert.True(Math.Max(Math.Abs(state.PlayerTile.X - -4), Math.Abs(state.PlayerTile.Z - 4)) <= 1);
        Assert.True(state.HoldPosition);
        var held = state.PlayerTile;
        await Tick(svc);
        Assert.Equal(held, state.PlayerTile); // no auto-chase while holding

        // The enemy remains the target and keeps chasing — but even once it
        // catches up and stands adjacent, a held player does NOT retaliate.
        // The enemy is still the target throughout; only engagement changed.
        for (int i = 0; i < 8 && state.DistanceToNpc > 1; i++) await Tick(svc);
        Assert.Equal(1, state.DistanceToNpc);
        await Tick(svc);
        Assert.Equal(hitsBefore, Hitsplats(state, LogEntryKind.HitsplatPlayer)); // still no attack
        Assert.Equal(held, state.PlayerTile); // never moved — held throughout

        // Re-engage (click the enemy / a weapon / ATTACK) — attacking resumes
        // immediately since the enemy, still the target, is already adjacent.
        state.Engage();
        await Tick(svc);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > hitsBefore);
    }

    [Fact]
    public async Task Engage_ResumesChase()
    {
        var (svc, state) = Build(Tank(AttackType.Ranged)); // NPC stands off at spawn

        state.OrderMove(-4, 4);
        while (state.PlayerMoveTarget is not null) await Tick(svc);
        Assert.True(state.HoldPosition);

        state.Engage();
        Assert.False(state.HoldPosition);
        var before = state.DistanceToNpc;
        await Tick(svc);
        Assert.True(state.DistanceToNpc < before); // walking back in
    }

    [Fact]
    public void OrderMove_ClampsToArena()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Crush)));

        // Arena is a square (see GameState.InArena) — a diagonal order clamps
        // to the corner, not to a circle inscribed inside it.
        state.OrderMove(99, -99);
        var t = state.PlayerMoveTarget!.Value;
        Assert.True(Math.Abs(t.X) <= GameState.ArenaRadius);
        Assert.True(Math.Abs(t.Z) <= GameState.ArenaRadius);
        Assert.True(GameState.InArena(t));
    }

    // ── Pathfinding (straight line unless blocked, around solid obstacles) ──

    // Calls the private static NextStepToward so we get the exact tile-by-tile
    // path, independent of NPC turns / tick cadence.
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
            if (next == cur) break; // stuck
            cur = next;
            path.Add(cur);
        }
        return path;
    }

    // Mirror of GameTickService.BresenhamLine (diagonal steps allowed).
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

    [Fact]
    public void Path_ClearOfObstacles_IsStraightLine()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        var from = (0, 3); var goal = (4, 1); // off-axis, clear of the fixed obstacles
        Assert.DoesNotContain(Bresenham(from, goal), state.IsObstacle); // sanity: line is clear

        var path = WalkPath(state, from, goal, (99, 99));
        Assert.Equal(goal, path[^1]);
        Assert.Equal(Bresenham(from, goal), path); // exact straight line, no dogleg
    }

    [Fact]
    public void Path_BlockedByObstacle_RoutesAroundAndReaches()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        var obstacle = (2, 1);
        Assert.True(state.IsObstacle(obstacle)); // part of the fixed layout
        var from = (0, 1); var goal = (4, 1);     // straight line runs through (2,1)
        Assert.Contains(obstacle, Bresenham(from, goal));

        var path = WalkPath(state, from, goal, (99, 99));
        Assert.Equal(goal, path[^1]);                       // still arrives
        Assert.DoesNotContain(path, state.IsObstacle);      // never enters a solid tile
        Assert.Contains(path, t => t.Z != 1);               // deviated off the blocked line
    }

    [Fact]
    public void Path_NeverEntersOpponentTile()
    {
        var (_, state) = Build(Tank(AttackType.Crush));
        var opponent = (0, 0);
        var path = WalkPath(state, (0, 3), (0, -3), opponent); // opponent sits on the line
        Assert.DoesNotContain(opponent, path);
    }

    [Fact]
    public void OrderMove_OntoObstacle_SnapsToFreeTile()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Crush)));
        var obstacle = state.Obstacles.First();

        state.OrderMove(obstacle.X, obstacle.Z);
        var t = state.PlayerMoveTarget!.Value;
        Assert.False(state.IsObstacle(t));
        Assert.True(GameState.InArena(t));
    }

    [Fact]
    public void Obstacles_DoNotDisconnectArena()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        state.StartDuel(new NpcInstance(Tank(AttackType.Crush)));

        // Flood from the player spawn over free tiles; every free in-arena tile
        // must be reachable — no obstacle layout may wall a region off.
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
        state.FreezeEnemy(true);       // isolate the player's approach (NPC won't move)
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 4);     // directly in front, same column

        // Player auto-chases (not holding). It must close along x=0 — a straight
        // step in — never sidestepping to a diagonal flank tile.
        for (int i = 0; i < 6 && state.DistanceToNpc > 1; i++)
        {
            await Tick(svc);
            Assert.Equal(0, state.PlayerTile.X); // stayed straight in front
        }
        Assert.Equal(1, state.DistanceToNpc);    // reached adjacency
        Assert.Equal(0, state.PlayerTile.X);
    }

    [Fact]
    public async Task MeleeApproach_TwoTilesStraight_LiveEnemy_ClosesCardinal()
    {
        // The reported case: standing two tiles dead ahead and attacking must
        // walk one tile straight in — never a forward-then-diagonal dogleg —
        // with the NPC live and closing too.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 2);

        await Tick(svc);

        Assert.Equal(0, state.PlayerTile.X);     // no sidestep
        Assert.Equal(0, state.NpcTile.X);        // enemy stayed on the line too
        Assert.Equal(1, state.DistanceToNpc);    // cardinal adjacency reached
    }

    [Fact]
    public async Task Melee_FromDiagonal_SquaresUpToCardinal_NeverAttacksAcrossCorner()
    {
        // OSRS melee rule: diagonal (corner) tiles are OUT of melee range.
        // Starting Chebyshev-adjacent on the corner, the player must step to
        // a cardinal neighbour rather than swing across the diagonal.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.FreezeEnemy(true);
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(1, 1);

        Assert.False(state.InAttackRange(AttackRange.Melee)); // corner ≠ in range

        await Tick(svc);

        var manhattan = Math.Abs(state.PlayerTile.X) + Math.Abs(state.PlayerTile.Z);
        Assert.Equal(1, manhattan);                            // squared up N/S/E/W
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
        Assert.False(state.InAttackRange(AttackRange.Melee));  // diagonal excluded
        Assert.True(state.InAttackRange(AttackRange.Distant)); // ranged unaffected
        state.SetPlayerTile(0, 0 + AttackRange.Distant);
        Assert.True(state.InAttackRange(AttackRange.Distant));
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

    [Fact]
    public async Task MoveOrder_HoldsAfterArrival_NoAutoAttack_UntilReengaged()
    {
        // The enemy stays the target the whole time (1v1 — nothing else to
        // target) but a move order suspends attacking, and — unlike the old
        // "auto-retaliate once in range" behaviour — it STAYS suspended
        // after arrival even though the enemy is adjacent, until re-engaged.
        var (svc, state) = Build(Tank(AttackType.Crush));
        state.FreezeEnemy(true); // isolate: NPC never moves or swings back
        state.SetNpcTile(0, 0);
        state.SetPlayerTile(0, 1); // already cardinal-adjacent
        state.OrderMove(0, 1);     // click-to-move "onto" the same tile: arrives instantly

        await Tick(svc);

        Assert.True(state.HoldPosition);
        Assert.Null(state.PlayerMoveTarget);
        Assert.Equal(1, state.DistanceToNpc); // adjacent...
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer)); // ...but not attacking

        // Still holding a tick later — no attack just because time passed.
        await Tick(svc);
        Assert.Equal(0, Hitsplats(state, LogEntryKind.HitsplatPlayer));

        // Re-engage (what clicking the enemy / a weapon / ATTACK all do).
        state.Engage();
        await Tick(svc);

        Assert.False(state.HoldPosition);
        Assert.True(Hitsplats(state, LogEntryKind.HitsplatPlayer) > 0);
    }
}
