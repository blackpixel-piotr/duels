using Duels.Domain.Entities;
using Xunit;

namespace Duels.Domain.Tests;

// UI bible §7 (ratified M2 pre-plan): 28-slot bag, unbounded bank.
public sealed class PlayerBankTests
{
    [Fact]
    public void Deposit_MovesItemFromBagToBank()
    {
        var player = new Player("p1", "Hero");
        player.AddToInventory("wpn_melee_t1");

        Assert.True(player.Deposit("wpn_melee_t1"));

        Assert.DoesNotContain("wpn_melee_t1", player.Inventory);
        Assert.Contains("wpn_melee_t1", player.BankedItems);
    }

    [Fact]
    public void Deposit_FailsWhenNotInBag()
    {
        var player = new Player("p1", "Hero");
        Assert.False(player.Deposit("nothing_here"));
    }

    [Fact]
    public void Withdraw_MovesItemFromBankToBag()
    {
        var player = new Player("p1", "Hero");
        player.AddToBank("flask_health");

        Assert.True(player.Withdraw("flask_health"));

        Assert.Contains("flask_health", player.Inventory);
        Assert.DoesNotContain("flask_health", player.BankedItems);
    }

    [Fact]
    public void Withdraw_FailsWhenBagIsFull()
    {
        var player = new Player("p1", "Hero");
        for (int i = 0; i < Player.BagCapacity; i++) player.AddToInventory($"filler_{i}");
        player.AddToBank("flask_health");

        Assert.False(player.Withdraw("flask_health"));
        Assert.Contains("flask_health", player.BankedItems); // stays banked, not lost
    }

    [Fact]
    public void Withdraw_FailsWhenNotBanked()
    {
        var player = new Player("p1", "Hero");
        Assert.False(player.Withdraw("nothing_here"));
    }
}
