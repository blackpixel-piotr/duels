namespace Duels.Application.Abstractions;

public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : IGameCommand;
}
