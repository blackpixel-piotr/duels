using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record WithdrawItemCommand(string PlayerId, string ItemId) : IGameCommand;
