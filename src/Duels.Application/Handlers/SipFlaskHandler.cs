using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class SipFlaskHandler : ICommandHandler<SipFlaskCommand>
{
    // Rotward's 15-tick immunity window (backlog resolution batch 1 §3).
    private const int RotwardImmunityTicks = 15;

    // Flask restore amounts (design decisions doc: unspecified — T-list tunable).
    private static readonly Dictionary<string, (string Label, Action<Domain.Entities.Player, GameState> Apply)> Effects = new()
    {
        ["flask_health"] = ("Health Flask", (p, _) => p.Heal(40)),
        ["flask_prayer"] = ("Prayer Flask", (p, _) => p.RestorePrayerPoints(40)),
        ["flask_rotward"] = ("Rotward Flask", (_, s) => { s.CurePoison(); s.GrantPoisonImmunity(RotwardImmunityTicks); }),
    };

    private readonly IGameStateRepository _stateRepo;

    public SipFlaskHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(SipFlaskCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (!state.InDuel) return CommandResult.Fail("Not in a duel.");
        if (command.Slot is not (0 or 1)) return CommandResult.Fail("Invalid flask slot.");

        var slot = state.Player.FlaskBelt.Slots[command.Slot];
        if (slot.FlaskId is not { } flaskId)
            return CommandResult.Fail("No flask bound to that slot — bind one in the Loadout Editor.");
        if (!Effects.TryGetValue(flaskId, out var effect))
            return CommandResult.Fail($"Unknown flask '{flaskId}'.");
        if (!slot.TrySip())
            return CommandResult.Fail($"{effect.Label} is empty.");

        effect.Apply(state.Player, state);
        state.AppendLog($"You sip the {effect.Label}. ({slot.SipsRemaining}/{slot.MaxSips} sips left)", LogEntryKind.Info);

        // Weapon-speed ratification: sipping always costs tempo, never a full
        // attack slot — it adds exactly +1 tick to whatever's currently on the
        // cooldown clock (0 if idle), rather than granting a free sip whenever
        // the player happened to already be mid-cooldown.
        state.DelayPlayerAttack(1);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
