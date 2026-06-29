namespace Duels.Domain.ValueObjects;

public sealed record CombatStats(
    int Attack,
    int Strength,
    int Defence,
    int Hitpoints
)
{
    public static CombatStats Default => new(1, 1, 1, 10);
}
