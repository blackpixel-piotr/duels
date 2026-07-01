using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class DrinkPotionHandler : ICommandHandler<DrinkPotionCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public DrinkPotionHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(DrinkPotionCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (!player.RemoveFromInventory("super_combat_potion"))
            return CommandResult.Fail("You don't have a Super Combat Potion. Buy one from the shop.");

        player.DrinkSuperCombat();
        state.AppendLog($"You drink the Super Combat Potion! Atk/Str boosted for {player.CombatBoostRoundsLeft} rounds.", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
