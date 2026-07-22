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

    private CancellationTokenSource? _cts;
    private Action? _notify;

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

        state.TickStartProtection = player.ActiveProtection;
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

        ProcessPlayerMovement(state, player);
        ProcessNpcMovement(state, npc);
        // Persistent target lock (M1 revision): moving on a tick simply
        // defers the attack — it's never cancelled, just delayed to the
        // next tick the player is truly stationary. preTickPlayerTile is
        // already captured above (for hazard Perfect-Dodge), so this is
        // free to reuse rather than a second snapshot.
        bool playerMovedThisTick = state.PlayerTile != preTickPlayerTile;

        int playerRange = GetPlayerWeaponRange(player);
        bool targetInRange = state.CurrentTargetAdd is { } targetAdd
            ? Chebyshev(state.PlayerTile, targetAdd.Tile) <= playerRange
            : state.InAttackRange(playerRange);

        if (state.PlayerCooldown == 0 && state.PlayerMoveTarget is null && !playerMovedThisTick && state.Engaged && targetInRange)
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
            ProcessHazardResolution(state, player, preTickPlayerTile, erupting);

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
        if (npc.IsAlive) npc.TickSap();

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
    // Stationary=true, so this is dormant in practice today — kept for
    // future non-stationary bosses/mobs and exercised by the movement tests.
    private static void ProcessNpcMovement(GameState state, NpcInstance npc)
    {
        if (state.EnemyFrozen || state.NpcStationary) return;
        int npcRange = npc.Template.DummyStyle is { } st ? AttackRange.ForStyle(st) : AttackRange.Melee;
        if (state.InAttackRange(npcRange)) return;

        var step = NextStepToward(state, state.NpcTile, ApproachSlot(state.NpcTile, state.PlayerTile), state.PlayerTile);
        state.SetNpcTile(step.X, step.Z);
    }

    private void ProcessAdds(GameState state)
    {
        foreach (var add in state.Adds)
        {
            if (!add.IsAlive) continue;

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

    // ── Player offense ──────────────────────────────────────────────────

    private async Task ExecutePlayerAction(GameState state, Player player, NpcInstance npc, string action)
    {
        if (action == "spec")
        {
            PerformSpecialAttack(state, player, npc);
        }
        else if (state.CurrentTargetAdd is { } add)
        {
            ExecuteBasicAttackOnAdd(state, add);
        }
        else
        {
            await ExecuteBasicAttackOnBoss(state, player, npc);
        }
    }

    private async Task ExecuteBasicAttackOnBoss(GameState state, Player player, NpcInstance npc)
    {
        var weapon = GetPlayerWeapon(player);
        var attacker = BuildAttackerProfile(player, weapon);
        // Accuracy is rolled vs the boss's per-style Evasion for the doctrine
        // this weapon attacks with (items doc §1) — neutral (0) for Maggot
        // King, the future "favors ranged" lever for other bosses.
        var doctrine = weapon?.AttackType ?? AttackType.Crush;
        var roll = _damage.Roll(attacker, new DefenderProfile(0, false, npc.EvasionFor(doctrine)));

        if (!roll.Hit)
        {
            state.AppendLog("0:miss", LogEntryKind.HitsplatPlayer);
            await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id));
            return;
        }

        bool punished = npc.InPunishWindow;
        int damage = punished ? (int)Math.Round(roll.Damage * 1.25) : roll.Damage;
        npc.TakeDamage(damage);
        // A max-hit (rolled the weapon's 2×Power ceiling) gets its own hitsplat
        // tier + MaxHit log kind (the latter already fires a screen shake) so it
        // reads distinctly from an ordinary hit — items doc §1's "distinct
        // max-hit visual."
        string tier = roll.MaxHit ? "max" : "normal";
        state.AppendLog($"{damage}:{tier}", LogEntryKind.HitsplatPlayer);
        string punishMsg = punished ? " (punish window!)" : "";
        string maxMsg = roll.MaxHit ? " — MAX HIT!" : "";
        state.AppendLog($"You hit {npc.Template.Name} for {damage}{punishMsg}{maxMsg}. [{npc.CurrentHp}/{npc.MaxHp} HP]",
            roll.MaxHit ? LogEntryKind.MaxHit : LogEntryKind.PlayerHit);
        await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, damage));
    }

    private static void ExecuteBasicAttackOnAdd(GameState state, AddInstance add)
    {
        // Adds are fodder (Boss Bible: "dies to 2 hits") — every landed hit
        // does at least 1 damage regardless of weapon roll.
        add.TakeDamage(1);
        state.AppendLog("1:normal", LogEntryKind.HitsplatPlayer);
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
        state.SetPlayerTile(adjacent.X, adjacent.Z);
        // Renderer interpolation layer: this is a genuine teleport (closes
        // the gap instantly, not a walked step) — mark it so the on-screen
        // position snaps instead of smoothly lerping across the gap.
        state.AppendLog("lunge", LogEntryKind.PlayerTeleport);
        ExecuteSpecialHit(state, player, npc, weapon, "Lunge");
    }

    private void ExecuteSpecialHit(GameState state, Player player, NpcInstance npc, Weapon weapon, string name,
        double damageMult = 1.0, int burnTicks = 0, int burnPerTick = 0, Action? onHit = null)
    {
        var attacker = BuildAttackerProfile(player, weapon);
        var roll = _damage.Roll(attacker, new DefenderProfile(0, false, npc.EvasionFor(weapon.AttackType)));

        if (!roll.Hit)
        {
            state.AppendLog($"⚡ SPEC! You miss {npc.Template.Name} with {name}.", LogEntryKind.PlayerMiss);
            state.AppendLog("0:miss", LogEntryKind.HitsplatPlayer);
            return;
        }

        bool punished = npc.InPunishWindow;
        int damage = (int)Math.Round(roll.Damage * damageMult * (punished ? 1.25 : 1.0));
        npc.TakeDamage(damage);
        state.AppendLog($"⚡ SPEC! {name} hits {npc.Template.Name} for {damage}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.SpecHit);
        state.AppendLog($"{damage}:spec:{weapon.Id}", LogEntryKind.HitsplatPlayer);

        if (burnTicks > 0) state.ApplyBleed(burnTicks, burnPerTick);
        onHit?.Invoke();
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
            state.AppendLog("★ The Maggot King convulses — the assault frenzies! Phase 2 begins.", LogEntryKind.BossSpecial);
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
    private static readonly (int X, int Z)[] _swarmCorners =
    {
        (-GameState.ArenaRadius, GameState.ArenaRadius),
        (GameState.ArenaRadius, GameState.ArenaRadius),
    };
    private void SpawnSwarmsUpTo(GameState state, BossPhaseDef phase, int target)
    {
        int alive = state.Adds.Count(a => a.IsAlive);
        int toSpawn = target - alive;
        if (toSpawn <= 0) return;
        for (int i = 0; i < toSpawn; i++)
            state.SpawnAdd(new AddInstance($"swarm_{state.FightTicks}_{alive + i}", _swarmCorners[(alive + i) % _swarmCorners.Length], phase.SwarmHp));
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

        var attackId = ResolveAttackId(state, step.Action);
        var attack = npc.Template.Script!.Attacks[attackId];

        // Dev toggle (M1 playtest tooling): boss autos off → the King telegraphs
        // but lands no direct attack, so a hazard interaction can be isolated.
        if (!state.IsMechanicEnabled(BossMechanic.BossAutos)) return;

        // Global Combat Grammar "impact-resolution prayer": ranged/magic
        // attacks travel as a homing doctrine-colored projectile — damage
        // (and the protection-prayer check) lands on the tick it actually
        // arrives, not this cast tick. Melee has no travel time and still
        // resolves here, instantly, exactly as before.
        if (attack.Style is AttackType.Ranged or AttackType.Magic)
            SpawnProjectileAttack(state, attack, source: "ResolveRotationStep(P1)");
        else
            ResolveBossAttack(state, player, npc, attack);
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
        state.AppendLog(StyleToken(attack.Style), LogEntryKind.BossCast);
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

    private string ForecastMessage(NpcInstance npc, string action)
    {
        if (action.Contains('/'))
            return "The Maggot King's mandibles glow amber — melee or ranged incoming, mind your spacing!";
        var atk = npc.Template.Script!.Attacks[action];
        return $"The Maggot King's mandibles glow — {StyleName(atk.Style)} incoming!";
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
        state.AppendLog($"{damage}:{tier}:{StyleToken(attack.Style)}", LogEntryKind.HitsplatNpc);
        string prayedMsg = prayerReduction > 0 ? " (prayed)" : "";
        state.AppendLog($"{npc.Template.Name} uses {attack.Name} for {damage}{prayedMsg}. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
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
                if (GameState.InArena(t) && !state.IsObstacle(t)) candidates.Add(t);
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
            state.AppendLog($"{dmg}:hazard", LogEntryKind.HitsplatNpc);
            state.AppendLog($"The ground ERUPTS beneath you for {dmg}! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            if (!state.PlayerPoisoned)
            {
                state.ApplyPoison();
                state.AppendLog("The writhing mass poisons you!", LogEntryKind.System);
            }
        }
        else if (player.IsAlive && state.IsPool(state.PlayerTile) && state.IsMechanicEnabled(BossMechanic.Pools))
        {
            int dmg = ResolveIncomingDamage(state, player, npc, e.PoolDamagePerTick, style: null, unprayable: true);
            player.TakeDamage(dmg);
            state.RecordDamageTaken(dmg);
            if (dmg > 0) state.SetKilledBy("Poison pool (unprayable)");
            state.AppendLog($"{dmg}:poison", LogEntryKind.HitsplatNpc);
            state.AppendLog($"Acrid slime burns at your feet. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }

        // Perfect Dodge (m1-plan Workstream C.8): stood on a tile that was
        // erupting THIS tick at tick-start, vacated it, and wasn't caught by
        // any other eruption this tick.
        if (erupting.Contains(preTickPlayerTile) && preTickPlayerTile != state.PlayerTile && !erupted.Contains(state.PlayerTile))
        {
            player.RechargeSpecial(15, MaxSpecialEnergy(player));
            state.AppendLog("✦ PERFECT DODGE! +15 special energy.", LogEntryKind.System);
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
            state.AppendLog($"{dmg}:hazard", LogEntryKind.HitsplatNpc);
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

            var corners = new (int X, int Z)[]
            {
                (-GameState.ArenaRadius, GameState.ArenaRadius),
                (GameState.ArenaRadius, GameState.ArenaRadius),
            };
            for (int i = 0; i < wave.Count; i++)
                state.SpawnAdd(new AddInstance($"swarm_{wave.ThresholdPercent}_{i}", corners[i % corners.Length], wave.Hp));

            state.AppendLog($"⚠ Maggot swarms erupt from the corners! ({wave.Count})", LogEntryKind.BossSpecial);
        }
    }

    // ── Damage-over-time ─────────────────────────────────────────────────

    private static void ApplyDots(GameState state, Player player, NpcInstance npc)
    {
        if (!state.IsMechanicEnabled(BossMechanic.Dots)) return; // dev toggle

        if (state.BleedTicksLeft > 0 && player.IsAlive)
        {
            player.TakeDamage(state.BleedPerTick);
            state.RecordDamageTaken(state.BleedPerTick);
            if (state.BleedPerTick > 0) state.SetKilledBy("Bleed");
            state.AppendLog($"{state.BleedPerTick}:poison", LogEntryKind.HitsplatNpc);
            state.AppendLog($"You bleed for {state.BleedPerTick} damage. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            state.TickBleed();
        }

        if (player.IsAlive && state.TickPoison())
        {
            player.TakeDamage(3);
            state.RecordDamageTaken(3);
            state.SetKilledBy("Poison");
            state.AppendLog("3:poison", LogEntryKind.HitsplatNpc);
            state.AppendLog($"The poison courses through you. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }
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

    private List<string> RollLoot(GameState state, Player player, NpcTemplate template)
    {
        var lootedItems = new List<string>();
        foreach (var entry in template.LootTable)
        {
            if (entry.OnceOnly && player.HasItem(entry.ItemId)) continue;
            if (_random.NextDouble() >= entry.DropChance) continue;

            int qty = entry.MaxQty > entry.MinQty ? _random.Next(entry.MinQty, entry.MaxQty + 1) : entry.MinQty;
            var itemName = _items.GetItemName(entry.ItemId) ?? entry.ItemId;

            for (int i = 0; i < qty; i++)
            {
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
                }
            }
            state.AppendLog($"You loot {(qty > 1 ? $"{qty}× " : "")}{itemName}.", LogEntryKind.Loot);
        }
        return lootedItems;
    }

    private async Task HandleDefeat(GameState state, Player player, NpcInstance npc)
    {
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
            if (!GameState.InArena(line[i]) || state.IsBlocked(line[i], avoid))
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
                    if (!GameState.InArena(n) || state.IsBlocked(n, avoid)) continue;
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
