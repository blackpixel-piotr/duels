using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface IItemRepository
{
    GearPiece? GetGear(string itemId);
    Weapon? GetWeapon(string itemId);
    string? GetItemName(string itemId);
    bool IsWeapon(string itemId);
    IReadOnlyList<(string Id, string Name, int Price)> GetShopItems();
    /// <summary>Gold value when an item can't fit in inventory and must be fenced. Half shop price if sold; a flat value for drop-only items.</summary>
    int GetFenceValue(string itemId);
}
