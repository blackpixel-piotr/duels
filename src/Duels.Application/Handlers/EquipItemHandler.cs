using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class EquipItemHandler : ICommandHandler<EquipItemCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public EquipItemHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(EquipItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;
        if (!player.HasItem(command.ItemId))
            return CommandResult.Fail($"You don't have '{command.ItemId}' in your inventory.");

        var gear = _itemRepo.GetGear(command.ItemId);
        if (gear is null)
            return CommandResult.Fail($"'{command.ItemId}' cannot be equipped.");

        player.Equip(command.ItemId, gear.Slot);
        state.AppendLog($"You equip {gear.Name}.", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok($"Equipped {gear.Name}.");
    }
}
