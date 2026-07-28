using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

/// <summary>One entry in a boss's fixed-tick rotation loop. Action is one of:
/// "idle", "style_telegraph", a single attack id (key into
/// <see cref="BossScript.Attacks"/>), or two ids joined by "/" — resolved at
/// cast time to the first id if the player is melee-adjacent, else the
/// second (Boss Bible "Lash if adjacent / Grub Volley if not").</summary>
public sealed record RotationStep(int Tick, string Action);

/// <summary>One named boss attack. Damage is the top of the Boss Bible's damage
/// band (Light 5–10, Medium 15–20, Heavy 30–40, Severe 50–60); a standard auto
/// rolls 60–100% of it each cast (items doc §1). Boss attacks always land (no
/// accuracy roll) unless dodged positionally, then fully negated by a matching
/// protection prayer (unless Unprayable). Mechanic/hazard damage (eruptions,
/// Rot Burst) and DoTs are deterministic — they never roll a band.
/// ProjectileSpeedTiles only matters for Ranged/Magic (melee never travels as
/// a projectile — cast tick == impact tick already); 3.0 is the Global Combat
/// Grammar default, per-attack overridable for a future slow/fast special.</summary>
public sealed record BossAttackDef(string Id, string Name, AttackType Style, int Damage, bool Unprayable = false, double ProjectileSpeedTiles = 3.0);

/// <summary>A boss's per-style Evasion (items doc §1 / Global Combat Grammar):
/// percentage points subtracted from a player's hit chance for the doctrine
/// they're attacking with. Neutral (all zero) leaves the ~80% at-tier baseline
/// untouched; a positive value on one style is the "this boss favors ranged"
/// tuning lever, no mechanic changes required.</summary>
public sealed record NpcEvasion(double Melee = 0, double Ranged = 0, double Magic = 0)
{
    public static readonly NpcEvasion Zero = new();
}

/// <summary>Independent eruption-hazard timer (Boss Bible "Eruption"):
/// every CooldownTicks, TilesPerWave tiles (+ the player's tile) get a
/// WarningTicks fuse, then erupt for EruptDamage (unprayable), leaving a pool
/// that burns PoolDamagePerTick for PoolTicks before drying into permanent,
/// safe scorch.</summary>
public sealed record EruptionDef(int CooldownTicks, int WarningTicks, int TilesPerWave, int EruptDamage, int PoolTicks, int PoolDamagePerTick);

/// <summary>Maggot swarm wave, spawned once when the boss's HP crosses
/// ThresholdPercent (Boss Bible "Maggot swarms").</summary>
public sealed record SwarmWaveDef(int ThresholdPercent, int Count, int Hp);

/// <summary>Channeled arena-wide blast (Boss Bible "Rot Burst"): InhaleTicks
/// of cast time (cast bar UI), then Damage to everyone not standing on a
/// scorch tile (ignores prayer), then SlumpTicks of punish window on the
/// boss (+25% damage taken, cannot act). Fires roughly every CadenceTicks —
/// checked opportunistically after each rotation loop restart, never
/// mid-loop, so it can't interrupt a telegraphed style read.</summary>
public sealed record RotBurstDef(int CadenceTicks, int InhaleTicks, int Damage, int SlumpTicks);

/// <summary>One phase's full choreography (Boss Bible P1/P2).
/// <paramref name="TelegraphLeadTicks"/> is how many ticks ahead a
/// style-shift telegraph in this phase warns before the new style's first
/// hit — per the Global Combat Grammar's "Prayer grammar": 3 for a Tier-1
/// boss's baseline (introductory phase), 2 is standard, 1 is
/// invocation-tier. A later phase may deliberately tighten below the
/// boss's own tier baseline as an escalation (Maggot King's Phase 2 stays
/// at 2 even though Phase 1 is 3, per the Boss Bible's own Phase 2 note).</summary>
public sealed record BossPhaseDef(
    int LoopLength,
    IReadOnlyList<RotationStep> Rotation,
    EruptionDef Eruption,
    RotBurstDef? RotBurst = null,
    IReadOnlyList<SwarmWaveDef>? Swarms = null,
    int TelegraphLeadTicks = 2,
    // Master-script phase (Global Combat Grammar "Master-script rule"): the
    // whole phase runs on one fixed-tick clock with no independent mechanic
    // timers. When true, GameTickService drives it via ProcessMasterScript and
    // the fields below apply; the independent Eruption/RotBurst/Swarm timers are
    // skipped. Numbers still come from Eruption/RotBurst here.
    bool MasterScript = false,
    int PoolCap = int.MaxValue,      // max concurrent pools (oldest → scorch early)
    int ScorchTicks = -1,            // scorch lifetime before reverting to clean floor (<0 = permanent)
    int SwarmHp = 2,                 // per-swarm HP (P2 master script: 1)
    int SwarmMaxAlive = 2,           // cap on concurrent swarms
    int RotBurstEveryNCycles = 3,    // Rot Burst fires on every Nth master cycle
    IReadOnlyList<DroneWaveDef>? Drones = null, // Hive Matron's real-HP orbiting adds
    AttunementDef? Attunement = null, // Mirrorhide: per-phase (P2 tightens the window)
    BleedOnHitDef? BleedOnHit = null); // Bloodtithe: per-phase (P2 raises the stack cap)

