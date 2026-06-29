namespace Duels.Domain.ValueObjects;

public sealed record DamageRange(int Min, int Max)
{
    public static DamageRange Zero => new(0, 0);
}
