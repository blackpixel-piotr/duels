using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Application.GameSession;

public sealed class GameState
{
    public string PlayerId { get; }
    public Player Player { get; }
    public NpcInstance? ActiveNpc { get; private set; }
    public bool InDuel => ActiveNpc is { IsAlive: true };
    public string? LastOpponentId { get; private set; }
    public List<CombatLogEntry> CombatLog { get; } = new();

    // Tick engine
    public int PlayerCooldown { get; private set; }
    public int NpcCooldown { get; private set; }
    public string? QueuedAction { get; private set; }
    public ProtectionPrayer TickStartProtection { get; set; }

    // Prayer drain cadence (playtest revision, twice now: original was
    // 2 pts/tick protection, 1 pt/tick boost, drained every tick. First cut
    // moved that to every 3rd tick — a third of the original rate. Second
    // cut ("3 times less" again, on top of the first) moved it to every 9th
    // tick — a ninth of the original rate overall. Same per-event amounts
    // throughout (2 / 1); only the cadence changes, so every drain event is
    // still a whole number of points, no fractional drift. Independent
    // counters since protection and boost can be toggled independently.
    private const int PrayerDrainCadenceTicks = 9;
    public int ProtectionDrainTickCounter { get; private set; }
    public int BoostDrainTickCounter { get; private set; }
    public bool TickProtectionDrainDue()
    {
        ProtectionDrainTickCounter++;
        if (ProtectionDrainTickCounter < PrayerDrainCadenceTicks) return false;
        ProtectionDrainTickCounter = 0;
        return true;
    }
    public bool TickBoostDrainDue()
    {
        BoostDrainTickCounter++;
        if (BoostDrainTickCounter < PrayerDrainCadenceTicks) return false;
        BoostDrainTickCounter = 0;
        return true;
    }

    // Arena positions (duel-scoped). Fixed 9×9 arena (Boss Bible: Maggot
    // King) — M1 ships one boss, so the radius isn't per-duel data yet.
    public const int ArenaRadius = 4;
    public (int X, int Z) PlayerTile { get; private set; }
    public (int X, int Z) NpcTile { get; private set; }

    // Boss footprint (Boss Bible: the King is 2×2 and pivots, doesn't walk).
    // NpcTile is the footprint's anchor (min X, min Z) corner.
    public (int Width, int Height) NpcFootprint { get; private set; } = (1, 1);
    public bool NpcStationary { get; private set; }

    public IEnumerable<(int X, int Z)> NpcFootprintTiles()
    {
        for (int dx = 0; dx < NpcFootprint.Width; dx++)
            for (int dz = 0; dz < NpcFootprint.Height; dz++)
                yield return (NpcTile.X + dx, NpcTile.Z + dz);
    }

    // Solid obstacles (duel-scoped): walkable tiles that block movement.
    // Empty for M1's only boss (Maggot King's arena has none per the Boss
    // Bible) — kept as reusable pathfinding infrastructure for future bosses
    // (e.g. Millstone Golem's player-built rubble walls).
    private static readonly (int X, int Z)[] ObstacleLayout = Array.Empty<(int, int)>();
    private readonly HashSet<(int X, int Z)> _obstacles = new();
    public IReadOnlyCollection<(int X, int Z)> Obstacles => _obstacles;
    public bool IsObstacle((int X, int Z) tile) => _obstacles.Contains(tile);

    /// <summary>A tile a mover may not enter: a solid obstacle or the given
    /// occupant (the opponent's tile, so combatants never stack).</summary>
    public bool IsBlocked((int X, int Z) tile, (int X, int Z) occupant) =>
        tile == occupant || _obstacles.Contains(tile);

    // Tile hazards v2 (m1-plan Workstream C.4): warning fuse -> pool -> scorch
    // (permanent, walkable, safe — and the Rot Burst's safe tile). Hazards
    // never block pathing — walking THROUGH danger is allowed, only ENDING
    // the tick there costs you.
    private readonly List<HazardTile> _hazards = new();
    public IReadOnlyList<HazardTile> Hazards => _hazards;
    public bool IsPool((int X, int Z) tile) => _hazards.Any(h => h.State == HazardState.Pool && (h.X, h.Z) == tile);
    public bool IsScorch((int X, int Z) tile) => _hazards.Any(h => h.State == HazardState.Scorch && (h.X, h.Z) == tile);

