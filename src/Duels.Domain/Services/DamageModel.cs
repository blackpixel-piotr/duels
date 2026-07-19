using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Domain.Services;

/// <summary>Combat-math-v2 (items doc §1): 100 HP baseline, 80% base hit
/// chance, weapon Power flat damage (no roll-to-max ramp — variance comes from
/// hit/miss and mechanics), Precision as a flat hit-chance bonus, armour Def
/// points reducing incoming damage. Replaces the OSRS <c>CombatCalculator</c>
/// retired in M1 (m1-plan Workstream A, D1).</summary>
public sealed class DamageModel : IDamageModel
{
    public const int BaseHitChancePercent = 80;
    public const double GearDefCap = 0.40;       // 40% cap from gear Def points
    public const double DefPointValue = 0.004;   // 0.4% per point

    // Design assumption (items doc doesn't define "defense value" units for
    // Defensive style — flagged in m1-findings.md): the style's own +20%
    // "defense value" reduces incoming damage by 20%, stacking additively with
    // (and uncapped by) the gear Def-point cap above.
    public const double DefensiveStyleIncomingReduction = 0.20;

    private readonly IRandomProvider _random;

    public DamageModel(IRandomProvider random)
    {
        _random = random;
    }

    public DamageResult Roll(AttackerProfile attacker, DefenderProfile defender)
    {
        bool hit = _random.NextDouble() < HitChance(attacker);
        return hit ? new DamageResult(true, ComputeDamage(attacker, defender)) : new DamageResult(false, 0);
    }

    private static int ComputeDamage(AttackerProfile attacker, DefenderProfile defender)
    {
        double dmg = attacker.Power * (1.0 + attacker.LineDamageBonus) * StyleDamageMultiplier(attacker.Style);
        dmg *= 1.0 - MitigationFraction(defender);
        return Math.Max(0, (int)Math.Round(dmg));
    }

    private static double HitChance(AttackerProfile attacker)
    {
        double pct = BaseHitChancePercent + StyleHitModifierPercent(attacker.Style) + attacker.Precision * 100.0;
        return Math.Clamp(pct, 0, 100) / 100.0;
    }

    private static double StyleHitModifierPercent(AttackStyle style) => style switch
    {
        AttackStyle.Accurate => 10,
        AttackStyle.Aggressive => -10,
        _ => 0,
    };

    private static double StyleDamageMultiplier(AttackStyle style) => style switch
    {
        AttackStyle.Aggressive => 1.20,
        AttackStyle.Defensive => 0.90,
        _ => 1.0,
    };

    private static double MitigationFraction(DefenderProfile defender)
    {
        double gearReduction = Math.Min(defender.DefPoints * DefPointValue, GearDefCap);
        double total = gearReduction + (defender.DefensiveStyle ? DefensiveStyleIncomingReduction : 0.0);
        return Math.Clamp(total, 0, 0.95);
    }
}
