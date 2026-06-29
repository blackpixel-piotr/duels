namespace Duels.Domain.Events;

public sealed record LevelUp(string PlayerId, string Skill, int NewLevel) : DomainEvent;
