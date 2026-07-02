namespace Duels.Web.Models;

public sealed record SaveData(
    string PlayerId,
    string PlayerName,
    int Gold,
    int CurrentHp,
    int SpecialEnergy,
    int PrestigeLevel,
    int WinStreak,
    int BestEndlessWave,
    List<string> Inventory,
    Dictionary<string, string> Equipped,
    List<string> UnlockedOpponents,
    // v2: xp fields — -1 sentinel means "legacy save" (grandfathered to max level on restore)
    int AttackXp = -1,
    int StrengthXp = -1,
    int DefenceXp = -1,
    int HitpointsXp = -1,
    string ChosenStyle = "Accurate"
);
