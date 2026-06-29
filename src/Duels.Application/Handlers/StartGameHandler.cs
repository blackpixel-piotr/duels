using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;

namespace Duels.Application.Handlers;

public sealed class StartGameHandler : ICommandHandler<StartGameCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IPlayerRepository _playerRepo;

    public StartGameHandler(IGameStateRepository stateRepo, IPlayerRepository playerRepo)
    {
        _stateRepo = stateRepo;
        _playerRepo = playerRepo;
    }

    public async Task<CommandResult> HandleAsync(StartGameCommand command, CancellationToken ct = default)
    {
        var playerId = command.PlayerId;
        var player = new Player(playerId, command.PlayerName);
        var state = new GameState(playerId, player);

        state.AppendLog($"Welcome, {player.Name}! Your dueling career begins.", LogEntryKind.System);
        state.AppendLog("Start with: !duel bandit  — defeat foes to earn gold and unlock harder opponents.", LogEntryKind.System);
        state.AppendLog("Visit the shop with !shop, buy gear with !buy <item_id>.", LogEntryKind.System);
        state.AppendLog("Use !help for a list of commands.", LogEntryKind.System);

        await _playerRepo.SaveAsync(player, ct);
        await _stateRepo.SaveAsync(state, ct);

        return CommandResult.Ok($"Character '{player.Name}' created. Good luck!");
    }
}
