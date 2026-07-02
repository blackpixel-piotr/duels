using Duels.Application.Abstractions;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Commands;

public sealed record SetStyleCommand(string PlayerId, AttackStyle Style) : IGameCommand;
