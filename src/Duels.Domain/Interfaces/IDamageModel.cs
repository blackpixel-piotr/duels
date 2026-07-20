using Duels.Domain.ValueObjects;

namespace Duels.Domain.Interfaces;

/// <summary>An attacker's resolved offense for one roll: Power/Precision from
/// the wielded weapon, style modifiers already folded in by the caller isn't
/// required — <see cref="IDamageModel"/> applies style itself. <see cref="Power"/>
/// is the MEAN of the damage roll (items doc §1): a landed hit deals a uniform
/// 0..2×Power, so Power is average damage and 2×Power is the max hit.</summary>
public sealed record AttackerProfile(
    int Power,
    double Precision,
    AttackStyle Style,
    double LineDamageBonus = 0.0); // identity + set bonus %, already summed by the caller

/// <summary>A defender's damage mitigation + evasion for the incoming style:
/// gear Def points (0.4%/pt, 40% cap), the player's Defensive-style incoming
/// reduction, and — for a boss defender — its per-style <see cref="Evasion"/>
/// (percentage points subtracted from the attacker's hit chance for the doctrine
/// being used against it; the "this boss favors ranged" tuning lever).</summary>
public sealed record DefenderProfile(double DefPoints, bool DefensiveStyle = false, double Evasion = 0.0);

/// <summary><see cref="MaxHit"/> is set when the damage roll landed its ceiling
/// (2×Power before mitigation) — drives the distinct max-hit visual.</summary>
public sealed record DamageResult(bool Hit, int Damage, bool MaxHit = false);

/// <summary>Combat-math-v2 (items doc §1): 100 HP baseline, ~80% base hit
/// chance (accuracy roll = Precision + style mod vs the defender's per-style
/// Evasion), then a uniform 0..2×Power damage roll on a hit (Power = mean).
/// Replaces the OSRS-formula <c>CombatCalculator</c> retired in M1 (m1-plan
/// Workstream A).</summary>
public interface IDamageModel
{
    DamageResult Roll(AttackerProfile attacker, DefenderProfile defender);
}
