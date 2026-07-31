using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.Services;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Services;

/// <summary>Drives the fixed 0.6s combat tick: movement, the boss rotation-
/// script engine (m1-plan Workstream C), tile hazards, DoT, prayer and the
/// flask belt. M1 retired the OSRS ladder — the only opponent is a
/// data-driven boss (<see cref="Domain.Entities.BossScript"/>); there is no
/// per-NPC branching left in this class.</summary>
public sealed class GameTickService : IDisposable
{
    private readonly IGameStateRepository _states;
    private readonly IDamageModel _damage;
    private readonly IRandomProvider _random;
    private readonly IItemRepository _items;
    private readonly IEventBus _events;
    private readonly ITickSource _tickSource;

    // Items doc §3, backlog resolution batch 1: Rotfang's on-hit poison is
    // hooked by item id (a single-item passive, not a generic system).
    private const string RotfangItemId = "wpn_unique_mk";

    private CancellationTokenSource? _cts;
    private Action? _notify;

    // Playtest autopilot (GameState.AutoPlay). Stateless and boss-agnostic, so
    // one shared instance drives every auto duel. Only ever consulted when the
    // duel opted in via StartDuelCommand.AutoPlay — a pure input source.
    private readonly AutoPlay.IPlayerBrain _autoBrain = new AutoPlay.AutoPlayBrain();

    public GameTickService(
        IGameStateRepository states,
        IDamageModel damage,
        IRandomProvider random,
        IItemRepository items,
        IEventBus events,
        ITickSource tickSource)
    {
        _states = states;
        _damage = damage;
        _random = random;
        _items = items;
        _events = events;
        _tickSource = tickSource;
    }

    public void RegisterNotify(Action callback) => _notify = callback;

    /// <summary>Advance the simulation exactly one tick for the given player.
    /// The production loop (<see cref="Start"/>) drives this off the tick
    /// source; unit tests and the headless dev sim harness
    /// (tools/Duels.SimHarness) call it directly to run deterministic,
    /// non-real-time fights. This is a thin, side-effect-free wrapper over the
    /// same <c>ProcessTick</c> the loop uses — no separate code path.</summary>
    public Task TickOnceAsync(string playerId) => ProcessTick(playerId);

    public void Start(string playerId)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _tickSource.Reset();
        _ = Loop(playerId, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task Loop(string playerId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _tickSource.WaitForNextTickAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            if (ct.IsCancellationRequested) break;
            await ProcessTick(playerId);
            _notify?.Invoke();
        }
    }

    public async Task KickMoveAsync(string playerId)
    {
        if (_tickSource.ElapsedMsIntoCurrentTick >= TickConstants.TickDurationMs - TickConstants.InputBufferWindowMs)
            return;

        var state = await _states.GetAsync(playerId);
        if (state is null || !state.InDuel || state.PlayerMoveTarget is not { } moveTarget) return;

        for (int i = 0; i < 2 && state.PlayerMoveTarget is not null; i++)
        {
            var step = NextStepToward(state, state.PlayerTile, moveTarget, state.NpcTile);
            bool blocked = step == state.PlayerTile;
            state.SetPlayerTile(step.X, step.Z);
            // Movement dust is no longer emitted here: footfall timing lives
            // in the renderer's gait phase (toon.js updateFootsteps), driven
            // off the smooth interpolated position, not these discrete tile
            // steps — see the combat-feel-2 footstep-dust follow-up.
            if (state.PlayerTile == moveTarget || blocked)
                state.ClearMoveOrder();
        }
        await _states.SaveAsync(state);
        _notify?.Invoke();
    }

    private async Task ProcessTick(string playerId)
    {
        var state = await _states.GetAsync(playerId);
        if (state is null || !state.InDuel) return;

        var player = state.Player;
        var npc = state.ActiveNpc!;

        // Playtest autopilot: let the brain write this tick's inputs (prayer,
        // move, attack, special) BEFORE anything reads them — the prayer set
        // here must be captured by TickStartProtection below, and any move/
        // attack consumed by the movement + attack-gate later this tick. Same
        // seam and ordering the sim harness uses (brain.Decide → tick).
        if (state.AutoPlay)
        {
            // Offer the autopilot a ranged weapon from the bar so it can kite
            // (the safest way to let a whole boss script play out on screen).
            string? rangedWeaponId = player.Loadout.WeaponSlots
                .FirstOrDefault(w => w is not null && (_items.GetWeapon(w)?.Range ?? 1) >= 2);
            _autoBrain.Decide(new AutoPlay.SimContext(state, GetPlayerWeaponRange(player), rangedWeaponId));
        }

        state.TickStartProtection = player.ActiveProtection;
        state.ClearVfxEvents(); // this tick's vfx events only — see GameState.VfxEvents
        var preTickPlayerTile = state.PlayerTile;
        var erupting = state.TilesErupting(); // captured before movement/new waves

        state.DecrementCooldowns();

        state.ResetWeaponSwapGate();
        if (state.ConsumePendingWeaponSwap() is { } pendingWeaponId && player.HasItem(pendingWeaponId))
        {
            state.TryClaimWeaponSwapSlot();
            player.Equip(pendingWeaponId, EquipmentSlot.Weapon);
            var pendingWeaponName = _items.GetItemName(pendingWeaponId) ?? pendingWeaponId;
            state.AppendLog($"You ready your {pendingWeaponName}.", LogEntryKind.Info);
        }

        // Drones body-block the player's approach lane (Hive Matron): their
        // live tiles are soft blockers the PLAYER must path around (kill them or
        // circle to bait them aside), scoped to this movement call only so the
        // boss and the drones themselves are never blocked by them.
        state.SetSoftBlockers(state.Adds.Where(a => a.IsAlive && a.Kind == AddKind.Drone).Select(a => a.Tile));
        ProcessPlayerMovement(state, player);
        state.ClearSoftBlockers();
        ProcessNpcMovement(state, npc);
        // Persistent target lock (M1 revision): moving on a tick simply
        // defers the attack — it's never cancelled, just delayed to the
        // next tick the player is truly stationary. preTickPlayerTile is
        // already captured above (for hazard Perfect-Dodge), so this is
        // free to reuse rather than a second snapshot.
        bool playerMovedThisTick = state.PlayerTile != preTickPlayerTile;
        // (Movement dust used to be emitted here as an entity_moved vfxEvent;
        // it's now renderer-driven off the actual gait phase — see the
        // combat-feel-2 footstep-dust follow-up. playerMovedThisTick is still
        // needed below to defer the attack on a moving tick.)

        int playerRange = GetPlayerWeaponRange(player);
        bool targetInRange = state.CurrentTargetAdd is { } targetAdd
            ? Chebyshev(state.PlayerTile, targetAdd.Tile) <= playerRange
            : state.InAttackRange(playerRange);

        // Melee "attack on arrival" (Hive Matron rework, Q2): a melee swing may
        // land on the very tick a move COMPLETES in range — the move order is
        // already done (PlayerMoveTarget null) even though the player moved this
        // tick. Without this the weave's "step in → hit" can never land on the
        // step-in tick, which is what made melee feel bad. Ranged/magic keep
        // deferring to a stationary tick (kiting shouldn't auto-fire on arrival).
        bool meleeArrival = playerRange <= AttackRange.Melee && targetInRange;
        bool mayActThisTick = !playerMovedThisTick || meleeArrival;

        if (state.PlayerCooldown == 0 && state.PlayerMoveTarget is null && mayActThisTick && state.Engaged && targetInRange)
        {
            var action = state.QueuedAction ?? "attack";
            await ExecutePlayerAction(state, player, npc, action);
            state.ResetPlayerCooldown(GetPlayerWeaponSpeed(player));
            state.SetQueuedAction(null);
        }

        if (!npc.IsAlive)
        {
            await HandleVictory(state);
            await _states.SaveAsync(state);
            return;
        }

        ProcessAdds(state);

        if (!state.EnemyFrozen)
        {
            // Impact-resolution prayer (Global Combat Grammar): in-flight
            // ranged/magic projectiles advance/arrive here, against THIS
            // tick's fresh TickStartProtection and this tick's already-
            // updated PlayerTile (movement ran earlier, above) — before
            // ProcessBossScript can spawn a brand new one this same tick,
            // same reasoning as TickForecast below (a projectile cast this
            // tick must not immediately advance on its own casting tick —
            // that's what guarantees at least 1 tick of flight).
            int logCountBeforeRotation = state.CombatLog.Count;
            foreach (var impact in state.AdvanceProjectiles(state.PlayerTile))
                ResolveBossAttack(state, player, npc, impact);

            // Forecast countdown must land before the boss script can set a
            // fresh one this same tick — otherwise a just-armed telegraph
            // is immediately decremented on its own setup tick.
            npc.TickForecast();

            if (npc.UsesMasterScript)
            {
                // Master-script phase (P2): one fixed-tick clock drives attacks,
                // eruptions, Rot Burst and swarms — no independent timers, so no
                // stagger machinery is needed (overlaps are authored, not drift).
                ProcessMasterScript(state, player, npc);
            }
            else
            {
                ProcessBossScript(state, player, npc); // may flip P1 → P2 this tick

                // Independent-timer path (P1 only). Skipped once the flip above
                // has entered a master-script phase — its mechanics live in the
                // master clock. Eruption stagger (legacy): don't pile a hazard
                // wave onto the same tick as a telegraph/warning/impact; detected
                // from what actually got logged this tick.
                if (!npc.UsesMasterScript)
                {
                    bool rotationEventThisTick = state.CombatLog.Skip(logCountBeforeRotation).Any(e =>
                        e.Kind == LogEntryKind.HitsplatNpc ||
                        (e.Kind == LogEntryKind.BossSpecial && (e.Message.Contains("mandibles glow") || e.Message.Contains("ROT BURST incoming"))));
                    ProcessEruptionTimer(state, npc, rotationEventThisTick);
                    ProcessSwarmSpawns(state, npc);
                }
            }
        }

        if (!npc.IsAlive)
        {
            await HandleVictory(state);
            await _states.SaveAsync(state);
            return;
        }

        if (!state.EnemyFrozen)
        {
            ProcessHazardResolution(state, player, preTickPlayerTile, erupting);
            if (npc.Template.Script?.SpacingAi is not null)
                ProcessSpacingAiMechanics(state, player, npc, preTickPlayerTile);
            if (npc.Template.Script?.Cloak is not null)
                ProcessMirrorhideMechanics(state, player, npc);
            if (npc.Template.Script?.FacingAura is not null)
                ProcessBloodtitheMechanics(state, player, npc);
        }

        // Prayer drain at tick end (D7: 2 pts per drain event for a
        // protection, 1 pt for boost — playtest revision, twice now: cut to
        // a ninth of the original rate overall, delivered once every 9
        // ticks instead of every tick via
        // TickProtectionDrainDue/TickBoostDrainDue) — only if still on at
        // tick end, which is what makes flicking work.
        if (player.ActiveProtection != ProtectionPrayer.None)
        {
            if (state.TickProtectionDrainDue())
            {
                int before = player.PrayerPoints;
                player.DrainPrayer(2);
                if (player.PrayerPoints == 0 && before > 0)
                    state.AppendLog("Your prayer has run out!", LogEntryKind.System);
            }
        }
        if (player.BoostPrayerActive)
        {
            if (state.TickBoostDrainDue())
            {
                int before = player.PrayerPoints;
                player.DrainPrayer(1);
                if (player.PrayerPoints == 0 && before > 0)
                    state.AppendLog("Your prayer has run out!", LogEntryKind.System);
            }
        }

        // Special energy regen (items doc §1): 1/tick in combat, replacing
        // the old +10-per-attack rule. Full restore happens at duel start.
        player.RechargeSpecial(1, MaxSpecialEnergy(player));

        ApplyDots(state, player, npc);
        if (npc.IsAlive) { npc.TickSap(); ApplyNpcPoison(state, npc); }

        if (!npc.IsAlive)
        {
            await HandleVictory(state);
            await _states.SaveAsync(state);
            return;
        }

        if (!player.IsAlive)
        {
            await HandleDefeat(state, player, npc);
        }
        else
        {
            state.TickFight();
        }

        await _states.SaveAsync(state);
    }

    private void ProcessPlayerMovement(GameState state, Player player)
    {
        if (state.PlayerMoveTarget is { } moveTarget)
        {
            for (int i = 0; i < 2 && state.PlayerMoveTarget is not null; i++)
            {
                var step = NextStepToward(state, state.PlayerTile, moveTarget, state.NpcTile);
                bool blocked = step == state.PlayerTile;
                state.SetPlayerTile(step.X, step.Z);
                if (state.PlayerTile == moveTarget || blocked)
                    state.ClearMoveOrder();
            }
            return;
        }

        // Auto-chase-into-range is a movement convenience, independent of
        // the (persistent) target lock — it only runs while
        // EngageApproachActive, which OrderMove above retires immediately
        // and only Engage() re-arms. This is what stops a kited-away player
        // from being auto-dragged back the instant they stop walking.
        if (!state.EngageApproachActive) return;

        int playerRange = GetPlayerWeaponRange(player);
        var chaseAdd = state.CurrentTargetAdd;
        var chaseTile = chaseAdd?.Tile ?? state.NpcFootprintTiles().OrderBy(t => Chebyshev(state.PlayerTile, t)).First();
        bool InRange() => chaseAdd is not null
            ? Chebyshev(state.PlayerTile, chaseTile) <= playerRange
            : state.InAttackRange(playerRange);

        for (int i = 0; i < 2 && !InRange(); i++)
        {
            var step = NextStepToward(state, state.PlayerTile, ApproachSlot(state.PlayerTile, chaseTile), state.NpcTile);
            if (step == state.PlayerTile) break;
            state.SetPlayerTile(step.X, step.Z);
        }
    }

