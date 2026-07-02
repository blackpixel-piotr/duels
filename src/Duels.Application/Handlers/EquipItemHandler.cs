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

        var weapon = _itemRepo.GetWeapon(command.ItemId);
        if (weapon is not null && player.AttackLevel < weapon.AttackLevelRequired)
        {
            state.AppendLog($"You need {weapon.AttackLevelRequired} Attack to wield {weapon.Name}. (You: {player.AttackLevel})", LogEntryKind.System);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Fail($"Requires {weapon.AttackLevelRequired} Attack.");
        }
        if (weapon is null && player.DefenceLevel < gear.DefenceLevelRequired)
        {
            state.AppendLog($"You need {gear.DefenceLevelRequired} Defence to wear {gear.Name}. (You: {player.DefenceLevel})", LogEntryKind.System);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Fail($"Requires {gear.DefenceLevelRequired} Defence.");
        }

        player.Equip(command.ItemId, gear.Slot);
        state.AppendLog($"You equip {gear.Name}.", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok($"Equipped {gear.Name}.");
    }
}
