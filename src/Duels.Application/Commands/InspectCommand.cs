using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record InspectCommand(string PlayerId, string Target) : IGameCommand;
