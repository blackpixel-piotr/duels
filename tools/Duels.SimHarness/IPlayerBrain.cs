using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.SimHarness;

/// <summary>
/// Supplies the player's inputs for one tick. Called once per tick, BEFORE the
/// sim advances, exactly where a human's finger-taps would land in the input
/// buffer. A brain reads the observable fight state through <see cref="SimContext"/>
/// and issues the same commands the real UI dispatches (prayer, move, attack,
/// special, weapon swap) — nothing a real player couldn't do.
/// </summary>
public interface IPlayerBrain
{
    string Name { get; }
    void Decide(SimContext ctx);
}

/// <summary>
/// The read/write surface a brain sees each tick. Reads expose only what a
/// player could actually perceive on-screen (positions, HP, in-flight
/// projectiles, telegraphed marks). Writes go through the same GameState entry
/// points the Blazor UI uses.
/// </summary>
public sealed class SimContext
{
    private readonly GameState _state;
    public SimContext(GameState state) => _state = state;

    // ── perception ──────────────────────────────────────────────────────────
    public int Tick => _state.FightTicks;
    public Player Player => _state.Player;
    public NpcInstance Boss => _state.ActiveNpc!;
    public (int X, int Z) PlayerTile => _state.PlayerTile;
    public (int X, int Z) BossTile => _state.NpcTile;
    public int ArenaRadius => _state.ArenaRadius;
    public int DistanceToBoss => _state.DistanceToNpc;
    public bool PlayerCanActThisTick => _state.PlayerCooldown == 0;
    public ProtectionPrayer ActivePrayer => _state.Player.ActiveProtection;

    /// <summary>The style of the nearest in-flight boss projectile, if any —
    /// the readable "what should I pray?" cue (a real player reads the
    /// doctrine-colored projectile in the air).</summary>
    public AttackType? IncomingProjectileStyle =>
        _state.Projectiles.Count == 0 ? null : _state.Projectiles[0].Attack.Style;

    public bool IncomingProjectile => _state.Projectiles.Count > 0;

    /// <summary>Tiles marked by a Sting Lob glob about to land (Warning-state
    /// hazard fuses). Standing on one when it erupts takes the hit.</summary>
    public IReadOnlyList<(int X, int Z)> WarnedHazardTiles =>
        _state.Hazards.Where(h => h.State == HazardState.Warning).Select(h => (h.X, h.Z)).ToList();

    /// <summary>Tiles currently holding a settled venom/poison pool — they
    /// keep hurting for their whole duration, so a dodge must not land on one.</summary>
    public IReadOnlyList<(int X, int Z)> PoolTiles =>
        _state.Hazards.Where(h => h.State == HazardState.Pool).Select(h => (h.X, h.Z)).ToList();

    /// <summary>Every tile that will hurt if you end a tick on it — an
    /// about-to-land glob OR a settled pool. The dodge target must avoid all.</summary>
    public bool IsDangerTile((int X, int Z) t) => WarnedHazardTiles.Contains(t) || PoolTiles.Contains(t);

    public bool StandingInDanger => IsDangerTile(PlayerTile);

    /// <summary>The tiles Pin will charge through, if a Pin is currently
    /// marked. Empty when no Pin is pending.</summary>
    public IReadOnlyList<(int X, int Z)> PinLine =>
        Boss.LineChargeTiles ?? (IReadOnlyList<(int X, int Z)>)Array.Empty<(int X, int Z)>();

    public bool PinIncoming => Boss.LineChargeTiles is { Count: > 0 };
    public bool StandingOnPinLine => PinLine.Contains(PlayerTile);

    /// <summary>True while the boss is slumped and cannot act — the punish
    /// window a perfect player pours damage into.</summary>
    public bool BossInPunishWindow => Boss.InPunishWindow;
    public bool ChitinGuardActive => Boss.DamageReductionActive;

    public bool InArena((int X, int Z) t) => _state.InArena(t);
    public int Chebyshev((int X, int Z) a, (int X, int Z) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));

    // ── commands ─────────────────────────────────────────────────────────────

    /// <summary>Hold (or drop) a protection prayer. Idempotent — safe to call
    /// with the same value every tick.</summary>
    public void Pray(ProtectionPrayer prayer)
    {
        if (_state.Player.ActiveProtection != prayer)
            _state.Player.ToggleProtection(prayer);
    }

    /// <summary>Lock onto the boss and let the auto-chase/auto-attack engine
    /// run this tick (the player attacks if in range, stationary, off
    /// cooldown). Cancels any pending move — call this on ticks you want to
    /// attack, not dodge.</summary>
    public void HoldAndFight() => _state.Engage();

    /// <summary>Walk toward a tile this tick (2 tiles/tick). Suppresses the
    /// attack this tick (you can't swing mid-step) — the dodge primitive.</summary>
    public void MoveTo((int X, int Z) tile) => _state.OrderMove(tile.X, tile.Z);

    /// <summary>Queue a special attack for this tick's swing (consumed by the
    /// attack gate if in range + off cooldown + enough energy).</summary>
    public void QueueSpecial() => _state.SetQueuedAction("spec");

    /// <summary>Request a weapon swap; it takes effect at the start of next
    /// tick (one swap per tick, same gate the UI uses).</summary>
    public void SwapWeapon(string weaponId)
    {
        if (_state.Player.GetEquippedWeaponId() != weaponId)
            _state.SetPendingWeaponSwap(weaponId);
    }

    public string? EquippedWeaponId => _state.Player.GetEquippedWeaponId();
}
