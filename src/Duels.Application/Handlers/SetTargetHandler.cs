using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class SetTargetHandler : ICommandHandler<SetTargetCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public SetTargetHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(SetTargetCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null || !state.InDuel) return CommandResult.Fail("Not in a duel.");

        if (command.AddId is not null && state.Adds.All(a => a.Id != command.AddId))
            return CommandResult.Fail("That add is no longer here.");

        state.SetTarget(command.AddId);
        state.Engage();
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