/// <summary>Boss footprint in tiles (plain record, not a ValueTuple — System.Text.Json
/// has no built-in ValueTuple converter).</summary>
public sealed record FootprintDef(int Width, int Height);

// ── M3 Workstream A: shared systems generalized beyond Maggot King ────────
// These are additive, optional fields on BossScript/BossPhaseDef (all default
// null/off) rather than a rewrite of the M1 rotation-script shape — Maggot
// King's own script is untouched by any of them.

/// <summary>Boss movement AI beyond stationary/chase-to-melee (Hive Matron,
/// Boss Bible §2 "Core movement AI"): holds a preferred range band, dashing
/// DashDistanceTiles every DashEveryNAttacks landed attacks to reset spacing
/// against a player who's closed in.</summary>
public sealed record SpacingAiDef(int PreferredRangeMin, int PreferredRangeMax, int DashEveryNAttacks, int DashDistanceTiles);

/// <summary>Adjacency punish (Hive Matron's Tail Stab): staying within
/// Chebyshev 1 of the boss for AdjacencyTicks consecutive ticks answers with
/// an instant fixed-damage hit, knockback, and (optionally) a poison
/// application — the "melee must be danced, not held" teaching tool. Written
/// generically enough for any future boss's own face-tank punish.</summary>
public sealed record AdjacencyPunishDef(int AdjacencyTicks, int Damage, int KnockbackTiles, string? Name = null);

/// <summary>Periodic self-buff reducing incoming damage from the listed
/// styles by ReductionPercent for DurationTicks, every CadenceTicks (Hive
/// Matron's Chitin Guard: −50% ranged/magic for 8 ticks every ~25 ticks).</summary>
public sealed record DamageReductionWindowDef(int CadenceTicks, int DurationTicks, double ReductionPercent, IReadOnlyList<AttackType> AffectedStyles);

/// <summary>A wave of non-swarm adds (Hive Matron's drones): real HP (unlike
/// SwarmWaveDef's 1-HP fodder), station-keeping at OrbitRadius around the
/// boss rather than crawling toward the player.</summary>
public sealed record DroneWaveDef(int ThresholdPercent, int Count, int Hp, int OrbitRadius);

/// <summary>Pin's line-charge (Hive Matron, signature attack): marks a line
/// of tiles through the player's tile-at-cast, charges after WarningTicks —
/// a hit on the line is Damage + StunTicks pinned; a miss that reaches the
/// arena edge is a "hit the wall" punish window (MissPunishTicks). Perfect-
/// Dodge eligible (sidestep the line on its final warning tick).</summary>
public sealed record LineChargeDef(int WarningTicks, int Damage, int StunTicks, int MissPunishTicks, bool DoubleChainInPhase2 = false);

/// <summary>Mirrorhide's Attunement (Boss Bible §3, "Core mechanic"): after
/// being hit by the same style HitsToAttune times in a row, shimmers
/// (ShimmerTicks warning), then becomes immune to that style for
/// ImmuneTicks. A DIFFERENT style landed during the shimmer window Shatters
/// it instead — cancels the attunement and opens a ShatterWindowTicks
/// bonus-damage window (reuses the shared punish-window primitive).</summary>
public sealed record AttunementDef(int HitsToAttune, int ShimmerTicks, int ImmuneTicks, int ShatterWindowTicks);

/// <summary>Mirrorhide's periodic cloak: untargetable + repositions behind
/// the player for DurationTicks, every CadenceTicks. PounceOnEnd (Phase 2):
/// a single-tile mark on the player's tile as the cloak ends, 1-tick
/// warning, Perfect-Dodge eligible.</summary>
public sealed record CloakDef(int CadenceTicks, int DurationTicks, int PounceDamage, bool PounceOnEnd = false);

/// <summary>Mirrorhide's Reflection (signature): channels for ChannelTicks,
/// then reflects ReflectPercent of the player's own damage — when dealt in
/// the currently-attuned style — back at them for WindowTicks.</summary>
public sealed record ReflectDef(int ChannelTicks, int WindowTicks, double ReflectPercent);

