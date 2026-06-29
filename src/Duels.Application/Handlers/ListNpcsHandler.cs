using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class ListNpcsHandler : ICommandHandler<ListNpcsCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly INpcRepository _npcRepo;

    public ListNpcsHandler(IGameStateRepository stateRepo, INpcRepository npcRepo)
    {
        _stateRepo = stateRepo;
        _npcRepo = npcRepo;
    }

    public async Task<CommandResult> HandleAsync(ListNpcsCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        state.AppendLog("--- Unlocked Opponents ---", LogEntryKind.Info);
        foreach (var id in state.UnlockedOpponents)
        {
            var npc = _npcRepo.GetTemplate(id);
            if (npc is not null)
                state.AppendLog($"  !duel {npc.Id,-20} Level {npc.CombatLevel,3}  {npc.Name}  ({npc.GoldReward}g reward)", LogEntryKind.Info);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
