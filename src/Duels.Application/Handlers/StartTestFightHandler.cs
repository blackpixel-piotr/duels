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

        // PoC 3D loadout: the steel sword + full ranger set render on the
        // battle-scene character (weapon in hand, armor skinned onto the rig).
        var poc = new (string Id, EquipmentSlot Slot)[]
        {
            ("steel_sword", EquipmentSlot.Weapon),
            ("ranger_hood", EquipmentSlot.Helmet),
            ("ranger_tunic", EquipmentSlot.Body),
            ("ranger_trousers", EquipmentSlot.Legs),
            ("ranger_boots", EquipmentSlot.Boots),
            ("ranger_bracers", EquipmentSlot.Gloves),
            ("ranger_pauldrons", EquipmentSlot.Cape),
        };
        foreach (var (id, slot) in poc)
        {
            if (!player.HasItem(id)) player.AddToInventory(id);
            player.Equip(id, slot);
        }

        var template = _npcRepo.GetTemplate("barbarian")
            ?? throw new InvalidOperationException("barbarian template missing");
        state.StartDuel(new NpcInstance(template));
        state.SetTestScene(true); // open-field scene instead of the arena ring

        state.AppendLog($"[TEST] Steel sword + ranger set equipped (whip + DDS in bag). Fight {template.Name}!", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
