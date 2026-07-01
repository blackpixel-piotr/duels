using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class VengeanceHandler : ICommandHandler<VengeanceCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public VengeanceHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(VengeanceCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (state.VengActive)
            return CommandResult.Fail("Vengeance is already active — waiting to proc.");

        if (state.VengCooldownRounds > 0)
            return CommandResult.Fail($"Vengeance is on cooldown ({state.VengCooldownRounds} rounds remaining).");

        state.ActivateVeng();
        state.AppendLog("Vengeance is ready! The next hit you take will be reflected for 75%.", LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
