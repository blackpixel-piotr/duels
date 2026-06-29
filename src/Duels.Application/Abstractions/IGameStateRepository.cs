using Duels.Application.GameSession;

namespace Duels.Application.Abstractions;

public interface IGameStateRepository
{
    Task<GameState?> GetAsync(string playerId, CancellationToken ct = default);
    Task SaveAsync(GameState state, CancellationToken ct = default);
}