    // Generic mover for a non-stationary NPC (m1-plan Workstream C.9): closes
    // to its style's range and stops. The King (M1's only content) is always
    // Stationary=true, so this path is dormant for him — kept for the
    // movement tests. M3, Hive Matron: a boss with SpacingAi routes to its
    // own preferred-range mover instead (see ProcessSpacingAiMovement).
    private static void ProcessNpcMovement(GameState state, NpcInstance npc)
    {
        if (state.EnemyFrozen || state.NpcStationary) return;

        if (npc.Template.Script?.SpacingAi is { } spacingAi)
        {
            ProcessSpacingAiMovement(state, npc, spacingAi);
            return;
        }

        int npcRange = npc.Template.DummyStyle is { } st ? AttackRange.ForStyle(st) : AttackRange.Melee;
        if (state.InAttackRange(npcRange)) return;

        // M3, Bloodtithe: relentless approach throttled to 1 tile per
        // MovementTicksPerStep ticks (Crimson Pact's SpeedBoosted overrides
        // to full speed) — every other scripted boss keeps MovementTicksPerStep's
        // default of 1 (a step every tick, unchanged from before M3).
        int ticksPerStep = npc.Template.Script?.MovementTicksPerStep ?? 1;
        if (ticksPerStep > 1 && !npc.SpeedBoosted)
        {
            npc.TickMovementCounter();
            if (npc.MovementTickCounter < ticksPerStep) return;
            npc.ResetMovementCounter();
        }

        var step = NextStepToward(state, state.NpcTile, ApproachSlot(state.NpcTile, state.PlayerTile), state.PlayerTile);
        state.SetNpcTile(step.X, step.Z);
        // Boss movement dust is renderer-driven off the gait phase now (same
        // footstep path as the player) — see the combat-feel-2 footstep-dust
        // follow-up; no entity_moved vfxEvent is emitted from the sim.
    }

    // Hive Matron's movement AI (Boss Bible §2, revised by the melee rework):
    // she closes when the player kites out past her preferred band, and
    // otherwise HOLDS — she no longer flees every tick the player steps inside
    // her minimum range. That continuous flee made melee impossible to reach
    // and contradicted "melee is possible." Her space-making is now the
    // telegraphed, dodgeable Needle Spit (see ProcessSpacingAiMechanics), not a
    // reflex — and melee is the rewarded line (MeleeVulnerabilityPercent), so
    // letting the player into range is the point.
    private static void ProcessSpacingAiMovement(GameState state, NpcInstance npc, SpacingAiDef ai)
    {
        int dist = state.DistanceToNpc;
        if (dist > ai.PreferredRangeMax)
        {
            var step = NextStepToward(state, state.NpcTile, ApproachSlot(state.NpcTile, state.PlayerTile), state.PlayerTile);
            state.SetNpcTile(step.X, step.Z);
        }
        // Boss movement dust is renderer-driven off the gait phase now (see
        // ProcessNpcMovement's note / the combat-feel-2 footstep-dust follow-up).
    }

    private void ProcessAdds(GameState state)
    {
        foreach (var add in state.Adds)
        {
            if (!add.IsAlive) continue;

            if (add.Kind == AddKind.Drone)
            {
                // Melee rework: drones orbit to sit BETWEEN the boss and the
                // player (on the boss→player lane at OrbitRadius, the two drones
                // fanned to either side), tracking the player every tick — so
                // they actually body-block the melee approach (see the soft-
                // blocker wiring in ProcessTick) and circling her drags them out
                // of the lane. Fixes the old bug where they spawned at fixed
                // east/west angles and then froze.
                var target = DroneLaneTile(state, add);
                if (add.Tile != target)
                    add.MoveTo(StepToward(add.Tile, target));
                continue;
            }

            // Stop at adjacency — the add only needs Chebyshev<=1 for contact,
            // walking onto the player's exact tile was a real bug (playtest
            // report: "it's literally under me"), not the intended crawl-and-
            // menace read.
            if (Chebyshev(add.Tile, state.PlayerTile) > 1)
                add.MoveTo(StepToward(add.Tile, state.PlayerTile));

            bool adjacent = Chebyshev(add.Tile, state.PlayerTile) <= 1;
            if (adjacent && !add.HasBitten)
            {
                // Edge-triggered: one bleed stack per contact (boss bible
                // "contact applies 1 bleed stack"), not a continuous refresh
                // for every tick it stays adjacent — that was the "constant
                // damage" bug (ApplyBleed unconditionally on every tick in
                // range never let the DoT actually expire).
                add.MarkBitten();
                state.ApplyBleed(4, 2);
                state.AppendLog("A maggot sinks its jaws in — you're bleeding!", LogEntryKind.NpcHit);
            }
            else if (!adjacent)
            {
                add.ResetBite();
            }
        }
        state.RemoveDeadAdds();
    }

    // Where a drone wants to be: on the boss→player lane at its OrbitRadius,
    // fanned to one side (by its spawn index) so the two drones flank the
    // approach rather than stacking. Falls back onto the lane centre, then the
    // boss tile, if the fanned tile is out of bounds or occupied.
    private static (int X, int Z) DroneLaneTile(GameState state, AddInstance drone)
    {
        var boss = state.NpcTile;
        var player = state.PlayerTile;
        if (boss == player) return drone.Tile; // degenerate; hold
        double baseAng = Math.Atan2(player.Z - boss.Z, player.X - boss.X);
        int idx = int.TryParse(drone.Id.Split('_').Last(), out var i) ? i : 0;
        double fan = (idx % 2 == 0 ? 1 : -1) * 0.55; // ~±31° to either side of the lane
        int r = drone.OrbitRadius > 0 ? drone.OrbitRadius : 2;

        (int X, int Z) At(double ang) =>
            (boss.X + (int)Math.Round(r * Math.Cos(ang)), boss.Z + (int)Math.Round(r * Math.Sin(ang)));

        var fanned = At(baseAng + fan);
        if (state.InArena(fanned) && fanned != player && fanned != boss) return fanned;
        var centre = At(baseAng);
        if (state.InArena(centre) && centre != player && centre != boss) return centre;
        return boss;
    }

    // ── Player offense ──────────────────────────────────────────────────

    private async Task ExecutePlayerAction(GameState state, Player player, NpcInstance npc, string action)
    {
        if (action == "spec")
        {
            PerformSpecialAttack(state, player, npc);
        }
        else if (state.CurrentTargetAdd is { } add)
        {
            ExecuteBasicAttackOnAdd(state, player, add);
        }
        else
        {
            await ExecuteBasicAttackOnBoss(state, player, npc);
        }
    }

