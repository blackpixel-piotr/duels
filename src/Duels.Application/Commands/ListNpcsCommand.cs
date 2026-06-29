using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record ListNpcsCommand(string PlayerId) : IGameCommand;
