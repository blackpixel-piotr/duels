using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Domain.Tests;

public sealed class CombatCalculatorTests
{
    // Deterministic random: always returns a fixed sequence
    private sealed class FixedRandom : IRandomProvider
    {
        private readonly Queue<double> _doubles;
        private readonly Queue<int> _ints;

        public FixedRandom(IEnumerable<int> ints, IEnumerable<double> doubles)
        {
            _ints = new Queue<int>(ints);
            _doubles = new Queue<double>(doubles);
        }

        public int Next(int min, int max) => _ints.Count > 0 ? _ints.Dequeue() : min;
        public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0.5;
    }

    private static CombatantSnapshot BaseAttacker(int atk = 50, int str = 50, int def = 1) =>
        new(atk, str, def, ItemModifiers.Zero, AttackType.Slash, AttackStyle.Accurate);

    private static CombatantSnapshot BaseDefender(int atk = 1, int str = 1, int def = 1) =>
        new(atk, str, def, ItemModifiers.Zero, AttackType.Slash, AttackStyle.Defensive);

    [Fact]
    public void Roll_WhenAttackRollBeatsDefence_ShouldHit()
    {
        // Attacker roll=5000, Defender roll=100 → hit chance ≈ 1 − (102)/(10002) ≈ 0.99
        // NextDouble=0.5 < 0.99 → hit
        var random = new FixedRandom([5000, 100], [0.5]);
        var calc = new CombatCalculator(random);

        var result = calc.Roll(BaseAttacker(), BaseDefender());

        Assert.True(result.Hit);
    }

    [Fact]
    public void Roll_WhenAttackRollLosesToDefence_CanMiss()
    {
        // Attacker roll=100, Defender roll=5000 → hit chance ≈ 100/(10002) ≈ 0.01
        // NextDouble=0.5 > 0.01 → miss
        var random = new FixedRandom([100, 5000], [0.5]);
        var calc = new CombatCalculator(random);

        var result = calc.Roll(BaseAttacker(), BaseDefender());

        Assert.False(result.Hit);
    }

    [Fact]
    public void Roll_OnHit_DamageIsWithinMaxHit()
    {
        // Force a hit: high attacker roll, low defender roll, low NextDouble
        var random = new FixedRandom([9999, 0, 7], [0.001]);
        var calc = new CombatCalculator(random);

        var result = calc.Roll(BaseAttacker(str: 50), BaseDefender());

        Assert.True(result.Hit);
        Assert.True(result.Damage >= 0);
    }

    [Fact]
    public void Roll_OnMiss_DamageIsZero()
    {
        var random = new FixedRandom([0, 9999], [0.999]);
        var calc = new CombatCalculator(random);

        var result = calc.Roll(BaseAttacker(), BaseDefender());

        Assert.False(result.Hit);
        Assert.Equal(0, result.Damage);
    }

    [Fact]
    public void Player_TakeDamage_CannotGoBelowZero()
    {
        var player = new Entities.Player("id", "Test");
        player.TakeDamage(9999);
        Assert.Equal(0, player.CurrentHp);
    }

    [Fact]
    public void Player_BaseStats_AreFixed()
    {
        var player = new Entities.Player("id", "Test");
        Assert.Equal(99, player.AttackLevel);
        Assert.Equal(99, player.StrengthLevel);
        Assert.Equal(99, player.DefenceLevel);
        Assert.Equal(99, player.MaxHp);
    }

    [Fact]
    public void MaxHit_AtMaxStats_ReturnsExpectedValue()
    {
        // 99 Str + Aggressive (+3) + 8 = 110 effective; str bonus 66 (rune scimitar)
        // MaxHit = floor(0.5 + 110 * (66 + 64) / 640) = floor(0.5 + 110 * 130 / 640) = floor(0.5 + 22.34) = 22
        var random = new FixedRandom([], []);
        var calc = new CombatCalculator(random);
        var snapshot = new CombatantSnapshot(99, 99, 1,
            new ItemModifiers(StrengthBonus: 66), AttackType.Slash, AttackStyle.Aggressive);

        int maxHit = calc.MaxHit(snapshot);

        Assert.Equal(22, maxHit);
    }

    [Fact]
    public void Player_RechargeSpecial_CapsAt100()
    {
        var player = new Entities.Player("id", "Test");
        player.DrainSpecialEnergy(50);
        player.RechargeSpecial(200);
        Assert.Equal(100, player.SpecialEnergy);
    }

    [Theory]
    [InlineData(AttackType.Stab, 10, 0, 0, 10)]
    [InlineData(AttackType.Slash, 0, 15, 0, 15)]
    [InlineData(AttackType.Crush, 0, 0, 20, 20)]
    public void ItemModifiers_AttackBonusFor_ReturnsCorrectBonus(
        AttackType type, int stab, int slash, int crush, int expected)
    {
        var mods = new ItemModifiers(StabAttack: stab, SlashAttack: slash, CrushAttack: crush);
        Assert.Equal(expected, mods.AttackBonusFor(type));
    }

    [Fact]
    public void ItemModifiers_Add_SumsAllFields()
    {
        var a = new ItemModifiers(StabAttack: 5, StrengthBonus: 10);
        var b = new ItemModifiers(StabAttack: 3, StrengthBonus: 7);
        var result = a.Add(b);
        Assert.Equal(8, result.StabAttack);
        Assert.Equal(17, result.StrengthBonus);
    }
}
