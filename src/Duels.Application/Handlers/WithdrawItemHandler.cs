using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class WithdrawItemHandler : ICommandHandler<WithdrawItemCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public WithdrawItemHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(WithdrawItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        int idx = state.Bank.LastIndexOf(command.ItemId);
        if (idx < 0) return CommandResult.Fail("Item not in bank.");

        state.Bank.RemoveAt(idx);
        state.Player.AddToInventory(command.ItemId);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
