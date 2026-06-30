using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class ShopHandler : ICommandHandler<ShopCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public ShopHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(ShopCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        state.AppendLog($"--- Shop  (Your gold: {state.Player.Gold}g) ---", LogEntryKind.Info);
        foreach (var (id, name, price) in _itemRepo.GetShopItems())
            state.AppendLog($"  {id,-22} {name,-22} {price,7}g   (!buy {id})", LogEntryKind.Info);
        state.AppendLog("Buy with: !buy <item_id>", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