    private async Task ExecuteBasicAttackOnBoss(GameState state, Player player, NpcInstance npc)
    {
        // M3, Mirrorhide's cloak: untargetable, the attack finds nothing there.
        if (npc.IsCloaked)
        {
            state.AppendLog($"Your attack passes through empty air — {npc.Template.Name} is cloaked!", LogEntryKind.PlayerMiss);
            return;
        }

        var weapon = GetPlayerWeapon(player);
        var attacker = BuildAttackerProfile(player, weapon);
        // Accuracy is rolled vs the boss's per-style Evasion for the doctrine
        // this weapon attacks with (items doc §1) — neutral (0) for Maggot
        // King, the future "favors ranged" lever for other bosses.
        var doctrine = weapon?.AttackType ?? AttackType.Crush;
        var roll = _damage.Roll(attacker, new DefenderProfile(0, false, npc.EvasionFor(doctrine)));

        if (!roll.Hit)
        {
            state.AppendHitsplat(onEnemy: true, 0, "miss", style: StyleToken(doctrine));
            await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id));
            return;
        }

        // M3, Mirrorhide's Attunement: fully immune to its currently-attuned
        // style — the hit still "connects" (it was accurate) but deals zero,
        // same distinct-from-a-miss treatment prayer blocks get.
        bool attuneImmune = npc.IsAttuned && npc.AttunedStyle == doctrine;

        bool punished = npc.InPunishWindow;
        int damage = attuneImmune ? 0 : (punished ? (int)Math.Round(roll.Damage * 1.25) : roll.Damage);
        damage = ApplyBossDamageReduction(npc, doctrine, damage);
        damage = ApplyMeleeVulnerability(npc, doctrine, damage);
        damage = ApplyBloodtitheBackBonus(npc, state, damage);
        npc.TakeDamage(damage);

        // Rotfang (items doc §3, backlog resolution batch 1): on-hit poison,
        // any landed hit while wielded, independent of the damage roll itself.
        if (weapon?.Id == RotfangItemId && damage > 0)
        {
            npc.ApplyRotfangPoison();
            state.AppendLog($"Rotfang's venom sinks in. ({npc.PoisonStacks} stack{(npc.PoisonStacks > 1 ? "s" : "")})", LogEntryKind.Info);
        }

        if (attuneImmune)
        {
            state.AppendHitsplat(onEnemy: true, 0, "blocked", style: StyleToken(doctrine));
            state.AppendLog($"{npc.Template.Name} shrugs it off — immune to {StyleName(doctrine)} right now!", LogEntryKind.PlayerMiss);
        }
        else
        {
            // A max-hit (rolled the weapon's 2×Power ceiling) gets its own hitsplat
            // tier + MaxHit log kind (the latter already fires a screen shake) so it
            // reads distinctly from an ordinary hit — items doc §1's "distinct
            // max-hit visual."
            string tier = roll.MaxHit ? "max" : "normal";
            state.AppendHitsplat(onEnemy: true, damage, tier, style: StyleToken(doctrine));
            string punishMsg = punished ? " (punish window!)" : "";
            string maxMsg = roll.MaxHit ? " — MAX HIT!" : "";
            state.AppendLog($"You hit {npc.Template.Name} for {damage}{punishMsg}{maxMsg}. [{npc.CurrentHp}/{npc.MaxHp} HP]",
                roll.MaxHit ? LogEntryKind.MaxHit : LogEntryKind.PlayerHit);

            RecordMirrorhideHitAndReflect(state, player, npc, doctrine, damage);
        }

        await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, damage));
    }

    // M3, Mirrorhide: Echo Offense (her next attack's style = the style you
    // just hit her with) + Attunement bookkeeping + Reflection's payback,
    // all keyed off a real landed (non-immune) hit. No-op for any boss
    // without an Attunement/Reflect def — currently only Mirrorhide sets one.
    private void RecordMirrorhideHitAndReflect(GameState state, Player player, NpcInstance npc, AttackType doctrine, int damageDealt)
    {
        if (npc.Template.Script is null) return; // non-scripted test fixtures (no boss content) never reach here otherwise

        var attunementDef = npc.ActivePhaseDef.Attunement;
        if (attunementDef is not null || npc.Template.Script?.Reflect is not null)
        {
            switch (npc.RecordPlayerHitStyle(doctrine, attunementDef))
            {
                case "attune":
                    state.AppendLog($"{npc.Template.Name}'s scales shimmer {StyleName(doctrine)} — about to become immune!", LogEntryKind.BossSpecial);
                    break;
                case "shatter":
                    npc.StartSlump(attunementDef!.ShatterWindowTicks);
                    state.AppendLog($"SHATTERED! {npc.Template.Name}'s attunement breaks — punish window! (+25% damage, {attunementDef.ShatterWindowTicks} ticks)", LogEntryKind.BossSpecial);
                    break;
            }
        }

        if (npc.ReflectWindowActive && npc.ReflectStyle == doctrine && damageDealt > 0)
        {
            int reflected = (int)Math.Round(damageDealt * npc.Template.Script!.Reflect!.ReflectPercent);
            if (reflected > 0)
            {
                player.TakeDamage(reflected);
                state.RecordDamageTaken(reflected);
                state.SetKilledBy("Reflection");
                state.AppendHitsplat(onEnemy: false, reflected, "normal", StyleToken(doctrine));
                state.AppendLog($"Your own {StyleName(doctrine)} damage reflects back at you for {reflected}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            }
        }
    }

    private void ExecuteBasicAttackOnAdd(GameState state, Player player, AddInstance add)
    {
        if (add.Kind == AddKind.Drone)
        {
            // Drones (M3, Hive Matron) have real HP — a normal accuracy roll
            // and weapon damage, unlike swarm fodder below.
            var weapon = GetPlayerWeapon(player);
            var attacker = BuildAttackerProfile(player, weapon);
            var roll = _damage.Roll(attacker, new DefenderProfile(0, false, 0));
            var droneStyle = StyleToken(weapon?.AttackType ?? AttackType.Crush);
            if (!roll.Hit)
            {
                state.AppendHitsplat(onEnemy: true, 0, "miss", style: droneStyle, targetEntityId: add.Id);
                return;
            }
            add.TakeDamage(roll.Damage);
            state.AppendHitsplat(onEnemy: true, roll.Damage, "normal", style: droneStyle, targetEntityId: add.Id);
            state.AppendLog($"You strike the drone for {roll.Damage}.", LogEntryKind.PlayerHit);
            if (!add.IsAlive)
            {
                state.AppendLog("The drone dies.", LogEntryKind.System);
                if (state.TargetId == add.Id) state.SetTarget(null);
            }
            return;
        }

        // Swarms are fodder (Boss Bible: "any hit kills") — every landed hit
        // does at least 1 damage regardless of weapon roll.
        add.TakeDamage(1);
        state.AppendHitsplat(onEnemy: true, 1, "normal", style: StyleToken(GetPlayerWeapon(player)?.AttackType ?? AttackType.Crush), targetEntityId: add.Id);
        state.AppendLog("You strike the maggot swarm.", LogEntryKind.PlayerHit);
        if (!add.IsAlive)
        {
            state.AppendLog("The maggot swarm dies.", LogEntryKind.System);
            if (state.TargetId == add.Id) state.SetTarget(null);
        }
    }

    private void PerformSpecialAttack(GameState state, Player player, NpcInstance npc)
    {
        var weapon = GetPlayerWeapon(player);
        var special = weapon?.Doc.Special;

        if (special is null)
        {
            state.AppendLog("No special attack — equip a weapon with a special.", LogEntryKind.System);
            return;
        }

        if (!player.DrainSpecialEnergy(special.Cost))
        {
            state.AppendLog($"Not enough special energy ({player.SpecialEnergy}% / need {special.Cost}%).", LogEntryKind.System);
            return;
        }

        switch (special.Id)
        {
            case "lunge": ExecuteLunge(state, player, npc, weapon!); break;
            case "snipe": ExecuteSpecialHit(state, player, npc, weapon!, "Snipe", damageMult: 1.5); break;
            case "scorch": ExecuteSpecialHit(state, player, npc, weapon!, "Scorch", burnTicks: 3, burnPerTick: 3); break;
            case "rend": ExecuteSpecialHit(state, player, npc, weapon!, "Rend", damageMult: 1.3, burnTicks: 4, burnPerTick: 3); break;
            case "pin_shot": ExecuteSpecialHit(state, player, npc, weapon!, "Pin Shot", onHit: () => npc.ApplyPinDelay(1)); break;
            case "sap": ExecuteSpecialHit(state, player, npc, weapon!, "Sap", onHit: () => npc.ApplySap(5)); break;
            default: state.AppendLog($"Unknown special '{special.Id}'.", LogEntryKind.System); break;
        }
    }

    // Lunge (items doc: "next hit from 2 tiles, closes the gap") — snaps the
    // player to the nearest melee-adjacent tile, then resolves an immediate hit.
    private void ExecuteLunge(GameState state, Player player, NpcInstance npc, Weapon weapon)
    {
        var nearest = state.NpcFootprintTiles().OrderBy(t => Chebyshev(state.PlayerTile, t)).First();
        var adjacent = ApproachSlot(state.PlayerTile, nearest);
        var fromTile = state.PlayerTile;
        state.SetPlayerTile(adjacent.X, adjacent.Z);
        // Renderer interpolation layer: this is a genuine teleport (closes
        // the gap instantly, not a walked step) — mark it so the on-screen
        // position snaps instead of smoothly lerping across the gap.
        state.AppendLog("lunge", LogEntryKind.PlayerTeleport);
        state.AppendVfxEvent("forced_move", "player", new Dictionary<string, object>
        {
            ["fromX"] = (double)fromTile.X, ["fromZ"] = (double)fromTile.Z,
            ["toX"] = (double)adjacent.X, ["toZ"] = (double)adjacent.Z,
            // What kind of forced move this was — the player's own gap-closer,
            // not being hit. The renderer keys the reaction off it (a lunge
            // slides into the swing; a knockback also plays a hit reaction).
            ["cause"] = "lunge",
        });
        ExecuteSpecialHit(state, player, npc, weapon, "Lunge");
    }

    private void ExecuteSpecialHit(GameState state, Player player, NpcInstance npc, Weapon weapon, string name,
        double damageMult = 1.0, int burnTicks = 0, int burnPerTick = 0, Action? onHit = null)
    {
        state.RecordPlayerSpecial(name); // M3, Mirrorhide's Copycat: last-landed-or-attempted special, named

        if (npc.IsCloaked)
        {
            state.AppendLog($"⚡ SPEC! {name} finds nothing — {npc.Template.Name} is cloaked!", LogEntryKind.PlayerMiss);
            return;
        }

        var attacker = BuildAttackerProfile(player, weapon);
        var roll = _damage.Roll(attacker, new DefenderProfile(0, false, npc.EvasionFor(weapon.AttackType)));

        if (!roll.Hit)
        {
            state.AppendLog($"⚡ SPEC! You miss {npc.Template.Name} with {name}.", LogEntryKind.PlayerMiss);
            state.AppendHitsplat(onEnemy: true, 0, "miss", style: StyleToken(weapon.AttackType));
            return;
        }

        bool attuneImmune = npc.IsAttuned && npc.AttunedStyle == weapon.AttackType;
        bool punished = npc.InPunishWindow;
        int damage = attuneImmune ? 0 : (int)Math.Round(roll.Damage * damageMult * (punished ? 1.25 : 1.0));
        damage = ApplyBossDamageReduction(npc, weapon.AttackType, damage);
        damage = ApplyMeleeVulnerability(npc, weapon.AttackType, damage);
        damage = ApplyBloodtitheBackBonus(npc, state, damage);
        npc.TakeDamage(damage);

        // M3, Bloodtithe's Transfusion: "interrupted only by hitting him
        // with a special attack." Any landed special (this call only runs
        // once roll.Hit is true, above) ends the channel immediately.
        if (npc.TransfusionActive)
        {
            npc.InterruptTransfusion();
            state.AppendLog("Your special interrupts Bloodtithe's Transfusion!", LogEntryKind.BossSpecial);
        }

        if (attuneImmune)
        {
            state.AppendHitsplat(onEnemy: true, 0, "blocked", style: StyleToken(weapon.AttackType));
            state.AppendLog($"⚡ SPEC! {npc.Template.Name} shrugs off {name} — immune to {StyleName(weapon.AttackType)} right now!", LogEntryKind.PlayerMiss);
        }
        else
        {
            state.AppendLog($"⚡ SPEC! {name} hits {npc.Template.Name} for {damage}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.SpecHit);
            state.AppendHitsplat(onEnemy: true, damage, "spec", style: StyleToken(weapon.AttackType), weapon: weapon.Id);

            if (burnTicks > 0) state.ApplyBleed(burnTicks, burnPerTick);
            onHit?.Invoke();
            RecordMirrorhideHitAndReflect(state, player, npc, weapon.AttackType, damage);
        }
    }

    // ── Boss rotation-script engine (m1-plan Workstream C.1) ────────────

    private void ProcessBossScript(GameState state, Player player, NpcInstance npc)
    {
        if (!npc.IsAlive || npc.Template.Script is null) return;

        if (npc.InPunishWindow)
        {
            npc.TickSlump();
            return; // "cannot act" — universal punish-window rule
        }

        if (npc.RotBurstInhaling)
        {
            if (npc.TickRotBurstInhale())
                ResolveRotBurst(state, player, npc);
            return;
        }

        // Pin Shot (player special): skip the boss's whole turn this tick —
        // the schedule shifts by exactly one tick, no risk of an
        // already-resolved action re-firing on the delayed cursor.
        if (npc.ConsumePinDelay()) return;

        var phaseDef = npc.ActivePhaseDef;
        var step = phaseDef.Rotation.FirstOrDefault(r => r.Tick == npc.RotationTick);
        if (step is not null)
            ResolveRotationStep(state, player, npc, step);

        bool phaseChanged = npc.AdvanceRotation();
        if (phaseChanged)
        {
            state.AppendLog(PhaseTwoBanner(npc), LogEntryKind.BossSpecial);
            if (npc.UsesMasterScript) EnterMasterScriptPhase(state, npc);
            return;
        }

        npc.TickRotBurstCooldown();
        var rb = npc.ActivePhaseDef.RotBurst;
        if (rb is not null && npc.RotationTick == 0 && npc.RotBurstCooldown <= 0
            && state.IsMechanicEnabled(BossMechanic.RotBurst))
        {
            npc.StartRotBurstInhale(rb.InhaleTicks);
            state.AppendLog("⚠ The Maggot King's body swells — ROT BURST incoming!", LogEntryKind.BossSpecial);
        }
    }

    // The phase-2 transition banner. Data-driven per boss (FlavorDef); the
    // generic fallback uses the boss's own name so a boss without authored
    // flavor never borrows Maggot King's — that leak ("The Maggot King
    // convulses…" in the Hive Matron/Mirrorhide/Bloodtithe fights) was the bug
    // this fixes.
    private static string PhaseTwoBanner(NpcInstance npc) =>
        npc.Template.Script?.Flavor?.PhaseTwoBanner
        ?? $"★ {npc.Template.Name} shifts into a higher gear — Phase 2 begins!";

    // ── Master-script engine (Global Combat Grammar "Master-script rule") ──
    // One fixed-tick clock per phase; attacks, eruptions, Rot Burst and swarms
    // are all placed on it, so overlapping demands are authored, never produced
    // by independent-timer drift. Maggot King P2 (28 ticks): see the boss bible.

    private void EnterMasterScriptPhase(GameState state, NpcInstance npc)
    {
        var phase = npc.ActivePhaseDef;
        // Transition: 3-tick roar; all in-flight marks/projectiles cleared;
        // first swarm pair spawns; the phase's board economy (pool cap) applies.
        npc.StartRoar(3);
        state.ClearHazardWarnings();
        state.ClearProjectiles();
        state.SetPoolCap(phase.PoolCap);
        if (state.IsMechanicEnabled(BossMechanic.Swarms))
            SpawnSwarmsUpTo(state, phase, phase.SwarmMaxAlive);
        state.AppendLog("The Maggot King throws back his head and ROARS — the brood surges!", LogEntryKind.BossSpecial);
    }

    private void ProcessMasterScript(GameState state, Player player, NpcInstance npc)
    {
        if (!npc.IsAlive) return;

        // Transition roar: hold the cursor at T0, no actions, for 3 ticks.
        if (npc.RoarTicksLeft > 0) { npc.TickRoar(); return; }

        // Rot Burst inhale (begun at T10 of a Rot Burst cycle) resolves at T14;
        // the cursor keeps advancing so the slump lands on schedule.
        if (npc.RotBurstInhaling)
        {
            if (npc.TickRotBurstInhale()) ResolveRotBurst(state, player, npc); // → slump
            npc.AdvanceMasterTick();
            return;
        }

        // Slump / punish window: the boss takes no actions, but the master clock
        // keeps running — no cursor freeze (that would reintroduce drift).
        if (npc.InPunishWindow) { npc.TickSlump(); npc.AdvanceMasterTick(); return; }

        int t = npc.RotationTick;
        bool rb = npc.IsRotBurstCycle;

        switch (t)
        {
            case 0:  RollCycleStyles(npc); MasterTelegraph(state, npc, npc.StyleAId); break;
            case 3:  MasterAttack(state, player, npc, npc.StyleAId); break;
            case 7:  MasterAttack(state, player, npc, npc.StyleAId); break;
            case 10: if (rb) BeginRotBurstInhale(state, npc); else MasterEruption(state, npc); break;
            case 14: if (!rb) MasterAttack(state, player, npc, npc.StyleAId); break; // rb: burst resolves via inhale
            case 17: if (!rb) MasterTelegraph(state, npc, npc.StyleBId); break;
            case 20:
                if (rb) { RollCycleStyles(npc); MasterTelegraph(state, npc, npc.StyleAId); }
                else MasterAttack(state, player, npc, npc.StyleBId);
                break;
            case 23:
                if (rb) MasterAttack(state, player, npc, npc.StyleAId);
                else MasterSwarmSpawn(state, npc);
                break;
        }

        npc.AdvanceMasterTick();
    }

    // Style A ∈ {magic bile, positional}; B is always the other (Telegraph B ≠ A).
    private void RollCycleStyles(NpcInstance npc)
    {
        const string bile = "bile_spit", positional = "lash/grub_volley";
        bool aIsBile = _random.NextDouble() < 0.5;
        npc.SetCycleStyles(aIsBile ? bile : positional, aIsBile ? positional : bile);
    }

    private void MasterTelegraph(GameState state, NpcInstance npc, string styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return;
        npc.SetForecast(styleId, npc.ActivePhaseDef.TelegraphLeadTicks);
        state.AppendLog($"⚠ {ForecastMessage(npc, styleId)}", LogEntryKind.BossSpecial);
    }

    private void MasterAttack(GameState state, Player player, NpcInstance npc, string styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return;
        if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
        var attackId = ResolveAttackId(state, styleId);
        var attack = npc.Template.Script!.Attacks[attackId];
        if (attack.Style is AttackType.Ranged or AttackType.Magic)
            SpawnProjectileAttack(state, attack, source: "MasterAttack(P2)");
        else
            ResolveBossAttack(state, player, npc, attack);
    }

    private void MasterEruption(GameState state, NpcInstance npc)
    {
        if (!state.IsMechanicEnabled(BossMechanic.Eruptions)) return;
        var e = npc.ActivePhaseDef.Eruption;
        var tiles = PickHazardTiles(state, e.TilesPerWave); // player tile + (TilesPerWave-1) random
        state.AddHazardWave(tiles, e.WarningTicks, e.PoolTicks, npc.ActivePhaseDef.ScorchTicks);
        state.AppendLog("⚠ The Maggot King's brood burrows beneath you — MOVE!", LogEntryKind.BossSpecial);
    }

    private void BeginRotBurstInhale(GameState state, NpcInstance npc)
    {
        if (!state.IsMechanicEnabled(BossMechanic.RotBurst)) return; // toggle off: cycle still advances
        var rb = npc.ActivePhaseDef.RotBurst!;
        state.BurnPoolsToScorch(); // safe ground guaranteed + visible from the inhale's first tick
        npc.StartRotBurstInhale(rb.InhaleTicks);
        state.AppendLog("⚠ The Maggot King's body swells — ROT BURST incoming! (scorch tiles are safe)", LogEntryKind.BossSpecial);
    }

    private void MasterSwarmSpawn(GameState state, NpcInstance npc)
    {
        if (!state.IsMechanicEnabled(BossMechanic.Swarms)) return;
        SpawnSwarmsUpTo(state, npc.ActivePhaseDef, npc.ActivePhaseDef.SwarmMaxAlive);
    }

    // Top up to `target` live swarms (never exceeds the cap), 1 HP each in P2.
    // Corners scale with the duel's own ArenaRadius (was a static array keyed
    // off the old compile-time constant before M3's per-duel arena size).
    private static (int X, int Z)[] SwarmCorners(GameState state) => new[]
    {
        (-state.ArenaRadius, state.ArenaRadius),
        (state.ArenaRadius, state.ArenaRadius),
    };
    private void SpawnSwarmsUpTo(GameState state, BossPhaseDef phase, int target)
    {
        int alive = state.Adds.Count(a => a.IsAlive);
        int toSpawn = target - alive;
        if (toSpawn <= 0) return;
        var corners = SwarmCorners(state);
        for (int i = 0; i < toSpawn; i++)
            state.SpawnAdd(new AddInstance($"swarm_{state.FightTicks}_{alive + i}", corners[(alive + i) % corners.Length], phase.SwarmHp));
        state.AppendLog($"⚠ Maggot swarms surge from the corners! ({toSpawn})", LogEntryKind.BossSpecial);
    }

    private void ResolveRotationStep(GameState state, Player player, NpcInstance npc, RotationStep step)
    {
        if (step.Action == "idle") return;

        if (step.Action == "style_telegraph")
        {
            var nextAction = FindNextAttackAction(npc.ActivePhaseDef, npc.RotationTick);
            if (nextAction is null) return;
            npc.SetForecast(nextAction, npc.ActivePhaseDef.TelegraphLeadTicks);
            state.AppendLog($"⚠ {ForecastMessage(npc, nextAction)}", LogEntryKind.BossSpecial);
            return;
        }

        // M3, Hive Matron's Pin (signature): marks a line through the
        // player's current tile instead of resolving a normal Attacks-dict
        // entry — LineChargeDef.WarningTicks later, ResolveLineCharge (called
        // from ProcessSpacingAiMechanics) lands it.
        if (step.Action == "pin" && npc.Template.Script?.LineCharge is { } lineCharge)
        {
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            var tiles = LineThrough(state, state.NpcTile, state.PlayerTile);
            npc.StartLineCharge(tiles, lineCharge.WarningTicks);
            state.AppendLog($"⚠ {npc.Template.Name} marks a line through you — PIN incoming!", LogEntryKind.BossSpecial);
            RecordAttackAndMaybeDash(state, npc);
            return;
        }

        // M3, Hive Matron's Sting Lob: an arcing attack that marks the
        // player's cast-tile (not a homing projectile), landing after a
        // 2-tick fuse and leaving a venom pool — reuses the existing
        // hazard-tile state machine (ProcessHazardResolution resolves the
        // damage generically off this boss's own Eruption slot, which for
        // Hive Matron holds Sting Lob's numbers rather than an eruption's;
        // her Eruption never auto-fires on a timer — see npcs.json).
        if (step.Action == "sting_lob")
        {
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            var e = npc.ActivePhaseDef.Eruption;
            state.AddHazardWave(new[] { state.PlayerTile }, warningTicks: e.WarningTicks, poolTicks: e.PoolTicks);
            state.AppendLog($"⚠ {npc.Template.Name} lobs a venomous glob — MOVE!", LogEntryKind.BossSpecial);
            RecordAttackAndMaybeDash(state, npc);
            return;
        }

        // M3, Mirrorhide's Echo Offense (Boss Bible §3, "Core mechanic"): her
        // attacks always use the style the player last hit her with — a
        // dynamic BossAttackDef built at cast time rather than a fixed
        // Attacks-dict entry, defaulting to melee before the player's first
        // landed hit (nothing to echo yet).
        if (step.Action == "echo_strike")
        {
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            var echoStyle = npc.LastHitStyle ?? AttackType.Crush;
            var echoAttack = new BossAttackDef("echo_strike", "Echo Strike", echoStyle, EchoStrikeDamage);
            if (echoStyle is AttackType.Ranged or AttackType.Magic)
                SpawnProjectileAttack(state, echoAttack, source: "ResolveRotationStep(EchoStrike)");
            else
                ResolveBossAttack(state, player, npc, echoAttack);
            return;
        }

        // M3, Mirrorhide's Reflection (signature): starts a channel; the
        // reflect window itself opens later, from ProcessMirrorhideMechanics,
        // once TickReflectChannel() reports the channel resolved.
        if (step.Action == "reflect" && npc.Template.Script?.Reflect is { } reflectDef)
        {
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            npc.StartReflectChannel(reflectDef.ChannelTicks);
            state.AppendLog($"{npc.Template.Name} begins to shimmer — channeling Reflection!", LogEntryKind.BossSpecial);
            return;
        }

        // M3, Bloodtithe's Transfusion (signature): starts a self-heal
        // channel, ticked/interrupted from ProcessBloodtitheMechanics and
        // PerformSpecialAttack respectively.
        if (step.Action == "transfusion" && npc.Template.Script?.Transfusion is { } transfusionDef)
        {
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            npc.StartTransfusion(transfusionDef.ChannelTicks);
            state.AppendLog("⚠ Bloodtithe reaches out, draining your vitality into himself — TRANSFUSION! Interrupt with a special!", LogEntryKind.BossSpecial);
            return;
        }

        var attackId = ResolveAttackId(state, step.Action);
        var attack = npc.Template.Script!.Attacks[attackId];

        // Dev toggle (M1 playtest tooling): boss autos off → the King telegraphs
        // but lands no direct attack, so a hazard interaction can be isolated.
        if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;

        // M3, Bloodtithe's Scythe Arc: a 2-tick windup, THEN the player's
        // position is re-checked at resolution (ProcessBloodtitheMechanics) —
        // "the cue to slip behind. Missing it gives a 3-tick punish window."
        // Chosen here (not always cast) exactly like Maggot King's
        // "lash/grub_volley" range-dependent pick — Blood Lance is the
        // ranged alternative when the player's kited out of melee.
        if (attackId == "scythe_arc")
        {
            npc.StartScytheArcWindup(2);
            state.AppendLog("⚠ Bloodtithe raises his scythe for a wide arc!", LogEntryKind.BossSpecial);
            RecordAttackAndMaybeDash(state, npc);
            return;
        }

        // Global Combat Grammar "impact-resolution prayer": ranged/magic
        // attacks travel as a homing doctrine-colored projectile — damage
        // (and the protection-prayer check) lands on the tick it actually
        // arrives, not this cast tick. Melee has no travel time and still
        // resolves here, instantly, exactly as before.
        if (attack.Style is AttackType.Ranged or AttackType.Magic)
            SpawnProjectileAttack(state, attack, source: "ResolveRotationStep(P1)");
        else
            ResolveBossAttack(state, player, npc, attack);

        RecordAttackAndMaybeDash(state, npc);
    }

    // PROVISIONAL: Echo Strike's damage — Boss Bible gives only "Medium"
    // (no exact number for Mirrorhide specifically), consistent with the
    // established precedent of reusing Maggot King's own Medium value (18)
    // rather than inventing a new one (see m3-plan.md's item-doc precedent).
    private const int EchoStrikeDamage = 18;

    // Hive Matron's spacing AI (melee rework): every Nth attack she makes
    // space — but as the telegraphed "Needle Spit" (a leap + a needle volley
    // you can read and dodge), and ONLY when the player is actually near her.
    // A far kiter never triggers it, killing the old "dash back from nobody."
    // The silent-dash fallback below is dead for current content (Hive Matron
    // ships NeedleSpit) but kept for any SpacingAi boss that doesn't.
    private void RecordAttackAndMaybeDash(GameState state, NpcInstance npc)
    {
        var ai = npc.Template.Script?.SpacingAi;
        if (ai is null) return;
        npc.RecordAttackForDash();
        if (npc.AttacksSinceDash < ai.DashEveryNAttacks) return;
        npc.ResetDashCounter();

        if (npc.Template.Script?.NeedleSpit is { } needle)
        {
            if (npc.NeedleSpitTiles is not null) return;                 // already winding up
            if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;
            if (state.DistanceToNpc > needle.TriggerWithinRange) return; // far kiter → no leap
            npc.StartNeedleSpit(PlusPattern(state, state.PlayerTile), needle.WarningTicks);
            state.AppendLog($"⚠ {npc.Template.Name} rears back, wings screaming — NEEDLE SPIT! Pray Range and step diagonally!", LogEntryKind.BossSpecial);
            return;
        }

        var dashed = state.NpcTile;
        for (int i = 0; i < ai.DashDistanceTiles; i++)
        {
            var next = StepAwayFrom(state, dashed, state.PlayerTile);
            if (!state.InArena(next) || state.IsBlocked(next, state.PlayerTile)) break;
            dashed = next;
        }
        if (dashed != state.NpcTile)
        {
            state.SetNpcTile(dashed.X, dashed.Z);
            state.AppendLog($"{npc.Template.Name} dashes back to reset the distance!", LogEntryKind.BossSpecial);
        }
    }

    // The Needle Spit "+" footprint: the player's tile-at-cast plus its 4
    // cardinal neighbours (in-arena). The only safe step is diagonal.
    private static readonly (int X, int Z)[] Cardinals = { (0, 1), (0, -1), (1, 0), (-1, 0) };
    private static IReadOnlyList<(int X, int Z)> PlusPattern(GameState state, (int X, int Z) center)
    {
        var tiles = new List<(int X, int Z)> { center };
        foreach (var (dx, dz) in Cardinals)
        {
            var t = (X: center.X + dx, Z: center.Z + dz);
            if (state.InArena(t)) tiles.Add(t);
        }
        return tiles;
    }

    // Resolves a Needle Spit: needles land on the marked "+", then she leaps
    // back. Two-layer damage — a Range-typed volley (pray Range negates) plus
    // an unprayable venom nick, so only a diagonal dodge takes zero. Perfect
    // Dodge eligible, same helper the eruption/Pin miss use.
    private void ResolveNeedleSpit(GameState state, Player player, NpcInstance npc, (int X, int Z) preTickPlayerTile)
    {
        var ns = npc.Template.Script!.NeedleSpit!;
        var tiles = npc.NeedleSpitTiles!;
        bool wasOnTile = tiles.Contains(preTickPlayerTile);
        bool stillOnTile = tiles.Contains(state.PlayerTile);
        npc.ClearNeedleSpit();

        if (stillOnTile && player.IsAlive)
        {
            int needle = ResolveIncomingDamage(state, player, npc, ns.NeedleDamage, style: AttackType.Ranged, unprayable: false);
            int nick = ResolveIncomingDamage(state, player, npc, ns.VenomNickDamage, style: null, unprayable: true);
            int total = needle + nick;
            player.TakeDamage(total);
            state.RecordDamageTaken(total);
            if (total > 0) state.SetKilledBy("Needle Spit");
            state.AppendHitsplat(onEnemy: false, total, needle > 0 ? "normal" : "poison", "ranged");
            string tail = needle == 0 ? " (you prayed the volley — only the venom bit)" : "";
            state.AppendLog($"Needles rake you for {total}{tail}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }
        else
        {
            state.AppendLog($"{npc.Template.Name}'s needles rattle off empty tiles — you slipped it!", LogEntryKind.BossSpecial);
            TryPerfectDodge(state, player, wasOnDangerTile: wasOnTile, stillOnDangerTile: stillOnTile);
        }

        // The leap: she springs LeapTiles back to make the space she wanted.
        var landed = state.NpcTile;
        for (int i = 0; i < ns.LeapTiles; i++)
        {
            var next = StepAwayFrom(state, landed, state.PlayerTile);
            if (!state.InArena(next) || state.IsBlocked(next, state.PlayerTile)) break;
            landed = next;
        }
        if (landed != state.NpcTile)
            state.SetNpcTile(landed.X, landed.Z);
    }

    // Boss Bible: "Ranged/magic attacks travel as simulated doctrine-colored
    // projectiles at ~3 tiles/tick, homing; impact and prayer evaluation
    // occur on the arrival tick, so flight time scales with distance." Cast
    // time only spawns the sim-authoritative projectile (at the caster's
    // nearest footprint tile) and fires the cast visual — no damage, no
    // prayer check, no hitsplat yet. ResolveBossAttack (and therefore
    // GetPrayerReduction's read of TickStartProtection) doesn't run until
    // the projectile actually arrives, via ProcessTick's AdvanceProjectiles
    // loop.
    private void SpawnProjectileAttack(GameState state, BossAttackDef attack, string source)
    {
        var spawnTile = NearestFootprintTileEuclidean(state, state.PlayerTile);
        state.SpawnProjectile(spawnTile, attack);
#if DEBUG
        Console.WriteLine($"[PROJ][spawn-source] path={source} tick={state.FightTicks}");
#endif
        var style = StyleToken(attack.Style);
        state.AppendLog(style, LogEntryKind.BossCast);
        // The windup/swing itself — this cast's later impact (ResolveBossAttack,
        // via AppendHitsplat) suppresses its own attack_swing for ranged/magic
        // since it already played here.
        state.AppendVfxEvent("attack_swing", "enemy", new Dictionary<string, object>
        {
            ["targetId"] = "player",
            ["style"] = style,
            ["tier"] = "normal",
        });
    }

    private static string ResolveAttackId(GameState state, string action)
    {
        if (!action.Contains('/')) return action;
        var parts = action.Split('/');
        return state.InAttackRange(AttackRange.Melee) ? parts[0] : parts[1];
    }

    private static string? FindNextAttackAction(BossPhaseDef phase, int currentTick)
    {
        var candidates = phase.Rotation.Where(r => r.Action is not ("idle" or "style_telegraph")).ToList();
        if (candidates.Count == 0) return null;
        var next = candidates.Where(r => r.Tick > currentTick).OrderBy(r => r.Tick).FirstOrDefault();
        return (next ?? candidates.OrderBy(r => r.Tick).First()).Action;
    }

    // Maggot-King-exact wording is preserved verbatim (a test and this file's
    // own P1 eruption-stagger detection both substring-match "mandibles
    // glow" — see ProcessTick's rotationEventThisTick check) — every other
    // boss gets a generic, name-driven forecast instead of a copy-pasted
    // flavor string.
    private string ForecastMessage(NpcInstance npc, string action)
    {
        if (npc.Template.Id == "maggot_king")
        {
            if (action.Contains('/'))
                return "The Maggot King's mandibles glow amber — melee or ranged incoming, mind your spacing!";
            var mkAtk = npc.Template.Script!.Attacks[action];
            return $"The Maggot King's mandibles glow — {StyleName(mkAtk.Style)} incoming!";
        }
        if (action.Contains('/'))
            return $"{npc.Template.Name} tenses — an attack is coming, mind your positioning!";
        var atk = npc.Template.Script!.Attacks[action];
        return $"{npc.Template.Name} winds up — {StyleName(atk.Style)} incoming!";
    }

    // Actually lands the damage — called synchronously for melee (cast tick
    // == impact tick) and from ProcessTick's AdvanceProjectiles loop for a
    // ranged/magic attack, on whichever tick its projectile actually travels
    // far enough to arrive. Either way, ResolveIncomingDamage reads whatever
    // TickStartProtection is fresh THIS tick — that's the whole point.
    private void ResolveBossAttack(GameState state, Player player, NpcInstance npc, BossAttackDef attack)
    {
        if (!player.IsAlive)
        {
#if DEBUG
            Console.WriteLine($"[PROJ][impact-dropped] tick={state.FightTicks} style={attack.Style} attackId={attack.Id} reason=player-dead");
#endif
            return;
        }

        // Standard boss autos roll 60–100% of their listed band each cast
        // (items doc §1). Mechanic/hazard damage (eruptions, Rot Burst) and
        // DoTs never reach here — they resolve deterministically in their own
        // paths, dodge-checks that always land for exactly their listed value.
        int band = RollAttackBand(attack.Damage);
        // Compute the prayer reduction once, up front — the "(prayed)"/"blocked"
        // messaging keys off actual prayer negation, NOT off the band roll (a
        // low band roll landing under the listed value is variance, not prayer).
        double prayerReduction = attack.Unprayable ? 0.0 : GetPrayerReduction(state, attack.Style);
        int damage = ResolveIncomingDamage(state, player, npc, band, attack.Style, attack.Unprayable);
        player.TakeDamage(damage);
        state.RecordDamageTaken(damage);
        if (damage > 0) state.SetKilledBy($"{attack.Name} ({StyleName(attack.Style)})");

        // A matching protection prayer fully negates the hit (Global Combat
        // Grammar's 100%-block rule), so a "blocked" hitsplat is a distinct
        // outcome from a plain 0-damage hitsplat — the renderer swaps the
        // usual damage numeral for a doctrine-colored "prevented" icon
        // instead of showing what would otherwise read as a weak 0 hit.
        bool blockedByPrayer = damage == 0 && prayerReduction >= 1.0;
        string tier = blockedByPrayer ? "blocked" : "normal";
        state.AppendHitsplat(onEnemy: false, damage, tier, StyleToken(attack.Style));
        string prayedMsg = prayerReduction > 0 ? " (prayed)" : "";
        state.AppendLog($"{npc.Template.Name} uses {attack.Name} for {damage}{prayedMsg}. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);

        // M3, Bloodtithe's bleed-on-hit (Boss Bible §4): "every hit he lands
        // applies a bleed stack... correctly prayed hits apply no stack" —
        // generic on ResolveBossAttack so it covers Scythe Arc AND Blood
        // Lance for free, rather than special-casing each attack.
        if (npc.ActivePhaseDef.BleedOnHit is { } bleedOnHit && !blockedByPrayer)
        {
            state.ApplyBleedStack(bleedOnHit.MaxStacks, bleedOnHit.DurationTicks, bleedOnHit.DamagePerTick);
            state.AppendLog($"You're bleeding! ({state.PlayerBleedStacks} stack{(state.PlayerBleedStacks == 1 ? "" : "s")})", LogEntryKind.Info);
        }
#if DEBUG
        Console.WriteLine($"[PROJ][impact] tick={state.FightTicks} style={attack.Style} attackId={attack.Id} band={band} prayerReduction={prayerReduction:F2} damage={damage} blocked={blockedByPrayer}");
#endif
    }

    // Boss standard autos roll 60–100% of their listed band (items doc §1);
    // deterministic mechanics/DoTs never call this. Uses Next(60,101) — a test
    // RNG returning the top of the range yields a full-band value, keeping the
    // choreography suite's exact-damage assertions stable.
    private int RollAttackBand(int band)
    {
        int pct = _random.Next(60, 101); // 60..100 inclusive
        return Math.Max(1, (int)Math.Round(band * pct / 100.0));
    }

    // ── Eruption hazard timer (independent of the rotation loop) ───────

    private void ProcessEruptionTimer(GameState state, NpcInstance npc, bool rotationEventThisTick)
    {
        if (!npc.IsAlive || npc.Template.Script is null) return;
        if (!state.IsMechanicEnabled(BossMechanic.Eruptions)) return; // dev toggle
        npc.TickEruptionCooldown();
        if (npc.EruptionCooldown > 0) return;

        // Minimal 1-tick nudge: don't pile a fresh hazard wave onto the same
        // tick as a style telegraph, a Rot Burst warning, or an attack/Rot
        // Burst impact. Re-checks next tick rather than assuming one nudge
        // is always enough — astronomically rare in practice, but cheap to
        // get right.
        if (rotationEventThisTick)
        {
            npc.ResetEruptionCooldown(1);
            return;
        }

        var e = npc.ActivePhaseDef.Eruption;
        var tiles = PickHazardTiles(state, e.TilesPerWave);
        state.AddHazardWave(tiles, e.WarningTicks, e.PoolTicks);
        npc.ResetEruptionCooldown(e.CooldownTicks);
        state.AppendLog("⚠ The Maggot King's brood burrows beneath you — MOVE!", LogEntryKind.BossSpecial);
    }

    private List<(int X, int Z)> PickHazardTiles(GameState state, int count)
    {
        var tiles = new List<(int X, int Z)> { state.PlayerTile };
        var candidates = new List<(int X, int Z)>();
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var t = (X: state.PlayerTile.X + dx, Z: state.PlayerTile.Z + dz);
                if (state.InArena(t) && !state.IsObstacle(t)) candidates.Add(t);
            }
        for (int i = 0; i < count - 1 && candidates.Count > 0; i++)
        {
            int pick = _random.Next(0, candidates.Count);
            tiles.Add(candidates[pick]);
            candidates.RemoveAt(pick);
        }
        return tiles;
    }

    private void ProcessHazardResolution(GameState state, Player player, (int X, int Z) preTickPlayerTile, List<(int X, int Z)> erupting)
    {
        var npc = state.ActiveNpc;
        if (npc?.Template.Script is null) return; // no script, no hazards were ever created

        var erupted = state.TickHazards();
        var e = npc.ActivePhaseDef.Eruption;

        if (player.IsAlive && erupted.Contains(state.PlayerTile))
        {
            int dmg = ResolveIncomingDamage(state, player, npc, e.EruptDamage, style: null, unprayable: true);
            player.TakeDamage(dmg);
            state.RecordDamageTaken(dmg);
            state.SetKilledBy("Eruption (unprayable)");
            state.AppendHitsplat(onEnemy: false, dmg, "hazard");
            var flavor = npc.Template.Script?.Flavor;
            state.AppendLog($"{flavor?.HazardLand ?? "The ground ERUPTS beneath you"} for {dmg}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            if (!state.PlayerPoisoned && state.PoisonImmuneTicksLeft <= 0)
            {
                state.ApplyPoison();
                state.AppendLog(flavor?.HazardPoison ?? "The writhing mass poisons you!", LogEntryKind.System);
            }
            else if (state.PoisonImmuneTicksLeft > 0)
            {
                state.AppendLog("Rotward's ward shrugs off the poison.", LogEntryKind.System);
            }
        }
        else if (player.IsAlive && state.IsPool(state.PlayerTile) && state.IsMechanicEnabled(BossMechanic.Pools))
        {
            int dmg = ResolveIncomingDamage(state, player, npc, e.PoolDamagePerTick, style: null, unprayable: true);
            player.TakeDamage(dmg);
            state.RecordDamageTaken(dmg);
            if (dmg > 0) state.SetKilledBy("Poison pool (unprayable)");
            state.AppendHitsplat(onEnemy: false, dmg, "poison");
            state.AppendLog($"{npc.Template.Script?.Flavor?.HazardPool ?? "Acrid slime burns at your feet."} [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }

        // Perfect Dodge (m1-plan Workstream C.8; generalized M3 Workstream
        // A.2): stood on a tile that was erupting THIS tick at tick-start,
        // vacated it, and wasn't caught by any other eruption this tick.
        TryPerfectDodge(state, player, wasOnDangerTile: erupting.Contains(preTickPlayerTile),
            stillOnDangerTile: preTickPlayerTile == state.PlayerTile || erupted.Contains(state.PlayerTile));
    }

    /// <summary>Global Combat Grammar's universal reward: "vacating a hazard
    /// tile on its final fuse tick, or sidestepping a locked projectile on
    /// its landing tick, grants +15 special energy." One shared check for
    /// any mechanic that marks a tile/line and resolves it on a fixed tick —
    /// Maggot King's eruptions (via ProcessHazardResolution above) and Hive
    /// Matron's Pin line-charge (see ResolveLineCharge) both call this
    /// instead of re-deriving the reward inline.</summary>
    private void TryPerfectDodge(GameState state, Player player, bool wasOnDangerTile, bool stillOnDangerTile)
    {
        if (!wasOnDangerTile || stillOnDangerTile) return;
        player.RechargeSpecial(15, MaxSpecialEnergy(player));
        state.AppendLog("✦ PERFECT DODGE! +15 special energy.", LogEntryKind.System);
        state.AppendVfxEvent("perfect_dodge", "player");
    }

    // ── M3, Hive Matron's per-tick mechanics ────────────────────────────
    // Everything here is additive and gated on the boss actually declaring
    // the relevant data (AdjacencyPunish/DamageReductionWindow/Drones/
    // LineCharge) — currently only Hive Matron sets any of them, so this is
    // dormant for every other boss, same pattern as ProcessSpacingAiMovement.

    private void ProcessSpacingAiMechanics(GameState state, Player player, NpcInstance npc, (int X, int Z) preTickPlayerTile)
    {
        var script = npc.Template.Script!;

        if (script.AdjacencyPunish is { } tailStab)
        {
            // Chebyshev adjacency (matches how swarm-add contact is judged
            // elsewhere), not the cardinal-only melee-attack rule — "stands
            // adjacent" is a positional read, not an attack-landing one.
            //
            // Boss Bible §2: Tail Stab fires "if the player STANDS adjacent for
            // 2 consecutive ticks" — the weave is "step in → hit (1 tick) →
            // step out." A tick the player is only *passing through* adjacency
            // (they moved this tick) is not "standing," so it must not count,
            // or a clean weave (arrive-adjacent one tick, strike the next, leave
            // the third) would eat a Tail Stab on the strike tick and the whole
            // melee rhythm — the fight's entire lesson — becomes unplayable.
            // Only a tick the player ends STATIONARY while adjacent counts.
            bool movedThisTick = state.PlayerTile != preTickPlayerTile;
            bool standingAdjacent = state.DistanceToNpc <= 1 && !movedThisTick;
            npc.TickAdjacency(standingAdjacent);
            if (npc.AdjacentTicksCount >= tailStab.AdjacencyTicks)
            {
                npc.ResetAdjacency();
                int dmg = ResolveIncomingDamage(state, player, npc, tailStab.Damage, style: AttackType.Slash, unprayable: false);
                player.TakeDamage(dmg);
                state.RecordDamageTaken(dmg);
                string name = tailStab.Name ?? "Tail Stab";
                if (dmg > 0) state.SetKilledBy(name);
                state.AppendHitsplat(onEnemy: false, dmg, "normal", "melee");
                state.AppendLog($"{npc.Template.Name} answers your closeness with {name} for {dmg}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
                Knockback(state, tailStab.KnockbackTiles);
                // Boss Bible: "Tail Stab — Heavy melee + 2-tile knockback +
                // poison." Reuses the existing player-side poison track
                // (same one Maggot King's eruptions apply) rather than a new
                // mechanism.
                if (!state.PlayerPoisoned && state.PoisonImmuneTicksLeft <= 0)
                {
                    state.ApplyPoison();
                    state.AppendLog("Her stinger leaves you poisoned!", LogEntryKind.System);
                }
            }
        }

        if (script.DamageReductionWindow is { } guard)
        {
            npc.TickDamageReductionCooldown();
            npc.TickDamageReductionWindow();
            if (npc.DamageReductionCooldown <= 0 && !npc.DamageReductionActive && state.IsMechanicEnabled(BossMechanic.BossAutos))
            {
                npc.StartDamageReductionWindow(guard.DurationTicks);
                npc.ResetDamageReductionCooldown(guard.CadenceTicks);
                state.AppendLog($"{npc.Template.Name} raises her wing casings — ranged/magic damage reduced!", LogEntryKind.BossSpecial);
            }
        }

        var drones = npc.ActivePhaseDef.Drones;
        if (drones is not null)
            foreach (var wave in drones)
            {
                if (npc.HpPercent > wave.ThresholdPercent) continue;
                if (!npc.TryFireDroneThreshold(wave.ThresholdPercent)) continue;
                for (int i = 0; i < wave.Count; i++)
                {
                    // Spawn already on the boss→player lane (DroneLaneTile keys
                    // off the id's trailing index for the fan) so they guard the
                    // approach from tick one instead of walking in from the side.
                    var seed = new AddInstance($"drone_{wave.ThresholdPercent}_{i}", state.NpcTile, wave.Hp, AddKind.Drone, wave.OrbitRadius);
                    seed.MoveTo(DroneLaneTile(state, seed));
                    state.SpawnAdd(seed);
                }
                state.AppendLog($"⚠ Drones rise to guard {npc.Template.Name}! ({wave.Count})", LogEntryKind.BossSpecial);
            }

        if (npc.LineChargeTiles is not null && npc.TickLineCharge())
            ResolveLineCharge(state, player, npc, preTickPlayerTile);

        if (npc.NeedleSpitTiles is not null && npc.TickNeedleSpit())
            ResolveNeedleSpit(state, player, npc, preTickPlayerTile);
    }

    private void ResolveLineCharge(GameState state, Player player, NpcInstance npc, (int X, int Z) preTickPlayerTile)
    {
        var lc = npc.Template.Script!.LineCharge!;
        var tiles = npc.LineChargeTiles!;
        bool wasOnLine = tiles.Contains(preTickPlayerTile);
        bool stillOnLine = tiles.Contains(state.PlayerTile);
        bool chainedThisResolve = npc.LineChargePendingSecondChain;
        npc.ClearLineCharge();

        if (stillOnLine && player.IsAlive)
        {
            int dmg = ResolveIncomingDamage(state, player, npc, lc.Damage, style: AttackType.Crush, unprayable: false);
            player.TakeDamage(dmg);
            state.RecordDamageTaken(dmg);
            if (dmg > 0) state.SetKilledBy("Pin");
            state.AppendHitsplat(onEnemy: false, dmg, "normal", "melee");
            state.AppendLog($"Pin slams into you for {dmg} — pinned! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            // Boss Bible: "pinned/stunned 2 ticks against the wall." No full
            // movement-lock exists in the engine yet (PROVISIONAL scoping,
            // see m3-findings.md) — approximated with the existing attack-
            // delay primitive (DelayPlayerAttack) rather than inventing a new
            // player-movement-lock system for one boss's one attack.
            state.DelayPlayerAttack(lc.StunTicks);
        }
        else
        {
            state.AppendLog($"{npc.Template.Name}'s Pin charges past you and slams into the wall!", LogEntryKind.BossSpecial);
            npc.StartSlump(lc.MissPunishTicks);
            TryPerfectDodge(state, player, wasOnDangerTile: wasOnLine, stillOnDangerTile: stillOnLine);
        }

        // Phase 2 double-chain (Boss Bible: "Pin now chains twice, second
        // charge 3 ticks after the first, re-aimed") — fires once per
        // original cast, never off the chained shot itself.
        if (lc.DoubleChainInPhase2 && npc.Phase == 2 && !chainedThisResolve)
        {
            var newTiles = LineThrough(state, state.NpcTile, state.PlayerTile);
            npc.StartLineCharge(newTiles, 3, chainSecond: true);
            state.AppendLog($"⚠ {npc.Template.Name} re-aims — PIN incoming again!", LogEntryKind.BossSpecial);
        }
    }

    // ── M3, Mirrorhide's per-tick mechanics ─────────────────────────────
    // Gated on Cloak being present — currently only Mirrorhide sets one, so
    // this whole block is dormant for every other boss.

    private void ProcessMirrorhideMechanics(GameState state, Player player, NpcInstance npc)
    {
        var script = npc.Template.Script!;

        if (npc.ActivePhaseDef.Attunement is { } attunement)
            npc.TickAttunement(attunement.ImmuneTicks);

        if (script.Cloak is { } cloak)
        {
            npc.TickCloakCooldown();
            if (npc.IsCloaked)
            {
                if (npc.TickCloak())
                {
                    // Reposition behind the player (point-reflected through
                    // their tile from wherever she currently stands).
                    var behind = (X: 2 * state.PlayerTile.X - state.NpcTile.X, Z: 2 * state.PlayerTile.Z - state.NpcTile.Z);
                    if (!state.InArena(behind)) behind = state.PlayerTile;
                    state.SetNpcTile(behind.X, behind.Z);
                    state.AppendLog($"{npc.Template.Name} melts back into view behind you!", LogEntryKind.BossSpecial);

                    if (cloak.PounceOnEnd && npc.Phase == 2 && state.IsMechanicEnabled(BossMechanic.BossAutos))
                    {
                        state.AddHazardWave(new[] { state.PlayerTile }, warningTicks: 1, poolTicks: 0);
                        state.AppendLog($"⚠ {npc.Template.Name} pounces — MOVE!", LogEntryKind.BossSpecial);
                    }
                }
            }
            else if (npc.CloakCooldown <= 0 && state.IsMechanicEnabled(BossMechanic.BossAutos))
            {
                npc.StartCloak(cloak.DurationTicks);
                npc.ResetCloakCooldown(cloak.CadenceTicks);
                state.AppendLog($"{npc.Template.Name} shimmers and vanishes — cloaked!", LogEntryKind.BossSpecial);
            }
        }

        if (script.Reflect is { } reflect)
        {
            if (npc.ReflectChannelTicksLeft > 0 && npc.TickReflectChannel())
            {
                var style = npc.AttunedStyle ?? npc.LastHitStyle ?? AttackType.Crush;
                npc.StartReflectWindow(reflect.WindowTicks, style);
                state.AppendLog($"{npc.Template.Name}'s scales flare — reflecting {StyleName(style)} damage!", LogEntryKind.BossSpecial);
            }
            npc.TickReflectWindow();
        }

        if (script.Copycat is { } copycat && npc.Phase == 2)
        {
            npc.TickCopycatCooldown();
            if (npc.CopycatTelegraphTicksLeft > 0)
            {
                if (npc.TickCopycatTelegraph())
                {
                    int dmg = ResolveIncomingDamage(state, player, npc, copycat.Damage, style: null, unprayable: true);
                    player.TakeDamage(dmg);
                    state.RecordDamageTaken(dmg);
                    if (dmg > 0) state.SetKilledBy("Copycat");
                    state.AppendHitsplat(onEnemy: false, dmg, "hazard");
                    string specialName = state.LastPlayerSpecialName ?? "your own special";
                    state.AppendLog($"{npc.Template.Name} throws {specialName} back at you for {dmg}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.BossSpecial);
                }
            }
            else if (npc.CopycatCooldown <= 0 && state.LastPlayerSpecialName is not null && state.IsMechanicEnabled(BossMechanic.BossAutos))
            {
                npc.StartCopycatTelegraph(copycat.TelegraphTicks);
                npc.ResetCopycatCooldown(copycat.CadenceTicks);
                state.AppendLog($"⚠ {npc.Template.Name}'s silhouette flickers with your own special — COPYCAT incoming!", LogEntryKind.BossSpecial);
            }
        }
    }

    // ── M3, Bloodtithe's per-tick mechanics ─────────────────────────────
    // Gated on FacingAura being present — currently only Bloodtithe sets one.

    // 0=North(+Z), 1=East(+X), 2=South(-Z), 3=West(-X) — his turn rate is
    // always exactly one quarter-turn per tick (Boss Bible: "90° per tick"),
    // so an int index avoids float drift entirely.
    private static readonly (int X, int Z)[] FacingOffsets = { (0, 1), (1, 0), (0, -1), (-1, 0) };

    private static int FacingIndexToward((int X, int Z) from, (int X, int Z) to)
    {
        int dx = to.X - from.X, dz = to.Z - from.Z;
        if (dx == 0 && dz == 0) return 0;
        return Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? 1 : 3) : (dz > 0 ? 0 : 2);
    }

    private void ProcessBloodtitheMechanics(GameState state, Player player, NpcInstance npc)
    {
        var script = npc.Template.Script!;
        var aura = script.FacingAura!;

        int currentFacing = (int)Math.Round(npc.FacingAngleDeg / 90.0) % 4;
        int idealFacing = FacingIndexToward(state.NpcTile, state.PlayerTile);
        if (currentFacing != idealFacing)
        {
            // Turn exactly one step per tick, shortest direction.
            int diff = ((idealFacing - currentFacing) % 4 + 4) % 4;
            int step = diff <= 2 ? 1 : -1;
            currentFacing = ((currentFacing + step) % 4 + 4) % 4;
            npc.SetFacing(currentFacing * 90.0);
        }

        var backOffset = FacingOffsets[(currentFacing + 2) % 4];
        var backTile = (X: state.NpcTile.X + backOffset.X, Z: state.NpcTile.Z + backOffset.Z);
        bool onBackTile = state.PlayerTile == backTile;
        bool inAuraRange = state.DistanceToNpc <= aura.AuraRadius;

        if (inAuraRange && !onBackTile && player.IsAlive)
        {
            int drain = Math.Max(1, (int)Math.Round(player.MaxHp * aura.DrainPercent));
            player.TakeDamage(drain);
            state.RecordDamageTaken(drain);
            npc.Heal(drain);
            state.AppendHitsplat(onEnemy: false, drain, "normal");
            state.AppendLog($"Bloodtithe's tithe aura drains {drain} from you. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }

        // Font tiles: standing on one (with something to purge, cooldown
        // ready) cleanses all current bleed stacks.
        if (state.PlayerBleedStacks > 0 && script.Fonts is { } fonts)
        {
            var font = fonts.FirstOrDefault(f => (f.X, f.Z) == state.PlayerTile);
            if (font is not null && state.TryUseFontAt(state.PlayerTile, font.CooldownTicks))
            {
                state.ConsumeAllPlayerBleedStacks();
                state.AppendLog("You purge your bleed at the Font!", LogEntryKind.System);
            }
        }
        state.TickFontCooldowns();

        // Scythe Arc resolution: range re-checked at the windup's end.
        if (npc.ScytheArcTicksLeft > 0 && npc.TickScytheArcWindup())
        {
            if (state.InAttackRange(AttackRange.Melee) && player.IsAlive)
                ResolveBossAttack(state, player, npc, npc.Template.Script!.Attacks["scythe_arc"]);
            else
            {
                state.AppendLog("Bloodtithe's scythe arcs through empty air!", LogEntryKind.BossSpecial);
                npc.StartSlump(3); // Boss Bible: "Missing it gives a 3-tick punish window"
            }
        }

        // Transfusion: heals him each tick; interrupt (a player special
        // landing during the channel) is handled at the special-attack call
        // site (see PerformSpecialAttack), not here.
        if (npc.TransfusionActive)
        {
            npc.Heal((int)Math.Round(npc.MaxHp * script.Transfusion!.HealPercentPerTick));
            npc.TickTransfusion();
        }

        // Phase 2: Crimson Pact (periodic HP-for-speed trade) + Harvest
        // (telegraph then consumes all bleed stacks for damage).
        if (npc.Phase == 2)
        {
            if (script.CrimsonPact is { } pact)
            {
                npc.TickCrimsonPactCooldown();
                npc.TickSpeedBoost();
                if (npc.CrimsonPactCooldown <= 0 && !npc.SpeedBoosted && state.IsMechanicEnabled(BossMechanic.BossAutos))
                {
                    int sacrifice = (int)Math.Round(npc.CurrentHp * pact.SacrificePercent);
                    npc.TakeDamage(sacrifice);
                    npc.StartSpeedBoost(pact.SpeedTicks);
                    npc.ResetCrimsonPactCooldown(pact.CadenceTicks);
                    state.AppendLog($"Bloodtithe tears at his own flesh — Crimson Pact! (-{sacrifice} HP, full speed for {pact.SpeedTicks} ticks)", LogEntryKind.BossSpecial);
                }
            }

            if (script.Harvest is { } harvest)
            {
                npc.TickHarvestCooldown();
                if (npc.HarvestTelegraphTicksLeft > 0)
                {
                    if (npc.TickHarvestTelegraph())
                    {
                        int stacks = state.ConsumeAllPlayerBleedStacks();
                        int dmg = stacks * harvest.DamagePerStack;
                        if (dmg > 0 && player.IsAlive)
                        {
                            player.TakeDamage(dmg);
                            state.RecordDamageTaken(dmg);
                            state.SetKilledBy("Harvest");
                            state.AppendHitsplat(onEnemy: false, dmg, "hazard");
                        }
                        state.AppendLog($"Bloodtithe harvests {stacks} bleed stack{(stacks == 1 ? "" : "s")} for {dmg} damage! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.BossSpecial);
                    }
                }
                else if (npc.HarvestCooldown <= 0 && state.IsMechanicEnabled(BossMechanic.BossAutos))
                {
                    npc.StartHarvestTelegraph(harvest.TelegraphTicks);
                    npc.ResetHarvestCooldown(harvest.CadenceTicks);
                    state.AppendLog("⚠ Bloodtithe reaches out — HARVEST incoming! Purge your bleed now!", LogEntryKind.BossSpecial);
                }
            }
        }
    }

    // ── Rot Burst + swarms ──────────────────────────────────────────────

    private void ResolveRotBurst(GameState state, Player player, NpcInstance npc)
    {
        var rb = npc.ActivePhaseDef.RotBurst!;
        bool safe = state.IsScorch(state.PlayerTile);

        if (!safe && player.IsAlive)
        {
            int dmg = ResolveIncomingDamage(state, player, npc, rb.Damage, style: null, unprayable: true);
            player.TakeDamage(dmg);
            state.RecordDamageTaken(dmg);
            state.SetKilledBy("Rot Burst (unprayable)");
            state.AppendHitsplat(onEnemy: false, dmg, "hazard");
            state.AppendLog($"★ ROT BURST detonates for {dmg}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.BossSpecial);
        }
        else
        {
            state.AppendLog("You shelter on the scorched ground — Rot Burst passes you by.", LogEntryKind.System);
        }

        npc.StartSlump(rb.SlumpTicks);
        npc.ResetRotBurstCooldown(rb.CadenceTicks);
        state.AppendLog($"The Maggot King slumps, exhausted — punish window! (+25% damage taken, {rb.SlumpTicks} ticks)", LogEntryKind.BossSpecial);
    }

    private void ProcessSwarmSpawns(GameState state, NpcInstance npc)
    {
        if (!npc.IsAlive || npc.Template.Script is null) return;
        if (!state.IsMechanicEnabled(BossMechanic.Swarms)) return; // dev toggle
        var swarms = npc.ActivePhaseDef.Swarms;
        if (swarms is null) return;

        foreach (var wave in swarms)
        {
            if (npc.HpPercent > wave.ThresholdPercent) continue;
            if (!npc.TrySpawnSwarmThreshold(wave.ThresholdPercent)) continue;

            var corners = SwarmCorners(state);
            for (int i = 0; i < wave.Count; i++)
                state.SpawnAdd(new AddInstance($"swarm_{wave.ThresholdPercent}_{i}", corners[i % corners.Length], wave.Hp));

            state.AppendLog($"⚠ Maggot swarms erupt from the corners! ({wave.Count})", LogEntryKind.BossSpecial);
        }
    }

    // ── Damage-over-time ─────────────────────────────────────────────────

    private static void ApplyDots(GameState state, Player player, NpcInstance npc)
    {
        state.TickPoisonImmunity(); // Rotward's 15-tick window (backlog batch 1 §3) counts down regardless of the dev DoT toggle
        if (!state.IsMechanicEnabled(BossMechanic.Dots)) return; // dev toggle

        if (state.BleedTicksLeft > 0 && player.IsAlive)
        {
            player.TakeDamage(state.BleedPerTick);
            state.RecordDamageTaken(state.BleedPerTick);
            if (state.BleedPerTick > 0) state.SetKilledBy("Bleed");
            state.AppendHitsplat(onEnemy: false, state.BleedPerTick, "poison");
            state.AppendLog($"You bleed for {state.BleedPerTick} damage. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            state.TickBleed();
        }

        if (player.IsAlive && state.TickPoison())
        {
            player.TakeDamage(3);
            state.RecordDamageTaken(3);
            state.SetKilledBy("Poison");
            state.AppendHitsplat(onEnemy: false, 3, "poison");
            state.AppendLog($"The poison courses through you. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }

        // M3, Bloodtithe's stacking bleed (distinct from the flat Rend/Scorch
        // bleed above — see GameState.ApplyBleedStack's doc comment). Damage
        // is captured BEFORE ticking, same reason Rotfang's NPC poison does
        // (TickPlayerBleedStacks zeroes the stack count on its final tick).
        if (player.IsAlive && state.PlayerBleedStacks > 0)
        {
            int stacksThisTick = state.PlayerBleedStacks;
            int dmg = stacksThisTick * state.PlayerBleedDamagePerTick;
            if (state.TickPlayerBleedStacks())
            {
                player.TakeDamage(dmg);
                state.RecordDamageTaken(dmg);
                if (dmg > 0) state.SetKilledBy("Bleed");
                    state.AppendHitsplat(onEnemy: false, dmg, "poison");
                state.AppendLog($"You bleed for {dmg} damage ({stacksThisTick} stacks). [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            }
        }
    }

    // Boss-side poison DoT (Rotfang, items doc §3, backlog resolution batch
    // 1) -- symmetric to ApplyDots' player-side poison above, but keyed off
    // NpcInstance.PoisonStacks/PoisonDamagePerTick instead of a flat rate.
    private static void ApplyNpcPoison(GameState state, NpcInstance npc)
    {
        int dmg = npc.PoisonDamagePerTick;
        if (!npc.TickPoison()) return;
        npc.TakeDamage(dmg);
        state.AppendHitsplat(onEnemy: true, dmg, "poison");
        state.AppendLog($"Rotfang's venom festers in {npc.Template.Name} for {dmg}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.PlayerHit);
    }

    // ── Damage/mitigation helpers ────────────────────────────────────────

    /// <summary>All boss/hazard damage to the player funnels through here:
    /// Sap's boss-damage debuff, then prayer (unless Unprayable/style-less),
    /// then the player's own armour Def-point reduction (items doc §1 —
    /// applies to any incoming style, no per-style split in M1).</summary>
    private int ResolveIncomingDamage(GameState state, Player player, NpcInstance npc, int baseDamage, AttackType? style, bool unprayable)
    {
        double dmg = baseDamage * npc.SapDamageMultiplier;
        if (!unprayable && style is { } s)
            dmg *= 1.0 - GetPrayerReduction(state, s);
        dmg *= 1.0 - PlayerDefReductionFraction(player);
        return Math.Max(0, (int)Math.Round(dmg));
    }

    /// <summary>Chitin Guard (M3, Hive Matron): a periodic self-buff reducing
    /// the player's damage output against the boss for the listed styles
    /// while active — the inverse of Sap (which reduces the boss's OWN
    /// outgoing damage). Any boss with a <see cref="DamageReductionWindowDef"/>
    /// gets this for free; only Hive Matron sets one today.</summary>
    private static int ApplyBossDamageReduction(NpcInstance npc, AttackType style, int damage)
    {
        var def = npc.Template.Script?.DamageReductionWindow;
        if (def is null || !npc.DamageReductionActive || !def.AffectedStyles.Contains(style)) return damage;
        return (int)Math.Round(damage * (1.0 - def.ReductionPercent));
    }

    /// <summary>Melee vulnerability (Hive Matron rework): a boss can be "weak to
    /// melee" — Stab/Slash/Crush hits deal +<c>MeleeVulnerabilityPercent</c>.
    /// The mirror of Chitin Guard's ranged/magic reduction, and the thing that
    /// makes the weave the *rewarded* line. No-op (percent 0) for every boss
    /// that doesn't declare it.</summary>
    private static int ApplyMeleeVulnerability(NpcInstance npc, AttackType style, int damage)
    {
        double pct = npc.Template.Script?.MeleeVulnerabilityPercent ?? 0.0;
        if (pct <= 0 || damage <= 0) return damage;
        bool melee = style is AttackType.Stab or AttackType.Slash or AttackType.Crush;
        return melee ? (int)Math.Round(damage * (1.0 + pct)) : damage;
    }

    /// <summary>Bloodtithe's back-tile bonus (Boss Bible §4): "his back tile
    /// is exempt [from the Tithe aura] and takes +30% damage." No-op for
    /// any boss without a FacingAura (currently only Bloodtithe).</summary>
    private static int ApplyBloodtitheBackBonus(NpcInstance npc, GameState state, int damage)
    {
        var aura = npc.Template.Script?.FacingAura;
        if (aura is null || damage <= 0) return damage;
        int facing = (int)Math.Round(npc.FacingAngleDeg / 90.0) % 4;
        var backOffset = FacingOffsets[(facing + 2) % 4];
        var backTile = (X: state.NpcTile.X + backOffset.X, Z: state.NpcTile.Z + backOffset.Z);
        return state.PlayerTile == backTile ? (int)Math.Round(damage * (1.0 + aura.BackDamageBonus)) : damage;
    }

    private double PlayerDefReductionFraction(Player player)
    {
        double points = 0;
        foreach (var (slot, itemId) in player.Equipped)
        {
            if (slot == EquipmentSlot.Weapon) continue;
            points += _items.GetGear(itemId)?.Doc.DefPoints ?? 0;
        }
        return Math.Min(points * Duels.Domain.Services.DamageModel.DefPointValue, Duels.Domain.Services.DamageModel.GearDefCap);
    }

    // Boss bible "Prayer grammar": a matching protection prayer fully negates
    // a non-Unprayable boss attack (100% block), not a percentage mitigation
    // — confirmed by the invocations doc's Doubt curse ("Protection prayers
    // block 75% instead of 100%"), which only makes sense as a debuff of a
    // 100% baseline. Doubt itself is out of scope for M1 (no invocation
    // system yet); when it lands this is the one number it should override.
    private static double GetPrayerReduction(GameState state, AttackType npcAttackType)
    {
        if (state.Player.PrayerPoints <= 0) return 0.0;
        return state.TickStartProtection switch
        {
            ProtectionPrayer.Melee => npcAttackType is AttackType.Stab or AttackType.Slash or AttackType.Crush ? 1.0 : 0.0,
            ProtectionPrayer.Range => npcAttackType == AttackType.Ranged ? 1.0 : 0.0,
            ProtectionPrayer.Magic => npcAttackType == AttackType.Magic ? 1.0 : 0.0,
            _ => 0.0,
        };
    }

    private static string StyleName(AttackType t) => t switch
    {
        AttackType.Magic => "Magic",
        AttackType.Ranged => "Ranged",
        _ => "Melee",
    };

    // Renderer-facing token (matches BattleScene.razor's StyleClass) — lets
    // the boss's hit-impact animation match its actual attack style instead
    // of defaulting to a melee swing for everything.
    private static string StyleToken(AttackType t) => t switch
    {
        AttackType.Magic => "magic",
        AttackType.Ranged => "ranged",
        _ => "melee",
    };

    private AttackerProfile BuildAttackerProfile(Player player, Weapon? weapon)
    {
        int power = weapon?.Doc.Power ?? 5;
        double precision = weapon?.Doc.Precision ?? 0;
        double bonus = ComputeLineDamageBonus(player, weapon);
        // Boost prayer (UI bible §3.2 "Boost prayer"): +20% damage while active.
        if (player.BoostPrayerActive) bonus += 0.20;
        return new AttackerProfile(power, precision, player.ChosenStyle, bonus);
    }

    /// <summary>Items doc §5: +1% line-style damage per equipped piece of the
    /// weapon's line (identity bonus), +5% more at a 4-piece set bonus.</summary>
    private double ComputeLineDamageBonus(Player player, Weapon? weapon) =>
        weapon is null ? 0 : GetLineDamageBonusPreview(player, weapon.Doc.Line);

    /// <summary>Public preview variant of the same items doc §5 formula
    /// (M2 Workstream C.2), for the equipment screen's stat sheet — "what
    /// damage bonus would a weapon of this line get from my worn armour"
    /// without requiring one to actually be equipped. Same rules, single
    /// source of truth: <see cref="ComputeLineDamageBonus"/> now just calls
    /// this with the equipped weapon's line.</summary>
    public double GetLineDamageBonusPreview(Player player, GearLine line)
    {
        if (line == GearLine.None) return 0;
        int pieces = 0;
        foreach (var (slot, itemId) in player.Equipped)
        {
            if (slot == EquipmentSlot.Weapon) continue;
            if (_items.GetGear(itemId)?.Doc.Line == line) pieces++;
        }
        double bonus = pieces * 0.01;
        if (pieces >= 4) bonus += 0.05;
        return bonus;
    }

    /// <summary>Items doc §5: a 6-piece set of one armour line grants +10 max
    /// special energy. Public (M2 Workstream C.2): also the equipment
    /// screen's stat-sheet source, not just combat's.</summary>
    public int MaxSpecialEnergy(Player player)
    {
        foreach (var line in new[] { GearLine.Warbound, GearLine.Stalker, GearLine.Occult })
        {
            int pieces = player.Equipped.Count(kv => kv.Key != EquipmentSlot.Weapon && _items.GetGear(kv.Value)?.Doc.Line == line);
            if (pieces >= 6) return 110;
        }
        return 100;
    }

    private Weapon? GetPlayerWeapon(Player player)
    {
        var id = player.GetEquippedWeaponId();
        return id is not null ? _items.GetWeapon(id) : null;
    }

    private int GetPlayerWeaponSpeed(Player player) => GetPlayerWeapon(player)?.AttackSpeed ?? 4;
    private int GetPlayerWeaponRange(Player player) => GetPlayerWeapon(player)?.Range ?? AttackRange.Melee;

    // ── Victory / defeat ─────────────────────────────────────────────────

    private async Task HandleVictory(GameState state)
    {
        var player = state.Player;
        var npc = state.ActiveNpc!;

        state.AppendLog($"You have defeated {npc.Template.Name}!", LogEntryKind.System);
        state.AppendLog("═══ DUEL WON ═══", LogEntryKind.System);

        bool flawless = state.DamageTakenThisDuel == 0;
        if (flawless) state.AppendLog("⭐ FLAWLESS VICTORY — you took zero damage!", LogEntryKind.Loot);

        if (npc.Template.GoldReward > 0)
        {
            player.AddGold(npc.Template.GoldReward);
            state.AppendLog($"You receive {npc.Template.GoldReward:N0}g. (Total: {player.Gold:N0}g)", LogEntryKind.Loot);
        }

        var lootItems = RollLoot(state, player, npc.Template);

        bool personalBest = player.PersonalBestKillTicks is null || state.FightTicks < player.PersonalBestKillTicks;
        if (personalBest) player.RecordKillTime(state.FightTicks);
        player.RecordBossKill(npc.Template.Id, state.FightTicks); // M3: per-boss roster stats

        state.SetDuelSummary(new DuelSummary(
            Won: true,
            NpcId: npc.Template.Id,
            NpcName: npc.Template.Name,
            KillTimeTicks: state.FightTicks,
            KilledBy: null,
            PersonalBest: personalBest,
            Flawless: flawless,
            GoldGained: npc.Template.GoldReward,
            LootItemIds: lootItems));

        state.EndDuel();
        await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, npc.Template.GoldReward));
    }

    // Backlog resolution batch 1 §3: shard -> flask auto-combine. 5 shards
    // convert to the flask (one-time, per flask type); once owned, further
    // shards convert to 100g on pickup instead of stacking further.
    private static readonly Dictionary<string, string> ShardToFlask = new() { ["rotward_shard"] = "flask_rotward" };
    private const int ShardsRequiredForFlask = 5;
    private const int SurplusShardGold = 100;

    private List<string> RollLoot(GameState state, Player player, NpcTemplate template)
    {
        var lootedItems = new List<string>();

        // Ungrouped entries: independent rolls, unchanged from before.
        foreach (var entry in template.LootTable.Where(e => e.GroupId is null))
        {
            if (entry.OnceOnly && player.HasItem(entry.ItemId)) continue;
            if (_random.NextDouble() >= entry.DropChance) continue;
            ResolveLootHit(state, player, entry, lootedItems);
        }

        // Grouped entries: economy doc §5's "one roll on... Slot" two-stage
        // model — roll the group's own chance once, then weighted-pick one
        // member (LootEntry.GroupId/Weight).
        foreach (var group in template.LootTable.Where(e => e.GroupId is not null).GroupBy(e => e.GroupId))
        {
            var members = group.ToList();
            if (_random.NextDouble() >= members[0].DropChance) continue; // every member shares the group's own chance

            double totalWeight = members.Sum(m => m.Weight);
            double roll = _random.NextDouble() * totalWeight;
            var picked = members[0];
            double cursor = 0;
            foreach (var m in members)
            {
                cursor += m.Weight;
                if (roll < cursor) { picked = m; break; }
            }

            if (picked.OnceOnly && player.HasItem(picked.ItemId)) continue;
            ResolveLootHit(state, player, picked, lootedItems);
        }

        return lootedItems;
    }

    private void ResolveLootHit(GameState state, Player player, LootEntry entry, List<string> lootedItems)
    {
        int qty = entry.MaxQty > entry.MinQty ? _random.Next(entry.MinQty, entry.MaxQty + 1) : entry.MinQty;

        // "gold" is a pseudo-item (economy §5 "Gold Cache"): direct gold, no inventory slot.
        if (entry.ItemId == "gold")
        {
            player.AddGold(qty);
            state.AppendLog($"You find a gold cache worth {qty:N0}g.", LogEntryKind.Loot);
            return;
        }

        var itemName = _items.GetItemName(entry.ItemId) ?? entry.ItemId;

        for (int i = 0; i < qty; i++)
        {
            // Shard already unlocked its flask -> surplus shards convert to gold on pickup, never stack further.
            if (ShardToFlask.TryGetValue(entry.ItemId, out var flaskId) && player.HasItem(flaskId))
            {
                player.AddGold(SurplusShardGold);
                state.AppendLog($"You find a {itemName} — already unlocked, worth {SurplusShardGold}g.", LogEntryKind.Loot);
                continue;
            }

            if (player.Inventory.Count >= Player.BagCapacity) // UI bible §7: bag is 28 slots (fixed); bank is the unbounded overflow store.
            {
                int fenceValue = _items.GetFenceValue(entry.ItemId);
                player.AddGold(fenceValue);
                state.AppendLog($"Your pack is full — you fence the {itemName} for {fenceValue:N0}g.", LogEntryKind.Loot);
            }
            else
            {
                player.AddToInventory(entry.ItemId);
                lootedItems.Add(entry.ItemId);
                state.AppendLog($"You loot {itemName}.", LogEntryKind.Loot);

                // Auto-combine: 5 shards -> the flask, one-time unlock.
                if (ShardToFlask.TryGetValue(entry.ItemId, out var unlockFlaskId)
                    && player.Inventory.Count(id => id == entry.ItemId) >= ShardsRequiredForFlask)
                {
                    for (int s = 0; s < ShardsRequiredForFlask; s++) player.RemoveFromInventory(entry.ItemId);
                    player.AddToInventory(unlockFlaskId);
                    var flaskName = _items.GetItemName(unlockFlaskId) ?? unlockFlaskId;
                    state.AppendLog($"★ {ShardsRequiredForFlask} {itemName}s combine into a {flaskName}!", LogEntryKind.Loot);
                }
            }
        }
    }

    private async Task HandleDefeat(GameState state, Player player, NpcInstance npc)
    {
        player.RecordBossDeath(npc.Template.Id); // M3: per-boss roster stats
        state.AppendLog($"You have been defeated by {npc.Template.Name}!", LogEntryKind.System);
        // Damage-source death logging: name the mechanic that actually landed
        // the killing blow (every player-damage source now sets KilledBy), so a
        // death to a pool/bleed/eruption reads distinctly from a boss auto.
        if (state.KilledBy is { } cause)
            state.AppendLog($"Slain by: {cause}", LogEntryKind.System);
        state.AppendLog("═══ DUEL LOST ═══", LogEntryKind.System);

        state.SetDuelSummary(new DuelSummary(
            Won: false,
            NpcId: npc.Template.Id,
            NpcName: npc.Template.Name,
            KillTimeTicks: state.FightTicks,
            KilledBy: state.KilledBy,
            PersonalBest: false,
            Flawless: false,
            GoldGained: 0,
            LootItemIds: []));

        player.RestoreHp();
        state.EndDuel();

        await _events.PublishAsync(new DuelLost(player.Id, npc.Template.Id, npc.Template.Name));
    }

    // ── Pathfinding (unchanged from M0) ─────────────────────────────────

    private static (int X, int Z) ApproachSlot((int X, int Z) from, (int X, int Z) to)
    {
        int dx = from.X - to.X, dz = from.Z - to.Z;
        return Math.Abs(dx) >= Math.Abs(dz) && dx != 0
            ? (to.X + Math.Sign(dx), to.Z)
            : (to.X, to.Z + Math.Sign(dz));
    }

    private static (int X, int Z) StepToward((int X, int Z) from, (int X, int Z) to) =>
        (from.X + Math.Sign(to.X - from.X), from.Z + Math.Sign(to.Z - from.Z));

    // M3, Hive Matron: the inverse of StepToward — one tile directly away
    // from a threat tile, for a boss (or drone) that wants to increase
    // distance rather than close it.
    private static (int X, int Z) StepAwayFrom(GameState state, (int X, int Z) from, (int X, int Z) threat)
    {
        var away = (X: from.X + Math.Sign(from.X - threat.X), Z: from.Z + Math.Sign(from.Z - threat.Z));
        if (away == from) away = (from.X + 1, from.Z); // threat exactly on top: arbitrary escape direction
        return away;
    }

    // M3: knocks the player `tiles` away from the boss along the boss→player
    // vector, clamped to the arena and stopping short of any obstacle —
    // Hive Matron's Tail Stab is the first mechanic to use this.
    private void Knockback(GameState state, int tiles)
    {
        if (tiles <= 0) return;
        var dir = (X: Math.Sign(state.PlayerTile.X - state.NpcTile.X), Z: Math.Sign(state.PlayerTile.Z - state.NpcTile.Z));
        if (dir == (0, 0)) dir = (1, 0);
        var target = state.PlayerTile;
        for (int i = 0; i < tiles; i++)
        {
            var next = (X: target.X + dir.X, Z: target.Z + dir.Z);
            if (!state.InArena(next) || state.IsBlocked(next, state.NpcTile)) break;
            target = next;
        }
        if (target != state.PlayerTile)
        {
            var from = state.PlayerTile;
            state.SetPlayerTile(target.X, target.Z);
            state.AppendLog("knockback", LogEntryKind.PlayerTeleport); // renderer: snap, not lerp
            state.AppendVfxEvent("forced_move", "player", new Dictionary<string, object>
            {
                ["fromX"] = (double)from.X, ["fromZ"] = (double)from.Z,
                ["toX"] = (double)target.X, ["toZ"] = (double)target.Z,
                ["cause"] = "knockback", // being hit away — see ExecuteLunge's note
            });
        }
    }

    // M3, Hive Matron's Pin: the full line of tiles from just past the boss,
    // through the marked tile, to the arena edge — the bible's own Perfect-
    // Dodge framing ("sidestepping a locked projectile") treats the whole
    // line as live, not just the originally-marked tile.
    private static List<(int X, int Z)> LineThrough(GameState state, (int X, int Z) from, (int X, int Z) through)
    {
        var tiles = new List<(int X, int Z)>();
        double dx = through.X - from.X, dz = through.Z - from.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.001) return tiles;
        dx /= len; dz /= len;
        for (int i = 0; i <= state.ArenaRadius * 3; i++)
        {
            var cur = (X: (int)Math.Round(through.X + dx * i), Z: (int)Math.Round(through.Z + dz * i));
            if (!state.InArena(cur)) break;
            if (!tiles.Contains(cur)) tiles.Add(cur);
        }
        return tiles;
    }

    private static (int X, int Z) NextStepToward(GameState state, (int X, int Z) from,
                                                 (int X, int Z) goal, (int X, int Z) avoid)
    {
        if (from == goal) return from;
        if (LineIsClear(state, from, goal, avoid))
            return BresenhamLine(from, goal)[1];
        var path = Bfs(state, from, goal, avoid);
        if (path is null || path.Count < 2) return from;
        var target = path[1];
        for (int i = path.Count - 1; i >= 1; i--)
            if (LineIsClear(state, from, path[i], avoid)) { target = path[i]; break; }
        return BresenhamLine(from, target)[1];
    }

    private static List<(int X, int Z)> BresenhamLine((int X, int Z) a, (int X, int Z) b)
    {
        var pts = new List<(int X, int Z)>();
        int x0 = a.X, z0 = a.Z;
        int dx = Math.Abs(b.X - x0), dz = Math.Abs(b.Z - z0);
        int sx = x0 < b.X ? 1 : -1, sz = z0 < b.Z ? 1 : -1;
        int err = dx - dz;
        while (true)
        {
            pts.Add((x0, z0));
            if (x0 == b.X && z0 == b.Z) break;
            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 < dx) { err += dx; z0 += sz; }
        }
        return pts;
    }

    private static bool LineIsClear(GameState state, (int X, int Z) from,
                                    (int X, int Z) to, (int X, int Z) avoid)
    {
        var line = BresenhamLine(from, to);
        for (int i = 1; i < line.Count; i++)
            if (!state.InArena(line[i]) || state.IsBlocked(line[i], avoid))
                return false;
        return true;
    }

    private static int Chebyshev((int X, int Z) a, (int X, int Z) b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));

    // A projectile's own motion is Euclidean (a real speed in tiles/tick),
    // so its spawn point should be the Euclidean-nearest footprint tile to
    // the caster, not the Chebyshev-nearest DistanceToNpc already uses for
    // range gating — the two can diverge on an off-axis approach to an
    // irregular footprint (moot for Maggot King's symmetric 2x2, but keeps
    // this correct for a future boss with an asymmetric one).
    private static (int X, int Z) NearestFootprintTileEuclidean(GameState state, (int X, int Z) from) =>
        state.NpcFootprintTiles().OrderBy(t => Math.Pow(from.X - t.X, 2) + Math.Pow(from.Z - t.Z, 2)).First();

    private static List<(int X, int Z)>? Bfs(GameState state, (int X, int Z) from,
                                             (int X, int Z) goal, (int X, int Z) avoid)
    {
        var came = new Dictionary<(int X, int Z), (int X, int Z)> { [from] = from };
        var q = new Queue<(int X, int Z)>();
        q.Enqueue(from);
        var best = from;
        int bestD = Chebyshev(from, goal);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == goal) { best = goal; break; }
            int d = Chebyshev(cur, goal);
            if (d < bestD) { bestD = d; best = cur; }
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var n = (X: cur.X + dx, Z: cur.Z + dz);
                    if (came.ContainsKey(n)) continue;
                    if (!state.InArena(n) || state.IsBlocked(n, avoid)) continue;
                    came[n] = cur;
                    q.Enqueue(n);
                }
        }
        var dest = came.ContainsKey(goal) ? goal : best;
        if (dest == from) return null;
        var path = new List<(int X, int Z)>();
        for (var t = dest; t != from; t = came[t]) path.Add(t);
        path.Add(from);
        path.Reverse();
        return path;
    }
}
