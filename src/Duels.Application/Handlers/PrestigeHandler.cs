using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class PrestigeHandler : ICommandHandler<PrestigeCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public PrestigeHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(PrestigeCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (!state.CanPrestige)
            return CommandResult.Fail("You haven't conquered the Duel Arena yet. Defeat the Champion first!");

        var player = state.Player;
        int newLevel = player.PrestigeLevel + 1;

        string phatId = newLevel switch
        {
            1 => "red_partyhat",
            2 => "blue_partyhat",
            _ => "white_partyhat"
        };

        string phatName = newLevel switch
        {
            1 => "[Red Phat]",
            2 => "[Blue Phat]",
            _ => "[White Phat]"
        };

        player.Prestige();
        player.AddToInventory(phatId);
        state.Reset();

        state.AppendLog($"{phatName} You have been reborn. The grind begins again.", LogEntryKind.System);
        state.AppendLog($"Prestige level: {player.PrestigeLevel}. All progress reset.", LogEntryKind.System);
        if (player.PrestigeLevel >= 2)
            state.AppendLog("Perk: Your max HP is now 109.", LogEntryKind.System);
        if (player.PrestigeLevel >= 3)
            state.AppendLog("Perk: You start with a Rune Scimitar.", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
