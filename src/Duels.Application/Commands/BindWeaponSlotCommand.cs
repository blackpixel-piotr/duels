using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Loadout Editor: bind (or clear, WeaponId=null) one of the 4 action-bar
/// slots. Manual binding only — acquiring a weapon never auto-fills a slot
/// (UI bible §4).</summary>
public sealed record BindWeaponSlotCommand(string PlayerId, int Slot, string? WeaponId) : IGameCommand;
