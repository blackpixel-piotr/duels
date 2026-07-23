using Duels.Application.Abstractions;
using Duels.Application.Commands;

namespace Duels.Application.Handlers;

public sealed class BindFlaskSlotHandler : ICommandHandler<BindFlaskSlotCommand>
{
    private static readonly HashSet<string> KnownFlasks = ["flask_health", "flask_prayer", "flask_rotward"];

    private readonly IGameStateRepository _stateRepo;

    public BindFlaskSlotHandler(IGameStateRepository stateRepo) => _stateRepo = stateRepo;

    public async Task<CommandResult> HandleAsync(BindFlaskSlotCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (state.InDuel) return CommandResult.Fail("The flask belt is locked during a fight.");

        if (command.FlaskId is { } id)
        {
            if (!KnownFlasks.Contains(id)) return CommandResult.Fail("Unknown flask.");
            if (!state.Player.HasItem(id)) return CommandResult.Fail("You don't own that flask.");
        }

        if (!state.Player.Loadout.BindFlask(command.Slot, command.FlaskId))
            return CommandResult.Fail("Invalid slot (0-1).");

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
