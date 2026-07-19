using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class WeaponShortcutHandler : ICommandHandler<WeaponShortcutCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;

    public WeaponShortcutHandler(IGameStateRepository stateRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(WeaponShortcutCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (!player.HasItem(command.WeaponId))
        {
            var name = _itemRepo.GetItemName(command.WeaponId) ?? command.WeaponId;
            return CommandResult.Fail($"You don't have {name}.");
        }

        var previousWeapon = player.GetEquippedWeaponId();
        bool switching = previousWeapon != command.WeaponId;

        // Input buffer (UI bible §3.2): mid-duel, only the first weapon swap
        // in a tick resolves now — an overflow tap buffers for the top of the
        // next tick (GameTickService applies PendingWeaponSwapId there)
        // instead of racing this tick's resolution.
        if (switching && state.InDuel && !state.TryClaimWeaponSwapSlot())
        {
            state.SetPendingWeaponSwap(command.WeaponId);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Ok();
        }

        // Equip immediately if not already equipped
        if (switching)
        {
            player.Equip(command.WeaponId, EquipmentSlot.Weapon);
            var name = _itemRepo.GetItemName(command.WeaponId) ?? command.WeaponId;
            state.AppendLog($"You ready your {name}.", LogEntryKind.Info);
        }

        if (state.InDuel)
        {
            // Re-engage: a weapon click always resumes the chase/attack even
            // if a prior move order left the player holding position.
            state.Engage();
            state.SetQueuedAction("attack");
            // If mid-duel weapon swap, schedule a revert to the previous weapon after this tick
            state.SetRevertWeapon(switching ? previousWeapon : null);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
