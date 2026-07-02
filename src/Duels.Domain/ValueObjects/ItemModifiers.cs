namespace Duels.Domain.ValueObjects;

public sealed record ItemModifiers(
    int StabAttack = 0,
    int SlashAttack = 0,
    int CrushAttack = 0,
    int MagicAttack = 0,
    int RangedAttack = 0,
    int StabDefence = 0,
    int SlashDefence = 0,
    int CrushDefence = 0,
    int MagicDefence = 0,
    int RangedDefence = 0,
    int StrengthBonus = 0,
    int PrayerBonus = 0
)
{
    public static readonly ItemModifiers Zero = new();

    public ItemModifiers Add(ItemModifiers other) => new(
        StabAttack + other.StabAttack,
        SlashAttack + other.SlashAttack,
        CrushAttack + other.CrushAttack,
        MagicAttack + other.MagicAttack,
        RangedAttack + other.RangedAttack,
        StabDefence + other.StabDefence,
        SlashDefence + other.SlashDefence,
        CrushDefence + other.CrushDefence,
        MagicDefence + other.MagicDefence,
        RangedDefence + other.RangedDefence,
        StrengthBonus + other.StrengthBonus,
        PrayerBonus + other.PrayerBonus
    );

    public int AttackBonusFor(AttackType type) => type switch
    {
        AttackType.Stab => StabAttack,
        AttackType.Slash => SlashAttack,
        AttackType.Crush => CrushAttack,
        AttackType.Ranged => RangedAttack,
        AttackType.Magic => MagicAttack,
        _ => 0
    };

    public int DefenceBonusFor(AttackType type) => type switch
    {
        AttackType.Stab => StabDefence,
        AttackType.Slash => SlashDefence,
        AttackType.Crush => CrushDefence,
        AttackType.Ranged => RangedDefence,
        AttackType.Magic => MagicDefence,
        _ => 0
    };
}
