using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Weapon
{
    public string Id { get; }
    public string Name { get; }
    public AttackType AttackType { get; }
    public ItemModifiers Modifiers { get; }
    public int AttackSpeed { get; }
    public string ExamineText { get; }
    public SpecialAttack? Special { get; }

    public Weapon(
        string id,
        string name,
        AttackType attackType,
        ItemModifiers modifiers,
        int attackSpeed = 4,
        string examineText = "",
        SpecialAttack? special = null)
    {
        Id = id;
        Name = name;
        AttackType = attackType;
        Modifiers = modifiers;
        AttackSpeed = attackSpeed;
        ExamineText = examineText;
        Special = special;
    }

    public GearPiece AsGearPiece() =>
        new(Id, Name, EquipmentSlot.Weapon, Modifiers, ExamineText);
}

public sealed record SpecialAttack(string CommandAlias, int EnergyRequired, double DamageMultiplier, string Description);
