using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class GrantDevLoadoutHandler : ICommandHandler<GrantDevLoadoutCommand>
{
    private static readonly string[] ArmorSlots = ["helmet", "body", "legs", "boots", "gloves", "cape"];

    private readonly IGameStateRepository _stateRepo;

    public GrantDevLoadoutHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(GrantDevLoadoutCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (state.InDuel) return CommandResult.Fail("Can't change loadout mid-fight.");
        if (command.Tier is not (1 or 2)) return CommandResult.Fail("Tier must be 1 or 2.");
        if (!Enum.TryParse<GearLine>(command.Line, ignoreCase: true, out var line) || line == GearLine.None)
            return CommandResult.Fail($"Unknown line '{command.Line}'.");

        var player = state.Player;
        string suffix = $"_t{command.Tier}";
        string[] weapons = [$"wpn_melee{suffix}", $"wpn_ranged{suffix}", $"wpn_magic{suffix}"];
        string lineKey = line.ToString().ToLowerInvariant();

        foreach (var w in weapons)
            if (!player.HasItem(w)) player.AddToInventory(w);

        foreach (var slot in ArmorSlots)
        {
            var id = $"arm_{lineKey}_{slot}{suffix}";
            if (!player.HasItem(id)) player.AddToInventory(id);
            if (Enum.TryParse<EquipmentSlot>(slot, ignoreCase: true, out var eqSlot))
                player.Equip(id, eqSlot);
        }

        var wieldedId = line switch
        {
            GearLine.Warbound => weapons[0],
            GearLine.Stalker => weapons[1],
            _ => weapons[2],
        };
        player.Equip(wieldedId, EquipmentSlot.Weapon);

        if (!player.HasItem("flask_health")) player.AddToInventory("flask_health");
        if (!player.HasItem("flask_prayer")) player.AddToInventory("flask_prayer");

        for (int i = 0; i < weapons.Length; i++) player.Loadout.BindWeapon(i, weapons[i]);
        player.Loadout.BindWeapon(3, null);
        player.Loadout.BindFlask(0, "flask_health");
        player.Loadout.BindFlask(1, "flask_prayer");

        state.AppendLog($"[DEV] T{command.Tier} {line} loadout granted and bound to the bar.", LogEntryKind.System);
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
