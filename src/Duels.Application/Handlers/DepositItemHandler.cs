using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class DepositItemHandler : ICommandHandler<DepositItemCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public DepositItemHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(DepositItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (!state.Player.RemoveFromInventory(command.ItemId))
            return CommandResult.Fail("Item not in inventory.");

        state.Bank.Add(command.ItemId);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
