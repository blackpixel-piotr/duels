using Duels.Domain.Entities;

namespace Duels.Web.Models;

/// <summary>Schema v4 (M3 Workstream F): adds per-boss BossRecords (kc/
/// deaths/best-time — UI bible §6's roster/pre-fight screens), replacing
/// the single Maggot-King-implicit PersonalBestKillTicks. v3 saves migrate
/// automatically in GameService.RestoreSaveAsync: PersonalBestKillTicks (if
/// set and no "maggot_king" entry already exists) becomes that boss's
/// BestTimeTicks — the field itself stays on the record so its data isn't
/// silently dropped on the (unlikely) chance BossRecords is somehow already
/// populated from a v4 save with no maggot_king entry yet.</summary>
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
    List<string>? BankedItems = null,
    Dictionary<string, BossRecord>? BossRecords = null
);
