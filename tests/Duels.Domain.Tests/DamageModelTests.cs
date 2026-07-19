using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Domain.Tests;

// Combat-math-v2 (items doc §1): 80% base hit, Power flat damage (no roll-to-max
// ramp), style modifiers, Precision as a flat hit bonus, Def-point mitigation.
public sealed class DamageModelTests
{
    private sealed class FixedRandom : IRandomProvider
    {
        private readonly double _rollChance;
        public FixedRandom(double rollChance) => _rollChance = rollChance;
        public int Next(int min, int max) => min;
        public double NextDouble() => _rollChance;
    }

    [Fact]
    public void AccurateStyle_HitsAt90PercentThreshold()
    {
        // 80% base + 10% Accurate = 90% hit chance. A roll just under 0.90 hits.
        var model = new DamageModel(new FixedRandom(0.89));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.True(result.Hit);
    }

    [Fact]
    public void AccurateStyle_MissesAtOrAboveThreshold()
    {
        var model = new DamageModel(new FixedRandom(0.90));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.False(result.Hit);
    }

    [Fact]
    public void AggressiveStyle_HitsFor20PercentMoreDamage_At70PercentChance()
    {
        var model = new DamageModel(new FixedRandom(0.0)); // always hits
        var aggressive = model.Roll(new AttackerProfile(10, 0, AttackStyle.Aggressive), new DefenderProfile(0));
        var accurate = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.Equal(12, aggressive.Damage); // 10 * 1.20
        Assert.Equal(10, accurate.Damage);

        // Aggressive drops hit chance to 70% — a 0.70 roll must miss.
        var model2 = new DamageModel(new FixedRandom(0.70));
        var miss = model2.Roll(new AttackerProfile(10, 0, AttackStyle.Aggressive), new DefenderProfile(0));
        Assert.False(miss.Hit);
    }

    [Fact]
    public void DefensiveStyle_Reduces10PercentOwnDamage()
    {
        var model = new DamageModel(new FixedRandom(0.0));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Defensive), new DefenderProfile(0));
        Assert.Equal(9, result.Damage); // 10 * 0.90
    }

    [Fact]
    public void Precision_AddsFlatHitChance()
    {
        // Base 80% + 0% style + 2% precision = 82%. A 0.815 roll hits only with precision.
        var model = new DamageModel(new FixedRandom(0.815));
        var withPrecision = model.Roll(new AttackerProfile(10, 0.02, AttackStyle.Aggressive), new DefenderProfile(0));
        // Aggressive: -10% + 2% = 72% — a 0.815 roll still misses even with precision.
        Assert.False(withPrecision.Hit);

        var accurateWithPrecision = model.Roll(new AttackerProfile(10, 0.02, AttackStyle.Accurate), new DefenderProfile(0));
        // Accurate: +10% + 2% = 92% — 0.815 hits.
        Assert.True(accurateWithPrecision.Hit);
    }

    [Fact]
    public void DefPoints_ReduceDamage_CappedAt40Percent()
    {
        var model = new DamageModel(new FixedRandom(0.0));
        // 0.4%/point * 100 points = 40% — right at the cap.
        var capped = model.Roll(new AttackerProfile(100, 0, AttackStyle.Accurate), new DefenderProfile(100));
        Assert.Equal(60, capped.Damage); // 100 * (1 - 0.40)

        // Beyond 100 points, mitigation stays capped at 40%.
        var overCap = model.Roll(new AttackerProfile(100, 0, AttackStyle.Accurate), new DefenderProfile(500));
        Assert.Equal(60, overCap.Damage);
    }

    [Fact]
    public void LineDamageBonus_ScalesDamageMultiplicatively()
    {
        var model = new DamageModel(new FixedRandom(0.0));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate, LineDamageBonus: 0.05), new DefenderProfile(0));
        Assert.Equal(10, result.Damage); // 10 * 1.05 = 10.5 -> rounds to 10 (banker's rounding at .5)
    }
}
