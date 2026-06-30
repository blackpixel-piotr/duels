using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class BuyItemHandler : ICommandHandler<BuyItemCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public BuyItemHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(BuyItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var shopItems = _itemRepo.GetShopItems();
        var entry = shopItems.FirstOrDefault(x => x.Id == command.ItemId);

        if (entry == default)
            return CommandResult.Fail($"'{command.ItemId}' is not sold here. Type !shop to see available items.");

        var (id, name, price) = entry;

        if (!state.Player.SpendGold(price))
        {
            state.AppendLog($"Not enough gold. {name} costs {price}g, you have {state.Player.Gold}g.", LogEntryKind.System);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Fail("Not enough gold.");
        }

        state.Player.AddToInventory(id);
        state.AppendLog($"You buy {name} for {price}g. (Remaining: {state.Player.Gold}g)", LogEntryKind.Loot);
        state.AppendLog($"Equip it with: !equip {id}", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
