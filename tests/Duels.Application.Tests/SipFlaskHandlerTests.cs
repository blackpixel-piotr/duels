using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

// Weapon-speed ratification: "a flask sip adds +1 tick to the current attack
// cooldown (sipping always costs tempo, never a full attack)." Previously
// sipping only set a fresh 1-tick cooldown when idle and was entirely FREE if
// the player happened to already be mid-cooldown — these tests guard the
// corrected always-adds-a-tick behavior.
public sealed class SipFlaskHandlerTests
{
    private static (SipFlaskHandler handler, GameState state) Build()
    {
        var player = new Player("p1", "Hero");
        player.Loadout.BindFlask(0, "flask_health"); // must bind before StartDuel's RefillForDuel reads it
        var state = new GameState("p1", player);
        var goblin = new NpcTemplate("goblin", "Goblin", "It's a goblin.",
            new CombatStats(1, 1, 1, 10), [], GoldReward: 5, DummyStyle: AttackType.Crush);
        state.StartDuel(new NpcInstance(goblin));

        var handler = new SipFlaskHandler(new InMemoryStateRepo(state));
        return (handler, state);
    }

    [Fact]
    public async Task Sip_WhileIdle_SetsOneTickCooldown()
    {
        var (handler, state) = Build();
        Assert.Equal(0, state.PlayerCooldown);

        var result = await handler.HandleAsync(new SipFlaskCommand("p1", 0));

        Assert.True(result.Success);
        Assert.Equal(1, state.PlayerCooldown);
    }

    [Fact]
    public async Task Sip_MidCooldown_AddsOneTick_NeverFree()
    {
        var (handler, state) = Build();
        state.ResetPlayerCooldown(3); // simulates an attack already on cooldown

        await handler.HandleAsync(new SipFlaskCommand("p1", 0));

        Assert.Equal(4, state.PlayerCooldown);
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }
}
