namespace Duels.Domain.Events;

public sealed record ItemUnlocked(string PlayerId, string ItemId, string ItemName) : DomainEvent;
