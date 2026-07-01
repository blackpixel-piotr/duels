using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record DrinkPotionCommand(string PlayerId) : IGameCommand;
