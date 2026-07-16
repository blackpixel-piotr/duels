using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed record NpcSpecialMove(string WarningText, double DamageMultiplier, int CooldownTurns);

/// <summary>Tile-hazard mechanic (modern boss fights): every CooldownTicks the
/// boss marks TilesPerWave tiles around the player; after WarningTicks they
/// erupt for EruptDamage (never prayer-reduced — dodge only) and leave pools
/// that cost PoolDamage per tick stood on for PoolTicks.</summary>
public sealed record HazardProfile(
    string WarningText,
    int CooldownTicks, int WarningTicks, int TilesPerWave,
    int EruptDamage, int PoolTicks, int PoolDamage);

public sealed class NpcTemplate
{
    public string Id { get; }
    public string Name { get; }
    public string ExamineText { get; }
    public CombatStats Stats { get; }
    public ItemModifiers Modifiers { get; }
    public AttackType AttackType { get; }
    public IReadOnlyList<LootEntry> LootTable { get; }
    public int GoldReward { get; }
    public int MaxWager { get; }
    public NpcSpecialMove? TelegraphedMove { get; }
    public HazardProfile? Hazards { get; }
    public int AttackSpeedTicks { get; }
    /// <summary>Attack styles the NPC cycles through; defaults to just AttackType.</summary>
    public IReadOnlyList<AttackType> StyleRotation { get; }
    /// <summary>Attacks performed in each style before rotating.</summary>
    public int AttacksPerStyle { get; }
    public int CombatLevel => CalculateCombatLevel(Stats);

    public NpcTemplate(
        string id,
        string name,
        string examineText,
        CombatStats stats,
        ItemModifiers modifiers,
        AttackType attackType,
        IReadOnlyList<LootEntry> lootTable,
        int goldReward = 0,
        int maxWager = 0,
        NpcSpecialMove? telegraphedMove = null,
        int attackSpeedTicks = 4,
        IReadOnlyList<AttackType>? styleRotation = null,
        int attacksPerStyle = 4,
        HazardProfile? hazards = null)
    {
        Id = id;
        Name = name;
        ExamineText = examineText;
        Stats = stats;
        Modifiers = modifiers;
        AttackType = attackType;
        LootTable = lootTable;
        GoldReward = goldReward;
        MaxWager = maxWager;
        TelegraphedMove = telegraphedMove;
        Hazards = hazards;
        AttackSpeedTicks = attackSpeedTicks;
        StyleRotation = styleRotation ?? [attackType];
        AttacksPerStyle = attacksPerStyle;
    }

    private static int CalculateCombatLevel(CombatStats s)
    {
        double base_ = (s.Defence + s.Hitpoints) * 0.25;
        double melee = (s.Attack + s.Strength) * 0.325;
        return (int)(base_ + melee);
    }
}

public sealed record LootEntry(string ItemId, double DropChance, int MinQty = 1, int MaxQty = 1, bool OnceOnly = false);

public sealed class NpcInstance
{
    public NpcTemplate Template { get; }
    public int CurrentHp { get; private set; }
    public int MaxHp => Template.Stats.Hitpoints;
    public bool IsAlive => CurrentHp > 0;

    // Telegraphed attack state
    public NpcSpecialMove? PendingSpecial { get; private set; }
    public int SpecialCooldown { get; private set; }

    // Tile-hazard state (see HazardProfile); ticks down with the fight
    public int HazardCooldown { get; private set; }
    public void ResetHazardCooldown(int ticks) => HazardCooldown = ticks;

    // Boss state
    public int TurnsInFight { get; private set; }
    public bool PhaseShiftUsed { get; private set; }
    public bool WarlordPrayerActive { get; private set; }
    public int WarlordPrayerCountdown { get; private set; }

    // Style rotation state
    public AttackType CurrentAttackType { get; private set; }
    public int AttacksInStyle { get; private set; }
    public int? AttacksPerStyleOverride { get; set; }

    // Poison (mirrors GameState's player poison; used by poison-on-hit weapons)
    public bool Poisoned { get; private set; }
    public int PoisonCounter { get; private set; }

    public void ApplyPoison() { Poisoned = true; PoisonCounter = 0; }
    public bool TickPoison()
    {
        if (!Poisoned) return false;
        PoisonCounter++;
        if (PoisonCounter < 4) return false;
        PoisonCounter = 0;
        return true;
    }

    public NpcInstance(NpcTemplate template)
    {
        Template = template;
        CurrentHp = template.Stats.Hitpoints;
        SpecialCooldown = template.TelegraphedMove?.CooldownTurns ?? 0;
        HazardCooldown = template.Hazards?.CooldownTicks ?? 0;
        WarlordPrayerCountdown = 3;
        CurrentAttackType = template.StyleRotation.Count > 0 ? template.StyleRotation[0] : template.AttackType;
    }

    /// <summary>Call after each NPC attack; returns true when the style just rotated.</summary>
    public bool AdvanceStyle()
    {
        if (Template.StyleRotation.Count <= 1) return false;
        AttacksInStyle++;
        int perStyle = AttacksPerStyleOverride ?? Template.AttacksPerStyle;
        if (AttacksInStyle < perStyle) return false;

        AttacksInStyle = 0;
        int idx = 0;
        for (int i = 0; i < Template.StyleRotation.Count; i++)
            if (Template.StyleRotation[i] == CurrentAttackType) { idx = i; break; }
        CurrentAttackType = Template.StyleRotation[(idx + 1) % Template.StyleRotation.Count];
        return true;
    }

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);

    public void TickFight()
    {
        TurnsInFight++;
        if (SpecialCooldown > 0) SpecialCooldown--;
    }

    /// <summary>Hazard cadence runs on the 600ms game tick, not the NPC's
    /// attack turns (TickFight), so waves keep coming while kiting.</summary>
    public void TickHazardCooldown() { if (HazardCooldown > 0) HazardCooldown--; }

    public void SetPendingSpecial(NpcSpecialMove move) => PendingSpecial = move;

    public void ConsumePendingSpecial()
    {
        SpecialCooldown = PendingSpecial?.CooldownTurns ?? 4;
        PendingSpecial = null;
    }

    public bool UsePhaseShift()
    {
        if (PhaseShiftUsed) return false;
        PhaseShiftUsed = true;
        return true;
    }

    // Returns true if prayer just flipped state
    public bool TickWarlordPrayer()
    {
        if (WarlordPrayerCountdown > 0) WarlordPrayerCountdown--;
        if (WarlordPrayerCountdown == 0)
        {
            WarlordPrayerActive = !WarlordPrayerActive;
            WarlordPrayerCountdown = 3;
            return true;
        }
        return false;
    }
}
