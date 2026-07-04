using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class StartTestFightHandler : ICommandHandler<StartTestFightCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly INpcRepository _npcRepo;

    public StartTestFightHandler(IGameStateRepository stateRepo, INpcRepository npcRepo)
    {
        _stateRepo = stateRepo;
        _npcRepo = npcRepo;
    }

    public async Task<CommandResult> HandleAsync(StartTestFightCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (state.InDuel) return CommandResult.Fail("Already in a duel.");

        var player = state.Player;

        // Force-give loadout, bypassing economy and level requirements.
        if (!player.HasItem("abyssal_whip")) player.AddToInventory("abyssal_whip");
        if (!player.HasItem("dragon_dagger")) player.AddToInventory("dragon_dagger");

        player.Equip("abyssal_whip", EquipmentSlot.Weapon);

        var template = _npcRepo.GetTemplate("barbarian")
            ?? throw new InvalidOperationException("barbarian template missing");
        state.StartDuel(new NpcInstance(template));

        state.AppendLog("[TEST] Whip + DDS loaded. Fight!", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
