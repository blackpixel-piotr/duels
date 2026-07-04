namespace Duels.Domain.ValueObjects;

/// <summary>Attack range in arena tiles (Chebyshev distance) by combat style.
/// Melee must be adjacent; ranged/magic fire from across the arena.</summary>
public static class AttackRange
{
    public const int Melee = 1;
    public const int Distant = 8;

    public static int ForStyle(AttackType t) =>
        t is AttackType.Ranged or AttackType.Magic ? Distant : Melee;
}
