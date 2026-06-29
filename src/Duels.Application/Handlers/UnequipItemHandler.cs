using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class UnequipItemHandler : ICommandHandler<UnequipItemCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public UnequipItemHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(UnequipItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var removed = state.Player.Unequip(command.Slot);
        if (removed is null)
            return CommandResult.Fail($"Nothing equipped in {command.Slot} slot.");

        state.AppendLog($"You unequip the item from your {command.Slot} slot.", LogEntryKind.Info);
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
