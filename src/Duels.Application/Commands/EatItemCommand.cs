using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record EatItemCommand(string PlayerId, string ItemId) : IGameCommand;
