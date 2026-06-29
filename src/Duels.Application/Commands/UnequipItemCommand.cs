using Duels.Application.Abstractions;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Commands;

public sealed record UnequipItemCommand(string PlayerId, EquipmentSlot Slot) : IGameCommand;
