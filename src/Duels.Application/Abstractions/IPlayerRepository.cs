using Duels.Domain.Entities;

namespace Duels.Application.Abstractions;

public interface IPlayerRepository
{
    Task<Player?> GetAsync(string playerId, CancellationToken ct = default);
    Task SaveAsync(Player player, CancellationToken ct = default);
}
