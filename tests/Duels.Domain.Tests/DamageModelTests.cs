using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Domain.Tests;

// Combat-math-v2 (items doc §1): ~80% base hit (accuracy roll = Precision +
// style mod vs the defender's per-style Evasion), then a uniform 0..2×Power
// damage roll on a hit (Power = mean, 2×Power = max hit), style modifiers,
// armour Def-point mitigation.
public sealed class DamageModelTests
{
    // Always hits (NextDouble low) and rolls the TOP of any integer range
    // (Next => max-1) — so the damage roll lands its 2×(effective Power)
    // ceiling. Mirrors the game's own AlwaysHitRandom test double.
    private sealed class MaxRollRandom : IRandomProvider
    {
        public int Next(int min, int max) => max > min ? max - 1 : min;
        public double NextDouble() => 0.0;
    }

    // Hits, but the damage roll comes up 0 (Next => min) — a genuine 0-damage
    // HIT, distinct from a miss (accuracy succeeded, damage rolled the floor).
    private sealed class ZeroRollRandom : IRandomProvider
    {
        public int Next(int min, int max) => min;
        public double NextDouble() => 0.0;
    }

    // Controls only the accuracy roll (NextDouble); the damage roll is
    // irrelevant to these hit/miss tests, so Next returns the range midpoint.
    private sealed class HitRollRandom : IRandomProvider
    {
        private readonly double _roll;
        public HitRollRandom(double roll) => _roll = roll;
        public int Next(int min, int max) => (min + max) / 2;
        public double NextDouble() => _roll;
    }

    // Real seeded RNG for the statistical mean/ceiling test.
    private sealed class SeededRandom : IRandomProvider
    {
        private readonly Random _r;
        public SeededRandom(int seed) => _r = new Random(seed);
        public int Next(int min, int max) => _r.Next(min, max);
        public double NextDouble() => _r.NextDouble();
    }

    // ── Accuracy roll ────────────────────────────────────────────────────

    [Fact]
    public void AccurateStyle_HitsAt90PercentThreshold()
    {
        // 80% base + 10% Accurate = 90% hit chance. A roll just under 0.90 hits.
        var model = new DamageModel(new HitRollRandom(0.89));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.True(result.Hit);
    }

    [Fact]
    public void AccurateStyle_MissesAtOrAboveThreshold()
    {
        var model = new DamageModel(new HitRollRandom(0.90));
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.False(result.Hit);
        Assert.Equal(0, result.Damage);
        Assert.False(result.MaxHit);
    }

    [Fact]
    public void AggressiveStyle_ShiftsHitAndDamage()
    {
        // Damage: max roll = 2×(effective Power). Aggressive = 10×1.2 = 12 mean
        // → 24 max; Accurate = 10 mean → 20 max.
        var model = new DamageModel(new MaxRollRandom());
        var aggressive = model.Roll(new AttackerProfile(10, 0, AttackStyle.Aggressive), new DefenderProfile(0));
        var accurate = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.Equal(24, aggressive.Damage);
        Assert.Equal(20, accurate.Damage);
        Assert.True(aggressive.MaxHit);

        // Aggressive drops hit chance to 70% — a 0.70 roll must miss.
        var missModel = new DamageModel(new HitRollRandom(0.70));
        var miss = missModel.Roll(new AttackerProfile(10, 0, AttackStyle.Aggressive), new DefenderProfile(0));
        Assert.False(miss.Hit);
    }

    [Fact]
    public void Precision_AddsFlatHitChance()
    {
        // Base 80% + 0% style + 2% precision = 82%. A 0.815 roll hits only with precision.
        var model = new DamageModel(new HitRollRandom(0.815));
        var withPrecision = model.Roll(new AttackerProfile(10, 0.02, AttackStyle.Aggressive), new DefenderProfile(0));
        // Aggressive: -10% + 2% = 72% — a 0.815 roll still misses even with precision.
        Assert.False(withPrecision.Hit);

        var accurateWithPrecision = model.Roll(new AttackerProfile(10, 0.02, AttackStyle.Accurate), new DefenderProfile(0));
        // Accurate: +10% + 2% = 92% — 0.815 hits.
        Assert.True(accurateWithPrecision.Hit);
    }

