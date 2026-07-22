using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>UI bible §7 bank: bag -> bank. Quantity &lt;= 0 means "as many as
/// owned" (the §7 "All" quantity-toggle option).</summary>
public sealed record DepositItemCommand(string PlayerId, string ItemId, int Quantity = 1) : IGameCommand;
