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

        int qty = Math.Max(1, command.Quantity);
        int totalCost = price * qty;

        if (state.Player.Gold < totalCost)
        {
            string need = qty > 1 ? $"{totalCost:N0}g ({qty}× {price:N0}g)" : $"{price:N0}g";
            state.AppendLog($"Not enough gold. {name} costs {need}, you have {state.Player.Gold:N0}g.", LogEntryKind.System);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Fail("Not enough gold.");
        }

        state.Player.SpendGold(totalCost);
        for (int i = 0; i < qty; i++) state.Player.AddToInventory(id);
        string bought = qty > 1 ? $"{qty}× {name}" : name;
        state.AppendLog($"Bought {bought} for {totalCost:N0}g. (Remaining: {state.Player.Gold:N0}g)", LogEntryKind.Loot);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
