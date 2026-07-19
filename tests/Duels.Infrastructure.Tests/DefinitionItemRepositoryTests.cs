using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Definitions;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionItemRepositoryTests
{
    // Loads the real embedded items.json — proves the pipeline (embedded
    // resource -> JSON -> domain entities) works end to end, and spot-checks
    // fidelity against the content it replaced (InMemoryItemRepository).
    [Fact]
    public void LoadsRealItemsJson_WithExpectedFidelity()
    {
        var repo = new DefinitionItemRepository();

        var steelSword = repo.GetWeapon("steel_sword");
        Assert.NotNull(steelSword);
        Assert.Equal("Steel Sword", steelSword!.Name);
        Assert.Equal(AttackType.Slash, steelSword.AttackType);
        Assert.Equal(16, steelSword.Modifiers.StabAttack);
        Assert.Equal(21, steelSword.Modifiers.SlashAttack);
        Assert.Equal(20, steelSword.Modifiers.StrengthBonus);
        Assert.Equal(1, steelSword.AttackLevelRequired);
        Assert.Null(steelSword.Special);

        var dagger = repo.GetWeapon("dragon_dagger");
        Assert.NotNull(dagger!.Special);
        Assert.Equal(2, dagger.Special!.Hits);
        Assert.Equal(25, dagger.Special.EnergyRequired);
        Assert.Equal(1.15, dagger.Special.AccuracyMultiplier);

        // Weapons are also gear pieces in the Weapon slot (AsGearPiece derivation).
        var swordAsGear = repo.GetGear("steel_sword");
        Assert.NotNull(swordAsGear);
        Assert.Equal(EquipmentSlot.Weapon, swordAsGear!.Slot);

        var maggotCrown = repo.GetGear("maggot_crown");
        Assert.NotNull(maggotCrown);
        Assert.Equal(EquipmentSlot.Helmet, maggotCrown!.Slot);
        Assert.Equal(70, maggotCrown.DefenceLevelRequired);

        Assert.True(repo.IsWeapon("abyssal_whip"));
        Assert.False(repo.IsWeapon("maggot_crown"));

        Assert.Equal("Shark", repo.GetItemName("shark"));
        Assert.Equal("Steel Sword", repo.GetItemName("steel_sword"));
        Assert.Equal("Maggot Crown", repo.GetItemName("maggot_crown"));

        // Shop price -> fence value is half price.
        Assert.Equal(50, repo.GetFenceValue("steel_sword")); // 100 / 2
        // Drop-only item uses the explicit fence table.
        Assert.Equal(50_000, repo.GetFenceValue("maggot_crown"));
        // Unlisted item falls back to the flat 100 default.
        Assert.Equal(100, repo.GetFenceValue("nonexistent_item"));

        var shopItems = repo.GetShopItems();
        Assert.Contains(shopItems, i => i.Id == "steel_sword" && i.Price == 100);
        // Ordered ascending by price.
        for (int i = 1; i < shopItems.Count; i++)
            Assert.True(shopItems[i - 1].Price <= shopItems[i].Price);
    }

    [Fact]
    public void ThrowsOnDuplicateWeaponId()
    {
        var file = new ItemsDefinitionFile(
            Weapons:
            [
                new Weapon("dupe", "Dupe A", AttackType.Slash, ItemModifiers.Zero),
                new Weapon("dupe", "Dupe B", AttackType.Slash, ItemModifiers.Zero),
            ],
            Gear: [],
            Consumables: [],
            ShopPrices: [],
            FenceValues: []);

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionItemRepository(file));
        Assert.Contains("dupe", ex.Message);
    }

    [Fact]
    public void ThrowsWhenShopPriceReferencesUnknownItem()
    {
        var file = new ItemsDefinitionFile(
            Weapons: [],
            Gear: [],
            Consumables: [],
            ShopPrices: new Dictionary<string, int> { ["ghost_item"] = 100 },
            FenceValues: []);

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionItemRepository(file));
        Assert.Contains("ghost_item", ex.Message);
    }
}
