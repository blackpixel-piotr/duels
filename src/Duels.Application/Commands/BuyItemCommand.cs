using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record BuyItemCommand(string PlayerId, string ItemId, int Quantity = 1) : IGameCommand;
