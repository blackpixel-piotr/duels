using Duels.Domain.Entities;

namespace Duels.Application.GameSession;

public sealed class GameState
{
    public string PlayerId { get; }
    public Player Player { get; }
    public NpcInstance? ActiveNpc { get; private set; }
    public bool InDuel => ActiveNpc is { IsAlive: true };
    public List<CombatLogEntry> CombatLog { get; } = new();
    public List<string> UnlockedOpponents { get; } = ["bandit"];

    public GameState(string playerId, Player player)
    {
        PlayerId = playerId;
        Player = player;
    }

    public void StartDuel(NpcInstance npc)
    {
        ActiveNpc = npc;
        CombatLog.Clear();
    }

    public void EndDuel()
    {
        ActiveNpc = null;
    }

    public void UnlockOpponent(string id)
    {
        if (!UnlockedOpponents.Contains(id))
            UnlockedOpponents.Add(id);
    }

    public void AppendLog(string message, LogEntryKind kind = LogEntryKind.Info)
    {
        CombatLog.Add(new CombatLogEntry(message, kind, DateTimeOffset.UtcNow));
        if (CombatLog.Count > 500)
            CombatLog.RemoveAt(0);
    }
}

public sealed record CombatLogEntry(string Message, LogEntryKind Kind, DateTimeOffset Timestamp);

public enum LogEntryKind
{
    Info,
    PlayerHit,
    PlayerMiss,
    NpcHit,
    NpcMiss,
    System,
    Loot,
}