/// <summary>Copycat (Mirrorhide, Phase 2): replays the player's last-used
/// special attack with boss numbers, TelegraphTicks ahead of landing, on a
/// CadenceTicks cooldown. Simplified to a single fixed-Damage hit rather
/// than porting each of the six specials' individual effects onto the boss
/// — see m3-findings.md's scoping note.</summary>
public sealed record CopycatDef(int TelegraphTicks, int Damage, int CadenceTicks);

/// <summary>Bloodtithe's facing + Tithe aura (Boss Bible §4): turns
/// DegreesPerTick toward the player (90 = a quarter-turn/tick); ending a
/// tick within AuraRadius of his front/sides drains DrainPercent of the
/// player's max HP as self-heal. His back tile is exempt from the aura and
/// takes BackDamageBonus extra player damage instead.</summary>
public sealed record FacingAuraDef(int DegreesPerTick, int AuraRadius, double DrainPercent, double BackDamageBonus);

/// <summary>One Font tile (Bloodtithe): standing on it purges all the
/// player's bleed stacks, then it's on CooldownTicks before it can be used
/// again (per-tile, independent cooldowns).</summary>
public sealed record FontTileDef(int X, int Z, int CooldownTicks);

/// <summary>Bleed-on-hit (Bloodtithe): every landed hit applies a stack
/// (Global Combat Grammar: "correctly prayed hits apply no stack"), each
/// stack dealing DamagePerTick for DurationTicks, capped at MaxStacks
/// (raised in Phase 2 — hence living on BossPhaseDef, not BossScript).</summary>
public sealed record BleedOnHitDef(int DamagePerTick, int DurationTicks, int MaxStacks);

/// <summary>Transfusion (Bloodtithe signature): a HealPercentPerTick-of-
/// max-HP channel for ChannelTicks, interrupted only by a player special
/// attack landing during it.</summary>
public sealed record TransfusionDef(int ChannelTicks, double HealPercentPerTick);

/// <summary>Crimson Pact (Bloodtithe, Phase 2): periodically sacrifices
/// SacrificePercent of his CURRENT HP for SpeedTicks of full (1-tile/tick)
/// movement speed, overriding BossScript.MovementTicksPerStep.</summary>
public sealed record CrimsonPactDef(int CadenceTicks, double SacrificePercent, int SpeedTicks);

/// <summary>Harvest (Bloodtithe, Phase 2 signature): TelegraphTicks warning,
/// then consumes every one of the player's current bleed stacks for
/// DamagePerStack each — countered by purging bleed at a Font first.</summary>
public sealed record HarvestDef(int TelegraphTicks, int DamagePerStack, int CadenceTicks);

/// <summary>Full boss-fight definition consumed by the shared boss engine
/// (m1-plan Workstream C) — the rotation script, hazards, swarms and Rot
/// Burst are all data here; GameTickService/BossEngine contain no
/// boss-specific branches. <see cref="ArenaRadius"/> and <see cref="Footprint"/>
/// describe the fixed 9×9 arena and the King's 2×2, pivot-only mound.</summary>
public sealed record BossScript(
    int PhaseTwoThresholdPercent,
    Dictionary<string, BossAttackDef> Attacks,
    BossPhaseDef Phase1,
    BossPhaseDef Phase2,
    int ArenaRadius,
    FootprintDef Footprint,
    bool Stationary,
    SpacingAiDef? SpacingAi = null,
    AdjacencyPunishDef? AdjacencyPunish = null,
    DamageReductionWindowDef? DamageReductionWindow = null,
    LineChargeDef? LineCharge = null,
    CloakDef? Cloak = null,
    ReflectDef? Reflect = null,
    CopycatDef? Copycat = null,
    int MovementTicksPerStep = 1, // Bloodtithe: 2 = "1 tile per 2 ticks"
    FacingAuraDef? FacingAura = null,
    IReadOnlyList<FontTileDef>? Fonts = null,
    TransfusionDef? Transfusion = null,
    CrimsonPactDef? CrimsonPact = null,
    HarvestDef? Harvest = null);

// DummyStyle: approach style for a non-scripted (Script=null) NPC's generic
// chase-to-range movement — the shared mover, not boss-specific code. Real M1
// content (the King) is always scripted and stationary; this exists for the
// pathfinding/movement test fixtures and any future non-boss mob.
public sealed record NpcTemplate(
    string Id,
    string Name,
    string ExamineText,
    CombatStats Stats,
    IReadOnlyList<LootEntry> LootTable,
    int GoldReward = 0,
    BossScript? Script = null,
    AttackType? DummyStyle = null,
    NpcEvasion? Evasion = null)
{
    public int MaxHp => Stats.Hitpoints;
}

