using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class WeaponShortcutHandler : ICommandHandler<WeaponShortcutCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;
    private readonly ICommandDispatcher _dispatcher;

    public WeaponShortcutHandler(
        IGameStateRepository stateRepo,
        IItemRepository itemRepo,
        ICommandDispatcher dispatcher)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
        _dispatcher = dispatcher;
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

        // Equip if not already the active weapon
        if (player.GetEquippedWeaponId() != command.WeaponId)
        {
            player.Equip(command.WeaponId, EquipmentSlot.Weapon);
            var name = _itemRepo.GetItemName(command.WeaponId) ?? command.WeaponId;
            state.AppendLog($"You ready your {name}.", LogEntryKind.Info);
            await _stateRepo.SaveAsync(state, ct);
        }

        if (!state.InDuel)
            return CommandResult.Ok();

        var weapon = _itemRepo.GetWeapon(command.WeaponId);
        bool useSpec = weapon?.Special is not null;
        return await _dispatcher.DispatchAsync(
            new AttackCommand(command.PlayerId, AttackStyle.Accurate, UseSpecial: useSpec, SkillAccuracy: command.SkillAccuracy), ct);
    }
}
