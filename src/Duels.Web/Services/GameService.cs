using System.Text.Json;
using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Parsing;
using Duels.Domain.Entities;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;
using Duels.Web.Models;
using Microsoft.JSInterop;

namespace Duels.Web.Services;

public sealed class GameService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly CommandParser _parser;
    private readonly IGameStateRepository _stateRepo;
    private readonly IPlayerRepository _playerRepo;
    private readonly IJSRuntime _js;

    public string? PlayerId { get; private set; }
    public bool IsStarted => PlayerId is not null;

    public event Action? StateChanged;

    public GameService(ICommandDispatcher dispatcher, CommandParser parser, IGameStateRepository stateRepo, IPlayerRepository playerRepo, IJSRuntime js)
    {
        _dispatcher = dispatcher;
        _parser = parser;
        _stateRepo = stateRepo;
        _playerRepo = playerRepo;
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

    public async Task<CommandResult> ExecuteCommandAsync(string input)
    {
        if (PlayerId is null) return CommandResult.Fail("No active game.");

        var parsed = _parser.Parse(PlayerId, input);
        if (!parsed.Success)
        {
            var state = await _stateRepo.GetAsync(PlayerId);
            state?.AppendLog(parsed.Error ?? "Unknown error.", LogEntryKind.System);
            NotifyStateChanged();
            return CommandResult.Fail(parsed.Error ?? "Parse error.");
        }

        var result = await _dispatcher.DispatchAsync((dynamic)parsed.Command!);
        NotifyStateChanged();
        await PersistAsync();
        return result;
    }

    public async Task<CommandResult> DispatchAsync<TCommand>(TCommand command) where TCommand : IGameCommand
    {
        var result = await _dispatcher.DispatchAsync(command);
        NotifyStateChanged();
        await PersistAsync();
        return result;
    }

    public async Task<GameState?> GetStateAsync() =>
        PlayerId is null ? null : await _stateRepo.GetAsync(PlayerId);

    public async Task<bool> HasSaveAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("loadGame");
            return json is not null;
        }
        catch { return false; }
    }

    public async Task<string?> GetSaveNameAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("loadGame");
            if (json is null) return null;
            var data = JsonSerializer.Deserialize<SaveData>(json);
            return data?.PlayerName;
        }
        catch { return null; }
    }

    public async Task<bool> RestoreSaveAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("loadGame");
            if (json is null) return false;

            var data = JsonSerializer.Deserialize<SaveData>(json);
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
            state.RestoreFromSave(data.WinStreak, data.BestEndlessWave, data.UnlockedOpponents);

            await _playerRepo.SaveAsync(player);
            await _stateRepo.SaveAsync(state);

            NotifyStateChanged();
            return true;
        }
        catch { return false; }
    }

    public async Task ClearSaveAsync()
    {
        try { await _js.InvokeVoidAsync("clearGame"); } catch { }
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
                ChosenStyle: p.ChosenStyle.ToString()
            );

            var json = JsonSerializer.Serialize(data);
            await _js.InvokeVoidAsync("saveGame", json);
        }
        catch { }
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
