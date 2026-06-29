using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;

namespace Duels.Application.Handlers;

public sealed class StartDuelHandler : ICommandHandler<StartDuelCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly INpcRepository _npcRepo;
    private readonly IEventBus _events;

    public StartDuelHandler(IGameStateRepository stateRepo, INpcRepository npcRepo, IEventBus events)
    {
        _stateRepo = stateRepo;
        _npcRepo = npcRepo;
        _events = events;
    }

    public async Task<CommandResult> HandleAsync(StartDuelCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game. Refresh the page.");

        if (state.InDuel)
            return CommandResult.Fail($"You are already in a duel with {state.ActiveNpc!.Template.Name}!");

        var template = _npcRepo.GetTemplate(command.NpcId);
        if (template is null)
            return CommandResult.Fail($"Unknown enemy: '{command.NpcId}'. Type !npcs to see available enemies.");

        if (!state.UnlockedOpponents.Contains(command.NpcId))
            return CommandResult.Fail($"You haven't earned the right to face {template.Name} yet. Defeat weaker foes first.");

        state.Player.RestoreHp();
        var npc = new NpcInstance(template);
        state.StartDuel(npc);

        state.AppendLog($"═══ DUEL STARTED ═══", LogEntryKind.System);
        state.AppendLog($"You challenge {template.Name} (level {template.CombatLevel})!", LogEntryKind.System);
        state.AppendLog($"{template.ExamineText}", LogEntryKind.Info);
        state.AppendLog($"Type !attack, !spec, or use a quickslot to fight.", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        await _events.PublishAsync(new DuelStarted(command.PlayerId, template.Id, template.Name), ct);

        return CommandResult.Ok();
    }
}
