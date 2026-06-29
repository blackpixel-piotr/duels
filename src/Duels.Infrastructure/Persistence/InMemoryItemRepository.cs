using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryItemRepository : IItemRepository
{
    private readonly Dictionary<string, GearPiece> _gear;
    private readonly Dictionary<string, Weapon> _weapons;

    public InMemoryItemRepository()
    {
        _weapons = BuildWeapons().ToDictionary(w => w.Id);
        _gear = BuildGear(_weapons).ToDictionary(g => g.Id);
    }

    public GearPiece? GetGear(string itemId) => _gear.GetValueOrDefault(itemId);
    public Weapon? GetWeapon(string itemId) => _weapons.GetValueOrDefault(itemId);
    public string? GetItemName(string itemId) =>
        (_gear.GetValueOrDefault(itemId)?.Name) ?? (_weapons.GetValueOrDefault(itemId)?.Name);
    public bool IsWeapon(string itemId) => _weapons.ContainsKey(itemId);

    private static IEnumerable<Weapon> BuildWeapons() =>
    [
        new("bronze_dagger", "Bronze Dagger", AttackType.Stab,
            new ItemModifiers(StabAttack: 4, SlashAttack: 3, StrengthBonus: 3),
            attackSpeed: 4, examineText: "A dagger made of bronze. Better than nothing."),
        new("bronze_sword", "Bronze Sword", AttackType.Slash,
            new ItemModifiers(StabAttack: 4, SlashAttack: 5, CrushAttack: -2, StrengthBonus: 6),
            attackSpeed: 5, examineText: "A short bronze sword."),
        new("iron_sword", "Iron Sword", AttackType.Slash,
            new ItemModifiers(StabAttack: 7, SlashAttack: 9, CrushAttack: -2, StrengthBonus: 10),
            attackSpeed: 5, examineText: "An iron sword."),
        new("steel_longsword", "Steel Longsword", AttackType.Slash,
            new ItemModifiers(StabAttack: 12, SlashAttack: 17, CrushAttack: -1, StrengthBonus: 17),
            attackSpeed: 5, examineText: "A sturdy steel longsword."),
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
            special: new SpecialAttack("!dds", 25, 1.15, "Rapid double stab — 115% damage, uses 25% special energy.")),
        new("abyssal_whip", "Abyssal Whip", AttackType.Slash,
            new ItemModifiers(StabAttack: 82, SlashAttack: 82, CrushAttack: 0, StrengthBonus: 82),
            attackSpeed: 4, examineText: "A weapon from the Abyssal Demons.",
            special: new SpecialAttack("!whip", 50, 1.0, "Drain target attack level by 5.")),
    ];

    private static IEnumerable<GearPiece> BuildGear(Dictionary<string, Weapon> weapons)
    {
        // Include weapon gear pieces
        foreach (var w in weapons.Values)
            yield return w.AsGearPiece();

        // Helmets
        yield return new("bronze_med_helm", "Bronze Med Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 4, SlashDefence: 6, CrushDefence: 3), "A basic bronze helmet.");
        yield return new("iron_full_helm", "Iron Full Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 8, SlashDefence: 9, CrushDefence: 7), "A sturdy iron helm.");
        yield return new("rune_full_helm", "Rune Full Helm", EquipmentSlot.Helmet,
            new ItemModifiers(StabDefence: 30, SlashDefence: 32, CrushDefence: 27), "A full helm of rune.");

        // Bodies
        yield return new("bronze_chainbody", "Bronze Chainbody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 6, SlashDefence: 10, CrushDefence: 4), "A chain of bronze rings.");
        yield return new("iron_platebody", "Iron Platebody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 16, SlashDefence: 17, CrushDefence: 15), "A solid iron platebody.");
        yield return new("rune_platebody", "Rune Platebody", EquipmentSlot.Body,
            new ItemModifiers(StabDefence: 82, SlashDefence: 80, CrushDefence: 72), "The finest melee armour.");

        // Shields
        yield return new("wooden_shield", "Wooden Shield", EquipmentSlot.Shield,
            new ItemModifiers(StabDefence: 3, SlashDefence: 2, CrushDefence: 4), "A simple wooden shield.");
        yield return new("iron_kiteshield", "Iron Kiteshield", EquipmentSlot.Shield,
            new ItemModifiers(StabDefence: 11, SlashDefence: 12, CrushDefence: 10), "An iron kite shield.");
    }
}
