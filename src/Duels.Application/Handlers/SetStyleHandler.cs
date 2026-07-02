using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class SetStyleHandler : ICommandHandler<SetStyleCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public SetStyleHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(SetStyleCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        state.Player.SetStyle(command.Style);
        var trains = command.Style switch
        {
            AttackStyle.Aggressive => "Strength",
            AttackStyle.Defensive  => "Defence",
            _                      => "Attack",
        };
        state.AppendLog($"Combat style: {command.Style} (training {trains}).", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
