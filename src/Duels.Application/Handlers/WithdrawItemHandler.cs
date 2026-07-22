using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class WithdrawItemHandler : ICommandHandler<WithdrawItemCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public WithdrawItemHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(WithdrawItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;
        int target = command.Quantity > 0 ? command.Quantity : int.MaxValue; // <=0 = "All"
        int moved = 0;
        while (moved < target && player.Withdraw(command.ItemId)) moved++;

        if (moved == 0)
        {
            return player.BankedItems.Contains(command.ItemId)
                ? CommandResult.Fail("Your bag is full.")
                : CommandResult.Fail($"You don't have '{command.ItemId}' banked.");
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok($"Withdrew {moved}x.");
    }
}
