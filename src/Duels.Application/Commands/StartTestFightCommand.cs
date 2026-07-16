using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record StartTestFightCommand(string PlayerId, string? NpcId = null) : IGameCommand;
