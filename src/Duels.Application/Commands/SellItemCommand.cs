using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record SellItemCommand(string PlayerId, string ItemId) : IGameCommand;
