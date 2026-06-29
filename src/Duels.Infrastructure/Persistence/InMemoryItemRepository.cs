using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryItemRepository : IItemRepository
{
    private readonly Dictionary<string, GearPiece> _gear;
    private readonly Dictionary<string, Weapon> _weapons;
    private readonly Dictionary<string, int> _shopPrices;

    public InMemoryItemRepository()
    {
        _weapons = BuildWeapons().ToDictionary(w => w.Id);
        _gear = BuildGear(_weapons).ToDictionary(g => g.Id);
        _shopPrices = BuildShopPrices();
    }

    public GearPiece? GetGear(string itemId) => _gear.GetValueOrDefault(itemId);
    public Weapon? GetWeapon(string itemId) => _weapons.GetValueOrDefault(itemId);
    public string? GetItemName(string itemId) =>
        (_gear.GetValueOrDefault(itemId)?.Name) ?? (_weapons.GetValueOrDefault(itemId)?.Name);
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

    private static Dictionary<string, int> BuildShopPrices() => new()
    {
        // Weapons
        ["iron_sword"]        = 50,
        ["steel_longsword"]   = 200,
        ["mithril_scimitar"]  = 600,
        ["adamant_scimitar"]  = 1_800,
        ["rune_scimitar"]     = 6_000,
        ["dragon_dagger"]     = 20_000,
        ["abyssal_whip"]      = 80_000,
        // Armour
        ["iron_full_helm"]    = 40,
        ["iron_platebody"]    = 100,
        ["mithril_platebody"] = 800,
        ["rune_full_helm"]    = 4_000,
        ["rune_platebody"]    = 15_000,
    };

    private static IEnumerable<Weapon> BuildWeapons() =>
    [
        new("iron_sword", "Iron Sword", AttackType.Slash,
            new ItemModifiers(StabAttack: 7, SlashAttack: 9, CrushAttack: -2, StrengthBonus: 10),
            attackSpeed: 5, examineText: "A sturdy iron sword."),
        new("steel_longsword", "Steel Longsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 12, SlashAttack: 17, CrushAttack: -1, StrengthBonus: 17),
            attackSpeed: 5, examineText: "A solid steel longsword."),
        new("mithril_scimitar", "Mithril Scimitar", AttackType.Slash,
            new ItemModifiers(StabAttack: 18, SlashAttack: 30, CrushAttack: -2, StrengthBonus: 26),
            attackSpeed: 4, examineText: "A fast mithril scimitar."),
        new("adamant_scimitar", "Adamant Scimitar", AttackType.Slash,
            new ItemModifiers(StabAttack: 25, SlashAttack: 42, CrushAttack: -2, StrengthBonus: 38),
            attackSpeed: 4, examineText: "A sharp adamant scimitar."),
        new("rune_scimitar", "Rune Scimitar", AttackType.Slash,
            new ItemModifiers(StabAttack: 45, SlashAttack: 67, CrushAttack: -2, StrengthBonus: 66),
            attackSpeed: 4, examineText: "A powerful rune scimitar."),
        new("dragon_dagger", "Dragon Dagger", AttackType.Stab,
            new ItemModifiers(StabAttack: 40, SlashAttack: 25, StrengthBonus: 40),
            attackSpeed: 4, examineText: "A vicious dragon dagger.",
            special: new SpecialAttack("!spec", 25, 1.15, "Double stab — 115% damage per hit, boosted accuracy, uses 25% special energy.")),
        new("abyssal_whip", "Abyssal Whip", AttackType.Slash,
            new ItemModifiers(StabAttack: 82, SlashAttack: 82, CrushAttack: 0, StrengthBonus: 82),
            attackSpeed: 4, examineText: "A weapon from the Abyssal Plane.",
            special: new SpecialAttack("!spec", 50, 1.0, "Leeches energy from the target. Uses 50% special energy.")),
    ];

    private static IEnumerable<GearPiece> BuildGear(Dictionary<string, Weapon> weapons)
    {
        foreach (var w in weapons.Values)
            yield return w.AsGearPiece();

        yield return new("iron_full_helm", "Iron Full Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 8, SlashDefence: 9, CrushDefence: 7), "A sturdy iron helm.");
        yield return new("mithril_full_helm", "Mithril Full Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 16, SlashDefence: 17, CrushDefence: 14), "A mithril helm.");
        yield return new("rune_full_helm", "Rune Full Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 30, SlashDefence: 32, CrushDefence: 27), "A full helm of rune.");

        yield return new("iron_platebody", "Iron Platebody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 16, SlashDefence: 17, CrushDefence: 15), "A solid iron platebody.");
        yield return new("mithril_platebody", "Mithril Platebody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 40, SlashDefence: 42, CrushDefence: 38), "A solid mithril platebody.");
        yield return new("rune_platebody", "Rune Platebody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 82, SlashDefence: 80, CrushDefence: 72), "The finest melee armour.");
    }
}
