using Duels.Application.Abstractions;
using Duels.Domain.Entities;

namespace Duels.Infrastructure.Persistence;

public sealed class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly Dictionary<string, Player> _store = new();

    public Task<Player?> GetAsync(string playerId, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(playerId));

    public Task SaveAsync(Player player, CancellationToken ct = default)
    {
        _store[player.Id] = player;
        return Task.CompletedTask;
    }
}
