using Duels.Domain.ValueObjects;

namespace Duels.Application.AutoPlay;

/// <summary>
/// The autopilot behind the "Playtest Fight" button and the harness's
/// <c>auto</c> brain: a competent, boss-agnostic player that survives long
/// enough for the whole boss script to play out on screen. It plays with
/// whatever loadout you brought (it never force-swaps your weapon), and adapts
/// its spacing to that weapon's range.
///
/// Each tick it:
///  • prays the style of any incoming projectile (Range/Magic/Melee) so
///    prayable damage is negated,
///  • steps off any tile about to erupt, any settled pool, and any Pin line
///    (perpendicular) — capturing Perfect Dodges,
///  • keeps ranged/magic weapons at a comfortable kiting distance and walks
///    melee weapons into range (weaving a step back out after a hit to avoid
///    adjacency punishes like Tail Stab),
///  • otherwise holds and attacks, spending a special into any punish window.
///
/// It is deliberately not "perfect" for every boss — it's a watchable, honest
/// playthrough for analysing a script, not a speed-kill. (For Hive Matron the
/// kiting line is in fact near-perfect; see hive-matron-fixes-findings.md.)
/// </summary>
public sealed class AutoPlayBrain : IPlayerBrain
{
    public string Name => "autoplay";

    // Remembers the last tick we ended stationary-adjacent, so a melee weave
    // never stands adjacent two ticks running (that is what eats a Tail Stab).
    private int _lastStruckAdjacentTick = -99;

    public void Decide(SimContext ctx)
    {
        // 0) Prefer to kite: if a melee weapon is equipped but the bar has a
        //    ranged one, swap to it (safest way to watch a full boss script).
        //    The swap is consumed later this same tick, so we treat ourselves
        //    as ranged from here on.
        bool ranged = ctx.PrefersRanged;
        if (ctx.PreferredRangedWeaponId is { } rangedWeapon)
        {
            if (ctx.EquippedWeaponId != rangedWeapon) ctx.SwapWeapon(rangedWeapon);
            ranged = true;
        }

        // 1) Prayer: match the incoming projectile's style; otherwise keep what
        //    we have (don't drop a prayer to None between shots).
        if (ctx.IncomingProjectileStyle is { } style)
            ctx.Pray(ProtectionFor(style));

        // 2) Get off anything about to hurt (glob/pool), or off a Pin line.
        if ((ctx.StandingInDanger || ctx.StandingOnPinLine) && TryDodge(ctx))
            return;

        // 3) Spacing, by weapon type.
        if (ranged)
        {
            // Keep the boss at arm's length; also keeps clear of melee punishes.
            if (ctx.DistanceToBoss < 3 && TryStepAwayIfImproves(ctx))
                return;
        }
        else
        {
            // Melee weave: after a hit landed while adjacent, step out before a
            // second consecutive stationary-adjacent tick triggers Tail Stab.
            if (ctx.DistanceToBoss <= 1 && _lastStruckAdjacentTick == ctx.Tick - 1
                && TryStepAwayIfImproves(ctx))
                return;
            if (ctx.DistanceToBoss <= 1 && ctx.PlayerCanActThisTick)
                _lastStruckAdjacentTick = ctx.Tick; // we'll strike this tick
        }

        // 4) Hold and attack; dump a special into a punish window.
        if (ctx.BossInPunishWindow && ctx.PlayerCanActThisTick)
            ctx.QueueSpecial();
        ctx.HoldAndFight();
    }

    private static ProtectionPrayer ProtectionFor(AttackType style) => style switch
    {
        AttackType.Ranged => ProtectionPrayer.Range,
        AttackType.Magic => ProtectionPrayer.Magic,
        _ => ProtectionPrayer.Melee,
    };

    // Step to the safest reachable neighbour: off every danger/Pin tile,
    // preferring the one furthest from the boss.
    private static bool TryDodge(SimContext ctx)
    {
        var safe = Neighbours(ctx.PlayerTile)
            .Where(ctx.InArena)
            .Where(t => !ctx.IsDangerTile(t) && !ctx.PinLine.Contains(t))
            .OrderByDescending(t => ctx.Chebyshev(t, ctx.BossTile))
            .Cast<(int X, int Z)?>()
            .FirstOrDefault();
        if (safe is not { } tile) return false;
        ctx.MoveTo(tile);
        return true;
    }

    private static bool TryStepAwayIfImproves(SimContext ctx)
    {
        var back = Neighbours(ctx.PlayerTile)
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

    private static IEnumerable<(int X, int Z)> Neighbours((int X, int Z) t)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (dx != 0 || dz != 0)
                    yield return (t.X + dx, t.Z + dz);
    }
}
