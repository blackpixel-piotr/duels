using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Infrastructure.Definitions;

namespace Duels.Infrastructure.Persistence;

/// <summary>Loads all weapons/gear/consumables/prices from items.json (an
/// embedded definition file mirroring the items doc 1:1) instead of hard-coding
/// them in C#. Behavior is identical to the old InMemoryItemRepository it
/// replaces — same lookups, same shop list, same fence values.</summary>
public sealed class DefinitionItemRepository : IItemRepository
{
    private readonly Dictionary<string, GearPiece> _gear;
    private readonly Dictionary<string, Weapon> _weapons;
    private readonly Dictionary<string, int> _shopPrices;
    private readonly Dictionary<string, int> _fenceValues;
    private readonly Dictionary<string, string> _consumableNames;

    public DefinitionItemRepository() : this(DefinitionLoader.Load<ItemsDefinitionFile>("items.json"))
    {
    }

    internal DefinitionItemRepository(ItemsDefinitionFile file)
    {
        _weapons = ToDictionaryOrThrow(file.Weapons, w => w.Id, "items.json", "weapon");

        // Every weapon also exists as its own gear entry in the Weapon slot
        // (Weapon.AsGearPiece()) — same derivation the old builder used.
        var weaponGear = _weapons.Values.Select(w => w.AsGearPiece());
        _gear = ToDictionaryOrThrow(weaponGear.Concat(file.Gear), g => g.Id, "items.json", "gear");

        _consumableNames = ToDictionaryOrThrow(file.Consumables, c => c.Id, "items.json", "consumable")
            .ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        _shopPrices = file.ShopPrices;
        _fenceValues = file.FenceValues;

        ValidateShopPricesResolve();
    }

    public GearPiece? GetGear(string itemId) => _gear.GetValueOrDefault(itemId);
    public Weapon? GetWeapon(string itemId) => _weapons.GetValueOrDefault(itemId);

    public string? GetItemName(string itemId) =>
        _consumableNames.GetValueOrDefault(itemId)
        ?? _gear.GetValueOrDefault(itemId)?.Name
        ?? _weapons.GetValueOrDefault(itemId)?.Name;

    public bool IsWeapon(string itemId) => _weapons.ContainsKey(itemId);

    public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems()
    {
        var result = new List<(string, string, int)>();
        foreach (var (id, price) in _shopPrices.OrderBy(kv => kv.Value))
        {
            var name = GetItemName(id) ?? id;
            result.Add((id, name, price));
        }
        return result;
    }

    /// <summary>Drop-table "common" sell value only (economy doc §3: 15% of
    /// shop-equivalent price, single canonical rate) — NOT shop buyback.
    /// Buyback (100% refund same session, 80% after) is a separate,
    /// session-scoped mechanic that reads purchase history, not item
    /// definitions; it has no representation here. <c>_fenceValues</c> is the
    /// explicit override table for drop-only items with no shop price
    /// (uniques: 15% of a T4 piece per items doc §4; rares: 0, never
    /// sellable). An item with neither a shop price nor an override falls
    /// back to 0 rather than an invented flat number.</summary>
    public int GetFenceValue(string itemId)
    {
        if (_shopPrices.TryGetValue(itemId, out var price)) return (int)Math.Round(price * 0.15);
        return _fenceValues.GetValueOrDefault(itemId, 0);
    }

    /// <summary>Every shop-priced item id must resolve to a real weapon/gear/
    /// consumable — a stray price row for a typo'd id would otherwise fail
    /// silently (GetItemName falls back to the raw id).</summary>
    private void ValidateShopPricesResolve()
    {
        var unresolved = _shopPrices.Keys.Where(id => GetItemName(id) is null).ToList();
        if (unresolved.Count > 0)
            throw new InvalidOperationException(
                $"items.json: shopPrices references unknown item id(s): {string.Join(", ", unresolved)}");
    }

    private static Dictionary<TKey, TValue> ToDictionaryOrThrow<TValue, TKey>(
        IEnumerable<TValue> source, Func<TValue, TKey> keySelector, string fileName, string kind)
        where TKey : notnull
    {
        var dict = new Dictionary<TKey, TValue>();
        foreach (var item in source)
        {
            var key = keySelector(item);
            if (!dict.TryAdd(key, item))
                throw new InvalidOperationException($"{fileName}: duplicate {kind} id '{key}'.");
        }
        return dict;
    }
}
