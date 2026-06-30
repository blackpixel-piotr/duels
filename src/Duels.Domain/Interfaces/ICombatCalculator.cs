using Duels.Domain.ValueObjects;

namespace Duels.Domain.Interfaces;

public sealed record CombatantSnapshot(
    int AttackLevel,
    int StrengthLevel,
    int DefenceLevel,
    ItemModifiers Modifiers,
    AttackType AttackType,
    AttackStyle Style
);

public sealed record CombatRollResult(bool Hit, int Damage);

public interface ICombatCalculator
{
    CombatRollResult Roll(CombatantSnapshot attacker, CombatantSnapshot defender);
    int MaxHit(CombatantSnapshot attacker);
}
