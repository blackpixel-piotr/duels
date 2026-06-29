using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record ShopCommand(string PlayerId) : IGameCommand;
