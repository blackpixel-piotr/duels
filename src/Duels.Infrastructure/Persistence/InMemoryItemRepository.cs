using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryItemRepository : IItemRepository
{
    private readonly Dictionary<string, GearPiece> _gear;
    private readonly Dictionary<string, Weapon> _weapons;
    private readonly Dictionary<string, int> _shopPrices;
    private readonly Dictionary<string, int> _fenceValues;

    private static readonly Dictionary<string, string> ConsumableNames = new()
    {
        ["shark"]               = "Shark",
        ["karambwan"]           = "Karambwan",
        ["anglerfish"]          = "Anglerfish",
        ["super_combat_potion"] = "Super Combat Potion",
        ["antidote"]            = "Antidote",
        ["red_partyhat"]        = "Red Partyhat",
        ["blue_partyhat"]       = "Blue Partyhat",
        ["white_partyhat"]      = "White Partyhat",
    };

    public InMemoryItemRepository()
    {
        _weapons = BuildWeapons().Concat(BuildDropOnlyWeapons()).ToDictionary(w => w.Id);
        _gear = BuildGear(_weapons).Concat(BuildArmor()).Concat(BuildDropOnlyGear()).ToDictionary(g => g.Id);
        _shopPrices = BuildShopPrices();
        _fenceValues = BuildFenceValues();
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

    public int GetFenceValue(string itemId)
    {
        if (_shopPrices.TryGetValue(itemId, out var price)) return price / 2;
        return _fenceValues.GetValueOrDefault(itemId, 100);
    }

    private static Dictionary<string, int> BuildFenceValues() => new()
    {
        ["lucky_doubloon"]      = 500,
        ["amulet_of_strength"]  = 2_000,
        ["venomous_fang"]       = 3_000,
        ["arena_defender"]      = 5_000,
        ["pirates_hook"]        = 10_000,
        ["berserker_ring"]      = 15_000,
        ["warlords_bulwark"]    = 40_000,
        ["champions_cape"]      = 100_000,
        ["corrupted_whip"]      = 30_000,
    };

    private static Dictionary<string, int> BuildShopPrices() => new()
    {
        ["karambwan"]          = 200,
        ["rune_scimitar"]      = 200,
        ["shark"]              = 500,
        ["dragon_scimitar"]    = 600,
        ["anglerfish"]         = 700,
        ["dragon_dagger"]      = 800,
        ["super_combat_potion"]= 1_000,
        ["antidote"]           = 300,
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

        // ─── Armor ───
        ["rune_full_helm"]      = 500,
        ["rune_kiteshield"]     = 800,
        ["rune_platelegs"]      = 1_200,
        ["rune_platebody"]      = 2_000,
        ["dragon_boots"]        = 4_000,
        ["dragon_med_helm"]     = 3_000,
        ["dragon_sq_shield"]    = 4_000,
        ["dragon_platelegs"]    = 6_000,
        ["barrows_gloves"]      = 6_000,
        ["dragon_platebody"]    = 9_000,
        ["amulet_of_fury"]      = 15_000,
        ["justiciar_faceguard"] = 30_000,
        ["dragonfire_shield"]   = 35_000,
        ["bandos_tassets"]      = 45_000,
        ["bandos_chestplate"]   = 60_000,
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

    // "Melee" defence = same value across stab/slash/crush.
    private static ItemModifiers MeleeDef(int melee, int ranged, int magic, int str = 0, int pray = 0) =>
        new(StabDefence: melee, SlashDefence: melee, CrushDefence: melee,
            RangedDefence: ranged, MagicDefence: magic, StrengthBonus: str, PrayerBonus: pray);

    private static IEnumerable<GearPiece> BuildArmor() =>
    [
        // ─── Rune tier (Def 60) ───
        new("rune_full_helm", "Rune Full Helm", EquipmentSlot.Helmet,
            MeleeDef(30, 30, -5), "Sturdy rune headgear.", defenceLevelRequired: 60),
        new("rune_kiteshield", "Rune Kiteshield", EquipmentSlot.Shield,
            MeleeDef(40, 40, -5), "A broad rune shield.", defenceLevelRequired: 60),
        new("rune_platelegs", "Rune Platelegs", EquipmentSlot.Legs,
            MeleeDef(50, 45, -10), "Heavy rune leg plates.", defenceLevelRequired: 60),
        new("rune_platebody", "Rune Platebody", EquipmentSlot.Body,
            MeleeDef(80, 75, -20), "A full rune chestplate.", defenceLevelRequired: 60),

        // ─── Dragon tier (Def 70) ───
        new("dragon_med_helm", "Dragon Med Helm", EquipmentSlot.Helmet,
            MeleeDef(45, 45, 0), "A fearsome dragon-forged helm.", defenceLevelRequired: 70),
        new("dragon_sq_shield", "Dragon Sq Shield", EquipmentSlot.Shield,
            MeleeDef(55, 55, 0), "A dragon-forged square shield.", defenceLevelRequired: 70),
        new("dragon_platelegs", "Dragon Platelegs", EquipmentSlot.Legs,
            MeleeDef(70, 65, -10), "Dragon-forged leg plates.", defenceLevelRequired: 70),
        new("dragon_platebody", "Dragon Platebody", EquipmentSlot.Body,
            MeleeDef(110, 100, -15), "A dragon-forged chestplate.", defenceLevelRequired: 70),

        // ─── Bandos / Justiciar tier (Def 82) ───
        new("justiciar_faceguard", "Justiciar Faceguard", EquipmentSlot.Helmet,
            MeleeDef(70, 65, 10, pray: 2), "Blessed plate that shrugs off magic.", defenceLevelRequired: 82),
        new("dragonfire_shield", "Dragonfire Shield", EquipmentSlot.Shield,
            MeleeDef(90, 85, 25), "Forged in dragonfire. Excellent all-round defence.", defenceLevelRequired: 82),
        new("bandos_tassets", "Bandos Tassets", EquipmentSlot.Legs,
            MeleeDef(100, 95, 10, str: 2), "War-god plate leg armor.", defenceLevelRequired: 82),
        new("bandos_chestplate", "Bandos Chestplate", EquipmentSlot.Body,
            MeleeDef(140, 130, 15, str: 4), "The chestplate of the Big High War God.", defenceLevelRequired: 82),

        // ─── Accessories ───
        new("dragon_boots", "Dragon Boots", EquipmentSlot.Boots,
            MeleeDef(12, 12, 0, str: 4), "Dragon-hide boots.", defenceLevelRequired: 65),
        new("barrows_gloves", "Barrows Gloves", EquipmentSlot.Gloves,
            MeleeDef(12, 12, 12, str: 8), "Gloves worn by the Barrows brothers.", defenceLevelRequired: 70),
        new("amulet_of_fury", "Amulet of Fury", EquipmentSlot.Amulet,
            MeleeDef(10, 10, 10, str: 8), "A cursed amulet radiating dark power.", defenceLevelRequired: 70),
    ];

    // ─── Drop-only gear (never sold; farmed from NPC loot tables) ───
    private static IEnumerable<GearPiece> BuildDropOnlyGear() =>
    [
        new("lucky_doubloon", "Lucky Doubloon", EquipmentSlot.Ring,
            ItemModifiers.Zero, "A pirate's lucky coin. Boosts bounty gold by 5%.", defenceLevelRequired: 1),
        new("amulet_of_strength", "Amulet of Strength", EquipmentSlot.Amulet,
            new ItemModifiers(StrengthBonus: 10), "A barbarian talisman.", defenceLevelRequired: 1),
        new("arena_defender", "Arena Defender", EquipmentSlot.Shield,
            MeleeDef(25, 20, 0, str: 6), "A gladiator's offensive buckler.", defenceLevelRequired: 60),
        new("pirates_hook", "Pirate's Hook", EquipmentSlot.Gloves,
            MeleeDef(10, 10, 10, str: 10), "A corsair's cruel prosthetic.", defenceLevelRequired: 65),
        new("berserker_ring", "Berserker Ring", EquipmentSlot.Ring,
            new ItemModifiers(StrengthBonus: 8), "Worn by the frenzied.", defenceLevelRequired: 75),
        new("warlords_bulwark", "Warlord's Bulwark", EquipmentSlot.Shield,
            MeleeDef(110, 100, 40, pray: 3), "A veteran's battle-scarred shield. Best in slot.", defenceLevelRequired: 85),
        new("champions_cape", "Champion's Cape", EquipmentSlot.Cape,
            MeleeDef(40, 40, 40, str: 8, pray: 4), "Worn only by the undefeated.", defenceLevelRequired: 90),
    ];

    private static IEnumerable<Weapon> BuildDropOnlyWeapons() =>
    [
        new("venomous_fang", "Venomous Fang", AttackType.Stab,
            new ItemModifiers(StabAttack: 88, StrengthBonus: 70),
            attackSpeed: 4, examineText: "A poison-tipped dagger looted from the Desert Bandit. 20% chance to poison on hit.",
            attackLevelRequired: 70),
    ];
}
