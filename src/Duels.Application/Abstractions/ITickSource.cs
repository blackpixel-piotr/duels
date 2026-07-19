namespace Duels.Application.Abstractions;

/// <summary>Timing-only authority for the game's 0.6s tick. Separated from
/// <c>GameTickService</c> (which owns what happens during a tick) so
/// scheduling can be swapped or driven deterministically in tests without
/// touching combat logic — see <c>TickScheduler</c> (Infrastructure, real
/// drift-corrected clock).</summary>
public interface ITickSource
{
    /// <summary>Milliseconds elapsed since the current tick period began.
    /// Used by input buffering to classify a tap as "this tick" vs "next
    /// tick" (UI bible §2). 0 immediately after <see cref="Reset"/>.</summary>
    long ElapsedMsIntoCurrentTick { get; }

    /// <summary>Starts a fresh tick epoch — call once per duel session so
    /// drift correction measures from a known origin.</summary>
    void Reset();

    /// <summary>Suspends until the next tick boundary, or throws
    /// <see cref="OperationCanceledException"/> if <paramref name="ct"/>
    /// fires first.</summary>
    Task WaitForNextTickAsync(CancellationToken ct);
}
