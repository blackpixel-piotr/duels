namespace Duels.Domain.Events;

public abstract record DomainEvent(DateTimeOffset OccurredAt)
{
    protected DomainEvent() : this(DateTimeOffset.UtcNow) { }
}
