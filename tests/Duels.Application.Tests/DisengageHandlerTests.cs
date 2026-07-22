using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

// Persistent target lock (M1 revision): Disengage is the only action, besides
// Engage, that changes the lock — dispatched by tapping the engagement
// indicator (UI bible §3.3/§3's reticle+sheathed element).
public sealed class DisengageHandlerTests
{
    private static (DisengageHandler handler, GameState state) Build(bool inDuel)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);

        if (inDuel)
        {
            var goblin = new NpcTemplate("goblin", "Goblin", "It's a goblin.",
                new CombatStats(1, 1, 1, 10), [], GoldReward: 5, DummyStyle: AttackType.Crush);
            state.StartDuel(new NpcInstance(goblin));
        }

        var handler = new DisengageHandler(new InMemoryStateRepo(state));
        return (handler, state);
    }

    [Fact]
    public async Task Disengage_InDuel_BreaksLock()
    {
        var (handler, state) = Build(inDuel: true);
        Assert.True(state.Engaged); // StartDuel's production default

        var result = await handler.HandleAsync(new DisengageCommand("p1"));

        Assert.True(result.Success);
        Assert.False(state.Engaged);
    }

    [Fact]
    public async Task Disengage_WithNoActiveDuel_ReturnsFail()
    {
        var (handler, state) = Build(inDuel: false);
        var result = await handler.HandleAsync(new DisengageCommand("p1"));
        Assert.False(result.Success);
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }
}
