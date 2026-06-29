using Duels.Application.Abstractions;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryNpcRepository : INpcRepository
{
    private readonly Dictionary<string, NpcTemplate> _npcs;

    public InMemoryNpcRepository()
    {
        _npcs = BuildNpcs().ToDictionary(n => n.Id);
    }

    public NpcTemplate? GetTemplate(string npcId) => _npcs.GetValueOrDefault(npcId);
    public IReadOnlyList<NpcTemplate> GetAll() => _npcs.Values.ToList();

    private static IEnumerable<NpcTemplate> BuildNpcs() =>
    [
        new("goblin", "Goblin",
            "A small green creature with a bad attitude.",
            new CombatStats(1, 1, 1, 5),
            ItemModifiers.Zero,
            AttackType.Crush,
            [new LootEntry("bronze_dagger", 0.75), new LootEntry("bronze_med_helm", 0.25)],
            goldReward: 3),

        new("guard", "Guard",
            "A city guard protecting the town gates.",
            new CombatStats(11, 11, 11, 13),
            new ItemModifiers(StabDefence: 8, SlashDefence: 10, CrushDefence: 6),
            AttackType.Slash,
            [new LootEntry("iron_sword", 0.6), new LootEntry("iron_full_helm", 0.2)],
            goldReward: 12),

        new("barbarian", "Barbarian",
            "A fierce warrior from the northern lands.",
            new CombatStats(18, 20, 14, 18),
            new ItemModifiers(StabDefence: 4, SlashDefence: 4, CrushDefence: 4),
            AttackType.Slash,
            [new LootEntry("steel_longsword", 0.5), new LootEntry("iron_platebody", 0.15)],
            goldReward: 25),

        new("chaos_druid", "Chaos Druid",
            "A druid who abandoned the light. Carries potions.",
            new CombatStats(7, 7, 4, 12),
            ItemModifiers.Zero,
            AttackType.Crush,
            [],
            goldReward: 15),

        new("black_knight", "Black Knight",
            "A dark knight in service of the Black Knights' Fortress.",
            new CombatStats(27, 30, 25, 28),
            new ItemModifiers(StabDefence: 20, SlashDefence: 22, CrushDefence: 18),
            AttackType.Slash,
            [new LootEntry("mithril_scimitar", 0.4), new LootEntry("iron_kiteshield", 0.3)],
            goldReward: 50),

        new("lesser_demon", "Lesser Demon",
            "A creature from the Infernal Plane. Dangerous.",
            new CombatStats(62, 68, 55, 70),
            new ItemModifiers(StabDefence: 35, SlashDefence: 35, CrushDefence: 35),
            AttackType.Crush,
            [new LootEntry("rune_full_helm", 0.3), new LootEntry("rune_scimitar", 0.15), new LootEntry("rune_platebody", 0.05)],
            goldReward: 150),

        new("greater_demon", "Greater Demon",
            "A powerful demon. Only the strongest survive.",
            new CombatStats(82, 88, 70, 85),
            new ItemModifiers(StabDefence: 55, SlashDefence: 55, CrushDefence: 55),
            AttackType.Crush,
            [new LootEntry("dragon_dagger", 0.2), new LootEntry("rune_platebody", 0.1)],
            goldReward: 300),

        new("abyssal_demon", "Abyssal Demon",
            "A Slayer assignment few survive. The whip awaits.",
            new CombatStats(85, 90, 80, 95),
            new ItemModifiers(StabDefence: 60, SlashDefence: 60, CrushDefence: 60, MagicDefence: 60),
            AttackType.Slash,
            [new LootEntry("abyssal_whip", 0.1)],
            goldReward: 500),
    ];
}
