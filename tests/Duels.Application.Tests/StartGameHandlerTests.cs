using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Xunit;

namespace Duels.Application.Tests;

// Backlog resolution batch 1 §4 (cold start): 600g + a free, equipped,
// bar-bound T1 weapon of the player's chosen style.
public sealed class StartGameHandlerTests
{
    private static (StartGameHandler handler, InMemoryStateRepo stateRepo, InMemoryPlayerRepo playerRepo) Build()
    {
        var stateRepo = new InMemoryStateRepo();
        var playerRepo = new InMemoryPlayerRepo();
        return (new StartGameHandler(stateRepo, playerRepo), stateRepo, playerRepo);
    }

    [Theory]
    [InlineData("Melee", "wpn_melee_t1")]
    [InlineData("Ranged", "wpn_ranged_t1")]
    [InlineData("Magic", "wpn_magic_t1")]
    public async Task GrantsFreeWeapon_Equipped_AndBoundToBarSlot0(string style, string expectedWeaponId)
    {
        var (handler, stateRepo, _) = Build();

        var result = await handler.HandleAsync(new StartGameCommand("p1", "Hero", style));

        Assert.True(result.Success);
        var player = stateRepo.Saved!.Player;
        Assert.Equal(600, player.Gold);
        Assert.Equal(expectedWeaponId, player.Equipped.GetValueOrDefault(EquipmentSlot.Weapon));
        Assert.Equal(expectedWeaponId, player.Loadout.WeaponSlots[0]);
    }

    [Fact]
    public async Task UnknownStyle_Fails()
    {
        var (handler, _, _) = Build();
        var result = await handler.HandleAsync(new StartGameCommand("p1", "Hero", "Stealth"));
        Assert.False(result.Success);
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        public GameState? Saved { get; private set; }
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(Saved);
        public Task SaveAsync(GameState s, CancellationToken ct = default) { Saved = s; return Task.CompletedTask; }
    }

    private sealed class InMemoryPlayerRepo : IPlayerRepository
    {
        public Player? Saved { get; private set; }
        public Task<Player?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(Saved);
        public Task SaveAsync(Player p, CancellationToken ct = default) { Saved = p; return Task.CompletedTask; }
    }
}
