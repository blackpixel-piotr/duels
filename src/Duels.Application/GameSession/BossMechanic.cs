namespace Duels.Application.GameSession;

/// <summary>Dev-only per-mechanic kill switches (M1 playtest tooling): toggle a
/// single Maggot King mechanic off to isolate an interaction while playtesting.
/// Default is <see cref="All"/> (everything live); toggles deliberately persist
/// across retries within a session (not reset on StartDuel) so a playtester
/// doesn't have to re-set them every attempt.</summary>
[System.Flags]
public enum BossMechanic
{
    None      = 0,
    BossAutos = 1 << 0, // Bile Spit / Lash / Grub Volley rotation attacks
    Eruptions = 1 << 1, // the eruption hazard waves (fuse → burst)
    Pools     = 1 << 2, // poison pools erupted tiles leave behind
    Swarms    = 1 << 3, // maggot swarm adds
    RotBurst  = 1 << 4, // the Rot Burst channel + punish window
    Dots      = 1 << 5, // bleed + poison damage-over-time ticks
    All       = BossAutos | Eruptions | Pools | Swarms | RotBurst | Dots,
}
