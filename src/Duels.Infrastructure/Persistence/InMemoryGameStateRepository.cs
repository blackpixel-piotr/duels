using Duels.Application.Abstractions;
using Duels.Application.GameSession;

namespace Duels.Infrastructure.Persistence;

/// <summary>
/// In-memory store scoped to the browser session.
/// For persistence across reloads, swap with LocalStorageGameStateRepository.
/// </summary>
public sealed class InMemoryGameStateRepository : IGameStateRepository
{
    private readonly Dictionary<string, GameState> _store = new();

    public Task<GameState?> GetAsync(string playerId, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(playerId));

    public Task SaveAsync(GameState state, CancellationToken ct = default)
    {
        _store[state.PlayerId] = state;
        return Task.CompletedTask;
    }
}
