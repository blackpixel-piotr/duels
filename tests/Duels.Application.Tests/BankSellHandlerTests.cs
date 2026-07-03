using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Handlers;
using Duels.Domain.Entities;
using Xunit;

namespace Duels.Application.Tests;

public sealed class BankSellHandlerTests
{
    private sealed class StubItemRepo : IItemRepository
    {
        public GearPiece? GetGear(string id) => null;
        public Weapon? GetWeapon(string id) => null;
        public string? GetItemName(string id) => id;
        public bool IsWeapon(string id) => false;
        public IReadOnlyList<(string Id, string Name, int Price)> GetShopItems() => [];
        public int GetFenceValue(string id) => 500;
    }

    private sealed class InMemoryStateRepo : IGameStateRepository
    {
        private readonly GameState _state;
        public InMemoryStateRepo(GameState state) => _state = state;
        public Task<GameState?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameState?>(_state);
        public Task SaveAsync(GameState s, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static GameState Build()
    {
        var state = new GameState("p1", new Player("p1", "Hero"));
        return state;
    }

    // ── Sell ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sell_RemovesItemAndCreditsGold()
    {
        var state = Build();
        int goldBefore = state.Player.Gold;
        state.Player.AddToInventory("shark");
        var handler = new SellItemHandler(new InMemoryStateRepo(state), new StubItemRepo());

        var result = await handler.HandleAsync(new SellItemCommand("p1", "shark"));

        Assert.True(result.Success);
        Assert.Empty(state.Player.Inventory);
        Assert.Equal(goldBefore + 500, state.Player.Gold);
    }

    [Fact]
    public async Task Sell_FailsWhenItemNotInInventory()
    {
        var state = Build();
        int goldBefore = state.Player.Gold;
        var handler = new SellItemHandler(new InMemoryStateRepo(state), new StubItemRepo());

        var result = await handler.HandleAsync(new SellItemCommand("p1", "shark"));

        Assert.False(result.Success);
        Assert.Equal(goldBefore, state.Player.Gold);
    }

    // ── Deposit ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deposit_MovesItemFromInventoryToBank()
    {
        var state = Build();
        state.Player.AddToInventory("rune_scimitar");
        var handler = new DepositItemHandler(new InMemoryStateRepo(state));

        var result = await handler.HandleAsync(new DepositItemCommand("p1", "rune_scimitar"));

        Assert.True(result.Success);
        Assert.Empty(state.Player.Inventory);
        Assert.Contains("rune_scimitar", state.Bank);
    }

    [Fact]
    public async Task Deposit_FailsWhenItemNotInInventory()
    {
        var state = Build();
        var handler = new DepositItemHandler(new InMemoryStateRepo(state));

        var result = await handler.HandleAsync(new DepositItemCommand("p1", "rune_scimitar"));

        Assert.False(result.Success);
        Assert.Empty(state.Bank);
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_MovesItemFromBankToInventory()
    {
        var state = Build();
        state.Bank.Add("abyssal_whip");
        var handler = new WithdrawItemHandler(new InMemoryStateRepo(state));

        var result = await handler.HandleAsync(new WithdrawItemCommand("p1", "abyssal_whip"));

        Assert.True(result.Success);
        Assert.Empty(state.Bank);
        Assert.Contains("abyssal_whip", state.Player.Inventory);
    }

    [Fact]
    public async Task Withdraw_FailsWhenItemNotInBank()
    {
        var state = Build();
        var handler = new WithdrawItemHandler(new InMemoryStateRepo(state));

        var result = await handler.HandleAsync(new WithdrawItemCommand("p1", "abyssal_whip"));

        Assert.False(result.Success);
        Assert.Empty(state.Player.Inventory);
    }

    // ── Bank state ────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsBank()
    {
        var state = Build();
        state.Bank.Add("dragon_dagger");
        state.Bank.Add("shark");

        state.Reset();

        Assert.Empty(state.Bank);
    }

    [Fact]
    public void RestoreFromSave_RoundTripsBank()
    {
        var state = Build();
        state.RestoreFromSave(0, 0, [], bank: ["shark", "shark", "dragon_claws"]);

        Assert.Equal(3, state.Bank.Count);
        Assert.Equal(2, state.Bank.Count(x => x == "shark"));
        Assert.Contains("dragon_claws", state.Bank);
    }

    [Fact]
    public void RestoreFromSave_NullBank_DefaultsToEmpty()
    {
        var state = Build();
        state.RestoreFromSave(0, 0, []);

        Assert.Empty(state.Bank);
    }
}
