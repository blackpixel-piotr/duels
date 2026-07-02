using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryItemRepository : IItemRepository
{
    private readonly Dictionary<string, GearPiece> _gear;
    private readonly Dictionary<string, Weapon> _weapons;
    private readonly Dictionary<string, int> _shopPrices;

    private static readonly Dictionary<string, string> ConsumableNames = new()
    {
        ["shark"]               = "Shark",
        ["karambwan"]           = "Karambwan",
        ["anglerfish"]          = "Anglerfish",
        ["super_combat_potion"] = "Super Combat Potion",
        ["red_partyhat"]        = "Red Partyhat",
        ["blue_partyhat"]       = "Blue Partyhat",
        ["white_partyhat"]      = "White Partyhat",
    };

    public InMemoryItemRepository()
    {
        _weapons = BuildWeapons().ToDictionary(w => w.Id);
        _gear = BuildGear(_weapons).ToDictionary(g => g.Id);
        _shopPrices = BuildShopPrices();
    }

    public GearPiece? GetGear(string itemId) => _gear.GetValueOrDefault(itemId);
    public Weapon? GetWeapon(string itemId) => _weapons.GetValueOrDefault(itemId);
    public string? GetItemName(string itemId) =>
        ConsumableNames.GetValueOrDefault(itemId)
        ?? (_gear.GetValueOrDefault(itemId)?.Name)
        ?? (_weapons.GetValueOrDefault(itemId)?.Name);
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
        ["karambwan"]          = 200,
        ["rune_scimitar"]      = 200,
        ["shark"]              = 500,
        ["dragon_scimitar"]    = 600,
        ["anglerfish"]         = 700,
        ["dragon_dagger"]      = 800,
        ["super_combat_potion"]= 1_000,
        ["granite_maul"]       = 1_800,
        ["abyssal_whip"]       = 2_500,
        ["zamorakian_hasta"]   = 3_500,
        ["armadyl_sword"]      = 5_000,
        ["dragon_claws"]       = 15_000,
        ["abyssal_bludgeon"]   = 28_000,
        ["bandos_godsword"]    = 30_000,
        ["zamorak_godsword"]   = 40_000,
        ["ghrazi_rapier"]      = 45_000,
        ["saradomin_godsword"] = 50_000,
        ["armadyl_godsword"]   = 65_000,
        ["elder_maul"]         = 90_000,
        ["scythe_of_vitur"]    = 150_000,
    };

    private static IEnumerable<Weapon> BuildWeapons() =>
    [
        new("rune_scimitar", "Rune Scimitar", AttackType.Slash,
            new ItemModifiers(StabAttack: 45, SlashAttack: 67, StrengthBonus: 66),
            attackSpeed: 4, examineText: "A powerful rune scimitar.", attackLevelRequired: 60),

        new("dragon_scimitar", "Dragon Scimitar", AttackType.Slash,
            new ItemModifiers(StabAttack: 40, SlashAttack: 60, StrengthBonus: 67),
            attackSpeed: 4, examineText: "A fearsome dragon scimitar.", attackLevelRequired: 65),

        new("dragon_dagger", "Dragon Dagger", AttackType.Stab,
            new ItemModifiers(StabAttack: 40, SlashAttack: 25, StrengthBonus: 40),
            attackSpeed: 4, examineText: "A vicious dragon dagger.",
            special: new SpecialAttack("!spec", 25, 1.0, "Double stab — boosted accuracy, uses 25% energy.",
                Hits: 2, AccuracyMultiplier: 1.15), attackLevelRequired: 65),

        new("abyssal_whip", "Abyssal Whip", AttackType.Slash,
            new ItemModifiers(StabAttack: 82, SlashAttack: 82, StrengthBonus: 82),
            attackSpeed: 4, examineText: "Fast, accurate, and drains nothing. Pure DPS.", attackLevelRequired: 70),

        new("armadyl_sword", "Armadyl Sword", AttackType.Slash,
            new ItemModifiers(StabAttack: 75, SlashAttack: 80, StrengthBonus: 85),
            attackSpeed: 4, examineText: "A sword blessed by Armadyl.", attackLevelRequired: 72),

        new("dragon_claws", "Dragon Claws", AttackType.Slash,
            new ItemModifiers(StabAttack: 41, SlashAttack: 57, StrengthBonus: 56),
            attackSpeed: 4, examineText: "Four rapid slashes.",
            special: new SpecialAttack("!spec", 50, 0.5, "Four rapid hits — each at half max hit. Uses 50% energy.",
                Hits: 4, AccuracyMultiplier: 1.0), attackLevelRequired: 75),

        new("bandos_godsword", "Bandos Godsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 132, SlashAttack: 132, StrengthBonus: 132),
            attackSpeed: 6, examineText: "The weapon of the Big High War God.",
            special: new SpecialAttack("!spec", 50, 1.0, "Two powerful strikes. Uses 50% energy.",
                Hits: 2, AccuracyMultiplier: 1.0), attackLevelRequired: 80),

        new("zamorak_godsword", "Zamorak Godsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 132, SlashAttack: 132, StrengthBonus: 132),
            attackSpeed: 6, examineText: "Frozen in time, twice as deadly.",
            special: new SpecialAttack("!spec", 50, 1.0, "Two hits — second always connects. Uses 50% energy.",
                Hits: 2, AccuracyMultiplier: 1.0, SecondHitGuaranteed: true), attackLevelRequired: 80),

        new("saradomin_godsword", "Saradomin Godsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 132, SlashAttack: 132, StrengthBonus: 132),
            attackSpeed: 6, examineText: "Heals the faithful.",
            special: new SpecialAttack("!spec", 50, 1.0, "Heals 50% of damage dealt. Uses 50% energy.",
                Hits: 1, AccuracyMultiplier: 1.0, HealOnHit: true), attackLevelRequired: 80),

        new("armadyl_godsword", "Armadyl Godsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 132, SlashAttack: 132, StrengthBonus: 132),
            attackSpeed: 6, examineText: "The most powerful spec in the arena.",
            special: new SpecialAttack("!spec", 50, 1.25, "Massive single hit — 125% damage, boosted accuracy. Uses 50% energy.",
                Hits: 1, AccuracyMultiplier: 1.375), attackLevelRequired: 82),

        new("scythe_of_vitur", "Scythe of Vitur", AttackType.Slash,
            new ItemModifiers(StabAttack: 75, SlashAttack: 110, StrengthBonus: 75),
            attackSpeed: 5, examineText: "Three hits per special. Costs 100% energy.",
            special: new SpecialAttack("!spec", 100, 1.0, "Three sweeping hits. Uses 100% energy.",
                Hits: 3, AccuracyMultiplier: 1.0), attackLevelRequired: 90),

        new("corrupted_whip", "Corrupted Whip", AttackType.Slash,
            new ItemModifiers(StabAttack: 90, SlashAttack: 90, StrengthBonus: 90),
            attackSpeed: 4, examineText: "A whip twisted by dark energy. Untradeable.",
            special: new SpecialAttack("!spec", 25, 1.0, "Dark lash — boosted strength. Uses 25% energy.",
                Hits: 1, AccuracyMultiplier: 1.10), attackLevelRequired: 75),

        // ─── Crush line ───
        new("granite_maul", "Granite Maul", AttackType.Crush,
            new ItemModifiers(CrushAttack: 81, StrengthBonus: 79),
            attackSpeed: 5, examineText: "Heavy granite on a stick. Smashes light armor.",
            special: new SpecialAttack("!spec", 50, 0.9, "Two quick smashes at 90% damage. Uses 50% energy.",
                Hits: 2, AccuracyMultiplier: 1.0), attackLevelRequired: 65),

        new("abyssal_bludgeon", "Abyssal Bludgeon", AttackType.Crush,
            new ItemModifiers(CrushAttack: 102, StrengthBonus: 85),
            attackSpeed: 4, examineText: "A spiked club from the abyss.",
            special: new SpecialAttack("!spec", 50, 1.2, "A crushing blow at 120% damage. Uses 50% energy.",
                Hits: 1, AccuracyMultiplier: 1.0), attackLevelRequired: 78),

        new("elder_maul", "Elder Maul", AttackType.Crush,
            new ItemModifiers(CrushAttack: 135, StrengthBonus: 147),
            attackSpeed: 6, examineText: "Colossal. Slow. Ruinous.",
            special: new SpecialAttack("!spec", 50, 1.0, "An accurate overhead slam. Uses 50% energy.",
                Hits: 1, AccuracyMultiplier: 1.25), attackLevelRequired: 85),

        // ─── Stab line ───
        new("zamorakian_hasta", "Zamorakian Hasta", AttackType.Stab,
            new ItemModifiers(StabAttack: 85, SlashAttack: 30, StrengthBonus: 75),
            attackSpeed: 4, examineText: "A blessed spear. Pierces heavy defences.",
            attackLevelRequired: 70),

        new("ghrazi_rapier", "Ghrazi Rapier", AttackType.Stab,
            new ItemModifiers(StabAttack: 94, SlashAttack: 35, StrengthBonus: 89),
            attackSpeed: 4, examineText: "A vampyric blade of surgical precision.",
            attackLevelRequired: 80),
    ];

    private static IEnumerable<GearPiece> BuildGear(Dictionary<string, Weapon> weapons)
    {
        foreach (var w in weapons.Values)
            yield return w.AsGearPiece();
    }
}
