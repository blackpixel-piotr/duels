using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class StartEndlessHandler : ICommandHandler<StartEndlessCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IEventBus _events;

    public StartEndlessHandler(IGameStateRepository stateRepo, IEventBus events)
    {
        _stateRepo = stateRepo;
        _events = events;
    }

    public async Task<CommandResult> HandleAsync(StartEndlessCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (state.InDuel)
            return CommandResult.Fail($"You are already in a duel with {state.ActiveNpc!.Template.Name}!");

        state.Player.RestoreHp();
        state.StartEndless();
        int wave = state.NextEndlessWave();
        var npc = BuildEndlessNpc(wave);
        state.StartDuel(npc);

        state.AppendLog("═══ ENDLESS MODE ═══", LogEntryKind.System);
        state.AppendLog($"Wave {wave}: {npc.Template.Name} ({npc.MaxHp} HP). Fight until you fall!", LogEntryKind.System);

        await _stateRepo.SaveAsync(state, ct);
        await _events.PublishAsync(new DuelStarted(command.PlayerId, npc.Template.Id, npc.Template.Name), ct);

        return CommandResult.Ok();
    }

    private static NpcInstance BuildEndlessNpc(int wave)
    {
        int hp = 50 + wave * 6;
        int mod = 20 + wave * 4;
        int lvl = Math.Min(99, 60 + wave);
        var template = new NpcTemplate(
            $"endless_w{wave}",
            $"Wave {wave} Fighter",
            $"A relentless wave {wave} challenger.",
            new CombatStats(lvl, lvl, lvl, hp),
            new ItemModifiers(SlashAttack: mod, StrengthBonus: mod),
            AttackType.Slash,
            [],
            goldReward: wave * 50);
        return new NpcInstance(template);
    }
}
