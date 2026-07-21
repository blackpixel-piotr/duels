using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class ToggleMechanicHandler : ICommandHandler<ToggleMechanicCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public ToggleMechanicHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(ToggleMechanicCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active session.");

        state.ToggleMechanic(command.Mechanic);
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
