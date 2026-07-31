using Duels.Domain.Interfaces;

namespace Duels.SimHarness;

/// <summary>Every accuracy roll lands and every damage roll is max — the
/// clean, deterministic backdrop for a scripted fight. Boss hits that reach
/// an unprotected player deal full band damage; a correctly-prayed player
/// takes zero, so the trace reads as pure skill expression, no RNG noise.</summary>
public sealed class AlwaysHitRandom : IRandomProvider
{
    public int Next(int min, int max) => max > min ? max - 1 : min;
    public double NextDouble() => 0.0;
}

/// <summary>Deterministic seeded RNG for repeatable-but-varied runs
/// (accuracy/damage spread, the 50/50 style rolls some bosses make).</summary>
public sealed class SeededRandom : IRandomProvider
{
    private readonly Random _r;
    public SeededRandom(int seed) => _r = new Random(seed);
    public int Next(int min, int max) => _r.Next(min, max);
    public double NextDouble() => _r.NextDouble();
}
