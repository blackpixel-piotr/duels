using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Gold Shop purchase (UI bible §9, economy §4). Quantity is mostly
/// for the two flasks (a stray double-buy is harmless, just wasted gold);
/// gear is normally bought one at a time.</summary>
public sealed record BuyItemCommand(string PlayerId, string ItemId, int Quantity = 1) : IGameCommand;
