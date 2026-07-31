using System.Text;
using Duels.Application.GameSession;

namespace Duels.SimHarness;

/// <summary>Captures a per-tick snapshot of the fight and renders it as a
/// readable table plus the combat-log deltas — the "what happened, tick by
/// tick" view that replaces squinting at the live browser.</summary>
public sealed class TraceRecorder
{
    private readonly GameState _state;
    private int _lastLogCount;
    private readonly List<Row> _rows = new();

    public TraceRecorder(GameState state) => _state = state;

    private readonly record struct Row(
        int Tick, int Phase, int BossHp, int BossHpPct, string BossTile, int Dist,
        int PlayerHp, int Prayer, string PlayerTile, string Flags, string Events);

    /// <summary>Call once immediately AFTER each TickAsync.</summary>
    public void Capture()
    {
        var npc = _state.ActiveNpc;
        var newLogs = _state.CombatLog.Skip(_lastLogCount)
            .Where(e => e.Kind is LogEntryKind.BossSpecial or LogEntryKind.BossCast
                or LogEntryKind.NpcHit or LogEntryKind.HitsplatNpc or LogEntryKind.System)
            .Select(e => e.Message.Trim())
            .ToList();
        _lastLogCount = _state.CombatLog.Count;

        var flags = new List<string>();
        if (npc is not null)
        {
            if (npc.InPunishWindow) flags.Add("PUNISH");
            if (npc.DamageReductionActive) flags.Add("CHITIN");
            if (npc.LineChargeTiles is { Count: > 0 }) flags.Add("PIN>");
            if (_state.Hazards.Any(h => h.State == HazardState.Warning)) flags.Add("GLOB>");
            if (_state.Projectiles.Count > 0) flags.Add($"proj:{_state.Projectiles[0].Attack.Style}");
            if (_state.Adds.Count(a => a.IsAlive) > 0) flags.Add($"drones:{_state.Adds.Count(a => a.IsAlive)}");
        }

        // Once the duel ends the boss instance is nulled; carry the last known
        // boss stats forward so the final row doesn't misleadingly read 0 HP
        // (it's the *player* who died, not the boss).
        int bossHp = npc?.CurrentHp ?? (_rows.Count > 0 ? _rows[^1].BossHp : 0);
        int bossPct = npc is null ? (_rows.Count > 0 ? _rows[^1].BossHpPct : 0)
            : (int)Math.Round(100.0 * npc.CurrentHp / npc.MaxHp);

        _rows.Add(new Row(
            _state.FightTicks,
            npc?.Phase ?? (_rows.Count > 0 ? _rows[^1].Phase : 0),
            bossHp,
            bossPct,
            $"({_state.NpcTile.X},{_state.NpcTile.Z})",
            _state.DistanceToNpc,
            _state.Player.CurrentHp,
            _state.Player.PrayerPoints,
            $"({_state.PlayerTile.X},{_state.PlayerTile.Z})",
            string.Join(" ", flags),
            string.Join(" | ", newLogs)));
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("  t  ph  bHP  b%   bTile  d   pHP pray  pTile    flags               events");
        sb.AppendLine("  ─  ──  ───  ──   ─────  ─   ─── ────  ─────    ─────               ──────");
        foreach (var r in _rows)
            sb.AppendLine(
                $"{r.Tick,3}  {r.Phase,2}  {r.BossHp,3}  {r.BossHpPct,2}  {r.BossTile,6}  {r.Dist,1}   " +
                $"{r.PlayerHp,3} {r.Prayer,4}  {r.PlayerTile,-7}  {r.Flags,-18}  {r.Events}");
        return sb.ToString();
    }

    // ── summary metrics ─────────────────────────────────────────────────────
    public int Ticks => _rows.Count;
    public int MinPlayerHp => _rows.Count == 0 ? 0 : _rows.Min(r => r.PlayerHp);

    /// <summary>Outcome read from the combat log, not from live entity state —
    /// the defeat handler heals the player back to full (respawn), so
    /// post-fight <c>Player.IsAlive</c> lies. The log doesn't.</summary>
    public string Outcome =>
        _state.CombatLog.Any(e => e.Message.Contains("DUEL WON")) ? "BOSS SLAIN"
        : _state.CombatLog.Any(e => e.Message.Contains("been defeated")) ? "PLAYER DIED"
        : "TIMED OUT (boss still alive)";
}
