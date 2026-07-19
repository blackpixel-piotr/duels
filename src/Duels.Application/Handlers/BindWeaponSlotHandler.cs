using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class BindWeaponSlotHandler : ICommandHandler<BindWeaponSlotCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _items;

    public BindWeaponSlotHandler(IGameStateRepository stateRepo, IItemRepository items)
    {
        _stateRepo = stateRepo;
        _items = items;
    }

    public async Task<CommandResult> HandleAsync(BindWeaponSlotCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        // Bar locked mid-fight (UI bible §4.2): the fight tests the bar you brought.
        if (state.InDuel) return CommandResult.Fail("The action bar is locked during a fight.");

        if (command.WeaponId is { } id)
        {
            if (!state.Player.HasItem(id)) return CommandResult.Fail("You don't own that weapon.");
            if (!_items.IsWeapon(id)) return CommandResult.Fail("That item isn't a weapon.");
        }

        if (!state.Player.Loadout.BindWeapon(command.Slot, command.WeaponId))
            return CommandResult.Fail("Invalid slot (0-3).");

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
