using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Domain.Tests;

public sealed class StyleRotationTests
{
    private static NpcInstance BuildSwitcher(int attacksPerStyle = 2) =>
        new(new NpcTemplate("switcher", "Switcher", "",
            new CombatStats(90, 90, 75, 85),
            new ItemModifiers(SlashAttack: 80, RangedAttack: 80, StrengthBonus: 80),
            AttackType.Slash,
            [],
            styleRotation: [AttackType.Slash, AttackType.Ranged],
            attacksPerStyle: attacksPerStyle));

    [Fact]
    public void StartsOnFirstRotationStyle()
    {
        var npc = BuildSwitcher();
        Assert.Equal(AttackType.Slash, npc.CurrentAttackType);
    }

    [Fact]
    public void RotatesAfterConfiguredAttacks()
    {
        var npc = BuildSwitcher(attacksPerStyle: 2);
        Assert.False(npc.AdvanceStyle()); // attack 1
        Assert.True(npc.AdvanceStyle());  // attack 2 → rotate
        Assert.Equal(AttackType.Ranged, npc.CurrentAttackType);
    }

    [Fact]
    public void RotationWrapsAround()
    {
        var npc = BuildSwitcher(attacksPerStyle: 1);
        Assert.True(npc.AdvanceStyle());
        Assert.Equal(AttackType.Ranged, npc.CurrentAttackType);
        Assert.True(npc.AdvanceStyle());
        Assert.Equal(AttackType.Slash, npc.CurrentAttackType);
    }

    [Fact]
    public void SingleStyleNpc_NeverRotates()
    {
        var npc = new NpcInstance(new NpcTemplate("static", "Static", "",
            new CombatStats(50, 50, 50, 50), ItemModifiers.Zero, AttackType.Crush, []));
        for (int i = 0; i < 10; i++)
            Assert.False(npc.AdvanceStyle());
        Assert.Equal(AttackType.Crush, npc.CurrentAttackType);
    }

    [Fact]
    public void OverrideSpeedsUpRotation()
    {
        var npc = BuildSwitcher(attacksPerStyle: 4);
        npc.AttacksPerStyleOverride = 1;
        Assert.True(npc.AdvanceStyle());
        Assert.Equal(AttackType.Ranged, npc.CurrentAttackType);
    }
}

public sealed class ItemModifierRoutingTests
{
    [Fact]
    public void RangedAndMagicBonuses_AreRouted()
    {
        var mods = new ItemModifiers(RangedAttack: 40, MagicAttack: 50, RangedDefence: 30, MagicDefence: 20);
        Assert.Equal(40, mods.AttackBonusFor(AttackType.Ranged));
        Assert.Equal(50, mods.AttackBonusFor(AttackType.Magic));
        Assert.Equal(30, mods.DefenceBonusFor(AttackType.Ranged));
        Assert.Equal(20, mods.DefenceBonusFor(AttackType.Magic));
    }
}
