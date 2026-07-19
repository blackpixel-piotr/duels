namespace Duels.Domain.Services;

/// <summary>The single authoritative tick timing for the whole game (M0
/// formal tick scheduler). One named home for the numbers that used to be
/// scattered literals — every tick-timed value in the game routes through
/// these two constants.</summary>
public static class TickConstants
{
    /// <summary>The tick period: 0.6s, per the boss bible's global combat grammar.</summary>
    public const int TickDurationMs = 600;

    /// <summary>UI bible §2: taps within the last 150ms of a tick queue for
    /// the next tick instead of racing the current tick's resolution.</summary>
    public const int InputBufferWindowMs = 150;
}
