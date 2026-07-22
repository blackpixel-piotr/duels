using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class DepositItemHandler : ICommandHandler<DepositItemCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public DepositItemHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(DepositItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;
        int target = command.Quantity > 0 ? command.Quantity : int.MaxValue; // <=0 = "All"
        int moved = 0;
        while (moved < target && player.Deposit(command.ItemId)) moved++;

        if (moved == 0)
            return CommandResult.Fail($"You don't have '{command.ItemId}' to deposit.");

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok($"Deposited {moved}x.");
    }
}
