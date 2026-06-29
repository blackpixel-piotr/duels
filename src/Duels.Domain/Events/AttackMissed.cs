namespace Duels.Domain.Events;

public sealed record AttackMissed(string AttackerId, string DefenderId) : DomainEvent;
