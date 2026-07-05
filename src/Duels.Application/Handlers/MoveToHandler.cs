using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class MoveToHandler : ICommandHandler<MoveToCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public MoveToHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(MoveToCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null || !state.InDuel) return CommandResult.Fail("Not in a duel.");

        state.OrderMove(command.X, command.Z);
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