/// <summary>One loot-table row. Ungrouped entries (<see cref="GroupId"/> null)
/// roll independently, exactly as before. Entries sharing a <see cref="GroupId"/>
/// form a two-stage roll (economy doc §5's "one roll on... Slot": Common 65%/
/// Uncommon 25% etc.) — the group's own chance (every member should carry the
/// same <see cref="DropChance"/>) is rolled ONCE, and only on a hit is exactly
/// one member picked, weighted by <see cref="Weight"/> (relative, not required
/// to sum to 1). <see cref="ItemId"/> == "gold" is a pseudo-item: RollLoot
/// grants MinQty..MaxQty gold directly instead of an inventory item (economy
/// doc §5's "Gold Cache").</summary>
public sealed record LootEntry(string ItemId, double DropChance, int MinQty = 1, int MaxQty = 1,
    bool OnceOnly = false, string? GroupId = null, double Weight = 1.0);

/// <summary>Distinguishes a swarm add's behavior (crawl straight at the
/// player, 1 HP, dies in one hit) from a drone's (M3, Hive Matron: real HP,
/// station-keeps at an orbit radius around the boss instead of closing).</summary>
public enum AddKind { Swarm, Drone }

/// <summary>One spawned add. Swarms (m1-plan Workstream C.7) crawl 1 tile/tick
/// toward the player; contact applies a bleed stack; dies in 1 hit (fixed low
/// HP — see SwarmWaveDef.Hp). Drones (M3, DroneWaveDef) instead orbit the
/// boss and take real weapon damage per hit.</summary>
public sealed class AddInstance
{
    public string Id { get; }
    public (int X, int Z) Tile { get; private set; }
    public int MaxHp { get; }
    public int CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0;
    public AddKind Kind { get; }
    public int OrbitRadius { get; }

    // Contact bleed is edge-triggered (bible: "contact applies 1 bleed
    // stack" — one stack per contact, not a continuous refresh): HasBitten
    // latches true the tick adjacency begins and blocks re-biting until the
    // add loses adjacency and regains it.
    public bool HasBitten { get; private set; }
    public void MarkBitten() => HasBitten = true;
    public void ResetBite() => HasBitten = false;

    public AddInstance(string id, (int X, int Z) tile, int hp, AddKind kind = AddKind.Swarm, int orbitRadius = 0)
    {
        Id = id;
        Tile = tile;
        MaxHp = hp;
        CurrentHp = hp;
        Kind = kind;
        OrbitRadius = orbitRadius;
    }

    public void MoveTo((int X, int Z) tile) => Tile = tile;
    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
}

/// <summary>A boss's Ranged/Magic attack in flight (Boss Bible "Global Combat
/// Grammar": simulated, homing, sim-authoritative position — not a fixed tick
/// countdown). Advances toward the player's live tile every tick at its
/// attack's ProjectileSpeedTiles; X/Z are fractional tile coordinates (not
/// the integer combatant-tile grid) so it can move at a real speed and feed
/// the renderer's smooth interpolation. Melee never creates one of these —
/// it resolves synchronously, cast tick == impact tick.</summary>
public sealed class InFlightProjectile
{
    public string Id { get; }
    public double X { get; private set; }
    public double Z { get; private set; }
    public BossAttackDef Attack { get; }

    public InFlightProjectile(string id, double x, double z, BossAttackDef attack)
    {
        Id = id;
        X = x;
        Z = z;
        Attack = attack;
    }

    public void MoveTo(double x, double z) { X = x; Z = z; }
}

public sealed class NpcInstance
{
    public NpcTemplate Template { get; }
    public int CurrentHp { get; private set; }
    public int MaxHp => Template.Stats.Hitpoints;
    public bool IsAlive => CurrentHp > 0;

    // Boss rotation-script cursor (m1-plan Workstream C.1)
    public int Phase { get; private set; } = 1;
    public int RotationTick { get; private set; }
    public BossPhaseDef ActivePhaseDef => Phase == 1 ? Template.Script!.Phase1 : Template.Script!.Phase2;

    // Master-script (P2) state: one fixed-tick clock, no independent timers.
    // CycleCount increments each LoopLength wrap; the every-Nth cycle is the Rot
    // Burst cycle. StyleA/B are rolled per cycle (B always differs from A).
    public int CycleCount { get; private set; }
    public int RoarTicksLeft { get; private set; } // phase-2 transition roar
    public string StyleAId { get; private set; } = "";
    public string StyleBId { get; private set; } = "";
    public bool UsesMasterScript => Template.Script is not null && ActivePhaseDef.MasterScript;
    public bool IsRotBurstCycle => ActivePhaseDef.RotBurstEveryNCycles > 0
        && CycleCount % ActivePhaseDef.RotBurstEveryNCycles == ActivePhaseDef.RotBurstEveryNCycles - 1;
    public void StartRoar(int ticks) => RoarTicksLeft = ticks;
    public void TickRoar() { if (RoarTicksLeft > 0) RoarTicksLeft--; }
    public void SetCycleStyles(string a, string b) { StyleAId = a; StyleBId = b; }

