using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class AttackHandler : ICommandHandler<AttackCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public AttackHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(AttackCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (!state.InDuel) return CommandResult.Fail("You are not in a duel. Type !duel <npc> to start one.");

        // Re-engage: attacking always resumes the chase/attack even if a
        // prior move order left the player holding position off-target.
        state.Engage();
        state.SetQueuedAction(command.UseSpecial ? "spec" : "attack");
        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
