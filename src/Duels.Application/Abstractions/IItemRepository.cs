using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface IItemRepository
{
    GearPiece? GetGear(string itemId);
    Weapon? GetWeapon(string itemId);
    string? GetItemName(string itemId);
    bool IsWeapon(string itemId);
    IReadOnlyList<(string Id, string Name, int Price)> GetShopItems();
    /// <summary>Drop-table sell/fence value (economy doc §3: 15% of shop price,
    /// single canonical rate), not shop buyback — buyback is a separate
    /// session-scoped mechanic that lives outside this repository.</summary>
    int GetFenceValue(string itemId);
}