    /// <summary>Advance the master-script cursor one tick; wrapping past
    /// LoopLength starts the next cycle (drives the every-Nth-cycle Rot Burst).</summary>
    public void AdvanceMasterTick()
    {
        RotationTick++;
        if (RotationTick >= ActivePhaseDef.LoopLength)
        {
            RotationTick = 0;
            CycleCount++;
        }
    }

    // Style-shift telegraph (2-tick warning) — forecast state for the HUD
    public string? ForecastAttackId { get; private set; }
    public int ForecastTicksLeft { get; private set; }
    public void SetForecast(string attackId, int ticks) { ForecastAttackId = attackId; ForecastTicksLeft = ticks; }
    public void TickForecast() { if (ForecastTicksLeft > 0) ForecastTicksLeft--; else ForecastAttackId = null; }

    // Eruption hazard cadence (independent of the rotation loop)
    public int EruptionCooldown { get; private set; }
    public void ResetEruptionCooldown(int ticks) => EruptionCooldown = ticks;
    public void TickEruptionCooldown() { if (EruptionCooldown > 0) EruptionCooldown--; }

    // Rot Burst channel + punish slump
    public int RotBurstCooldown { get; private set; }
    public bool RotBurstInhaling { get; private set; }
    public int RotBurstInhaleTicksLeft { get; private set; }
    public int SlumpTicksLeft { get; private set; } // punish window: +25% dmg taken, boss cannot act
    public void ResetRotBurstCooldown(int ticks) => RotBurstCooldown = ticks;
    public void TickRotBurstCooldown() { if (RotBurstCooldown > 0) RotBurstCooldown--; }
    public void StartRotBurstInhale(int ticks) { RotBurstInhaling = true; RotBurstInhaleTicksLeft = ticks; }
    public bool TickRotBurstInhale()
    {
        if (!RotBurstInhaling) return false;
        RotBurstInhaleTicksLeft--;
        if (RotBurstInhaleTicksLeft > 0) return false;
        RotBurstInhaling = false;
        return true; // resolves this tick
    }
    public void StartSlump(int ticks) => SlumpTicksLeft = ticks;
    public void TickSlump() { if (SlumpTicksLeft > 0) SlumpTicksLeft--; }
    public bool InPunishWindow => SlumpTicksLeft > 0;

    // HP-threshold-triggered spawns already fired this fight (never re-fire) —
    // shared by swarm waves (Maggot King) and drone waves (M3, Hive Matron):
    // both are "once, the first tick HP crosses X%" events, just spawning a
    // different AddKind.
    private readonly HashSet<int> _thresholdsFired = new();
    public bool TrySpawnSwarmThreshold(int thresholdPercent) => _thresholdsFired.Add(thresholdPercent);
    public bool TryFireDroneThreshold(int thresholdPercent) => _thresholdsFired.Add(thresholdPercent);

    // Sap special debuff: boss damage output -10% while active (player weapon special)
    public int SapTicksLeft { get; private set; }
    public double SapDamageMultiplier => SapTicksLeft > 0 ? 0.90 : 1.0;
    public void ApplySap(int ticks) => SapTicksLeft = ticks;
    public void TickSap() { if (SapTicksLeft > 0) SapTicksLeft--; }

    // Pin Shot special: delays the boss's next rotation advance by N ticks
    public int PinDelayTicks { get; private set; }
    public void ApplyPinDelay(int ticks) => PinDelayTicks += ticks;

    // ── M3, Hive Matron (Boss Bible §2) ─────────────────────────────────

    // Adjacency punish (Tail Stab): consecutive ticks the player has stood
    // within melee range, reset to 0 the tick the boss actually answers.
    public int AdjacentTicksCount { get; private set; }
    public void TickAdjacency(bool playerAdjacent)
    {
        AdjacentTicksCount = playerAdjacent ? AdjacentTicksCount + 1 : 0;
    }
    public void ResetAdjacency() => AdjacentTicksCount = 0;

    // Spacing AI: attacks landed since the last dash-reset.
    public int AttacksSinceDash { get; private set; }
    public void RecordAttackForDash() => AttacksSinceDash++;
    public void ResetDashCounter() => AttacksSinceDash = 0;

    // Chitin Guard: a periodic self-buff window (−N% incoming ranged/magic).
    public int DamageReductionCooldown { get; private set; }
    public int DamageReductionTicksLeft { get; private set; }
    public bool DamageReductionActive => DamageReductionTicksLeft > 0;
    public void ResetDamageReductionCooldown(int ticks) => DamageReductionCooldown = ticks;
    public void TickDamageReductionCooldown() { if (DamageReductionCooldown > 0) DamageReductionCooldown--; }
    public void StartDamageReductionWindow(int ticks) => DamageReductionTicksLeft = ticks;
    public void TickDamageReductionWindow() { if (DamageReductionTicksLeft > 0) DamageReductionTicksLeft--; }

