using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Domain.Services;

/// <summary>Combat-math-v2 (items doc §1): 100 HP baseline, ~80% base hit
/// chance, a uniform 0..2×Power damage roll on a hit (Power = mean, 2×Power =
/// max hit), Precision as a flat hit-chance bonus, the defender's per-style
/// Evasion subtracting from hit chance, armour Def points reducing incoming
/// damage. Replaces the OSRS <c>CombatCalculator</c> retired in M1 (m1-plan
/// Workstream A, D1).</summary>
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
        // Accuracy roll (items doc §1): Precision + style mod vs the defender's
        // per-style Evasion, ~80% at-tier. A miss deals nothing.
        if (_random.NextDouble() >= HitChance(attacker, defender))
            return new DamageResult(false, 0);

        // Damage roll: uniform 0..2×(effective Power) so Power is the mean and
        // 2×Power the max hit. The effective mean folds in style/line/boost
        // multipliers, so the ceiling tracks them too (an Aggressive max is
        // higher than an Accurate max). Mitigation applies to the rolled value.
        double effectiveMean = attacker.Power * (1.0 + attacker.LineDamageBonus) * StyleDamageMultiplier(attacker.Style);
        int maxRoll = Math.Max(0, (int)Math.Round(2.0 * effectiveMean));
        int raw = maxRoll == 0 ? 0 : _random.Next(0, maxRoll + 1); // inclusive 0..maxRoll
        bool isMax = maxRoll > 0 && raw == maxRoll;

        double mitigated = raw * (1.0 - MitigationFraction(defender));
        return new DamageResult(true, Math.Max(0, (int)Math.Round(mitigated)), isMax);
    }

    private static double HitChance(AttackerProfile attacker, DefenderProfile defender)
    {
        double pct = BaseHitChancePercent + StyleHitModifierPercent(attacker.Style)
                   + attacker.Precision * 100.0 - defender.Evasion;
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
