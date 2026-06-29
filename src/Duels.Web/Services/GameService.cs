using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Parsing;

namespace Duels.Web.Services;

public sealed class GameService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly CommandParser _parser;
    private readonly IGameStateRepository _stateRepo;

    public string? PlayerId { get; private set; }
    public bool IsStarted => PlayerId is not null;

    public event Action? StateChanged;

    public GameService(ICommandDispatcher dispatcher, CommandParser parser, IGameStateRepository stateRepo)
    {
        _dispatcher = dispatcher;
        _parser = parser;
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> StartNewGameAsync(string playerName)
    {
        PlayerId = Guid.NewGuid().ToString("N");
        var result = await _dispatcher.DispatchAsync(new StartGameCommand(PlayerId, playerName));
        NotifyStateChanged();
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
            if (state is not null) await SaveErrorToLog(state);
            NotifyStateChanged();
            return CommandResult.Fail(parsed.Error ?? "Parse error.");
        }

        var result = await _dispatcher.DispatchAsync((dynamic)parsed.Command!);
        NotifyStateChanged();
        return result;
    }

    public async Task<GameState?> GetStateAsync() =>
        PlayerId is null ? null : await _stateRepo.GetAsync(PlayerId);

    private async Task SaveErrorToLog(GameState state)
    {
        // state is already mutated in-memory (InMemoryGameStateRepository returns the same instance)
        await Task.CompletedTask;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
