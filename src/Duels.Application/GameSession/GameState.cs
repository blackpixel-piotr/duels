using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;

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

    // Tick engine
    public int PlayerCooldown { get; private set; }
    public int NpcCooldown { get; private set; }
    public string? QueuedAction { get; private set; }
    public string? RevertWeaponId { get; private set; }
    public ProtectionPrayer TickStartProtection { get; set; }

    // Arena positions (duel-scoped). Tile grid centered on the arena; melee
    // combatants must close to adjacency before they can hit.
    public const int ArenaRadius = 5;
    public (int X, int Z) PlayerTile { get; private set; }
    public (int X, int Z) NpcTile { get; private set; }

    /// <summary>Chebyshev distance between the combatants (diagonals count 1).</summary>
    public int DistanceToNpc =>
        Math.Max(Math.Abs(PlayerTile.X - NpcTile.X), Math.Abs(PlayerTile.Z - NpcTile.Z));

    /// <summary>True when the NPC can reach the player with its current style —
    /// the wind-up cue keys off this so it doesn't fire mid-approach.</summary>
    public bool NpcInRange =>
        ActiveNpc is { } npc && DistanceToNpc <= AttackRange.ForStyle(npc.CurrentAttackType);

    public void SetPlayerTile(int x, int z) => PlayerTile = (x, z);
    public void SetNpcTile(int x, int z) => NpcTile = (x, z);

    // Click-to-move: a ground click sets a move order (walking cancels
    // attacking); on arrival the player holds position — auto-retaliate
    // in range, but no chasing — until the enemy is clicked (Engage).
    public (int X, int Z)? PlayerMoveTarget { get; private set; }
    public bool HoldPosition { get; private set; }

    public void OrderMove(int x, int z)
    {
        // Clamp to the arena circle so orders can't walk out of the scene.
        x = Math.Clamp(x, -ArenaRadius, ArenaRadius);
        z = Math.Clamp(z, -ArenaRadius, ArenaRadius);
        while (x * x + z * z > ArenaRadius * ArenaRadius)
        {
            if (Math.Abs(x) >= Math.Abs(z)) x -= Math.Sign(x); else z -= Math.Sign(z);
        }
        PlayerMoveTarget = (x, z);
        HoldPosition = true;
    }

    public void ClearMoveOrder() => PlayerMoveTarget = null;

    public void Engage()
    {
        PlayerMoveTarget = null;
        HoldPosition = false;
    }

    // Test-fight convenience: spawn holding position (no auto-chase/attack)
    // so an admin can inspect animations before manually engaging.
    public void HoldPositionAtSpawn() => HoldPosition = true;

    // Test-fight duels render the open-field scene instead of the arena ring.
    public bool TestScene { get; private set; }
    public void SetTestScene(bool on) => TestScene = on;

    // Freeze the enemy (test-fight only): stops NPC movement and attacking.
    public bool EnemyFrozen { get; private set; }
    public void FreezeEnemy(bool frozen) => EnemyFrozen = frozen;

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

    // Damage-over-time (duel-scoped, cleared on Start/EndDuel)
    public int BleedTicksLeft { get; private set; }
    public int BleedPerTick { get; private set; }
    public bool PlayerPoisoned { get; private set; }
    public int PoisonTickCounter { get; private set; }

    // Collection log / achievements — persist across prestige (the account's permanent record)
    public List<string> CollectionLog { get; } = new();
    public List<string> DefeatedNpcs { get; } = new();

    // Bank — off-inventory storage; cleared on prestige
    public List<string> Bank { get; } = new();
    public int DamageTakenThisDuel { get; private set; }

    // Duel result — populated at duel end, consumed by the result overlay
    public DuelSummary? LastDuelSummary { get; private set; }
    public int XpGainedThisDuel { get; private set; }

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
        Player.RestorePrayer();
        InitDuelCooldowns();
        ClearDots();
        DamageTakenThisDuel = 0;
        XpGainedThisDuel = 0;
        // Opposite ends of the arena; melee walks in from here.
        PlayerTile = (0, 3);
        NpcTile = (1, -3);
        PlayerMoveTarget = null;
        HoldPosition = false;
        TestScene = false;
        EnemyFrozen = false;
    }

    public void RecordDamageTaken(int amount) { if (amount > 0) DamageTakenThisDuel += amount; }
    public void RecordXpGained(int xp) { if (xp > 0) XpGainedThisDuel += xp; }
    public void SetDuelSummary(DuelSummary summary) => LastDuelSummary = summary;

    public void RecordLoot(string itemId) { if (!CollectionLog.Contains(itemId)) CollectionLog.Add(itemId); }
    public void RecordDefeat(string npcId) { if (!DefeatedNpcs.Contains(npcId)) DefeatedNpcs.Add(npcId); }

    public void ApplyBleed(int ticks, int perTick) { BleedTicksLeft = ticks; BleedPerTick = perTick; }
    public void TickBleed() { if (BleedTicksLeft > 0) BleedTicksLeft--; }
    public void ApplyPoison() { PlayerPoisoned = true; PoisonTickCounter = 0; }
    public void CurePoison() { PlayerPoisoned = false; PoisonTickCounter = 0; }
    public bool TickPoison()
    {
        if (!PlayerPoisoned) return false;
        PoisonTickCounter++;
        if (PoisonTickCounter < 4) return false;
        PoisonTickCounter = 0;
        return true;
    }
    private void ClearDots()
    {
        BleedTicksLeft = 0;
        BleedPerTick = 0;
        PlayerPoisoned = false;
        PoisonTickCounter = 0;
    }

    public void InitDuelCooldowns()
    {
        PlayerCooldown = 0;
        NpcCooldown = 0;
    }

    public void SetQueuedAction(string? action) => QueuedAction = action;

    public void SetRevertWeapon(string? weaponId) => RevertWeaponId = weaponId;

    public void ResetPlayerCooldown(int ticks) => PlayerCooldown = ticks;

    /// <summary>Eating/drinking mid-duel pushes back the next attack — trades DPS for sustain.</summary>
    public void DelayPlayerAttack(int ticks) { if (ticks > 0) PlayerCooldown += ticks; }

    public void ResetNpcCooldown(int ticks) => NpcCooldown = ticks;

    public void DecrementCooldowns()
    {
        if (PlayerCooldown > 0) PlayerCooldown--;
        if (NpcCooldown > 0) NpcCooldown--;
    }

    public void EndDuel()
    {
        ActiveNpc = null;
        ClearDots();
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
        Bank.Clear();
    }

    public void RestoreFromSave(int winStreak, int bestEndlessWave, IEnumerable<string> unlockedOpponents,
        IEnumerable<string>? collectionLog = null, IEnumerable<string>? defeatedNpcs = null,
        IEnumerable<string>? bank = null)
    {
        WinStreak = winStreak;
        BestEndlessWave = bestEndlessWave;
        UnlockedOpponents.Clear();
        foreach (var op in unlockedOpponents)
            if (!UnlockedOpponents.Contains(op))
                UnlockedOpponents.Add(op);

        CollectionLog.Clear();
        foreach (var id in collectionLog ?? [])
            if (!CollectionLog.Contains(id))
                CollectionLog.Add(id);

        DefeatedNpcs.Clear();
        foreach (var id in defeatedNpcs ?? [])
            if (!DefeatedNpcs.Contains(id))
                DefeatedNpcs.Add(id);

        Bank.Clear();
        foreach (var id in bank ?? [])
            Bank.Add(id);
    }
}

public sealed record CombatLogEntry(string Message, LogEntryKind Kind, DateTimeOffset Timestamp);

/// <summary>Snapshot of a finished duel for the end-of-fight result overlay.</summary>
public sealed record DuelSummary(
    bool Won,
    string NpcId,
    string NpcName,
    int GoldGained,
    IReadOnlyList<string> LootItemIds,
    int XpGained,
    int WinStreak,
    bool Flawless,
    int EndlessWaveReached);

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
    Prayer,
    BossSpecial,
    HitsplatPlayer,
    HitsplatNpc,
    LevelUp,
}
