using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;

namespace Duels.Application.Handlers;

public sealed class StartGameHandler : ICommandHandler<StartGameCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IPlayerRepository _playerRepo;

    public StartGameHandler(IGameStateRepository stateRepo, IPlayerRepository playerRepo)
    {
        _stateRepo = stateRepo;
        _playerRepo = playerRepo;
    }

    // Backlog resolution batch 1 §4 (cold start ruling): 600g start + one free
    // T1 weapon of the player's chosen style, equipped and bound to bar slot 0
    // — replaces the old "no gold, no gear, grab a dev loadout" bootstrap.
    private static readonly Dictionary<string, string> StyleWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Melee"] = "wpn_melee_t1",
        ["Ranged"] = "wpn_ranged_t1",
        ["Magic"] = "wpn_magic_t1",
    };

    public async Task<CommandResult> HandleAsync(StartGameCommand command, CancellationToken ct = default)
    {
        if (!StyleWeapons.TryGetValue(command.ChosenStyle, out var weaponId))
            return CommandResult.Fail($"Unknown style '{command.ChosenStyle}' — choose Melee, Ranged, or Magic.");

        var playerId = command.PlayerId;
        var player = new Player(playerId, command.PlayerName);
        var state = new GameState(playerId, player);

        player.AddToInventory(weaponId);
        player.Equip(weaponId, Domain.ValueObjects.EquipmentSlot.Weapon);
        player.Loadout.BindWeapon(0, weaponId);

        var weaponName = weaponId switch
        {
            "wpn_melee_t1" => "Rustcleaver",
            "wpn_ranged_t1" => "Poacher's Bow",
            _ => "Cinder Wand",
        };
        state.AppendLog($"Welcome, {player.Name}! Your dueling career begins.", LogEntryKind.System);
        state.AppendLog($"You're handed a {weaponName} and {player.Gold:N0}g to get started.", LogEntryKind.System);
        state.AppendLog("Visit the Gold Shop for armour, then tap FIGHT to face the Maggot King.", LogEntryKind.System);

        await _playerRepo.SaveAsync(player, ct);
        await _stateRepo.SaveAsync(state, ct);

        return CommandResult.Ok($"Character '{player.Name}' created. Good luck!");
    }
}
