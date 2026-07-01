using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

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
        int maxWager = 0)
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
    }

    private static int CalculateCombatLevel(CombatStats s)
    {
        double base_ = (s.Defence + s.Hitpoints) * 0.25;
        double melee = (s.Attack + s.Strength) * 0.325;
        return (int)(base_ + melee);
    }
}

public sealed record LootEntry(string ItemId, double DropChance);

public sealed class NpcInstance
{
    public NpcTemplate Template { get; }
    public int CurrentHp { get; private set; }
    public int MaxHp => Template.Stats.Hitpoints;

    public NpcInstance(NpcTemplate template)
    {
        Template = template;
        CurrentHp = template.Stats.Hitpoints;
    }

    public bool IsAlive => CurrentHp > 0;

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
}
