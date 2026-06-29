using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record HelpCommand(string PlayerId) : IGameCommand;
