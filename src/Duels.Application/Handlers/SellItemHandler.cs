using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class SellItemHandler : ICommandHandler<SellItemCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public SellItemHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(SellItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (!state.Player.RemoveFromInventory(command.ItemId))
            return CommandResult.Fail("Item not in inventory.");

        int value = _itemRepo.GetFenceValue(command.ItemId);
        state.Player.AddGold(value);

        var name = _itemRepo.GetItemName(command.ItemId) ?? command.ItemId;
        state.AppendLog($"Sold {name} for {value:N0}g. (Gold: {state.Player.Gold:N0}g)", LogEntryKind.Loot);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
