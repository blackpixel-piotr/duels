namespace Duels.Domain.Events;

public sealed record AttackLanded(string AttackerId, string DefenderId, int Damage, bool IsCritical = false) : DomainEvent;
