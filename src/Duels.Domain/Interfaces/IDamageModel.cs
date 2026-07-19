using Duels.Domain.ValueObjects;

namespace Duels.Domain.Interfaces;

/// <summary>An attacker's resolved offense for one roll: Power/Precision from
/// the wielded weapon, style modifiers already folded in by the caller isn't
/// required — <see cref="IDamageModel"/> applies style itself.</summary>
public sealed record AttackerProfile(
    int Power,
    double Precision,
    AttackStyle Style,
    double LineDamageBonus = 0.0); // identity + set bonus %, already summed by the caller

/// <summary>A defender's damage mitigation for the incoming style: gear Def
/// points (0.4%/pt, 40% cap) plus, when the defender is the player and is
/// standing on Defensive style, the style's own incoming-damage reduction.</summary>
public sealed record DefenderProfile(double DefPoints, bool DefensiveStyle = false);

public sealed record DamageResult(bool Hit, int Damage);

/// <summary>Combat-math-v2 (items doc §1): 100 HP baseline, 80% base hit
/// chance, weapon Power/Precision, armour Def points, style modifiers. Replaces
/// the OSRS-formula <c>CombatCalculator</c> retired in M1 (m1-plan Workstream A).</summary>
public interface IDamageModel
{
    DamageResult Roll(AttackerProfile attacker, DefenderProfile defender);
}
