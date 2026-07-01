using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record StartEndlessCommand(string PlayerId) : IGameCommand;
