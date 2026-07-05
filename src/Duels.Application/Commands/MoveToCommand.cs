using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Click-to-move: walk to an arena tile, disengaging from combat.</summary>
public sealed record MoveToCommand(string PlayerId, int X, int Z) : IGameCommand;