    /// <summary>Tiles whose warning fuse will expire (erupt) THIS tick — read
    /// BEFORE movement/TickHazards to detect Perfect Dodge (vacating on the
    /// final fuse tick).</summary>
    public List<(int X, int Z)> TilesErupting() =>
        _hazards.Where(h => h.State == HazardState.Warning && h.TicksLeft == 1).Select(h => (h.X, h.Z)).ToList();

    public void AddHazardWave(IEnumerable<(int X, int Z)> tiles, int warningTicks, int poolTicks, int scorchTicks = -1)
    {
        foreach (var t in tiles)
            if (!_hazards.Any(h => (h.X, h.Z) == t))
                _hazards.Add(new HazardTile(t.X, t.Z, HazardState.Warning, warningTicks, poolTicks, scorchTicks));
    }

    // Concurrent-pool cap (master-script board economy). Default: no cap (P1).
    // The master-script P2 sets 8, converting the oldest excess pool to scorch
    // early rather than letting the floor fill without bound.
    private int _poolCap = int.MaxValue;
    public void SetPoolCap(int cap) => _poolCap = cap;

    /// <summary>Advance all hazards one tick. Warning fuses that hit zero erupt
    /// into pools (returned so the caller can damage whoever stands there);
    /// pools that dry out become scorch; timed scorch counts down and reverts
    /// to clean floor (permanent scorch — ScorchDurationTicks &lt; 0 — never
    /// does). Enforces the concurrent-pool cap after transitions.</summary>
    public List<(int X, int Z)> TickHazards()
    {
        var erupted = new List<(int X, int Z)>();
        for (int i = _hazards.Count - 1; i >= 0; i--)
        {
            var h = _hazards[i];
            if (h.State == HazardState.Scorch)
            {
                if (h.ScorchDurationTicks < 0) continue; // permanent
                var s = h with { TicksLeft = h.TicksLeft - 1 };
                if (s.TicksLeft <= 0) _hazards.RemoveAt(i); // reverts to clean floor
                else _hazards[i] = s;
                continue;
            }

            var next = h with { TicksLeft = h.TicksLeft - 1 };
            if (next.TicksLeft > 0) { _hazards[i] = next; continue; }

            if (h.State == HazardState.Warning)
            {
                erupted.Add((h.X, h.Z));
                _hazards[i] = h with { State = HazardState.Pool, TicksLeft = h.PoolDurationTicks };
            }
            else // Pool dried out
            {
                _hazards[i] = h with { State = HazardState.Scorch, TicksLeft = h.ScorchDurationTicks < 0 ? 0 : h.ScorchDurationTicks };
            }
        }
        EnforcePoolCap();
        return erupted;
    }

    // Keep at most _poolCap concurrent pools: convert the oldest excess (least
    // TicksLeft remaining — created earliest) to scorch early.
    private void EnforcePoolCap()
    {
        if (_poolCap == int.MaxValue) return;
        var poolIdx = new List<int>();
        for (int i = 0; i < _hazards.Count; i++)
            if (_hazards[i].State == HazardState.Pool) poolIdx.Add(i);
        int excess = poolIdx.Count - _poolCap;
        if (excess <= 0) return;
        poolIdx.Sort((a, b) => _hazards[a].TicksLeft.CompareTo(_hazards[b].TicksLeft));
        for (int k = 0; k < excess; k++)
        {
            var h = _hazards[poolIdx[k]];
            _hazards[poolIdx[k]] = h with { State = HazardState.Scorch, TicksLeft = h.ScorchDurationTicks < 0 ? 0 : h.ScorchDurationTicks };
        }
    }

