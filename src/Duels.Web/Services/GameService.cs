using System.Text.Json;
using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Duels.Web.Models;
using Microsoft.JSInterop;

namespace Duels.Web.Services;

public sealed class GameService
{
    private const string SaveKey = "duels_save";
    private const int CurrentSchemaVersion = 1;

    private readonly ICommandDispatcher _dispatcher;
    private readonly IGameStateRepository _stateRepo;
    private readonly IPlayerRepository _playerRepo;
    private readonly ISaveStore _saveStore;
    private readonly IJSRuntime _js;

    public string? PlayerId { get; private set; }
    public bool IsStarted => PlayerId is not null;

    public event Action? StateChanged;

    public GameService(ICommandDispatcher dispatcher, IGameStateRepository stateRepo, IPlayerRepository playerRepo,
        ISaveStore saveStore, IJSRuntime js)
    {
        _dispatcher = dispatcher;
        _stateRepo = stateRepo;
        _playerRepo = playerRepo;
        _saveStore = saveStore;
        _js = js;
    }

    public async Task<CommandResult> StartNewGameAsync(string playerName)
    {
        PlayerId = Guid.NewGuid().ToString("N");
        var result = await _dispatcher.DispatchAsync(new StartGameCommand(PlayerId, playerName));
        NotifyStateChanged();
        await PersistAsync();
        return result;
    }

    public async Task<CommandResult> DispatchAsync<TCommand>(TCommand command) where TCommand : IGameCommand
    {
        var result = await _dispatcher.DispatchAsync(command);
        NotifyStateChanged();
        if (ShouldPersistAfter(command))
            await PersistAsync();
        return result;
    }

    /// <summary>Explicit persistence trigger for moments the command cadence
    /// above doesn't cover on its own — a duel just ended, or a periodic
    /// safety interval while one is still running (see Game.razor's
    /// OnTickNotify, which drives both).</summary>
    public Task PersistNowAsync() => PersistAsync();

    // Reduced save cadence (M0): these are the high-frequency in-duel inputs
    // (an attack tap, a prayer flick, a weapon swap...) — the tick loop
    // already keeps in-memory state current, and Game.razor's OnTickNotify
    // covers duel-end and a periodic in-duel safety write. Persisting on
    // every one of these was a write per tap; everything else (bank, shop,
    // equip, prestige, starting/ending a session) is comparatively rare and
    // still persists immediately.
    private static bool ShouldPersistAfter<TCommand>(TCommand command) where TCommand : IGameCommand =>
        command is not (AttackCommand or PrayerCommand or WeaponShortcutCommand or SetStyleCommand
            or MoveToCommand or EngageCommand or DrinkPotionCommand or EatItemCommand or FreezeEnemyCommand);

    public async Task<GameState?> GetStateAsync() =>
        PlayerId is null ? null : await _stateRepo.GetAsync(PlayerId);

    public async Task<bool> HasSaveAsync()
    {
        try
        {
            var json = await LoadRawSaveWithMigrationAsync();
            return json is not null;
        }
        catch { return false; }
    }

    public async Task<string?> GetSaveNameAsync()
    {
        try
        {
            var json = await LoadRawSaveWithMigrationAsync();
            if (json is null) return null;
            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(json);
            return envelope?.Data.PlayerName;
        }
        catch { return null; }
    }

    public async Task<bool> RestoreSaveAsync()
    {
        try
        {
            var json = await LoadRawSaveWithMigrationAsync();
            if (json is null) return false;

            var envelope = JsonSerializer.Deserialize<SaveEnvelope>(json);
            var data = envelope?.Data;
            if (data is null) return false;

            PlayerId = data.PlayerId;

            var player = new Player(data.PlayerId, data.PlayerName);
            var equippedKvps = data.Equipped
                .Select(kv => (Enum.TryParse<EquipmentSlot>(kv.Key, out var slot), slot, kv.Value))
                .Where(t => t.Item1)
                .Select(t => new KeyValuePair<EquipmentSlot, string>(t.slot, t.Value));

            // Legacy saves (pre-xp) carry -1 sentinels: grandfather them to max level (they were 99s)
            int MigrateXp(int xp) => xp < 0 ? ExperienceTable.MaxLevelXp : xp;
            var style = Enum.TryParse<AttackStyle>(data.ChosenStyle, out var s) ? s : AttackStyle.Accurate;

            player.RestoreFromSave(data.Gold, data.CurrentHp, data.SpecialEnergy, data.PrestigeLevel,
                data.Inventory, equippedKvps,
                MigrateXp(data.AttackXp), MigrateXp(data.StrengthXp),
                MigrateXp(data.DefenceXp), MigrateXp(data.HitpointsXp), style);

            var state = new GameState(data.PlayerId, player);
            state.RestoreFromSave(data.WinStreak, data.BestEndlessWave, data.UnlockedOpponents,
                data.CollectionLog, data.DefeatedNpcs, data.Bank);

            await _playerRepo.SaveAsync(player);
            await _stateRepo.SaveAsync(state);

            NotifyStateChanged();
            return true;
        }
        catch { return false; }
    }

    public async Task ClearSaveAsync()
    {
        try { await _saveStore.DeleteAsync(SaveKey); } catch { }
    }

    /// <summary>Reads the current save, migrating a pre-M0 localStorage save
    /// into IndexedDB the first time it's found there. One-time: once
    /// imported, the legacy key is cleared and never consulted again.</summary>
    private async Task<string?> LoadRawSaveWithMigrationAsync()
    {
        var json = await _saveStore.LoadAsync(SaveKey);
        if (json is not null) return json;

        string? legacy;
        try { legacy = await _js.InvokeAsync<string?>("legacyLoadGame"); }
        catch { legacy = null; }
        if (legacy is null) return null;

        var legacyData = JsonSerializer.Deserialize<SaveData>(legacy);
        if (legacyData is null) return null;

        var migrated = JsonSerializer.Serialize(new SaveEnvelope(CurrentSchemaVersion, legacyData));
        await _saveStore.SaveAsync(SaveKey, migrated);
        try { await _js.InvokeVoidAsync("legacyClearGame"); } catch { }
        return migrated;
    }

    private async Task PersistAsync()
    {
        try
        {
            if (PlayerId is null) return;
            var state = await _stateRepo.GetAsync(PlayerId);
            if (state is null) return;

            var p = state.Player;
            var data = new SaveData(
                PlayerId: p.Id,
                PlayerName: p.Name,
                Gold: p.Gold,
                CurrentHp: p.CurrentHp,
                SpecialEnergy: p.SpecialEnergy,
                PrestigeLevel: p.PrestigeLevel,
                WinStreak: state.WinStreak,
                BestEndlessWave: state.BestEndlessWave,
                Inventory: p.Inventory.ToList(),
                Equipped: p.Equipped.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                UnlockedOpponents: state.UnlockedOpponents.ToList(),
                AttackXp: p.AttackXp,
                StrengthXp: p.StrengthXp,
                DefenceXp: p.DefenceXp,
                HitpointsXp: p.HitpointsXp,
                ChosenStyle: p.ChosenStyle.ToString(),
                CollectionLog: state.CollectionLog.ToList(),
                DefeatedNpcs: state.DefeatedNpcs.ToList(),
                Bank: state.Bank.ToList()
            );

            var json = JsonSerializer.Serialize(new SaveEnvelope(CurrentSchemaVersion, data));
            await _saveStore.SaveAsync(SaveKey, json);
        }
        catch { }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
