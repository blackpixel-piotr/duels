using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record BuyItemCommand(string PlayerId, string ItemId) : IGameCommand;
