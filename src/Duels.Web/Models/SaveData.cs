namespace Duels.Web.Models;

/// <summary>Schema v3 (M2 Workstream G.1): adds bank storage back (retired
/// as a ladder-era field in v2, now a real UI bible §7 bank distinct from
/// the old one). v1/v2 saves migrate automatically: System.Text.Json
/// ignores unknown old properties and leaves new ones at their defaults
/// (empty bank).</summary>
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
    List<string?>? LoadoutFlaskSlots = null,
    List<string>? BankedItems = null
);
