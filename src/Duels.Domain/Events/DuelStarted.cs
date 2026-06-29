namespace Duels.Domain.Events;

public sealed record DuelStarted(string PlayerId, string NpcId, string NpcName) : DomainEvent;
