using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record StartGameCommand(string PlayerId, string PlayerName) : IGameCommand;