    /// <summary>Rot Burst inhale (master script): every active pool instantly
    /// dries into scorch, so safe ground is guaranteed and visible from the
    /// inhale's first tick.</summary>
    public void BurnPoolsToScorch()
    {
        for (int i = 0; i < _hazards.Count; i++)
            if (_hazards[i].State == HazardState.Pool)
            {
                var h = _hazards[i];
                _hazards[i] = h with { State = HazardState.Scorch, TicksLeft = h.ScorchDurationTicks < 0 ? 0 : h.ScorchDurationTicks };
            }
    }

    /// <summary>Clears only in-flight warning marks (used at the P2 transition's
    /// roar); pools and scorch on the ground persist.</summary>
    public void ClearHazardWarnings() => _hazards.RemoveAll(h => h.State == HazardState.Warning);

    private void ClearHazards() => _hazards.Clear();

    // In-flight boss attacks (Boss Bible "Global Combat Grammar" — simulated
    // homing projectiles, sim-authoritative position, not a fixed tick
    // countdown): a Ranged/Magic attack travels toward the player's LIVE
    // tile every tick at its own ProjectileSpeedTiles, so a protection
    // prayer raised any time before it actually arrives — not just at cast —
    // still blocks it, and flight time scales with cast-time distance
    // instead of being a fixed constant. Melee never uses this; it has no
    // travel time, so cast tick == impact tick already.
    private readonly List<InFlightProjectile> _projectiles = new();
    private int _nextProjectileId;
    public IReadOnlyList<InFlightProjectile> Projectiles => _projectiles;

    public void SpawnProjectile((int X, int Z) spawnTile, BossAttackDef attack)
    {
        var id = $"proj_{FightTicks}_{_nextProjectileId++}";
        _projectiles.Add(new InFlightProjectile(id, spawnTile.X, spawnTile.Z, attack));
#if DEBUG
        // Diagnostic for "phantom projectile at style-switch" bug report:
        // this is the ONLY legal backend spawn path (SpawnProjectileAttack,
        // the sole production caller) — every real hit traces back to a
        // line here. If a player ever sees a projectile with no matching
        // [PROJ][spawn] line, it did not come from the sim.
        Console.WriteLine($"[PROJ][spawn] id={id} tick={FightTicks} phase={ActiveNpc?.Phase} rotTick={ActiveNpc?.RotationTick} style={attack.Style} attackId={attack.Id} spawnTile=({spawnTile.X},{spawnTile.Z})");
#endif
    }

