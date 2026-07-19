using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Definitions;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionItemRepositoryTests
{
    // Loads the real embedded items.json (M1 doc-item content) — proves the
    // pipeline (embedded resource -> JSON -> domain entities) works end to end.
    [Fact]
    public void LoadsRealItemsJson_WithExpectedFidelity()
    {
        var repo = new DefinitionItemRepository();

        var rustcleaver = repo.GetWeapon("wpn_melee_t1");
        Assert.NotNull(rustcleaver);
        Assert.Equal("Rustcleaver", rustcleaver!.Name);
        Assert.Equal(AttackType.Slash, rustcleaver.AttackType);
        Assert.Equal(10, rustcleaver.Doc.Power);
        Assert.Equal(GearLine.Warbound, rustcleaver.Doc.Line);
        Assert.Equal(1, rustcleaver.Range);
        Assert.NotNull(rustcleaver.Doc.Special);
        Assert.Equal("lunge", rustcleaver.Doc.Special!.Id);
        Assert.Equal(25, rustcleaver.Doc.Special.Cost);

        var bow = repo.GetWeapon("wpn_ranged_t2");
        Assert.NotNull(bow);
        Assert.Equal(14, bow!.Doc.Power);
        Assert.Equal(0.02, bow.Doc.Precision);
        Assert.Equal(7, bow.Range);
        Assert.Equal("pin_shot", bow.Doc.Special!.Id);

        // Weapons are also gear pieces in the Weapon slot (AsGearPiece derivation).
        var swordAsGear = repo.GetGear("wpn_melee_t1");
        Assert.NotNull(swordAsGear);
        Assert.Equal(EquipmentSlot.Weapon, swordAsGear!.Slot);

        var body = repo.GetGear("arm_warbound_body_t2");
        Assert.NotNull(body);
        Assert.Equal(EquipmentSlot.Body, body!.Slot);
        Assert.Equal(6, body.Doc.DefPoints);
        Assert.Equal(GearLine.Warbound, body.Doc.Line);

        Assert.True(repo.IsWeapon("wpn_melee_t1"));
        Assert.False(repo.IsWeapon("arm_warbound_body_t1"));

        Assert.Equal("Health Flask", repo.GetItemName("flask_health"));
        Assert.Equal("Rustcleaver", repo.GetItemName("wpn_melee_t1"));

        // No shop in M1 — unlisted items fall back to the flat default fence value.
        Assert.Equal(100, repo.GetFenceValue("nonexistent_item"));
        Assert.Empty(repo.GetShopItems());
    }

    [Fact]
    public void ThrowsOnDuplicateWeaponId()
    {
        var file = new ItemsDefinitionFile(
            Weapons:
            [
                new Weapon("dupe", "Dupe A", AttackType.Slash, DocStats.Zero),
                new Weapon("dupe", "Dupe B", AttackType.Slash, DocStats.Zero),
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
