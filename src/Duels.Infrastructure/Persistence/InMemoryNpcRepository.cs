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

    // CombatStats = (Attack, Strength, Defence, Hitpoints)
    // Defence bonuses define each NPC's weakness: attack the lowest number's type.
    private static IEnumerable<NpcTemplate> BuildNpcs() =>
    [
        new("goblin", "Arena Goblin",
            "A pitiful creature. No shame in starting here.",
            new CombatStats(1, 1, 1, 10),
            new ItemModifiers(),
            AttackType.Crush,
            [],
            goldReward: 5,
            maxWager: 100,
            attackSpeedTicks: 4),

        new("swashbuckler", "Swashbuckler Pete",
            "A lightning-fast pirate with a cutlass. Nimble — crush him.",
            new CombatStats(70, 65, 55, 55),
            new ItemModifiers(SlashAttack: 30, StrengthBonus: 30,
                StabDefence: 20, SlashDefence: 45, CrushDefence: 0),
            AttackType.Slash,
            [],
            goldReward: 0,
            maxWager: 1_000,
            telegraphedMove: new NpcSpecialMove(
                "Swashbuckler Pete winds up for a brutal overhead slash!",
                1.5, 4),
            attackSpeedTicks: 3),

        new("barbarian", "Iron Barbarian",
            "A brutish northerner swinging a warhammer. Slow but devastating — stab through the plate.",
            new CombatStats(80, 85, 60, 70),
            new ItemModifiers(CrushAttack: 50, StrengthBonus: 50,
                StabDefence: 10, SlashDefence: 35, CrushDefence: 55),
            AttackType.Crush,
            [],
            goldReward: 0,
            maxWager: 5_000,
            telegraphedMove: new NpcSpecialMove(
                "The Iron Barbarian bellows a war cry and raises his hammer!",
                1.7, 5),
            attackSpeedTicks: 5),

        new("desert_bandit", "Desert Bandit",
            "A thrown-knife specialist. Pray Range — and crush his light armor.",
            new CombatStats(85, 80, 55, 65),
            new ItemModifiers(RangedAttack: 40, StrengthBonus: 40,
                StabDefence: 40, SlashDefence: 40, CrushDefence: 5),
            AttackType.Ranged,
            [],
            goldReward: 0,
            maxWager: 20_000,
            telegraphedMove: new NpcSpecialMove(
                "The Desert Bandit fans a spread of poisoned knives!",
                1.6, 5),
            attackSpeedTicks: 4),

        new("gladiator", "Arena Gladiator",
            "Pit-trained with whip and javelin — switches styles mid-fight. Weak to stab.",
            new CombatStats(90, 88, 75, 85),
            new ItemModifiers(SlashAttack: 82, RangedAttack: 82, StrengthBonus: 82,
                StabDefence: 20, SlashDefence: 60, CrushDefence: 35),
            AttackType.Slash,
            [],
            goldReward: 0,
            maxWager: 75_000,
            telegraphedMove: new NpcSpecialMove(
                "The Arena Gladiator coils their whip for a crushing lash!",
                1.8, 5),
            attackSpeedTicks: 4,
            styleRotation: [AttackType.Slash, AttackType.Ranged],
            attacksPerStyle: 4),

        new("corsair", "Pirate Corsair",
            "Elite sea raider with a rapid-fire crossbow. Pray Range; crush the buckler.",
            new CombatStats(95, 92, 70, 90),
            new ItemModifiers(RangedAttack: 56, StrengthBonus: 56,
                StabDefence: 50, SlashDefence: 50, CrushDefence: 25),
            AttackType.Ranged,
            [],
            goldReward: 0,
            maxWager: 250_000,
            telegraphedMove: new NpcSpecialMove(
                "The Pirate Corsair loads a barbed bolt — a piercing shot incoming!",
                1.9, 5),
            attackSpeedTicks: 3),

        new("berserker", "Frenzied Berserker",
            "Godsword in hand, no fear in heart. Gains +5% damage per 10 HP lost. Weak to stab.",
            new CombatStats(105, 110, 80, 100),
            new ItemModifiers(CrushAttack: 100, StrengthBonus: 100,
                StabDefence: 25, SlashDefence: 50, CrushDefence: 70),
            AttackType.Crush,
            [],
            goldReward: 0,
            maxWager: 750_000,
            telegraphedMove: new NpcSpecialMove(
                "The Frenzied Berserker enters a blood frenzy — SPEC INCOMING!",
                2.0, 6),
            attackSpeedTicks: 5),

        new("warlord", "Battle Warlord",
            "A veteran battle-mage. Pray Magic; his plate is weak to crush. Flicks protection prayers every 3 rounds.",
            new CombatStats(110, 105, 95, 105),
            new ItemModifiers(MagicAttack: 115, StrengthBonus: 115,
                StabDefence: 80, SlashDefence: 70, CrushDefence: 35),
            AttackType.Magic,
            [],
            goldReward: 0,
            maxWager: 2_500_000,
            telegraphedMove: new NpcSpecialMove(
                "The Battle Warlord channels dark energy — a massive strike is coming!",
                2.0, 6),
            attackSpeedTicks: 5),

        new("champion", "Duel Champion",
            "Undefeated. Rotates melee, ranged, and magic — dance your prayers. Enters Phase 2 at 50% HP. Weak to stab.",
            new CombatStats(120, 118, 100, 120),
            new ItemModifiers(SlashAttack: 132, RangedAttack: 132, MagicAttack: 132, StrengthBonus: 132,
                StabDefence: 45, SlashDefence: 70, CrushDefence: 70),
            AttackType.Slash,
            [],
            goldReward: 0,
            maxWager: 10_000_000,
            telegraphedMove: new NpcSpecialMove(
                "The Champion charges their legendary technique!",
                2.2, 7),
            attackSpeedTicks: 4,
            styleRotation: [AttackType.Slash, AttackType.Ranged, AttackType.Magic],
            attacksPerStyle: 3),

        new("rare_tourist", "Wealthy Tourist",
            "Lost in the wrong part of the arena. Very wealthy, barely armed.",
            new CombatStats(60, 55, 40, 55),
            new ItemModifiers(RangedAttack: 20, StrengthBonus: 20,
                StabDefence: 20, SlashDefence: 20, CrushDefence: 20),
            AttackType.Ranged,
            [],
            goldReward: 8_000,
            attackSpeedTicks: 4),

        new("rare_gladiator", "Corrupted Gladiator",
            "A former champion, twisted by dark magic. Drops a unique weapon. Weak to stab.",
            new CombatStats(100, 95, 80, 80),
            new ItemModifiers(SlashAttack: 90, RangedAttack: 90, StrengthBonus: 90,
                StabDefence: 30, SlashDefence: 60, CrushDefence: 40),
            AttackType.Slash,
            [],
            goldReward: 0,
            telegraphedMove: new NpcSpecialMove(
                "The Corrupted Gladiator channels dark energy for a devastating strike!",
                1.9, 5),
            attackSpeedTicks: 4,
            styleRotation: [AttackType.Slash, AttackType.Ranged],
            attacksPerStyle: 4),
    ];
}
