using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record DrinkPotionCommand(string PlayerId, string ItemId = "super_combat_potion") : IGameCommand;
