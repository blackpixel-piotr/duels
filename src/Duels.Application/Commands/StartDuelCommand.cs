using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record StartDuelCommand(string PlayerId, string NpcId, int Wager = 0) : IGameCommand;
