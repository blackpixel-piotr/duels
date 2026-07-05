using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Click the enemy: cancel any move order and resume the chase.</summary>
public sealed record EngageCommand(string PlayerId) : IGameCommand;
