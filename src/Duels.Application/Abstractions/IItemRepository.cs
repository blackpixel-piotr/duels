using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface IItemRepository
{
    GearPiece? GetGear(string itemId);
    Weapon? GetWeapon(string itemId);
    string? GetItemName(string itemId);
    bool IsWeapon(string itemId);
    IReadOnlyList<(string Id, string Name, int Price)> GetShopItems();
}