    // Pin's line-charge: marked tiles + warning countdown. Cleared once resolved.
    public IReadOnlyList<(int X, int Z)>? LineChargeTiles { get; private set; }
    public int LineChargeTicksLeft { get; private set; }
    public bool LineChargePendingSecondChain { get; private set; } // P2: re-aim + fire again
    public void StartLineCharge(IReadOnlyList<(int X, int Z)> tiles, int warningTicks, bool chainSecond = false)
    {
        LineChargeTiles = tiles;
        LineChargeTicksLeft = warningTicks;
        LineChargePendingSecondChain = chainSecond;
    }
    public bool TickLineCharge()
    {
        if (LineChargeTiles is null) return false;
        LineChargeTicksLeft--;
        if (LineChargeTicksLeft > 0) return false;
        return true; // resolves this tick
    }
    public void ClearLineCharge() { LineChargeTiles = null; LineChargeTicksLeft = 0; }

    // ── M3, Mirrorhide (Boss Bible §3) ───────────────────────────────────

    // Echo Offense / Attunement tracking: the style the player's last
    // landed hit used, and how many consecutive hits have shared it.
    public AttackType? LastHitStyle { get; private set; }
    public int ConsecutiveStyleHits { get; private set; }

    public AttackType? AttunedStyle { get; private set; }
    public int AttunementShimmerTicksLeft { get; private set; }
    public int AttunementImmuneTicksLeft { get; private set; }
    public bool IsAttuned => AttunedStyle is not null && AttunementImmuneTicksLeft > 0;
    public bool IsShimmering => AttunedStyle is not null && AttunementShimmerTicksLeft > 0 && AttunementImmuneTicksLeft <= 0;

    /// <summary>Records a landed, non-immune player hit for Echo Offense +
    /// Attunement (immune-blocked hits never reach here — see
    /// GameTickService's Attunement-immunity check). Returns "shatter" if
    /// this hit broke a shimmering attunement (a different style during the
    /// warning window), "attune" if it just crossed the threshold and
    /// started the shimmer, else "none".</summary>
    public string RecordPlayerHitStyle(AttackType style, AttunementDef? def)
    {
        bool wasShimmering = IsShimmering;
        var priorStyle = LastHitStyle;
        LastHitStyle = style;

        if (wasShimmering && AttunedStyle != style)
        {
            AttunedStyle = null;
            AttunementShimmerTicksLeft = 0;
            ConsecutiveStyleHits = 1;
            return "shatter";
        }

        if (def is null) return "none";

        ConsecutiveStyleHits = priorStyle == style ? ConsecutiveStyleHits + 1 : 1;

        if (AttunedStyle is null && ConsecutiveStyleHits >= def.HitsToAttune)
        {
            AttunedStyle = style;
            AttunementShimmerTicksLeft = def.ShimmerTicks;
            return "attune";
        }
        return "none";
    }

    /// <summary>Advances the shimmer -> full-immunity handoff; called once
    /// per tick with the def's own ImmuneTicks (only consulted the instant
    /// the shimmer expires). Resets the streak once immunity actually
    /// starts, so the next attunement cycle counts fresh hits.</summary>
    public void TickAttunement(int immuneTicksOnExpiry)
    {
        if (AttunementShimmerTicksLeft > 0)
        {
            AttunementShimmerTicksLeft--;
            if (AttunementShimmerTicksLeft <= 0 && AttunedStyle is not null)
            {
                AttunementImmuneTicksLeft = immuneTicksOnExpiry;
                ConsecutiveStyleHits = 0;
            }
        }
        else if (AttunementImmuneTicksLeft > 0)
        {
            AttunementImmuneTicksLeft--;
            if (AttunementImmuneTicksLeft <= 0) AttunedStyle = null;
        }
    }

    // Cloak: untargetable window + cooldown.
    public int CloakCooldown { get; private set; }
    public int CloakTicksLeft { get; private set; }
    public bool IsCloaked => CloakTicksLeft > 0;
    public void ResetCloakCooldown(int ticks) => CloakCooldown = ticks;
    public void TickCloakCooldown() { if (CloakCooldown > 0) CloakCooldown--; }
    public void StartCloak(int ticks) => CloakTicksLeft = ticks;
    public bool TickCloak() // returns true the tick the cloak ENDS
    {
        if (CloakTicksLeft <= 0) return false;
        CloakTicksLeft--;
        return CloakTicksLeft <= 0;
    }

