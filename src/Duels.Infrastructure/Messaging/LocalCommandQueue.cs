using Duels.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Duels.Infrastructure.Messaging;

/// <summary>
/// In-process synchronous command dispatcher.
/// Swap this class for a Kafka/RabbitMQ implementation in DI registration only.
/// </summary>
public sealed class LocalCommandQueue : ICommandDispatcher
{
    private readonly IServiceProvider _services;

    public LocalCommandQueue(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<CommandResult> DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : IGameCommand
    {
        var handler = _services.GetService<ICommandHandler<TCommand>>();
        if (handler is null)
            return CommandResult.Fail($"No handler registered for {typeof(TCommand).Name}.");

        return await handler.HandleAsync(command, ct);
    }
}
