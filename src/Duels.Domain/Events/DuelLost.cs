namespace Duels.Domain.Events;

public sealed record DuelLost(string PlayerId, string NpcId, string NpcName) : DomainEvent;
