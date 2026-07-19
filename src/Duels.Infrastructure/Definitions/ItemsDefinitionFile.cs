using Duels.Domain.Entities;

namespace Duels.Infrastructure.Definitions;

/// <summary>Root shape of items.json — mirrors the items doc's tables 1:1
/// (weapons, gear, consumable names, shop prices, fence values). Deserializes
/// straight into the domain entities: <see cref="Weapon"/> and
/// <see cref="GearPiece"/> each have a single public constructor, which
/// System.Text.Json binds to by parameter name.</summary>
internal sealed record ItemsDefinitionFile(
    List<Weapon> Weapons,
    List<GearPiece> Gear,
    List<ConsumableDefinition> Consumables,
    Dictionary<string, int> ShopPrices,
    Dictionary<string, int> FenceValues);

internal sealed record ConsumableDefinition(string Id, string Name);
