using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class BegHandler : ICommandHandler<BegCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public BegHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(BegCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (player.Gold >= 100)
            return CommandResult.Fail($"You still have {player.Gold}g. Begging is beneath you.");

        if (state.HasBegged)
            return CommandResult.Fail("You've already begged once. No more handouts.");

        player.AddGold(100);
        state.SetHasBegged();
        state.AppendLog("You beg passersby for coins. Someone tosses you 100g. Embarrassing.", LogEntryKind.System);
        state.AppendLog($"Gold: {player.Gold}g", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
