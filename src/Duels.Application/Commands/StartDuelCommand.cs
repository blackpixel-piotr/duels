using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record StartDuelCommand(string PlayerId, string NpcId) : IGameCommand;
