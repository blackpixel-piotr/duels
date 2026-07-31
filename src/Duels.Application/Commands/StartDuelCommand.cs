using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <param name="AutoPlay">Start the duel under the playtest autopilot (the
/// "Playtest Fight" button) instead of manual control — the fight plays itself
/// so you can watch the boss script. Defaults to false (normal manual fight).</param>
public sealed record StartDuelCommand(string PlayerId, string NpcId, bool AutoPlay = false) : IGameCommand;
