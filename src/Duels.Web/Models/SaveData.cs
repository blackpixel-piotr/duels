namespace Duels.Web.Models;

/// <summary>Schema v2 (m1-plan Workstream G): drops the OSRS ladder fields
/// (xp, prestige, win streak, endless, bank, collection log — retired in the
/// M1 sweep) and adds the action bar + flask belt bindings. v1 saves migrate
/// automatically: System.Text.Json ignores the now-unknown old properties and
/// leaves the new ones at their defaults (empty bar).</summary>
public sealed record SaveData(
    string PlayerId,
    string PlayerName,
    int Gold,
    int CurrentHp,
    int SpecialEnergy,
    List<string> Inventory,
    Dictionary<string, string> Equipped,
    string ChosenStyle = "Accurate",
    int? PersonalBestKillTicks = null,
    List<string?>? LoadoutWeaponSlots = null,
    List<string?>? LoadoutFlaskSlots = null
);
