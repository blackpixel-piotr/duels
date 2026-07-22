using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Xunit;

namespace Duels.Application.Tests;

public sealed class BankAndShopHandlerTests
{
    private static (GameState state, InMemoryStateRepo repo) BuildState()
    {
        var player = new Player("p1", "Hero");
        var state = new GameState("p1", player);
        return (state, new InMemoryStateRepo(state));
    }

    [Fact]
    public async Task Deposit_MovesFromBagToBank()
    {
        var (state, repo) = BuildState();
        state.Player.AddToInventory("wpn_melee_t1");
        var handler = new DepositItemHandler(repo);

        var result = await handler.HandleAsync(new DepositItemCommand("p1", "wpn_melee_t1"));

        Assert.True(result.Success);
        Assert.Contains("wpn_melee_t1", state.Player.BankedItems);
    }

    [Fact]
    public async Task Deposit_All_MovesEveryCopy()
    {
        var (state, repo) = BuildState();
        state.Player.AddToInventory("flask_health");
        state.Player.AddToInventory("flask_health");
        state.Player.AddToInventory("flask_health");
        var handler = new DepositItemHandler(repo);

        var result = await handler.HandleAsync(new DepositItemCommand("p1", "flask_health", Quantity: 0)); // 0 = All

        Assert.True(result.Success);
        Assert.Equal(3, state.Player.BankedItems.Count(id => id == "flask_health"));
        Assert.DoesNotContain("flask_health", state.Player.Inventory);
    }

    [Fact]
    public async Task Withdraw_RespectsBagCap()
    {
        var (state, repo) = BuildState();
        for (int i = 0; i < Player.BagCapacity; i++) state.Player.AddToInventory($"filler_{i}");
        state.Player.AddToBank("flask_health");
        var handler = new WithdrawItemHandler(repo);

        var result = await handler.HandleAsync(new WithdrawItemCommand("p1", "flask_health"));

        Assert.False(result.Success);
        Assert.Contains("flask_health", state.Player.BankedItems);
    }

    [Fact]
    public async Task Buy_SpendsGoldAndAddsToBag()
    {
        var (state, repo) = BuildState();
        state.Player.AddGold(1000);
        var handler = new BuyItemHandler(repo, new StubItemRepo());

        var result = await handler.HandleAsync(new BuyItemCommand("p1", "wpn_melee_t1"));

        Assert.True(result.Success);
        Assert.Equal(500, state.Player.Gold);
        Assert.Contains("wpn_melee_t1", state.Player.Inventory);
    }

    [Fact]
    public async Task Buy_FailsWithoutEnoughGold()
    {
        var (state, repo) = BuildState();
        var handler = new BuyItemHandler(repo, new StubItemRepo());

        var result = await handler.HandleAsync(new BuyItemCommand("p1", "wpn_melee_t1"));

        Assert.False(result.Success);
        Assert.Empty(state.Player.Inventory);
    }

    [Fact]
    public async Task Buy_OverflowsToBankWhenBagFull()
    {
        var (state, repo) = BuildState();
        state.Player.AddGold(1000);
        for (int i = 0; i < Player.BagCapacity; i++) state.Player.AddToInventory($"filler_{i}");
        var handler = new BuyItemHandler(repo, new StubItemRepo());

        var result = await handler.HandleAsync(new BuyItemCommand("p1", "wpn_melee_t1"));

        Assert.True(result.Success);
        Assert.Contains("wpn_melee_t1", state.Player.BankedItems);
        Assert.DoesNotContain("wpn_melee_t1", state.Player.Inventory);
    }

    [Fact]
    public async Task Buy_FailsForNonShopItem()
    {
        var (state, repo) = BuildState();
        state.Player.AddGold(1000);
        var handler = new BuyItemHandler(repo, new StubItemRepo());

        var result = await handler.HandleAsync(new BuyItemCommand("p1", "wpn_rare_mk"));

        Assert.False(result.Success);
    }

    private sealed class StubItemRepo : IItemRepository
    {
        private static readonly Dictionary<string, int> Prices = new() { ["wpn_melee_t1"] = 500 };
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => null;
        public string? GetItemName(string id) => id;
        public bool IsWeapon(string id) => Prices.ContainsKey(id);
        public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems() =>
            Prices.Select(kv => (kv.Key, kv.Key, kv.Value)).ToList();
        public int? GetShopPrice(string id) => Prices.TryGetValue(id, out var p) ? p : null;
        public int GetFenceValue(string id) => 0;
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }
}
