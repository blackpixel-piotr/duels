using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Weapon
{
    public string Id { get; }
    public string Name { get; }
    public AttackType AttackType { get; }
    public int AttackSpeed { get; }
    public string ExamineText { get; }
    public int AttackLevelRequired { get; }

    /// <summary>Combat-math-v2 stats (Power/Precision/Special) — the items doc
    /// tables verbatim. Every M1 weapon carries this; never null in content
    /// shipped after the M1 ladder-retirement sweep.</summary>
    public DocStats Doc { get; }

    /// <summary>Attack range in arena tiles (Chebyshev). 1 = melee adjacency;
    /// ranged/magic weapons set it to AttackRange.Distant.</summary>
    public int Range { get; }

    public Weapon(
        string id,
        string name,
        AttackType attackType,
        DocStats doc,
        int attackSpeed = 4,
        string examineText = "",
        int attackLevelRequired = 1,
        int range = 1)
    {
        Id = id;
        Name = name;
        AttackType = attackType;
        Doc = doc;
        AttackSpeed = attackSpeed;
        ExamineText = examineText;
        AttackLevelRequired = attackLevelRequired;
        Range = range;
    }

    public GearPiece AsGearPiece() =>
        new(Id, Name, EquipmentSlot.Weapon, Doc, ExamineText);
}
