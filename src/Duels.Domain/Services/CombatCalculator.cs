using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Domain.Services;

public sealed class CombatCalculator : ICombatCalculator
{
    private readonly IRandomProvider _random;

    public CombatCalculator(IRandomProvider random)
    {
        _random = random;
    }

    public CombatRollResult Roll(CombatantSnapshot attacker, CombatantSnapshot defender)
    {
        int maxAttackRoll = MaxAttackRoll(attacker);
        int maxDefenceRoll = MaxDefenceRoll(defender, attacker.AttackType);

        int attackRoll = _random.Next(0, maxAttackRoll + 1);
        int defenceRoll = _random.Next(0, maxDefenceRoll + 1);

        double hitChance = attackRoll > defenceRoll
            ? 1.0 - (defenceRoll + 2.0) / (2.0 * (attackRoll + 1.0))
            : attackRoll / (2.0 * (defenceRoll + 1.0));

        bool hit = _random.NextDouble() < hitChance;
        if (!hit) return new CombatRollResult(false, 0);

        int maxHit = MaxHit(attacker);
        int damage = _random.Next(0, maxHit + 1);
        return new CombatRollResult(true, damage);
    }

    // OSRS formula: effective_attack * (equipment_attack_bonus + 64)
    private static int MaxAttackRoll(CombatantSnapshot s)
    {
        int effective = s.AttackLevel + StyleAttackBonus(s.Style) + 8;
        int equipBonus = s.Modifiers.AttackBonusFor(s.AttackType);
        return effective * (equipBonus + 64);
    }

    // OSRS formula: effective_defence * (equipment_defence_bonus + 64)
    private static int MaxDefenceRoll(CombatantSnapshot s, AttackType incomingType)
    {
        int effective = s.DefenceLevel + StyleDefenceBonus(s.Style) + 8;
        int equipBonus = s.Modifiers.DefenceBonusFor(incomingType);
        return effective * (equipBonus + 64);
    }

    // OSRS formula: floor(0.5 + effective_strength * (strength_bonus + 64) / 640)
    private static int MaxHit(CombatantSnapshot s)
    {
        int effective = s.StrengthLevel + StyleStrengthBonus(s.Style) + 8;
        return (int)(0.5 + effective * (s.Modifiers.StrengthBonus + 64) / 640.0);
    }

    private static int StyleAttackBonus(AttackStyle style) => style == AttackStyle.Accurate ? 3 : 0;
    private static int StyleStrengthBonus(AttackStyle style) => style == AttackStyle.Aggressive ? 3 : 0;
    private static int StyleDefenceBonus(AttackStyle style) => style == AttackStyle.Defensive ? 3 : 0;
}
