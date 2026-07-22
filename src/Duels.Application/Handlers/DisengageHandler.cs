using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class DisengageHandler : ICommandHandler<DisengageCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public DisengageHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(DisengageCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null || !state.InDuel) return CommandResult.Fail("Not in a duel.");

        state.Disengage();
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
