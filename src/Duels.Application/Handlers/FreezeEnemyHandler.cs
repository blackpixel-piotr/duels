using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class FreezeEnemyHandler : ICommandHandler<FreezeEnemyCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public FreezeEnemyHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(FreezeEnemyCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active session.");

        state.FreezeEnemy(command.Frozen);
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
