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
        new("swashbuckler", "Swashbuckler Pete",
            "A quick pirate with a cutlass.",
            new CombatStats(99, 99, 99, 50),
            new ItemModifiers(SlashAttack: 30, StrengthBonus: 30),
            AttackType.Slash,
            [],
            goldReward: 30),

        new("barbarian", "Iron Barbarian",
            "A brutish northerner. Hits hard.",
            new CombatStats(99, 99, 99, 65),
            new ItemModifiers(SlashAttack: 50, StrengthBonus: 50),
            AttackType.Slash,
            [],
            goldReward: 100),

        new("desert_bandit", "Desert Bandit",
            "A dagger specialist. Spec at your peril.",
            new CombatStats(99, 99, 99, 70),
            new ItemModifiers(SlashAttack: 40, StrengthBonus: 40),
            AttackType.Slash,
            [],
            goldReward: 300),

        new("gladiator", "Arena Gladiator",
            "Pit-trained, whip in hand.",
            new CombatStats(99, 99, 99, 80),
            new ItemModifiers(SlashAttack: 82, StrengthBonus: 82),
            AttackType.Slash,
            [],
            goldReward: 1_000),

        new("corsair", "Pirate Corsair",
            "Elite sea raider with dragon claws.",
            new CombatStats(99, 99, 99, 85),
            new ItemModifiers(SlashAttack: 56, StrengthBonus: 56),
            AttackType.Slash,
            [],
            goldReward: 3_500),

        new("berserker", "Frenzied Berserker",
            "Godsword in hand, no fear in heart.",
            new CombatStats(99, 99, 99, 90),
            new ItemModifiers(SlashAttack: 100, StrengthBonus: 100),
            AttackType.Slash,
            [],
            goldReward: 12_000),

        new("warlord", "Battle Warlord",
            "A veteran dueler in top-tier gear.",
            new CombatStats(99, 99, 99, 95),
            new ItemModifiers(SlashAttack: 115, StrengthBonus: 115),
            AttackType.Slash,
            [],
            goldReward: 40_000),

        new("champion", "Duel Champion",
            "Undefeated. Maxed. Waiting for you.",
            new CombatStats(99, 99, 99, 99),
            new ItemModifiers(SlashAttack: 132, StrengthBonus: 132),
            AttackType.Slash,
            [],
            goldReward: 120_000),
    ];
}
