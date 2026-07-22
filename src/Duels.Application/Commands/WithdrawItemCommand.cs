using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>UI bible §7 bank: bank -> bag, capped by the 28-slot bag.
/// Quantity &lt;= 0 means "as many as fit/owned" (the §7 "All" toggle).</summary>
public sealed record WithdrawItemCommand(string PlayerId, string ItemId, int Quantity = 1) : IGameCommand;
