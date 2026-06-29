namespace Duels.Application.Abstractions;

public interface IGameCommand { }

public sealed record CommandResult(bool Success, IReadOnlyList<string> Messages)
{
    public static CommandResult Ok(params string[] messages) => new(true, messages);
    public static CommandResult Fail(string message) => new(false, [message]);
}
