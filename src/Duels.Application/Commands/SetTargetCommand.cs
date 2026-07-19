using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Tap an add to target it, or null to revert to the boss (m1-plan
/// Workstream C.7 — targeting).</summary>
public sealed record SetTargetCommand(string PlayerId, string? AddId) : IGameCommand;