    // Reflection: channel then a reflect-damage window, against whichever
    // style was attuned (or last hit) at the moment the channel began.
    public int ReflectChannelTicksLeft { get; private set; }
    public int ReflectWindowTicksLeft { get; private set; }
    public bool ReflectWindowActive => ReflectWindowTicksLeft > 0;
    public AttackType? ReflectStyle { get; private set; }
    public void StartReflectChannel(int ticks) => ReflectChannelTicksLeft = ticks;
    public bool TickReflectChannel() // returns true the tick the channel resolves
    {
        if (ReflectChannelTicksLeft <= 0) return false;
        ReflectChannelTicksLeft--;
        return ReflectChannelTicksLeft <= 0;
    }
    public void StartReflectWindow(int ticks, AttackType style) { ReflectWindowTicksLeft = ticks; ReflectStyle = style; }
    public void TickReflectWindow() { if (ReflectWindowTicksLeft > 0) ReflectWindowTicksLeft--; }

    // Copycat (Phase 2): cooldown + pending telegraph.
    public int CopycatCooldown { get; private set; }
    public int CopycatTelegraphTicksLeft { get; private set; }
    public void ResetCopycatCooldown(int ticks) => CopycatCooldown = ticks;
    public void TickCopycatCooldown() { if (CopycatCooldown > 0) CopycatCooldown--; }
    public void StartCopycatTelegraph(int ticks) => CopycatTelegraphTicksLeft = ticks;
    public bool TickCopycatTelegraph()
    {
        if (CopycatTelegraphTicksLeft <= 0) return false;
        CopycatTelegraphTicksLeft--;
        return CopycatTelegraphTicksLeft <= 0;
    }

    // ── M3, Bloodtithe (Boss Bible §4) ───────────────────────────────────

    // Facing: degrees, 0 = facing +Z, turns toward the player each tick,
    // clamped to the def's own DegreesPerTick turn rate.
    public double FacingAngleDeg { get; private set; }
    public void SetFacing(double deg) => FacingAngleDeg = deg;

    // Relentless-walk throttle: counts ticks until the next 1-tile step.
    public int MovementTickCounter { get; private set; }
    public void TickMovementCounter() => MovementTickCounter++;
    public void ResetMovementCounter() => MovementTickCounter = 0;

    // Bleed-stack cap tracking (per-hit application lives in GameTickService;
    // this just exposes whether he's currently allowed to reapply — always
    // true, the cap is enforced on the PLAYER's stack count in GameState).

    // Transfusion: healing channel, interruptible by a player special.
    public int TransfusionTicksLeft { get; private set; }
    public bool TransfusionActive => TransfusionTicksLeft > 0;
    public void StartTransfusion(int ticks) => TransfusionTicksLeft = ticks;
    public bool TickTransfusion() // true the tick it would resolve/expire naturally
    {
        if (TransfusionTicksLeft <= 0) return false;
        TransfusionTicksLeft--;
        return true;
    }
    public void InterruptTransfusion() => TransfusionTicksLeft = 0;

    // Crimson Pact: cooldown + temporary full-speed window.
    public int CrimsonPactCooldown { get; private set; }
    public int SpeedBoostTicksLeft { get; private set; }
    public bool SpeedBoosted => SpeedBoostTicksLeft > 0;
    public void ResetCrimsonPactCooldown(int ticks) => CrimsonPactCooldown = ticks;
    public void TickCrimsonPactCooldown() { if (CrimsonPactCooldown > 0) CrimsonPactCooldown--; }
    public void StartSpeedBoost(int ticks) => SpeedBoostTicksLeft = ticks;
    public void TickSpeedBoost() { if (SpeedBoostTicksLeft > 0) SpeedBoostTicksLeft--; }

    // Scythe Arc: 2-tick windup, resolved (range re-checked) when it expires.
    public int ScytheArcTicksLeft { get; private set; }
    public void StartScytheArcWindup(int ticks) => ScytheArcTicksLeft = ticks;
    public bool TickScytheArcWindup()
    {
        if (ScytheArcTicksLeft <= 0) return false;
        ScytheArcTicksLeft--;
        return ScytheArcTicksLeft <= 0;
    }

    // Harvest: telegraph + cooldown.
    public int HarvestTelegraphTicksLeft { get; private set; }
    public int HarvestCooldown { get; private set; }
    public void ResetHarvestCooldown(int ticks) => HarvestCooldown = ticks;
    public void TickHarvestCooldown() { if (HarvestCooldown > 0) HarvestCooldown--; }
    public void StartHarvestTelegraph(int ticks) => HarvestTelegraphTicksLeft = ticks;
    public bool TickHarvestTelegraph()
    {
        if (HarvestTelegraphTicksLeft <= 0) return false;
        HarvestTelegraphTicksLeft--;
        return HarvestTelegraphTicksLeft <= 0;
    }

