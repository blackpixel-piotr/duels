using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record PrestigeCommand(string PlayerId) : IGameCommand;
