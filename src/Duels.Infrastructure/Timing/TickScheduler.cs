using System.Diagnostics;
using Duels.Application.Abstractions;
using Duels.Domain.Services;

namespace Duels.Infrastructure.Timing;

/// <summary>Drift-corrected 0.6s tick clock. Schedules each tick from a fixed
/// origin (<c>tickCount * TickDurationMs</c>) instead of chaining
/// <c>Task.Delay(600)</c> calls back to back — per-tick overhead (GC, JS
/// interop, browser timer jitter) would otherwise accumulate into slow drift
/// over a long fight instead of self-correcting.</summary>
public sealed class TickScheduler : ITickSource
{
    private readonly Stopwatch _clock = new();
    private long _tickCount;

    public long ElapsedMsIntoCurrentTick =>
        _clock.IsRunning ? _clock.ElapsedMilliseconds - _tickCount * TickConstants.TickDurationMs : 0;

    public void Reset()
    {
        _clock.Restart();
        _tickCount = 0;
    }

    public async Task WaitForNextTickAsync(CancellationToken ct)
    {
        if (!_clock.IsRunning) Reset();

        _tickCount++;
        long targetMs = _tickCount * TickConstants.TickDurationMs;
        long waitMs = targetMs - _clock.ElapsedMilliseconds;
        if (waitMs > 0)
            await Task.Delay((int)waitMs, ct).ConfigureAwait(false);
    }
}
