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
        new("bandit", "Bandit",
            "A desperate outlaw looking for easy coin.",
            new CombatStats(5, 6, 4, 8),
            ItemModifiers.Zero,
            AttackType.Slash,
            [],
            goldReward: 15),

        new("skeleton", "Skeleton",
            "Bones held together by dark magic.",
            new CombatStats(10, 12, 8, 14),
            new ItemModifiers(StabDefence: 5, SlashDefence: 5, CrushDefence: -5),
            AttackType.Crush,
            [],
            goldReward: 35),

        new("ghoul", "Ghoul",
            "A foul undead creature that feasts on the dead.",
            new CombatStats(16, 18, 12, 22),
            new ItemModifiers(StabDefence: 10, SlashDefence: 10, CrushDefence: 10),
            AttackType.Crush,
            [],
            goldReward: 70),

        new("troll", "Forest Troll",
            "A massive, dim-witted brute that hits hard.",
            new CombatStats(24, 28, 20, 35),
            new ItemModifiers(StabDefence: 15, SlashDefence: 15, CrushDefence: 15),
            AttackType.Crush,
            [],
            goldReward: 140),

        new("dark_mage", "Dark Mage",
            "A rogue wizard channelling forbidden power.",
            new CombatStats(30, 22, 25, 28),
            new ItemModifiers(StabDefence: 5, SlashDefence: 5, CrushDefence: 5),
            AttackType.Slash,
            [],
            goldReward: 200),

        new("vampire", "Vampire Lord",
            "An ancient predator. Don't let it drain you.",
            new CombatStats(38, 42, 35, 45),
            new ItemModifiers(StabDefence: 20, SlashDefence: 20, CrushDefence: 20),
            AttackType.Slash,
            [],
            goldReward: 400),

        new("orc_warlord", "Orc Warlord",
            "A savage commander hardened by a hundred battles.",
            new CombatStats(55, 60, 48, 65),
            new ItemModifiers(StabDefence: 35, SlashDefence: 35, CrushDefence: 35),
            AttackType.Slash,
            [],
            goldReward: 800),

        new("shadow_dragon", "Shadow Dragon",
            "The apex predator. Few have lived to speak of it.",
            new CombatStats(72, 78, 65, 90),
            new ItemModifiers(StabDefence: 50, SlashDefence: 50, CrushDefence: 50),
            AttackType.Slash,
            [],
            goldReward: 2000),
    ];
}
