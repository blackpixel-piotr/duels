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
        Assert.Equal(19, bow!.Doc.Power); // weapon-speed ratification: T2 DPS re-anchor (14 -> 19)
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

        // Unlisted items with no override fall back to 0, not an invented flat number.
        Assert.Equal(0, repo.GetFenceValue("nonexistent_item"));

        // M2 Workstream A: shop is populated (T1-T4 weapons/armour + flasks).
        var shopItems = repo.GetShopItems();
        Assert.NotEmpty(shopItems);
        Assert.Contains(shopItems, i => i.Id == "wpn_melee_t1" && i.Price == 500);
        Assert.Contains(shopItems, i => i.Id == "wpn_melee_t4" && i.Price == 30000);
        Assert.Contains(shopItems, i => i.Id == "flask_health" && i.Price == 1000);
        // economy §3's now-ratified 15% sell rate applies automatically off shopPrices.
        Assert.Equal(75, repo.GetFenceValue("wpn_melee_t1"));

        var t3Maul = repo.GetWeapon("wpn_melee_t3");
        Assert.NotNull(t3Maul);
        Assert.Equal(25, t3Maul!.Doc.Power);
        Assert.Equal("quake", t3Maul.Doc.Special!.Id);

        var t4Body = repo.GetGear("arm_warbound_body_t4");
        Assert.NotNull(t4Body);
        Assert.Equal(14, t4Body!.Doc.DefPoints);

        // Carrion Edge (Maggot King rare, items doc §4): ingested ahead of its
        // drop source, not in shopPrices, no fence override -> falls back to 0
        // (rares are never sellable, which the default-0 fallback already gives).
        var rare = repo.GetWeapon("wpn_rare_mk");
        Assert.NotNull(rare);
        Assert.Equal(28, rare!.Doc.Power);
        Assert.Equal("fester", rare.Doc.Special!.Id);
        Assert.Equal(0, repo.GetFenceValue("wpn_rare_mk"));
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
    public void GetFenceValue_IsFifteenPercentOfShopPrice_ForShopItems()
    {
        var file = new ItemsDefinitionFile(
            Weapons: [new Weapon("priced", "Priced Sword", AttackType.Slash, DocStats.Zero)],
            Gear: [],
            Consumables: [],
            ShopPrices: new Dictionary<string, int> { ["priced"] = 1000 },
            FenceValues: []);

        var repo = new DefinitionItemRepository(file);

        // economy doc §3: drop-table sell value is 15% of shop-equivalent price.
        Assert.Equal(150, repo.GetFenceValue("priced"));
        // No shop price and no override -> 0, not an invented flat number.
        Assert.Equal(0, repo.GetFenceValue("priced_unknown"));
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
