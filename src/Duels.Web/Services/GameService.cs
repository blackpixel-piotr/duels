using System.Text.Json;
using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Web.Models;
using Microsoft.JSInterop;

namespace Duels.Web.Services;

public sealed class GameService
{
    private const string SaveKey = "duels_save";
    private const int CurrentSchemaVersion = 2;

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

    public Task PersistNowAsync() => PersistAsync();

    // Reduced save cadence (M0): the tick loop keeps in-memory state current;
    // Game.razor's OnTickNotify covers duel-end and a periodic in-duel safety
    // write. Everything else (equip, loadout binds, starting/ending a
    // session) persists immediately.
    private static bool ShouldPersistAfter<TCommand>(TCommand command) where TCommand : IGameCommand =>
        command is not (AttackCommand or PrayerCommand or WeaponShortcutCommand or SetStyleCommand
            or MoveToCommand or EngageCommand or FreezeEnemyCommand or SipFlaskCommand or SetTargetCommand);

    public async Task<GameState?> GetStateAsync() =>
        PlayerId is null ? null : await _stateRepo.GetAsync(PlayerId);

    public async Task<bool> HasSaveAsync()
    {
        try { return await LoadRawSaveWithMigrationAsync() is not null; }
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

            var style = Enum.TryParse<AttackStyle>(data.ChosenStyle, out var s) ? s : AttackStyle.Accurate;

            player.RestoreFromSave(data.Gold, data.CurrentHp, data.SpecialEnergy,
                data.Inventory, equippedKvps, style, data.PersonalBestKillTicks);
            player.Loadout.RestoreFromSave(data.LoadoutWeaponSlots ?? [], data.LoadoutFlaskSlots ?? []);

            var state = new GameState(data.PlayerId, player);

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
                Inventory: p.Inventory.ToList(),
                Equipped: p.Equipped.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                ChosenStyle: p.ChosenStyle.ToString(),
                PersonalBestKillTicks: p.PersonalBestKillTicks,
                LoadoutWeaponSlots: p.Loadout.WeaponSlots.ToList(),
                LoadoutFlaskSlots: p.Loadout.FlaskSlots.ToList()
            );

            var json = JsonSerializer.Serialize(new SaveEnvelope(CurrentSchemaVersion, data));
            await _saveStore.SaveAsync(SaveKey, json);
        }
        catch { }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