    /// <summary>Advance every in-flight projectile one tick toward
    /// targetTile (the player's tile, already updated for THIS tick by the
    /// time this runs); returns the attacks that arrived (Euclidean distance
    /// closed to within their own speed) for the caller to resolve against
    /// this tick's fresh <see cref="TickStartProtection"/>. A projectile
    /// spawned this same tick is never in this list — cast happens later in
    /// ProcessTick than this call runs — which is what guarantees at least
    /// one tick of flight even for a point-blank cast.</summary>
    public List<BossAttackDef> AdvanceProjectiles((int X, int Z) targetTile)
    {
        var impacting = new List<BossAttackDef>();
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var p = _projectiles[i];
            double dx = targetTile.X - p.X, dz = targetTile.Z - p.Z;
            double dist = Math.Sqrt(dx * dx + dz * dz);
            double speed = p.Attack.ProjectileSpeedTiles;
            if (dist <= speed)
            {
#if DEBUG
                Console.WriteLine($"[PROJ][arrive] id={p.Id} tick={FightTicks} style={p.Attack.Style} attackId={p.Attack.Id} dist={dist:F2}");
#endif
                impacting.Add(p.Attack);
                _projectiles.RemoveAt(i);
            }
            else
            {
                p.MoveTo(p.X + dx / dist * speed, p.Z + dz / dist * speed);
            }
        }
        return impacting;
    }

    public void ClearProjectiles() => _projectiles.Clear();

    // Swarm adds (m1-plan Workstream C.7)
    private readonly List<AddInstance> _adds = new();
    public IReadOnlyList<AddInstance> Adds => _adds;
    public void SpawnAdd(AddInstance add) => _adds.Add(add);
    public void RemoveDeadAdds() => _adds.RemoveAll(a => !a.IsAlive);

    /// <summary>Current attack target: null = the boss (default). Tapping an
    /// add switches it; killing the current add target reverts to the boss.</summary>
    public string? TargetId { get; private set; }
    public void SetTarget(string? addId) => TargetId = addId;
    public AddInstance? CurrentTargetAdd => TargetId is null ? null : _adds.FirstOrDefault(a => a.Id == TargetId && a.IsAlive);

    /// <summary>True when a tile is inside the walkable arena square.</summary>
    public static bool InArena((int X, int Z) t) =>
        Math.Abs(t.X) <= ArenaRadius && Math.Abs(t.Z) <= ArenaRadius;

    /// <summary>Chebyshev distance to the NEAREST boss footprint tile.</summary>
    public int DistanceToNpc =>
        NpcFootprintTiles().Min(t => Math.Max(Math.Abs(PlayerTile.X - t.X), Math.Abs(PlayerTile.Z - t.Z)));

    /// <summary>OSRS melee rule: attacks land only from a CARDINAL neighbour
    /// tile of any footprint tile — never across a diagonal. Longer ranges stay Chebyshev.</summary>
    public bool InAttackRange(int range) =>
        range <= AttackRange.Melee
            ? NpcFootprintTiles().Any(t => Math.Abs(PlayerTile.X - t.X) + Math.Abs(PlayerTile.Z - t.Z) == 1)
            : DistanceToNpc <= range;

    public void SetPlayerTile(int x, int z) => PlayerTile = (x, z);
    public void SetNpcTile(int x, int z) => NpcTile = (x, z);

    // Targeting: persistent target lock (M1 revision — "persistent target
    // lock"). Two independent flags replace the old single HoldPosition:
    //   Engaged — the actual lock. Drives the attack gate. Movement NEVER
    //     touches it; only an explicit Engage()/Disengage() does (tapping
    //     the boss re-engages, tapping the engagement indicator disengages
    //     — UI bible §3.3/§3's reticle+sheathed element). This is what lets
    //     the player walk anywhere — including out of melee range to dodge
    //     — without losing the fight.
    //   EngageApproachActive — a movement-only convenience, unrelated to
    //     the lock: while true, ProcessPlayerMovement auto-walks the player
    //     into range of the current target every tick it's out of range
    //     (tracking a moving NPC/add live). OrderMove retires it
    //     immediately (a deliberate tap-to-move always wins over the
    //     auto-walk), so it only re-arms on the NEXT explicit Engage() —
    //     never automatically — which is what makes kiting work: walking
    //     away and stopping just leaves the player standing there, locked,
    //     not auto-dragged back into range.
    public (int X, int Z)? PlayerMoveTarget { get; private set; }
    public bool Engaged { get; private set; }
    public bool EngageApproachActive { get; private set; }

    public void OrderMove(int x, int z)
    {
        x = Math.Clamp(x, -ArenaRadius, ArenaRadius);
        z = Math.Clamp(z, -ArenaRadius, ArenaRadius);
        (x, z) = NearestFreeTile((x, z));
        PlayerMoveTarget = (x, z);
        EngageApproachActive = false;
    }

    public void ClearMoveOrder() => PlayerMoveTarget = null;

    private (int X, int Z) NearestFreeTile((int X, int Z) t)
    {
        if (!_obstacles.Contains(t)) return t;
        for (int r = 1; r <= ArenaRadius * 2; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    var c = (X: t.X + dx, Z: t.Z + dz);
                    if (InArena(c) && !_obstacles.Contains(c)) return c;
                }
        return t;
    }

    public void Engage()
    {
        PlayerMoveTarget = null;
        Engaged = true;
        EngageApproachActive = true;
    }

    // Explicit disengage (M1 revision — "persistent target lock"): the ONLY
    // other action, besides Engage(), that changes the lock. Tapping the
    // engagement indicator (UI bible §3.3/§3) dispatches this.
    public void Disengage()
    {
        Engaged = false;
        EngageApproachActive = false;
    }

    // Test-only: overrides StartDuel's production default (engaged from
    // tick 1) so a test can position the player deterministically before
    // anything auto-fires.
    public void DisengageAtSpawn() => Disengage();

    public bool TestScene { get; private set; }
    public void SetTestScene(bool on) => TestScene = on;

    public bool EnemyFrozen { get; private set; }
    public void FreezeEnemy(bool frozen) => EnemyFrozen = frozen;

    // Dev-only per-mechanic toggles (M1 playtest tooling). Everything live by
    // default; deliberately NOT reset in StartDuel so a playtester's choice
    // survives retries. Each flag gates one mechanic's processing this tick.
    public BossMechanic EnabledMechanics { get; private set; } = BossMechanic.All;
    public bool IsMechanicEnabled(BossMechanic m) => (EnabledMechanics & m) == m;
    public void ToggleMechanic(BossMechanic m) => EnabledMechanics ^= m;

    // Damage-over-time (duel-scoped). Reused for both Rend's bleed and
    // Scorch's burn (m1-plan Workstream B: "deliberately simplified" single
    // DoT track for M1 — a second application refreshes rather than stacks).
    public int BleedTicksLeft { get; private set; }
    public int BleedPerTick { get; private set; }
    public bool PlayerPoisoned { get; private set; }
    public int PoisonTickCounter { get; private set; }

    public int DamageTakenThisDuel { get; private set; }

    // Fight stats (m1-plan Workstream C.10 / H)
    public int FightTicks { get; private set; }
    public string? KilledBy { get; private set; }
    public void SetKilledBy(string description) => KilledBy = description;

    public DuelSummary? LastDuelSummary { get; private set; }

    public GameState(string playerId, Player player)
    {
        PlayerId = playerId;
        Player = player;
    }

    public void StartDuel(NpcInstance npc)
    {
        LastOpponentId = npc.Template.Id;
        ActiveNpc = npc;
        CombatLog.Clear();
        Player.RestorePrayer();
        Player.RestoreSpecialEnergy();
        Player.FlaskBelt.RefillForDuel(Player.Loadout);
        InitDuelCooldowns();
        ClearDots();
        DamageTakenThisDuel = 0;
        FightTicks = 0;
        KilledBy = null;
        TargetId = null;
        _adds.Clear();
        ClearProjectiles();
        _poolCap = int.MaxValue; // master-script P2 raises this on phase entry
        ProtectionDrainTickCounter = 0;
        BoostDrainTickCounter = 0;

        var script = npc.Template.Script;
        NpcFootprint = script?.Footprint is { } fp ? (fp.Width, fp.Height) : (1, 1);
        NpcStationary = script?.Stationary ?? false;

        // Opposite ends of the arena; the boss anchors center-north on its mound.
        PlayerTile = (0, 3);
        NpcTile = NpcStationary ? (-(NpcFootprint.Width / 2), -ArenaRadius) : (1, -3);

        _obstacles.Clear();
        foreach (var o in ObstacleLayout)
            if (o != PlayerTile && !NpcFootprintTiles().Contains(o))
                _obstacles.Add(o);

        PlayerMoveTarget = null;
        Engaged = true;
        EngageApproachActive = true;
        TestScene = false;
        EnemyFrozen = false;
        ClearHazards();
        WeaponSwapClaimedThisTick = false;
        PendingWeaponSwapId = null;
    }

    public void TickFight() => FightTicks++;

    public void RecordDamageTaken(int amount) { if (amount > 0) DamageTakenThisDuel += amount; }
    public void SetDuelSummary(DuelSummary summary) => LastDuelSummary = summary;

    public void ApplyBleed(int ticks, int perTick) { BleedTicksLeft = ticks; BleedPerTick = perTick; }
    public void TickBleed() { if (BleedTicksLeft > 0) BleedTicksLeft--; }
    public void ApplyPoison() { PlayerPoisoned = true; PoisonTickCounter = 0; }
    public void CurePoison() { PlayerPoisoned = false; PoisonTickCounter = 0; }
    public bool TickPoison()
    {
        if (!PlayerPoisoned) return false;
        PoisonTickCounter++;
        if (PoisonTickCounter < 4) return false;
        PoisonTickCounter = 0;
        return true;
    }
    private void ClearDots()
    {
        BleedTicksLeft = 0;
        BleedPerTick = 0;
        PlayerPoisoned = false;
        PoisonTickCounter = 0;
    }

    public void InitDuelCooldowns()
    {
        PlayerCooldown = 0;
        NpcCooldown = 0;
    }

    public void SetQueuedAction(string? action) => QueuedAction = action;

    // Weapon-swap input buffer (UI bible §3.2): "max one swap per tick;
    // extra taps buffer" to the following tick.
    public bool WeaponSwapClaimedThisTick { get; private set; }
    public string? PendingWeaponSwapId { get; private set; }

    public bool TryClaimWeaponSwapSlot()
    {
        if (WeaponSwapClaimedThisTick) return false;
        WeaponSwapClaimedThisTick = true;
        return true;
    }

    public void SetPendingWeaponSwap(string weaponId) => PendingWeaponSwapId = weaponId;

    public string? ConsumePendingWeaponSwap()
    {
        var id = PendingWeaponSwapId;
        PendingWeaponSwapId = null;
        return id;
    }

    public void ResetWeaponSwapGate() => WeaponSwapClaimedThisTick = false;

    public void ResetPlayerCooldown(int ticks) => PlayerCooldown = ticks;
    public void DelayPlayerAttack(int ticks) { if (ticks > 0) PlayerCooldown += ticks; }
    public void ResetNpcCooldown(int ticks) => NpcCooldown = ticks;

    public void DecrementCooldowns()
    {
        if (PlayerCooldown > 0) PlayerCooldown--;
        if (NpcCooldown > 0) NpcCooldown--;
    }

    public void EndDuel()
    {
        ActiveNpc = null;
        ClearDots();
        ClearHazards();
        _adds.Clear();
        ClearProjectiles();
    }

    public void AppendLog(string message, LogEntryKind kind = LogEntryKind.Info)
    {
        CombatLog.Add(new CombatLogEntry(message, kind, DateTimeOffset.UtcNow));
        if (CombatLog.Count > 500)
            CombatLog.RemoveAt(0);
    }
}

