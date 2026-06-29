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
            "  !duel <enemy>          Start a duel  (e.g. !duel bandit)",
            "  !npcs                  List unlocked opponents",
            "  !attack [style]        Attack: accurate | aggressive | defensive",
            "  !spec / !dds           Use weapon special attack",
            "  !shop                  Browse the item shop",
            "  !buy <item_id>         Purchase an item (e.g. !buy iron_sword)",
            "  !equip <item_id>       Equip an item from inventory",
            "  !unequip <slot>        Remove item from a slot (weapon / helmet / body / shield)",
            "  !stats                 Show your stats and gold",
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
