using Duels.Domain.Entities;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Domain.Tests;

public sealed class ExperienceTableTests
{
    [Fact]
    public void ZeroXp_IsMinLevel() =>
        Assert.Equal(60, ExperienceTable.LevelForXp(0));

    [Fact]
    public void NegativeXp_IsMinLevel() =>
        Assert.Equal(60, ExperienceTable.LevelForXp(-5));

    [Fact]
    public void FirstLevelBoundary()
    {
        // Level 61 needs XpToNext(60) = 100
        Assert.Equal(60, ExperienceTable.LevelForXp(99));
        Assert.Equal(61, ExperienceTable.LevelForXp(100));
    }

    [Fact]
    public void MaxLevelXp_ReachesLevel99()
    {
        Assert.Equal(99, ExperienceTable.LevelForXp(ExperienceTable.MaxLevelXp));
        Assert.Equal(98, ExperienceTable.LevelForXp(ExperienceTable.MaxLevelXp - 1));
    }

    [Fact]
    public void XpBeyondCap_StaysLevel99() =>
        Assert.Equal(99, ExperienceTable.LevelForXp(1_000_000));

    [Fact]
    public void XpForLevel_RoundTripsWithLevelForXp()
    {
        for (int lvl = 60; lvl <= 99; lvl++)
        {
            int xp = ExperienceTable.XpForLevel(lvl);
            Assert.Equal(lvl, ExperienceTable.LevelForXp(xp));
        }
    }

    [Fact]
    public void MaxLevelXpConstant_MatchesCumulativeSum()
    {
        int total = 0;
        for (int lvl = 60; lvl < 99; lvl++)
            total += ExperienceTable.XpToNext(lvl);
        Assert.Equal(ExperienceTable.MaxLevelXp, total);
        Assert.Equal(ExperienceTable.MaxLevelXp, ExperienceTable.XpForLevel(99));
    }

    [Fact]
    public void ProgressPercent_IsZeroAtLevelFloor_And100AtCap()
    {
        Assert.Equal(0, ExperienceTable.ProgressPercent(0));
        Assert.Equal(100, ExperienceTable.ProgressPercent(ExperienceTable.MaxLevelXp));
    }
}

public sealed class PlayerXpTests
{
    [Fact]
    public void NewPlayer_StartsAtLevel60_With60Hp()
    {
        var p = new Player("p1", "Hero");
        Assert.Equal(60, p.AttackLevel);
        Assert.Equal(60, p.StrengthLevel);
        Assert.Equal(60, p.DefenceLevel);
        Assert.Equal(60, p.MaxHp);
        Assert.Equal(60, p.CurrentHp);
    }

    [Fact]
    public void GainXp_ReportsLevelUps()
    {
        var p = new Player("p1", "Hero");
        var ups = p.GainXp(100, 0, 0, 0);
        Assert.Contains(ups, u => u.Skill == "Attack" && u.NewLevel == 61);
    }

    [Fact]
    public void GainXp_NoLevelUp_ReportsNothing()
    {
        var p = new Player("p1", "Hero");
        var ups = p.GainXp(50, 0, 0, 0);
        Assert.Empty(ups);
    }

    [Fact]
    public void Prestige_KeepsXp()
    {
        var p = new Player("p1", "Hero");
        p.GainXp(500, 500, 500, 500);
        int atkBefore = p.AttackLevel;
        p.Prestige();
        Assert.Equal(atkBefore, p.AttackLevel);
    }

    [Fact]
    public void RestoreFromSave_GrandfatheredXp_GivesLevel99()
    {
        var p = new Player("p1", "Hero");
        p.RestoreFromSave(1000, 99, 100, 0, [], [],
            ExperienceTable.MaxLevelXp, ExperienceTable.MaxLevelXp,
            ExperienceTable.MaxLevelXp, ExperienceTable.MaxLevelXp);
        Assert.Equal(99, p.AttackLevel);
        Assert.Equal(99, p.MaxHp);
        Assert.Equal(99, p.CurrentHp);
    }

    [Fact]
    public void SetStyle_ChangesChosenStyle()
    {
        var p = new Player("p1", "Hero");
        Assert.Equal(AttackStyle.Accurate, p.ChosenStyle);
        p.SetStyle(AttackStyle.Aggressive);
        Assert.Equal(AttackStyle.Aggressive, p.ChosenStyle);
    }
}
