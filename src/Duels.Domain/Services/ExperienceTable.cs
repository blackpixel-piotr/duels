namespace Duels.Domain.Services;

/// <summary>
/// Flat XP curve for a session game: levels 60–99, XpToNext(L) = 100 + 15*(L-60).
/// Total 60→99 is 15,015 XP per skill (~3,750 damage at 4 xp per damage).
/// </summary>
public static class ExperienceTable
{
    public const int MinLevel = 60;
    public const int MaxLevel = 99;

    private static readonly int[] Cumulative = BuildCumulative();

    public const int MaxLevelXp = 15_015;

    private static int[] BuildCumulative()
    {
        // Cumulative[i] = total xp required to reach level (MinLevel + i)
        var arr = new int[MaxLevel - MinLevel + 1];
        arr[0] = 0;
        for (int lvl = MinLevel; lvl < MaxLevel; lvl++)
            arr[lvl - MinLevel + 1] = arr[lvl - MinLevel] + XpToNext(lvl);
        return arr;
    }

    public static int XpToNext(int level) => 100 + 15 * (level - MinLevel);

    public static int XpForLevel(int level)
    {
        if (level <= MinLevel) return 0;
        if (level >= MaxLevel) return Cumulative[^1];
        return Cumulative[level - MinLevel];
    }

    public static int LevelForXp(int xp)
    {
        if (xp <= 0) return MinLevel;
        for (int i = Cumulative.Length - 1; i >= 0; i--)
            if (xp >= Cumulative[i])
                return MinLevel + i;
        return MinLevel;
    }

    /// <summary>Progress toward the next level as 0–100, or 100 at cap.</summary>
    public static int ProgressPercent(int xp)
    {
        int level = LevelForXp(xp);
        if (level >= MaxLevel) return 100;
        int floor = XpForLevel(level);
        int span = XpToNext(level);
        return span == 0 ? 100 : (int)((xp - floor) * 100.0 / span);
    }
}
