using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

/// <summary>One entry in a boss's fixed-tick rotation loop. Action is one of:
/// "idle", "style_telegraph", a single attack id (key into
/// <see cref="BossScript.Attacks"/>), or two ids joined by "/" — resolved at
/// cast time to the first id if the player is melee-adjacent, else the
/// second (Boss Bible "Lash if adjacent / Grub Volley if not").</summary>
public sealed record RotationStep(int Tick, string Action);

/// <summary>One named boss attack. Damage is a concrete value chosen inside
/// the Boss Bible's damage band (Light 5–10, Medium 15–20, Heavy 30–40,
/// Severe 50–60) — boss attacks are scripted, not rolled: they always land
/// unless dodged positionally, then reduced 75% by a matching protection
/// prayer (unless Unprayable).</summary>
public sealed record BossAttackDef(string Id, string Name, AttackType Style, int Damage, bool Unprayable = false);

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

/// <summary>One phase's full choreography (Boss Bible P1/P2).</summary>
public sealed record BossPhaseDef(
    int LoopLength,
    IReadOnlyList<RotationStep> Rotation,
    EruptionDef Eruption,
    RotBurstDef? RotBurst = null,
    IReadOnlyList<SwarmWaveDef>? Swarms = null);

/// <summary>Boss footprint in tiles (plain record, not a ValueTuple — System.Text.Json
/// has no built-in ValueTuple converter).</summary>
public sealed record FootprintDef(int Width, int Height);

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
    bool Stationary);

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
    AttackType? DummyStyle = null)
{
    public int MaxHp => Stats.Hitpoints;
}

public sealed record LootEntry(string ItemId, double DropChance, int MinQty = 1, int MaxQty = 1, bool OnceOnly = false);

/// <summary>One spawned swarm add (m1-plan Workstream C.7): crawls 1 tile/tick
/// toward the player; contact applies a bleed stack; dies in 2 hits (fixed
/// low HP — see SwarmWaveDef.Hp).</summary>
public sealed class AddInstance
{
    public string Id { get; }
    public (int X, int Z) Tile { get; private set; }
    public int MaxHp { get; }
    public int CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0;

    public AddInstance(string id, (int X, int Z) tile, int hp)
    {
        Id = id;
        Tile = tile;
        MaxHp = hp;
        CurrentHp = hp;
    }

    public void MoveTo((int X, int Z) tile) => Tile = tile;
    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
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

    // Swarm wave thresholds already triggered this fight (never re-fire)
    private readonly HashSet<int> _swarmThresholdsFired = new();
    public bool TrySpawnSwarmThreshold(int thresholdPercent) => _swarmThresholdsFired.Add(thresholdPercent);

    // Sap special debuff: boss damage output -10% while active (player weapon special)
    public int SapTicksLeft { get; private set; }
    public double SapDamageMultiplier => SapTicksLeft > 0 ? 0.90 : 1.0;
    public void ApplySap(int ticks) => SapTicksLeft = ticks;
    public void TickSap() { if (SapTicksLeft > 0) SapTicksLeft--; }

    // Pin Shot special: delays the boss's next rotation advance by N ticks
    public int PinDelayTicks { get; private set; }
    public void ApplyPinDelay(int ticks) => PinDelayTicks += ticks;

    public NpcInstance(NpcTemplate template)
    {
        Template = template;
        CurrentHp = template.Stats.Hitpoints;
        if (template.Script is { } s)
        {
            EruptionCooldown = s.Phase1.Eruption.CooldownTicks;
            RotBurstCooldown = s.Phase1.RotBurst?.CadenceTicks ?? 0;
        }
    }

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);

    public int HpPercent => MaxHp == 0 ? 0 : CurrentHp * 100 / MaxHp;

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
