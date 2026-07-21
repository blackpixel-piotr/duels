using Duels.Application.Abstractions;
using Duels.Application.GameSession;

namespace Duels.Application.Commands;

/// <summary>Dev-only (M1 playtest tooling): toggle a single boss mechanic on or
/// off mid-fight to isolate an interaction. Flips the flag; the change persists
/// across retries within the session.</summary>
public sealed record ToggleMechanicCommand(string PlayerId, BossMechanic Mechanic) : IGameCommand;
