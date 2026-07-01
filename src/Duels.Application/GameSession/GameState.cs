using Duels.Domain.Entities;

namespace Duels.Application.GameSession;

public sealed class GameState
{
    public string PlayerId { get; }
    public Player Player { get; }
    public NpcInstance? ActiveNpc { get; private set; }
    public bool InDuel => ActiveNpc is { IsAlive: true };
    public string? LastOpponentId { get; private set; }
    public List<CombatLogEntry> CombatLog { get; } = new();
    public List<string> UnlockedOpponents { get; } = ["swashbuckler", "goblin"];

    // Staking / winstreak
    public int CurrentWager { get; private set; }
    public int LastWager { get; private set; }
    public int WinStreak { get; private set; }
    public double WinStreakMultiplier => 1.0 + Math.Min(WinStreak * 0.10, 1.0);

    // Action economy
    public bool HasBegged { get; private set; }

    // Prestige
    public bool CanPrestige { get; private set; }

    // Vengeance
    public bool VengActive { get; private set; }
    public int VengCooldownRounds { get; private set; }

    // Endless mode
    public bool InEndlessMode { get; private set; }
    public int EndlessWave { get; private set; }
    public int BestEndlessWave { get; private set; }

    public GameState(string playerId, Player player)
    {
        PlayerId = playerId;
        Player = player;
    }

    public void StartDuel(NpcInstance npc)
    {
        LastOpponentId = npc.Template.Id;
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

    // Staking
    public void SetWager(int amount) => CurrentWager = amount;
    public void SetLastWager(int amount) => LastWager = amount;
    public void IncrementWinStreak() => WinStreak++;
    public void ResetWinStreak() => WinStreak = 0;

    // Beg
    public void SetHasBegged() => HasBegged = true;

    // Prestige
    public void SetCanPrestige() => CanPrestige = true;

    // Vengeance
    public void ActivateVeng() { VengActive = true; VengCooldownRounds = 5; }
    public void TickVeng() { if (VengCooldownRounds > 0) VengCooldownRounds--; }
    public void ConsumeVeng() => VengActive = false;

    // Endless
    public void StartEndless() { InEndlessMode = true; EndlessWave = 0; }
    public int NextEndlessWave() => ++EndlessWave;
    public void EndEndless()
    {
        if (EndlessWave > BestEndlessWave) BestEndlessWave = EndlessWave;
        InEndlessMode = false;
        EndlessWave = 0;
    }

    // Prestige reset
    public void Reset()
    {
        UnlockedOpponents.Clear();
        UnlockedOpponents.Add("swashbuckler");
        UnlockedOpponents.Add("goblin");
        WinStreak = 0;
        CurrentWager = 0;
        CanPrestige = false;
        LastOpponentId = null;
        VengActive = false;
        VengCooldownRounds = 0;
        HasBegged = false;
        InEndlessMode = false;
        EndlessWave = 0;
    }

    public void RestoreFromSave(int winStreak, int bestEndlessWave, IEnumerable<string> unlockedOpponents)
    {
        WinStreak = winStreak;
        BestEndlessWave = bestEndlessWave;
        UnlockedOpponents.Clear();
        foreach (var op in unlockedOpponents)
            if (!UnlockedOpponents.Contains(op))
                UnlockedOpponents.Add(op);
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
    MaxHit,
    SpecHit,
    Vengeance,
}
