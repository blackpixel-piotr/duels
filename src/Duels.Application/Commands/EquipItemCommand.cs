using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record EquipItemCommand(string PlayerId, string ItemId) : IGameCommand;
