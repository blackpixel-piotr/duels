using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class EngageHandler : ICommandHandler<EngageCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public EngageHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(EngageCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null || !state.InDuel) return CommandResult.Fail("Not in a duel.");

        state.Engage();
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
