using Duels.Application.AutoPlay;
using Duels.Domain.ValueObjects;

namespace Duels.SimHarness;

/// <summary>Does nothing — never prays, never moves, never attacks. Use it to
/// watch a boss's raw script play out against a stationary dummy (pure boss
/// behaviour, no player interference). Great for auditing telegraph cadence.</summary>
public sealed class IdleBrain : IPlayerBrain
{
    public string Name => "idle-dummy";
    public void Decide(SimContext ctx) { }
}

/// <summary>A deliberately mediocre player: stands at range, always prays
/// Range, never dodges the positional attacks. Shows what an *un*mastered
/// fight costs — the baseline the perfect run is measured against.</summary>
public sealed class NaiveRangedBrain : IPlayerBrain
{
    public string Name => "naive-ranged";
    public void Decide(SimContext ctx)
    {
        ctx.Pray(ProtectionPrayer.Range);
        ctx.HoldAndFight();
    }
}

/// <summary>Diagnostic brain: equips melee and tries to play "the weave" —
/// step in, hit, step out — to empirically test whether Hive Matron's signature
/// melee rhythm is actually executable in the current engine. Holds Range
/// against Dart Volley the whole time. Prints nothing special; read the trace.</summary>
public sealed class MeleeWeaveBrain : IPlayerBrain
{
    public string Name => "melee-weave-probe";
    private int _lastAdjacentTick = -99;

    public void Decide(SimContext ctx)
    {
        ctx.Pray(ProtectionPrayer.Range);
        ctx.SwapWeapon("wpn_melee_t2");

        // Dodge the positional threats first (glob/pool, Pin line, Needle Spit
        // "+"), stepping to a safe tile — a real melee player reads these too.
        if (ctx.StandingInDanger || ctx.StandingOnPinLine || ctx.StandingOnNeedleTile)
        {
            var safe = Neigh(ctx.PlayerTile).Where(ctx.InArena)
                .Where(t => !ctx.IsDangerTile(t) && !ctx.PinLine.Contains(t) && !ctx.NeedleTiles.Contains(t))
                .OrderBy(t => ctx.Chebyshev(t, ctx.BossTile)) // stay as close as we safely can
                .Cast<(int X, int Z)?>().FirstOrDefault();
            if (safe is { } s) { ctx.MoveTo(s); return; }
        }

        // Also step off a Pin line even when not currently standing on it isn't
        // needed — the dodge block above handles "standing on"; Pin is dodged
        // there. Here we run the weave.
        bool adjacent = ctx.DistanceToBoss <= 1;
        if (adjacent)
        {
            // Only ever be adjacent on the tick we actually strike. If we can't
            // swing this tick (on cooldown), step straight back out — lingering
            // adjacent is exactly what stacks the 2 ticks that answer with a
            // Tail Stab.
            if (ctx.PlayerCanActThisTick && _lastAdjacentTick != ctx.Tick - 1)
            {
                _lastAdjacentTick = ctx.Tick;
                ctx.HoldAndFight(); // land the weave hit this tick
                return;
            }
            var away = Neigh(ctx.PlayerTile).Where(ctx.InArena)
                .Where(t => !ctx.IsDangerTile(t) && !ctx.NeedleTiles.Contains(t) && !ctx.PinLine.Contains(t))
                .OrderByDescending(t => ctx.Chebyshev(t, ctx.BossTile))
                .Cast<(int X, int Z)?>().FirstOrDefault();
            if (away is { } a) { ctx.MoveTo(a); return; }
        }

        // Not adjacent: close in only when we're ready to strike on arrival, so
        // we don't creep into melee range and sit there off-cooldown.
        if (ctx.PlayerCanActThisTick || ctx.DistanceToBoss > 2)
            ctx.HoldAndFight();
    }

    private static IEnumerable<(int X, int Z)> Neigh((int X, int Z) t)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dz != 0) yield return (t.X + dx, t.Z + dz);
    }
}

/// <summary>
/// A perfect Hive Matron run, played the way the Boss Bible §2 intends her to
/// be beaten cleanly — ranged spacing discipline:
///  • hold Range against the ever-present Dart Volley (full negation),
///  • sidestep Sting Lob globs and never step back into the venom pool,
///  • step perpendicular off every Pin line — baiting the wall-slam that
///    opens her 4-tick punish window — and pour a special into that window,
///  • keep firing straight through Chitin Guard (ranged at half value still
///    beats trading Tail Stabs — see the weave finding in m3 follow-up).
/// The result is a zero-avoidable-damage kill. (Only the venom DoT from the
/// very first, unavoidable-by-a-cold-opener glob ever touches the player.)
/// </summary>
public sealed class PerfectHiveMatronBrain : IPlayerBrain
{
    public string Name => "perfect-hive-matron";

    private const string Ranged = "wpn_ranged_t2";

    public void Decide(SimContext ctx)
    {
        // Dart Volley is the only prayable threat; hold Range the whole fight.
        ctx.Pray(ProtectionPrayer.Range);
        ctx.SwapWeapon(Ranged);

        // 1) Highest priority: vacate a tile that is about to hurt (glob about
        //    to land, or a settled venom pool), or a Pin line about to charge.
        if ((ctx.StandingInDanger || ctx.StandingOnPinLine) && TryDodge(ctx)) return;

        // 2) Keep honest spacing: if the boss has closed inside comfortable
        //    ranged distance, drift back out (also keeps us clear of Tail Stab).
        if (ctx.DistanceToBoss < 3 && TryBackOff(ctx)) return;

        // 3) Otherwise hold and fire. Dump a special into any punish window.
        if (ctx.BossInPunishWindow && ctx.PlayerCanActThisTick)
            ctx.QueueSpecial();
        ctx.HoldAndFight();
    }

    // Step to the safest reachable tile: off every danger/Pin tile, keeping
    // distance from the boss. Prefers a tile that also isn't a fresh pool.
    private static bool TryDodge(SimContext ctx)
    {
        var safe = NeighbourTiles(ctx.PlayerTile)
            .Where(ctx.InArena)
            .Where(t => !ctx.IsDangerTile(t) && !ctx.PinLine.Contains(t))
            .OrderByDescending(t => ctx.Chebyshev(t, ctx.BossTile))
            .Cast<(int X, int Z)?>()
            .FirstOrDefault();
        if (safe is not { } tile) return false;
        ctx.MoveTo(tile);
        return true;
    }

    private static bool TryBackOff(SimContext ctx)
    {
        var back = NeighbourTiles(ctx.PlayerTile)
            .Where(ctx.InArena)
            .Where(t => !ctx.IsDangerTile(t) && !ctx.PinLine.Contains(t))
            .OrderByDescending(t => ctx.Chebyshev(t, ctx.BossTile))
            .Cast<(int X, int Z)?>()
            .FirstOrDefault();
        if (back is not { } tile || ctx.Chebyshev(tile, ctx.BossTile) <= ctx.DistanceToBoss)
            return false;
        ctx.MoveTo(tile);
        return true;
    }

    private static IEnumerable<(int X, int Z)> NeighbourTiles((int X, int Z) t)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dz != 0)
                    yield return (t.X + dx, t.Z + dz);
    }
}