    [Fact]
    public void Evasion_SubtractsFromHitChance()
    {
        // Defensive carries a 0% hit modifier, so it isolates the Evasion term.
        // Base 80% − 20 Evasion points = 60%: a 0.65 roll misses vs an evasive
        // defender but hits vs a neutral one — the "this boss favors X" lever.
        var model = new DamageModel(new HitRollRandom(0.65));
        var vsEvasive = model.Roll(new AttackerProfile(10, 0, AttackStyle.Defensive), new DefenderProfile(0, Evasion: 20));
        Assert.False(vsEvasive.Hit); // 80 − 20 = 60%, 0.65 misses
        var vsNeutral = model.Roll(new AttackerProfile(10, 0, AttackStyle.Defensive), new DefenderProfile(0));
        Assert.True(vsNeutral.Hit); // 80%, 0.65 hits
    }

    // ── Damage roll ──────────────────────────────────────────────────────

    [Fact]
    public void MaxRoll_DealsTwicePower_AndFlagsMaxHit()
    {
        var model = new DamageModel(new MaxRollRandom());
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.Equal(20, result.Damage); // 2 × Power
        Assert.True(result.MaxHit);
    }

    [Fact]
    public void ZeroDamageRoll_IsAHit_NotAMiss()
    {
        // Accuracy succeeded but the damage roll came up 0 — a real (if
        // harmless) hit, NOT a miss, and never a max hit.
        var model = new DamageModel(new ZeroRollRandom());
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate), new DefenderProfile(0));
        Assert.True(result.Hit);
        Assert.Equal(0, result.Damage);
        Assert.False(result.MaxHit);
    }

    [Fact]
    public void DefensiveStyle_ScalesDownItsOwnMaxHit()
    {
        var model = new DamageModel(new MaxRollRandom());
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Defensive), new DefenderProfile(0));
        Assert.Equal(18, result.Damage); // 2 × (10 × 0.90) = 18
    }

    [Fact]
    public void DefPoints_ReduceDamage_CappedAt40Percent()
    {
        var model = new DamageModel(new MaxRollRandom());
        // Max roll = 2 × 100 = 200; 0.4%/pt × 100 pts = 40% cap → 200 × 0.6 = 120.
        var capped = model.Roll(new AttackerProfile(100, 0, AttackStyle.Accurate), new DefenderProfile(100));
        Assert.Equal(120, capped.Damage);

        // Beyond 100 points, mitigation stays capped at 40%.
        var overCap = model.Roll(new AttackerProfile(100, 0, AttackStyle.Accurate), new DefenderProfile(500));
        Assert.Equal(120, overCap.Damage);
    }

    [Fact]
    public void LineDamageBonus_ScalesMaxHitMultiplicatively()
    {
        var model = new DamageModel(new MaxRollRandom());
        var result = model.Roll(new AttackerProfile(10, 0, AttackStyle.Accurate, LineDamageBonus: 0.05), new DefenderProfile(0));
        Assert.Equal(21, result.Damage); // 2 × (10 × 1.05) = 21
    }

    [Fact]
    public void UniformRoll_MeanApproximatesPower_AndNeverExceedsTwicePower()
    {
        // Power is the MEAN of the 0..2×Power roll: over many hits the average
        // sits near Power and nothing ever exceeds 2×Power or drops below 0.
        var model = new DamageModel(new SeededRandom(1234));
        const int power = 10, n = 20000;
        long total = 0; int min = int.MaxValue, max = int.MinValue, hits = 0;
        for (int i = 0; i < n; i++)
        {
            var r = model.Roll(new AttackerProfile(power, 0, AttackStyle.Accurate), new DefenderProfile(0));
            if (!r.Hit) continue; // Accurate = 90% hit; damage stats over the hits
            hits++;
            total += r.Damage;
            min = Math.Min(min, r.Damage);
            max = Math.Max(max, r.Damage);
        }
        double mean = (double)total / hits;
        Assert.InRange(mean, power - 0.6, power + 0.6); // ≈ Power
        Assert.True(min >= 0);
        Assert.True(max <= 2 * power); // never exceeds the max hit
    }
}
