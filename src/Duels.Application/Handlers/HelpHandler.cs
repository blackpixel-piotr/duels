using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class HelpHandler : ICommandHandler<HelpCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public HelpHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(HelpCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var lines = new[]
        {
            "--- Commands ---",
            "  !duel <enemy>          Start a duel  (e.g. !duel goblin)",
            "  !npcs                  List available enemies",
            "  !attack [style]        Attack with style: accurate | aggressive | defensive",
            "  !whip                  Quick attack (accurate)",
            "  !dds                   Dragon dagger special (aggressive, uses special energy)",
            "  !spec                  Use special attack",
            "  !equip <item_id>       Equip an item from inventory",
            "  !unequip <slot>        Remove item from a slot",
            "  !stats                 Show your stats",
            "  !inspect npc           Examine current enemy",
            "  !inspect <item_id>     Examine an item",
            "  !help                  Show this list",
        };

        foreach (var line in lines)
            state.AppendLog(line, LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
