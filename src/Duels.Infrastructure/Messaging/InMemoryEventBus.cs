using Duels.Application.Abstractions;
using Duels.Domain.Events;

namespace Duels.Infrastructure.Messaging;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Func<DomainEvent, CancellationToken, Task>>> _handlers = new();

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : DomainEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.ContainsKey(type))
            _handlers[type] = new();

        _handlers[type].Add((evt, ct) => handler((TEvent)evt, ct));
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : DomainEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        foreach (var handler in handlers)
            await handler(domainEvent, ct);
    }
}
