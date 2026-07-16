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

        var npcId = command.NpcId ?? "barbarian";
        var template = _npcRepo.GetTemplate(npcId)
            ?? throw new InvalidOperationException($"{npcId} template missing");
        state.StartDuel(new NpcInstance(template));
        state.SetTestScene(true); // open-field scene instead of the arena floor
        state.FreezeEnemy(true); // enemy starts frozen — no auto-chase/attack
        state.HoldPositionAtSpawn(); // player starts holding — no auto-chase/attack

        state.AppendLog($"[TEST] Whip + DDS loaded. Fight {template.Name}!", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
