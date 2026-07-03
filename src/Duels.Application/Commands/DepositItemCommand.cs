using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record DepositItemCommand(string PlayerId, string ItemId) : IGameCommand;
