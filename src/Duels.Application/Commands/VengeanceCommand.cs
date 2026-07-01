using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record VengeanceCommand(string PlayerId) : IGameCommand;
