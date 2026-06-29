using Duels.Domain.Interfaces;

namespace Duels.Infrastructure.Random;

public sealed class SystemRandomProvider : IRandomProvider
{
    public int Next(int minInclusive, int maxExclusive) =>
        System.Random.Shared.Next(minInclusive, maxExclusive);

    public double NextDouble() =>
        System.Random.Shared.NextDouble();
}
