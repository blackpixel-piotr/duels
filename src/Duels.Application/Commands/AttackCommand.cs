using Duels.Application.Abstractions;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Commands;

public sealed record AttackCommand(string PlayerId, AttackStyle Style, bool UseSpecial = false, int? SkillAccuracy = null) : IGameCommand;
