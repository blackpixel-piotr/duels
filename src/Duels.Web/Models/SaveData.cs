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
    List<string> UnlockedOpponents
);
