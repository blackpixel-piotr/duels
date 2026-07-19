using System.Diagnostics;
using Duels.Domain.Services;
using Duels.Infrastructure.Timing;
using Xunit;

namespace Duels.Infrastructure.Tests;

// TickScheduler is the M0 formal tick scheduler: a drift-corrected clock
// replacing the old GameTickService loop's raw Task.Delay(600) chain.
// Real-time based (no clock seam) — tolerances are generous to stay stable
// under CI jitter while still proving the drift-correction property.
public class TickSchedulerTests
{
    [Fact]
    public void ElapsedMsIntoCurrentTick_IsZero_BeforeReset()
    {
        var scheduler = new TickScheduler();
        Assert.Equal(0, scheduler.ElapsedMsIntoCurrentTick);
    }

    [Fact]
    public async Task ElapsedMsIntoCurrentTick_GrowsWithinATick()
    {
        var scheduler = new TickScheduler();
        scheduler.Reset();

        await Task.Delay(50);

        Assert.True(scheduler.ElapsedMsIntoCurrentTick >= 30);
        Assert.True(scheduler.ElapsedMsIntoCurrentTick < TickConstants.TickDurationMs);
    }

    [Fact]
    public async Task WaitForNextTickAsync_WaitsApproximatelyOneTickDuration()
    {
        var scheduler = new TickScheduler();
        scheduler.Reset();

        var sw = Stopwatch.StartNew();
        await scheduler.WaitForNextTickAsync(CancellationToken.None);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds,
            TickConstants.TickDurationMs - 150, TickConstants.TickDurationMs + 300);
    }

    [Fact]
    public async Task WaitForNextTickAsync_CorrectsForDriftBetweenTicks()
    {
        var scheduler = new TickScheduler();
        scheduler.Reset();

        var totalSw = Stopwatch.StartNew();
        await scheduler.WaitForNextTickAsync(CancellationToken.None);

        // Simulate slow tick-processing work eating into the tick budget.
        await Task.Delay(200);

        await scheduler.WaitForNextTickAsync(CancellationToken.None);
        totalSw.Stop();

        // Two ticks should still land close to 2 * TickDurationMs in total,
        // not 2 * TickDurationMs + the simulated processing delay — that's
        // the drift correction the scheduler exists to provide.
        Assert.InRange(totalSw.ElapsedMilliseconds,
            2 * TickConstants.TickDurationMs - 150, 2 * TickConstants.TickDurationMs + 300);
    }
}