    // Rotfang on-hit poison (items doc §3, backlog resolution batch 1):
    // 5-tick duration, 2 dmg/stack/tick, max 3 stacks; any landed hit while
    // wielded adds a stack (capped at 3) AND refreshes the duration to 5 --
    // "reapplication refreshes," not a fresh independent timer per stack.
    public const int MaxPoisonStacks = 3;
    public int PoisonStacks { get; private set; }
    public int PoisonDurationTicksLeft { get; private set; }
    public int PoisonDamagePerTick => PoisonStacks * 2;
    public void ApplyRotfangPoison()
    {
        PoisonStacks = Math.Min(MaxPoisonStacks, PoisonStacks + 1);
        PoisonDurationTicksLeft = 5;
    }
    /// <summary>Returns true if this tick deals poison damage (see
    /// <see cref="PoisonDamagePerTick"/>). Stacks clear when the duration
    /// runs out — no lingering single stack at zero duration.</summary>
    public bool TickPoison()
    {
        if (PoisonDurationTicksLeft <= 0) return false;
        PoisonDurationTicksLeft--;
        if (PoisonDurationTicksLeft <= 0) { PoisonStacks = 0; return true; } // last tick still hits
        return true;
    }

    public NpcInstance(NpcTemplate template)
    {
        Template = template;
        CurrentHp = template.Stats.Hitpoints;
        if (template.Script is { } s)
        {
            EruptionCooldown = s.Phase1.Eruption.CooldownTicks;
            RotBurstCooldown = s.Phase1.RotBurst?.CadenceTicks ?? 0;
            // M3: Chitin Guard's/Cloak's/Copycat's own cadences, same "seed
            // the cooldown so it doesn't fire on tick 0" pattern as
            // Eruption/RotBurst above.
            DamageReductionCooldown = s.DamageReductionWindow?.CadenceTicks ?? 0;
            CloakCooldown = s.Cloak?.CadenceTicks ?? 0;
            CopycatCooldown = s.Copycat?.CadenceTicks ?? 0;
            CrimsonPactCooldown = s.CrimsonPact?.CadenceTicks ?? 0;
            HarvestCooldown = s.Harvest?.CadenceTicks ?? 0;

            // Seed the forecast with the fight's opening move. The rotation's
            // own style-shift telegraphs only fire mid-loop (T8/T16) — without
            // this, the very first attack (T0, no lead-in) is a completely
            // blind read with no way to know which prayer to bring up before
            // it lands. Every other attack in the fight is telegraphed one way
            // or another; the opener shouldn't be the one exception.
            var opening = s.Phase1.Rotation.FirstOrDefault(r => r.Tick == 0);
            if (opening is not null && opening.Action is not ("idle" or "style_telegraph"))
                SetForecast(opening.Action, s.Phase1.TelegraphLeadTicks);
        }
    }

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);

    // M3, Bloodtithe: Tithe aura self-heal + Transfusion. Capped at MaxHp
    // (Transfusion doesn't overheal past full).
    public void Heal(int amount) => CurrentHp = Math.Min(MaxHp, CurrentHp + amount);

    public int HpPercent => MaxHp == 0 ? 0 : CurrentHp * 100 / MaxHp;

    /// <summary>This boss's Evasion (hit-chance penalty, percentage points) for
    /// the doctrine a player is attacking with — melee (Stab/Slash/Crush),
    /// Ranged, or Magic. Null template Evasion is neutral (zero everywhere).</summary>
    public double EvasionFor(AttackType doctrine)
    {
        var ev = Template.Evasion ?? NpcEvasion.Zero;
        return doctrine switch
        {
            AttackType.Ranged => ev.Ranged,
            AttackType.Magic => ev.Magic,
            _ => ev.Melee, // Stab/Slash/Crush
        };
    }

    /// <summary>Advances the rotation cursor by one tick. Also promotes
    /// Phase 1→2 the first tick HP is at/below the threshold, resetting the
    /// cursor and hazard cadence to the new phase's numbers.</summary>
    public bool AdvanceRotation()
    {
        var script = Template.Script!;
        if (Phase == 1 && HpPercent <= script.PhaseTwoThresholdPercent)
        {
            Phase = 2;
            RotationTick = 0;
            CycleCount = 0; // master-script cycle counter starts fresh in P2
            EruptionCooldown = script.Phase2.Eruption.CooldownTicks;
            RotBurstCooldown = script.Phase2.RotBurst?.CadenceTicks ?? 0;
            return true;
        }

        RotationTick = (RotationTick + 1) % ActivePhaseDef.LoopLength;
        return false;
    }

    /// <summary>Pin Shot (m1-plan Workstream B): consumes one tick of delay
    /// without resolving or advancing the rotation — the boss's whole turn
    /// is skipped, so its schedule shifts by exactly one tick with no risk
    /// of an already-resolved action re-firing.</summary>
    public bool ConsumePinDelay()
    {
        if (PinDelayTicks <= 0) return false;
        PinDelayTicks--;
        return true;
    }
}
