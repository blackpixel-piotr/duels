using Duels.Domain.Entities;
using Duels.Domain.ValueObjects;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionNpcRepositoryTests
{
    // Loads the real embedded npcs.json against the real items.json — proves
    // the pipeline end to end. M3 added Hive Matron alongside Maggot King.
    [Fact]
    public void LoadsRealNpcsJson_WithExpectedFidelity()
    {
        var items = new DefinitionItemRepository();
        var repo = new DefinitionNpcRepository(items);

        Assert.Equal(4, repo.GetAll().Count);

        var maggotKing = repo.GetTemplate("maggot_king");
        Assert.NotNull(maggotKing);
        Assert.Equal(450, maggotKing!.Stats.Hitpoints);
        Assert.NotNull(maggotKing.Script);

        var script = maggotKing.Script!;
        Assert.Equal(50, script.PhaseTwoThresholdPercent);
        Assert.True(script.Stationary);
        Assert.Equal(2, script.Footprint.Width);
        Assert.Equal(2, script.Footprint.Height);

        Assert.Equal(20, script.Phase1.LoopLength);
        Assert.Equal(35, script.Phase1.Eruption.EruptDamage);
        Assert.Null(script.Phase1.RotBurst);

        // Phase 2 is the single 28-tick master script (no independent timers).
        Assert.Equal(28, script.Phase2.LoopLength);
        Assert.True(script.Phase2.MasterScript);
        Assert.Equal(8, script.Phase2.PoolCap);
        Assert.Equal(40, script.Phase2.ScorchTicks);
        Assert.Equal(1, script.Phase2.SwarmHp);
        Assert.Equal(2, script.Phase2.SwarmMaxAlive);
        Assert.Equal(3, script.Phase2.RotBurstEveryNCycles);
        Assert.NotNull(script.Phase2.RotBurst);
        Assert.Equal(55, script.Phase2.RotBurst!.Damage);
        Assert.Equal(4, script.Phase2.Eruption.TilesPerWave);

        Assert.True(script.Attacks.ContainsKey("bile_spit"));
        Assert.Equal(AttackType.Magic, script.Attacks["bile_spit"].Style);

        // M3: Hive Matron — the shared systems' new optional fields round-trip.
        var hiveMatron = repo.GetTemplate("hive_matron");
        Assert.NotNull(hiveMatron);
        Assert.False(hiveMatron!.Script!.Stationary);
        Assert.Equal(5, hiveMatron.Script!.ArenaRadius);
        Assert.NotNull(hiveMatron.Script.SpacingAi);
        Assert.Equal(3, hiveMatron.Script.SpacingAi!.PreferredRangeMin);
        Assert.Equal(5, hiveMatron.Script.SpacingAi.PreferredRangeMax);
        Assert.NotNull(hiveMatron.Script.AdjacencyPunish);
        Assert.Equal(2, hiveMatron.Script.AdjacencyPunish!.AdjacencyTicks);
        Assert.NotNull(hiveMatron.Script.DamageReductionWindow);
        Assert.Equal(0.5, hiveMatron.Script.DamageReductionWindow!.ReductionPercent);
        Assert.NotNull(hiveMatron.Script.LineCharge);
        Assert.True(hiveMatron.Script.LineCharge!.DoubleChainInPhase2);
        Assert.NotNull(hiveMatron.Script.Phase1.Drones);
        Assert.Equal(3, hiveMatron.Script.Phase1.Drones!.Count);

        // M3: Mirrorhide — Attunement is per-phase, Cloak/Reflect/Copycat shared.
        var mirrorhide = repo.GetTemplate("mirrorhide");
        Assert.NotNull(mirrorhide);
        Assert.NotNull(mirrorhide!.Script!.Phase1.Attunement);
        Assert.Equal(4, mirrorhide.Script.Phase1.Attunement!.HitsToAttune);
        Assert.NotNull(mirrorhide.Script.Phase2.Attunement);
        Assert.Equal(3, mirrorhide.Script.Phase2.Attunement!.HitsToAttune);
        Assert.NotNull(mirrorhide.Script.Cloak);
        Assert.True(mirrorhide.Script.Cloak!.PounceOnEnd);
        Assert.NotNull(mirrorhide.Script.Reflect);
        Assert.Equal(0.5, mirrorhide.Script.Reflect!.ReflectPercent);
        Assert.NotNull(mirrorhide.Script.Copycat);

        // M3: Bloodtithe — movement throttle, facing aura, fonts, per-phase bleed cap.
        var bloodtithe = repo.GetTemplate("bloodtithe");
        Assert.NotNull(bloodtithe);
        var btScript = bloodtithe!.Script!;
        Assert.Equal(2, btScript.MovementTicksPerStep);
        Assert.NotNull(btScript.FacingAura);
        Assert.Equal(0.30, btScript.FacingAura!.BackDamageBonus);
        Assert.NotNull(btScript.Fonts);
        Assert.Equal(2, btScript.Fonts!.Count);
        Assert.NotNull(btScript.Transfusion);
        Assert.NotNull(btScript.CrimsonPact);
        Assert.NotNull(btScript.Harvest);
        Assert.Equal(5, btScript.Phase1.BleedOnHit!.MaxStacks);
        Assert.Equal(8, btScript.Phase2.BleedOnHit!.MaxStacks);
    }

    [Fact]
    public void ThrowsOnDuplicateNpcId()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("dupe", "Dupe A", "", CombatStats.Default, []),
            new("dupe", "Dupe B", "", CombatStats.Default, []),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionNpcRepository(templates, items));
        Assert.Contains("dupe", ex.Message);
    }

    [Fact]
    public void ThrowsWhenLootTableReferencesUnknownItem()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("ghost_npc", "Ghost", "", CombatStats.Default,
                [new LootEntry("nonexistent_item", 1.0)]),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionNpcRepository(templates, items));
        Assert.Contains("nonexistent_item", ex.Message);
    }

    [Fact]
    public void AllowsGoldAsALootEntryWithoutValidation()
    {
        var items = new DefinitionItemRepository();
        var templates = new List<NpcTemplate>
        {
            new("gold_only", "Gold Only", "", CombatStats.Default,
                [new LootEntry("gold", 1.0, MinQty: 10, MaxQty: 20)]),
        };

        var repo = new DefinitionNpcRepository(templates, items);
        Assert.Single(repo.GetAll());
    }
}
