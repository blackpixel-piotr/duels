using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class PrayerHandler : ICommandHandler<PrayerCommand>
{
    private readonly IGameStateRepository _stateRepo;

    public PrayerHandler(IGameStateRepository stateRepo)
    {
        _stateRepo = stateRepo;
    }

    public async Task<CommandResult> HandleAsync(PrayerCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (player.PrayerPoints <= 0)
            return CommandResult.Fail("No prayer points remaining. Start a new duel to restore them.");

        switch (command.PrayerName)
        {
            case "protect_melee":
                player.ToggleProtection(ProtectionPrayer.Melee);
                state.AppendLog(
                    player.ActiveProtection == ProtectionPrayer.Melee
                        ? "⛉ Protect from Melee activated. NPC melee damage reduced by 75%."
                        : "Protect from Melee deactivated.",
                    LogEntryKind.Prayer);
                break;

            case "protect_range":
                player.ToggleProtection(ProtectionPrayer.Range);
                state.AppendLog(
                    player.ActiveProtection == ProtectionPrayer.Range
                        ? "⛉ Protect from Range activated."
                        : "Protect from Range deactivated.",
                    LogEntryKind.Prayer);
                break;

            case "protect_magic":
                player.ToggleProtection(ProtectionPrayer.Magic);
                state.AppendLog(
                    player.ActiveProtection == ProtectionPrayer.Magic
                        ? "⛉ Protect from Magic activated."
                        : "Protect from Magic deactivated.",
                    LogEntryKind.Prayer);
                break;

            case "piety":
                player.TogglePiety();
                state.AppendLog(
                    player.PietyActive
                        ? "⚡ Piety activated! +20% attack and strength. Prayer drains 2x faster."
                        : "Piety deactivated.",
                    LogEntryKind.Prayer);
                break;

            default:
                return CommandResult.Fail($"Unknown prayer '{command.PrayerName}'. Try: protect_melee, piety.");
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }
}
