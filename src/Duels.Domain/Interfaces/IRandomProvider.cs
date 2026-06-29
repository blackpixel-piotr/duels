namespace Duels.Domain.Interfaces;

public interface IRandomProvider
{
    int Next(int minInclusive, int maxExclusive);
    double NextDouble();
}
