using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Domain.Entities;

namespace Duels.Application.Handlers;

public sealed class BuyItemHandler : ICommandHandler<Commands.BuyItemCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public BuyItemHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(Commands.BuyItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var price = _itemRepo.GetShopPrice(command.ItemId);
        if (price is null) return CommandResult.Fail($"'{command.ItemId}' isn't for sale.");

        var player = state.Player;
        int qty = Math.Max(1, command.Quantity);
        int bought = 0;
        for (int i = 0; i < qty; i++)
        {
            if (!player.SpendGold(price.Value)) break;
            // Purchase overflow goes to the bank, never fenced (economy doc:
            // buying an item and having it instantly auto-sold would be a
            // trap, not a QoL — that fence path is loot-only, see RollLoot).
            if (player.Inventory.Count < Player.BagCapacity) player.AddToInventory(command.ItemId);
            else player.AddToBank(command.ItemId);
            bought++;
        }

        if (bought == 0)
            return CommandResult.Fail($"Not enough gold (need {price:N0}g, have {player.Gold:N0}g).");

        var name = _itemRepo.GetItemName(command.ItemId) ?? command.ItemId;
        state.AppendLog($"You buy {(bought > 1 ? $"{bought}x " : "")}{name} for {price * bought:N0}g.", LogEntryKind.Loot);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok($"Bought {bought}x {name}.");
    }
}
