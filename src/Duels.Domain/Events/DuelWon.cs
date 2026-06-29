namespace Duels.Domain.Events;

public sealed record DuelWon(string PlayerId, string NpcId, string NpcName, int GoldEarned) : DomainEvent;
