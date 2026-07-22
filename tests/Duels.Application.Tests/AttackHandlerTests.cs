using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

public sealed class AttackHandlerTests
{
    private static (AttackHandler handler, GameState state) Build(bool inDuel)
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);

        if (inDuel)
        {
            var goblin = new NpcTemplate("goblin", "Goblin", "It's a goblin.",
                new CombatStats(1, 1, 1, 10), [], GoldReward: 5, DummyStyle: AttackType.Crush);
            state.StartDuel(new NpcInstance(goblin));
        }

        var handler = new AttackHandler(new InMemoryStateRepo(state));
        return (handler, state);
    }

    [Fact]
    public async Task Attack_InDuel_QueuesAttackAction()
    {
        var (handler, state) = Build(inDuel: true);
        var result = await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate));
        Assert.True(result.Success);
        Assert.Equal("attack", state.QueuedAction);
    }

    [Fact]
    public async Task Attack_WithSpecial_QueuesSpecAction()
    {
        var (handler, state) = Build(inDuel: true);
        var result = await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate, UseSpecial: true));
        Assert.True(result.Success);
        Assert.Equal("spec", state.QueuedAction);
    }

    [Fact]
    public async Task Attack_WhileDisengaged_ReEngages()
    {
        // Persistent target lock (M1 revision): OrderMove no longer
        // disengages (see RangeAndMovementTests), so the only way to lose
        // the lock is the explicit Disengage() action. ATTACK must always
        // be able to re-engage from it.
        var (handler, state) = Build(inDuel: true);
        state.Disengage();
        Assert.False(state.Engaged);

        var result = await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate));

        Assert.True(result.Success);
        Assert.True(state.Engaged);
        Assert.Equal("attack", state.QueuedAction);
    }

    [Fact]
    public async Task Attack_WithNoActiveDuel_ReturnsFail()
    {
        var (handler, state) = Build(inDuel: false);
        var result = await handler.HandleAsync(new AttackCommand("p1", AttackStyle.Accurate));
        Assert.False(result.Success);
        Assert.Null(state.QueuedAction);
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }
}
