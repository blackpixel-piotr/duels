namespace Duels.Application.Abstractions;

public interface ICommandHandler<TCommand> where TCommand : IGameCommand
{
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken ct = default);
}
