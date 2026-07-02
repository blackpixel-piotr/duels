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

        var result = command.ItemId switch
        {
            "antidote" => DrinkAntidote(state, player),
            _ => DrinkSuperCombat(state, player),
        };

        if (!result.Success) return result;

        if (state.InDuel) state.DelayPlayerAttack(1);

        await _stateRepo.SaveAsync(state, ct);
        return result;
    }

    private static CommandResult DrinkSuperCombat(GameState state, Domain.Entities.Player player)
    {
        if (!player.RemoveFromInventory("super_combat_potion"))
            return CommandResult.Fail("You don't have a Super Combat Potion. Buy one from the shop.");

        player.DrinkSuperCombat();
        state.AppendLog($"You drink the Super Combat Potion! Atk/Str boosted for {player.CombatBoostRoundsLeft} rounds. (+1 tick delay)", LogEntryKind.Info);
        return CommandResult.Ok();
    }

    private static CommandResult DrinkAntidote(GameState state, Domain.Entities.Player player)
    {
        if (!player.RemoveFromInventory("antidote"))
            return CommandResult.Fail("You don't have an Antidote. Buy one from the shop.");

        if (!state.PlayerPoisoned)
        {
            state.AppendLog("You drink the Antidote, but you weren't poisoned. (+1 tick delay)", LogEntryKind.Info);
            return CommandResult.Ok();
        }

        state.CurePoison();
        state.AppendLog("You drink the Antidote — the poison fades from your veins. (+1 tick delay)", LogEntryKind.Info);
        return CommandResult.Ok();
    }
}
