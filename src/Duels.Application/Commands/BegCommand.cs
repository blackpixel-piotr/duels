using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record BegCommand(string PlayerId) : IGameCommand;
