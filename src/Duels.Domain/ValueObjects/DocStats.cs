namespace Duels.Domain.ValueObjects;

/// <summary>Armour style line (items doc §5). None = unlined items (weapons,
/// flasks) — line identity/set bonuses only apply to armour pieces.</summary>
public enum GearLine
{
    None,
    Warbound,   // melee
    Stalker,    // ranged
    Occult      // magic
}

/// <summary>A weapon's special attack, identified by <see cref="Id"/> and
/// dispatched by <c>SpecialEffectId</c> in GameTickService — shared machinery,
/// not per-weapon code (items doc §2, m1-plan Workstream B).</summary>
public sealed record SpecialEffect(string Id, int Cost, string Description);

/// <summary>Combat-math-v2 stat block (items doc §1) carried by weapons and
/// armour pieces. Replaces the OSRS <c>ItemModifiers</c> aggregation entirely
/// — M1 retires the old ladder math (m1-plan Workstream A/D1).</summary>
public sealed record DocStats(
    int Power = 0,
    double Precision = 0.0,   // flat hit-chance bonus, e.g. 0.02 = +2%
    double DefPoints = 0.0,   // armour def points; 0.4%/point, 40% cap from gear
    GearLine Line = GearLine.None,
    int Tier = 0,
    SpecialEffect? Special = null)
{
    public static readonly DocStats Zero = new();
}
