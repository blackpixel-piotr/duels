using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class EatItemHandler : ICommandHandler<EatItemCommand>
{
    private static readonly Dictionary<string, (int Heal, bool CanOverheal)> FoodData = new()
    {
        ["shark"]       = (20, false),
        ["karambwan"]   = (18, false),
        ["anglerfish"]  = (22, true),
    };

    private readonly IGameStateRepository _stateRepo;

    public EatItemHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(EatItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (!FoodData.TryGetValue(command.ItemId, out var food))
            return CommandResult.Fail($"'{command.ItemId}' is not food. Try: shark, karambwan, anglerfish.");

        if (!player.RemoveFromInventory(command.ItemId))
            return CommandResult.Fail($"You don't have any {command.ItemId}.");

        if (player.CurrentHp >= player.MaxHp && !food.CanOverheal)
        {
            player.AddToInventory(command.ItemId);
            return CommandResult.Fail("You are already at full HP.");
        }

        int before = player.CurrentHp;
        player.HealFood(food.Heal, food.CanOverheal);
        int healed = player.CurrentHp - before;
        state.AppendLog($"You eat the {command.ItemId}. HP: {before} → {player.CurrentHp}/{player.MaxHp} (+{healed})", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
