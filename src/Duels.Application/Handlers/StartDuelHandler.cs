using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;

namespace Duels.Application.Handlers;

public sealed class StartDuelHandler : ICommandHandler<StartDuelCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly INpcRepository _npcRepo;
    private readonly IEventBus _events;
    private readonly IRandomProvider _random;

    private static readonly string[] RareIds = ["rare_tourist", "rare_gladiator"];

    public StartDuelHandler(IGameStateRepository stateRepo, INpcRepository npcRepo, IEventBus events, IRandomProvider random)
    {
        _stateRepo = stateRepo;
        _npcRepo = npcRepo;
        _events = events;
        _random = random;
    }

    public async Task<CommandResult> HandleAsync(StartDuelCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game. Refresh the page.");

        if (state.InDuel)
            return CommandResult.Fail($"You are already in a duel with {state.ActiveNpc!.Template.Name}!");

        var npcId = command.NpcId;
        var template = _npcRepo.GetTemplate(npcId);
        if (template is null)
            return CommandResult.Fail($"Unknown enemy: '{npcId}'. Type !npcs to see available enemies.");

        if (!state.UnlockedOpponents.Contains(npcId))
            return CommandResult.Fail($"You haven't earned the right to face {template.Name} yet. Defeat weaker foes first.");

        // Wager validation
        var player = state.Player;
        if (command.Wager > 0)
        {
            if (player.Gold < command.Wager)
                return CommandResult.Fail($"Not enough gold. You have {player.Gold}g, wager requires {command.Wager}g.");
            if (template.MaxWager > 0 && command.Wager > template.MaxWager)
                return CommandResult.Fail($"{template.Name} won't match a stake above {template.MaxWager:N0}g.");
            player.SpendGold(command.Wager);
            state.SetWager(command.Wager);
            state.SetLastWager(command.Wager);
        }

        // 5% rare encounter (skip for goblin / rare / endless / bosses)
        if (npcId != "goblin" && npcId != "maggot_king" && !npcId.StartsWith("rare_") && !npcId.StartsWith("endless_") && _random.NextDouble() < 0.05)
        {
            var rareId = RareIds[_random.Next(0, RareIds.Length)];
            var rareTemplate = _npcRepo.GetTemplate(rareId);
            if (rareTemplate is not null)
            {
                template = rareTemplate;
                npcId = rareId;
                state.AppendLog("★ A rare challenger appears!", LogEntryKind.System);
            }
        }

        player.RestoreHp();
        var npc = new NpcInstance(template);
        state.StartDuel(npc);

        state.AppendLog("═══ DUEL STARTED ═══", LogEntryKind.System);
        state.AppendLog($"You challenge {template.Name} ({npc.MaxHp} HP)!", LogEntryKind.System);
        state.AppendLog($"{template.ExamineText}", LogEntryKind.Info);

        if (command.Wager > 0)
        {
            int potentialPayout = (int)(command.Wager * 2 * state.WinStreakMultiplier);
            state.AppendLog($"You stake {command.Wager:N0}g! Win = {potentialPayout:N0}g (streak ×{state.WinStreakMultiplier:F1})", LogEntryKind.Loot);
        }

        state.AppendLog("Fight!", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        await _events.PublishAsync(new DuelStarted(command.PlayerId, template.Id, template.Name), ct);

        return CommandResult.Ok();
    }
}