public sealed record CombatLogEntry(string Message, LogEntryKind Kind, DateTimeOffset Timestamp);

public enum HazardState { Warning, Pool, Scorch }

/// <summary>One hazard tile: Warning counts down its fuse, Pool counts down
/// until it dries into Scorch, which then counts down until it reverts to clean
/// floor. PoolDurationTicks / ScorchDurationTicks are captured at creation so a
/// state transition doesn't need the caller to repass them. ScorchDurationTicks
/// &lt; 0 means permanent scorch (P1's original behavior; the master-script P2
/// passes a finite lifetime).</summary>
public readonly record struct HazardTile(int X, int Z, HazardState State, int TicksLeft, int PoolDurationTicks, int ScorchDurationTicks = -1);

/// <summary>Snapshot of a finished duel for the end-of-fight result overlay
/// (m1-plan Workstream H).</summary>
public sealed record DuelSummary(
    bool Won,
    string NpcId,
    string NpcName,
    int KillTimeTicks,
    string? KilledBy,
    bool PersonalBest,
    bool Flawless,
    int GoldGained,
    IReadOnlyList<string> LootItemIds);

public enum LogEntryKind
{
    Info,
    PlayerHit,
    PlayerMiss,
    NpcHit,
    NpcMiss,
    System,
    Loot,
    MaxHit,
    SpecHit,
    Prayer,
    BossSpecial,
    HitsplatPlayer,
    HitsplatNpc,
    BossCast,
    // Discontinuous-movement markers (renderer interpolation layer): a
    // one-tick signal that this tick's tile change should SNAP on-screen,
    // not lerp — the sim itself doesn't distinguish "a step" from "a jump"
    // beyond distance, so the mechanic that causes the jump (currently only
    // Lunge) is the one that knows and logs it. NpcTeleport has no trigger
    // yet in M1 (Maggot King is stationary) — kept for a future
    // dash/knockback mechanic so the renderer-side plumbing stays symmetric
    // per entity rather than needing a second wiring pass later.
    PlayerTeleport,
    NpcTeleport,
}
