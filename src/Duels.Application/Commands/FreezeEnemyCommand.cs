using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Test-fight only: freeze or unfreeze the enemy (stops movement and attacks).</summary>
public sealed record FreezeEnemyCommand(string PlayerId, bool Frozen) : IGameCommand;
