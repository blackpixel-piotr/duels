using Duels.Application.Abstractions;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Services;

public sealed class GameTickService : IDisposable
{
    private static readonly string[] Ladder =
        ["swashbuckler", "barbarian", "desert_bandit", "gladiator", "corsair", "berserker", "warlord", "champion"];

    private readonly IGameStateRepository _states;
    private readonly ICombatCalculator _combat;
    private readonly IRandomProvider _random;
    private readonly IItemRepository _items;
    private readonly INpcRepository _npcs;
    private readonly IEventBus _events;

    private CancellationTokenSource? _cts;
    private Action? _notify;

    public GameTickService(
        IGameStateRepository states,
        ICombatCalculator combat,
        IRandomProvider random,
        IItemRepository items,
        INpcRepository npcs,
        IEventBus events)
    {
        _states = states;
        _combat = combat;
        _random = random;
        _items = items;
        _npcs = npcs;
        _events = events;
    }

    public void RegisterNotify(Action callback) => _notify = callback;

    public void Start(string playerId)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
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
            try { await Task.Delay(600, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            if (ct.IsCancellationRequested) break;
            await ProcessTick(playerId);
            _notify?.Invoke();
        }
    }

    private async Task ProcessTick(string playerId)
    {
        var state = await _states.GetAsync(playerId);
        if (state is null || !state.InDuel) return;

        var player = state.Player;
        var npc = state.ActiveNpc!;

        // Snapshot prayer state for flick detection (evaluated against tick-start)
        state.TickStartProtection = player.ActiveProtection;

        state.DecrementCooldowns();

        // Player's attack
        if (state.PlayerCooldown == 0)
        {
            var action = state.QueuedAction ?? "attack";
            await ExecutePlayerAction(state, player, npc, action);
            int speed = GetPlayerWeaponSpeed(player);
            state.ResetPlayerCooldown(speed);
            state.SetQueuedAction(null);

            // Revert to previous weapon after a one-shot weapon switch
            if (state.RevertWeaponId is { } revertId)
            {
                if (player.HasItem(revertId))
                {
                    player.Equip(revertId, EquipmentSlot.Weapon);
                    var revertName = _items.GetItemName(revertId) ?? revertId;
                    state.AppendLog($"You switch back to your {revertName}.", LogEntryKind.Info);
                }
                state.SetRevertWeapon(null);
            }
        }

        // Check if NPC died from player attack
        if (!npc.IsAlive)
        {
            await HandleVictory(state);
            await _states.SaveAsync(state);
            return;
        }

        // NPC's attack
        if (state.NpcCooldown == 0)
        {
            await NpcRetaliate(state, player, npc);
            state.ResetNpcCooldown(npc.Template.AttackSpeedTicks);
        }

        // Prayer drain at tick end (after all combat)
        // Drain only if prayer is still ON at tick end — allows prayer flicking
        if (player.ActiveProtection != ProtectionPrayer.None)
        {
            int before = player.PrayerPoints;
            player.DrainPrayer(1);
            if (player.PrayerPoints == 0 && before > 0)
                state.AppendLog("Your prayer has run out!", LogEntryKind.System);
        }
        if (player.PietyActive)
        {
            int before = player.PrayerPoints;
            player.DrainPrayer(1);
            if (player.PrayerPoints == 0 && before > 0)
                state.AppendLog("Your prayer has run out!", LogEntryKind.System);
        }

        // Damage-over-time — not prayer-reducible
        ApplyDots(state, player, npc);

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

        await _states.SaveAsync(state);
    }

    private static void ApplyDots(GameState state, Player player, NpcInstance npc)
    {
        if (state.BleedTicksLeft > 0 && player.IsAlive)
        {
            player.TakeDamage(state.BleedPerTick);
            state.AppendLog($"{state.BleedPerTick}:poison", LogEntryKind.HitsplatNpc);
            state.AppendLog($"You bleed for {state.BleedPerTick} damage. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            state.TickBleed();
        }

        if (player.IsAlive && state.TickPoison())
        {
            player.TakeDamage(3);
            state.AppendLog("3:poison", LogEntryKind.HitsplatNpc);
            state.AppendLog($"The poison courses through you. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
        }

        if (npc.IsAlive && npc.TickPoison())
        {
            npc.TakeDamage(2);
            state.AppendLog("2:poison", LogEntryKind.HitsplatPlayer);
            state.AppendLog($"The venom burns {npc.Template.Name}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.PlayerHit);
        }
    }

    private async Task ExecutePlayerAction(GameState state, Player player, NpcInstance npc, string action)
    {
        if (action == "spec")
        {
            PerformSpecialAttack(state, player, npc);
        }
        else
        {
            var playerSnapshot = BuildPlayerSnapshot(state, player.ChosenStyle);
            var npcSnapshot = BuildNpcSnapshot(npc);
            var roll = _combat.Roll(playerSnapshot, npcSnapshot);

            if (roll.Hit)
            {
                int damage = roll.Damage;

                if (npc.Template.Id == "warlord" && npc.WarlordPrayerActive)
                {
                    damage = (int)(damage * 0.25);
                    state.AppendLog($"⛉ {npc.Template.Name}'s prayer absorbs your strike. You hit {damage}. [{npc.CurrentHp - damage}/{npc.MaxHp} HP]", LogEntryKind.BossSpecial);
                    state.AppendLog($"{damage}:normal", LogEntryKind.HitsplatPlayer);
                }
                else
                {
                    int maxPossible = _combat.MaxHit(playerSnapshot);
                    bool isMax     = maxPossible > 0 && roll.Damage == maxPossible;
                    bool isTopTier = maxPossible > 0 && roll.Damage >= (int)(maxPossible * 0.80);
                    if (isMax)
                    {
                        state.AppendLog($"{player.PhatPrefix}MAXIMUM DAMAGE! {npc.Template.Name} is pulverized!", LogEntryKind.MaxHit);
                        state.AppendLog($"{damage}:heavy", LogEntryKind.HitsplatPlayer);
                    }
                    else if (isTopTier)
                    {
                        state.AppendLog($"{player.PhatPrefix}Critical Impact — {npc.Template.Name} staggers from the blow!", LogEntryKind.MaxHit);
                        state.AppendLog($"{damage}:heavy", LogEntryKind.HitsplatPlayer);
                    }
                    else
                    {
                        state.AppendLog($"{damage}:normal", LogEntryKind.HitsplatPlayer);
                    }
                }

                npc.TakeDamage(damage);
                AwardOffensiveXp(state, player, damage);

                // Venomous Fang: 20% chance to poison the NPC on a landed hit
                if (player.GetEquippedWeaponId() == "venomous_fang" && !npc.Poisoned && _random.NextDouble() < 0.20)
                {
                    npc.ApplyPoison();
                    state.AppendLog($"The Venomous Fang poisons {npc.Template.Name}!", LogEntryKind.System);
                }

                await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, damage));
            }
            else
            {
                state.AppendLog("0:miss", LogEntryKind.HitsplatPlayer);
                await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id));
            }
        }

        player.RechargeSpecial(10);
        player.TickCombatBoost();
    }

    private void PerformSpecialAttack(GameState state, Player player, NpcInstance npc)
    {
        var weaponId = player.GetEquippedWeaponId();
        var weapon = weaponId is not null ? _items.GetWeapon(weaponId) : null;
        var spec = weapon?.Special;

        if (spec is null)
        {
            state.AppendLog("No special attack — equip a weapon with a special.", LogEntryKind.System);
            return;
        }

        if (!player.DrainSpecialEnergy(spec.EnergyRequired))
        {
            state.AppendLog($"Not enough special energy ({player.SpecialEnergy}% / need {spec.EnergyRequired}%).", LogEntryKind.System);
            return;
        }

        var baseSnapshot    = BuildPlayerSnapshot(state, player.ChosenStyle);
        var boostedSnapshot = baseSnapshot with { AttackLevel = (int)(baseSnapshot.AttackLevel * spec.AccuracyMultiplier) };
        var npcSnapshot     = BuildNpcSnapshot(npc);

        bool warlordBlocking = npc.Template.Id == "warlord" && npc.WarlordPrayerActive;

        for (int i = 0; i < spec.Hits; i++)
        {
            bool forced = i == 1 && spec.SecondHitGuaranteed;
            var roll = forced
                ? new CombatRollResult(true, _random.Next(0, _combat.MaxHit(boostedSnapshot) + 1))
                : _combat.Roll(boostedSnapshot, npcSnapshot);

            string suffix = spec.Hits > 1 ? $" (hit {i + 1})" : "";
            if (roll.Hit)
            {
                int damage = (int)(roll.Damage * spec.DamageMultiplier);
                if (warlordBlocking) damage = (int)(damage * 0.25);

                npc.TakeDamage(damage);
                string healMsg = "";
                if (spec.HealOnHit)
                {
                    int healAmount = damage / 2;
                    player.Heal(healAmount);
                    healMsg = $" [healed {healAmount}]";
                }
                string blockMsg = warlordBlocking ? " (⛉ blocked)" : "";
                state.AppendLog($"{player.PhatPrefix}⚡ SPEC! You hit {npc.Template.Name} for {damage}{healMsg}{blockMsg}{suffix}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.SpecHit);
                state.AppendLog($"{damage}:spec", LogEntryKind.HitsplatPlayer);
                AwardOffensiveXp(state, player, damage);
            }
            else
            {
                state.AppendLog($"{player.PhatPrefix}⚡ SPEC! You miss {npc.Template.Name}{suffix}.", LogEntryKind.PlayerMiss);
                state.AppendLog("0:miss", LogEntryKind.HitsplatPlayer);
            }

            if (!npc.IsAlive) break;
        }
    }

    private async Task NpcRetaliate(GameState state, Player player, NpcInstance npc)
    {
        state.TickVeng();

        if (npc.PendingSpecial is { } pending)
        {
            await ExecuteTelegraphedAttack(state, player, npc, pending);
            npc.ConsumePendingSpecial();
        }
        else
        {
            var npcAtk    = BuildNpcAttackSnapshot(npc);
            var playerDef = BuildPlayerDefSnapshot(player);
            var roll = _combat.Roll(npcAtk, playerDef);

            if (roll.Hit)
            {
                int damage = roll.Damage;

                // Frenzied Berserker rage: +5% per 10 HP lost
                if (npc.Template.Id == "berserker")
                {
                    int hpLost = npc.MaxHp - npc.CurrentHp;
                    if (hpLost >= 10)
                    {
                        double rageMult = 1.0 + (hpLost / 10) * 0.05;
                        int raged = (int)(damage * rageMult);
                        if (raged > damage)
                        {
                            state.AppendLog($"🔥 BERSERKER RAGE! ({hpLost} HP lost, ×{rageMult:F2})", LogEntryKind.BossSpecial);
                            damage = raged;
                        }
                    }
                }

                // Protection prayer uses tick-start snapshot (enables flicking)
                double reduction = GetPrayerReduction(state, npc.CurrentAttackType);
                if (reduction > 0)
                    damage = (int)(damage * (1.0 - reduction));

                player.TakeDamage(damage);
                AwardDefensiveXp(state, player, damage);
                state.AppendLog($"{damage}:normal", LogEntryKind.HitsplatNpc);
                await _events.PublishAsync(new AttackLanded(npc.Template.Id, player.Id, damage));

                // Desert Bandit: 25% chance to poison on a landed hit
                if (npc.Template.Id == "desert_bandit" && !state.PlayerPoisoned && _random.NextDouble() < 0.25)
                {
                    state.ApplyPoison();
                    state.AppendLog("The bandit's blade was tipped with poison!", LogEntryKind.System);
                }

                // Pirate Corsair: drains player special energy on a landed hit
                if (npc.Template.Id == "corsair" && player.SpecialEnergy > 0)
                {
                    int drained = Math.Min(10, player.SpecialEnergy);
                    player.DrainSpecialEnergy(drained);
                    state.AppendLog($"The Corsair's strike saps your special energy! (-{drained}%)", LogEntryKind.System);
                }

                if (state.VengActive && damage > 0)
                {
                    int vengDmg = (int)(damage * 0.75);
                    npc.TakeDamage(vengDmg);
                    state.ConsumeVeng();
                    state.AppendLog($"VENGEANCE! {vengDmg} damage reflected!", LogEntryKind.Vengeance);
                }
            }
            else
            {
                state.AppendLog("0:miss", LogEntryKind.HitsplatNpc);
                await _events.PublishAsync(new AttackMissed(npc.Template.Id, player.Id));
            }
        }

        // Style rotation — telegraph the NEXT style so the player can pre-switch prayers
        if (npc.IsAlive && npc.AdvanceStyle())
            state.AppendLog(StyleWarning(npc), LogEntryKind.BossSpecial);

        // Warlord prayer flick every 3 rounds
        if (npc.Template.Id == "warlord")
        {
            if (npc.TickWarlordPrayer())
            {
                if (npc.WarlordPrayerActive)
                    state.AppendLog("⛉ The Warlord raises a protection prayer! Your attacks are heavily blocked!", LogEntryKind.BossSpecial);
                else
                    state.AppendLog("⚔ The Warlord's prayer falters — strike hard!", LogEntryKind.BossSpecial);
            }
        }

        // Champion phase shift at ≤50% HP
        if (npc.Template.Id == "champion" && npc.IsAlive && npc.CurrentHp <= npc.MaxHp / 2)
        {
            if (npc.UsePhaseShift())
            {
                player.ClearCombatBoost();
                var phaseMove = new NpcSpecialMove("★ THE CHAMPION ENTERS PHASE 2! Your combat boost fades!", 2.0, 8);
                npc.SetPendingSpecial(phaseMove);
                npc.AttacksPerStyleOverride = 2;
                state.AppendLog("★ THE CHAMPION ENTERS PHASE 2!", LogEntryKind.BossSpecial);
                state.AppendLog("Your combat potion boost fades. The Champion's style rotation quickens!", LogEntryKind.BossSpecial);
            }
        }

        npc.TickFight();
        if (npc.PendingSpecial is null && npc.SpecialCooldown == 0 && npc.Template.TelegraphedMove is { } move)
        {
            npc.SetPendingSpecial(move);
            state.AppendLog($"⚠ {move.WarningText}", LogEntryKind.BossSpecial);
        }

        // Check if veng killed NPC
        if (!npc.IsAlive && player.IsAlive)
        {
            await HandleVictory(state);
        }
    }

    private async Task ExecuteTelegraphedAttack(GameState state, Player player, NpcInstance npc, NpcSpecialMove spec)
    {
        var npcAtk = BuildNpcAttackSnapshot(npc);
        int maxHit = _combat.MaxHit(npcAtk);
        int rawDamage = (int)(maxHit * spec.DamageMultiplier);

        double reduction = GetPrayerReduction(state, npc.CurrentAttackType);
        int damage = reduction > 0 ? (int)(rawDamage * (1.0 - reduction)) : rawDamage;

        player.TakeDamage(damage);
        AwardDefensiveXp(state, player, damage);
        state.AppendLog($"★ {npc.Template.Name} unleashes their special attack for {damage} damage! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.BossSpecial);
        state.AppendLog($"{damage}:boss", LogEntryKind.HitsplatNpc);
        await _events.PublishAsync(new AttackLanded(npc.Template.Id, player.Id, damage));

        if (npc.Template.Id == "barbarian" && damage > 0)
        {
            state.ApplyBleed(4, 2);
            state.AppendLog("You are bleeding from the barbarian's blow!", LogEntryKind.System);
        }

        if (state.VengActive && damage > 0)
        {
            int vengDmg = (int)(damage * 0.75);
            npc.TakeDamage(vengDmg);
            state.ConsumeVeng();
            state.AppendLog($"VENGEANCE! {vengDmg} damage reflected!", LogEntryKind.Vengeance);
        }
    }

    private static string StyleWarning(NpcInstance npc) => npc.CurrentAttackType switch
    {
        AttackType.Ranged => $"⚠ {npc.Template.Name} nocks an arrow — RANGED attacks incoming!",
        AttackType.Magic  => $"⚠ {npc.Template.Name} begins channeling — MAGIC attacks incoming!",
        _                 => $"⚠ {npc.Template.Name} closes in — MELEE attacks incoming!",
    };

    // Offensive xp: 4×damage to the chosen style's skill, HP xp = damage×4/3
    private static void AwardOffensiveXp(GameState state, Player player, int damage)
    {
        if (damage <= 0) return;
        int xp = damage * 4;
        int hpXp = damage * 4 / 3;
        var ups = player.ChosenStyle switch
        {
            AttackStyle.Aggressive => player.GainXp(0, xp, 0, hpXp),
            AttackStyle.Defensive  => player.GainXp(0, 0, xp, hpXp),
            _                      => player.GainXp(xp, 0, 0, hpXp),
        };
        LogLevelUps(state, ups);
    }

    // Defensive xp: taking hits trains Defence — losses still pay
    private static void AwardDefensiveXp(GameState state, Player player, int damage)
    {
        if (damage <= 0) return;
        int xp = damage * 3 * (player.ChosenStyle == AttackStyle.Defensive ? 2 : 1);
        LogLevelUps(state, player.GainXp(0, 0, xp, 0));
    }

    private static void LogLevelUps(GameState state, IReadOnlyList<(string Skill, int NewLevel)> ups)
    {
        foreach (var (skill, level) in ups)
            state.AppendLog($"✨ Congratulations! Your {skill} level is now {level}!", LogEntryKind.LevelUp);
    }

    private static double GetPrayerReduction(GameState state, AttackType npcAttackType)
    {
        if (state.Player.PrayerPoints <= 0) return 0.0;
        return state.TickStartProtection switch
        {
            ProtectionPrayer.Melee => npcAttackType is AttackType.Stab or AttackType.Slash or AttackType.Crush ? 0.75 : 0.0,
            ProtectionPrayer.Range => npcAttackType == AttackType.Ranged ? 0.75 : 0.0,
            ProtectionPrayer.Magic => npcAttackType == AttackType.Magic ? 0.75 : 0.0,
            _ => 0.0,
        };
    }

    private async Task HandleVictory(GameState state)
    {
        var player = state.Player;
        var npc = state.ActiveNpc!;

        state.AppendLog($"You have defeated {npc.Template.Name}!", LogEntryKind.System);
        state.AppendLog("═══ DUEL WON ═══", LogEntryKind.System);

        // Bounty gold pays on every win (income floor); a wager's profit stacks on top.
        double prestigeBonus = player.PrestigeLevel >= 1 ? 1.05 : 1.0;
        double doubloonBonus = player.HasItem("lucky_doubloon") ? 1.05 : 1.0;
        int bounty = (int)(npc.Template.GoldReward * prestigeBonus * doubloonBonus);
        if (bounty > 0)
        {
            player.AddGold(bounty);
            state.AppendLog($"You receive a {bounty:N0}g bounty. (Total: {player.Gold:N0}g)", LogEntryKind.Loot);
        }

        int payout = bounty;
        if (state.CurrentWager > 0)
        {
            int wagerPayout = (int)(state.CurrentWager * 2 * state.WinStreakMultiplier);
            player.AddGold(wagerPayout);
            state.AppendLog($"Wager payout: {wagerPayout:N0}g. (Total: {player.Gold:N0}g)", LogEntryKind.Loot);
            state.SetWager(0);
            payout += wagerPayout;
        }

        state.IncrementWinStreak();
        if (state.WinStreak > 1)
            state.AppendLog($"Win streak: {state.WinStreak}! (×{state.WinStreakMultiplier:F1} multiplier)", LogEntryKind.System);

        RollLoot(state, player, npc.Template);

        if (npc.Template.Id == "champion")
        {
            state.SetCanPrestige();
            state.AppendLog("You have conquered the Duel Arena! Type 'prestige' to reset and earn a Partyhat.", LogEntryKind.System);
        }

        if (state.InEndlessMode)
        {
            int wave = state.NextEndlessWave();
            state.AppendLog($"Wave {wave - 1} cleared! Preparing wave {wave}...", LogEntryKind.System);
            var waveNpc = BuildEndlessNpc(wave);
            state.StartDuel(waveNpc);
            state.AppendLog($"═══ WAVE {wave} ═══", LogEntryKind.System);
            state.AppendLog($"A wave {wave} fighter appears ({waveNpc.MaxHp} HP)!", LogEntryKind.System);
            await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, payout));
            return;
        }

        int idx = Array.IndexOf(Ladder, npc.Template.Id);
        if (idx >= 0 && idx + 1 < Ladder.Length)
        {
            var nextId = Ladder[idx + 1];
            state.UnlockOpponent(nextId);
            var nextTemplate = _npcs.GetTemplate(nextId);
            var nextName = nextTemplate?.Name ?? nextId;
            state.AppendLog($"You have proven yourself — {nextName} now challenges you! (!duel {nextId})", LogEntryKind.System);
        }

        player.RestoreSpecialEnergy();
        state.EndDuel();
        await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, payout));
    }

    private void RollLoot(GameState state, Player player, NpcTemplate template)
    {
        foreach (var entry in template.LootTable)
        {
            if (entry.OnceOnly && player.HasItem(entry.ItemId)) continue;
            if (_random.NextDouble() >= entry.DropChance) continue;

            int qty = entry.MaxQty > entry.MinQty ? _random.Next(entry.MinQty, entry.MaxQty + 1) : entry.MinQty;
            bool isRareDrop = entry.DropChance <= 1.0 / 15.0;

            if (entry.ItemId == "gold")
            {
                player.AddGold(qty);
                state.AppendLog($"You find {qty:N0} gold on the body.", LogEntryKind.Loot);
                continue;
            }

            var itemName = _items.GetItemName(entry.ItemId) ?? entry.ItemId;

            for (int i = 0; i < qty; i++)
            {
                if (player.Inventory.Count >= 28)
                {
                    int fenceValue = _items.GetFenceValue(entry.ItemId);
                    player.AddGold(fenceValue);
                    state.AppendLog($"Your pack is full — you fence the {itemName} for {fenceValue:N0}g.", LogEntryKind.Loot);
                }
                else
                {
                    player.AddToInventory(entry.ItemId);
                }
            }

            if (isRareDrop)
            {
                state.AppendLog("═══ RARE DROP ═══", LogEntryKind.Loot);
                state.AppendLog($"{template.Name} drops a {itemName}{(qty > 1 ? $" ×{qty}" : "")} — it's yours!", LogEntryKind.Loot);
            }
            else
            {
                state.AppendLog($"You loot {(qty > 1 ? $"{qty}× " : "")}{itemName}.", LogEntryKind.Loot);
            }
        }
    }

    private async Task HandleDefeat(GameState state, Player player, NpcInstance npc)
    {
        state.AppendLog($"You have been defeated by {npc.Template.Name}! You respawn at full health.", LogEntryKind.System);
        state.AppendLog("═══ DUEL LOST ═══", LogEntryKind.System);

        state.ResetWinStreak();

        if (state.InEndlessMode)
        {
            state.AppendLog($"Endless run over! You reached wave {state.EndlessWave}. Best: {state.BestEndlessWave}.", LogEntryKind.System);
            state.EndEndless();
        }

        player.RestoreHp();
        state.EndDuel();

        if (player.Gold == 0)
            state.AppendLog("You've been cleaned. Type 'beg' for emergency coin, or 'duel goblin' to rebuild.", LogEntryKind.System);

        await _events.PublishAsync(new DuelLost(player.Id, npc.Template.Id, npc.Template.Name));
    }

    private int GetPlayerWeaponSpeed(Player player)
    {
        var weaponId = player.GetEquippedWeaponId();
        if (weaponId is null) return 4;
        var weapon = _items.GetWeapon(weaponId);
        return weapon?.AttackSpeed ?? 4;
    }

    private CombatantSnapshot BuildPlayerSnapshot(GameState state, AttackStyle style)
    {
        var player = state.Player;
        var mods = AggregatePlayerMods(player);
        var weaponId = player.GetEquippedWeaponId();
        var attackType = weaponId is not null
            ? (_items.GetWeapon(weaponId)?.AttackType ?? AttackType.Slash)
            : AttackType.Slash;

        int atk = player.CombatBoostRoundsLeft > 0 ? (int)(player.AttackLevel * 1.15) + 5 : player.AttackLevel;
        int str = player.CombatBoostRoundsLeft > 0 ? (int)(player.StrengthLevel * 1.15) + 5 : player.StrengthLevel;

        if (player.PietyActive && player.PrayerPoints > 0)
        {
            atk = (int)(atk * 1.20);
            str = (int)(str * 1.20);
        }

        return new CombatantSnapshot(atk, str, player.DefenceLevel, mods, attackType, style);
    }

    private CombatantSnapshot BuildPlayerDefSnapshot(Player player)
    {
        var mods = AggregatePlayerMods(player);
        return new CombatantSnapshot(player.AttackLevel, player.StrengthLevel, player.DefenceLevel, mods, AttackType.Slash, player.ChosenStyle);
    }

    private ItemModifiers AggregatePlayerMods(Player player)
    {
        var mods = ItemModifiers.Zero;
        foreach (var (_, itemId) in player.Equipped)
        {
            var gear = _items.GetGear(itemId);
            if (gear is not null) mods = mods.Add(gear.Modifiers);
        }
        return mods;
    }

    private static CombatantSnapshot BuildNpcSnapshot(NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.CurrentAttackType, AttackStyle.Accurate);
    }

    private static CombatantSnapshot BuildNpcAttackSnapshot(NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.CurrentAttackType, AttackStyle.Aggressive);
    }

    private NpcInstance BuildEndlessNpc(int wave)
    {
        int hp  = 50 + wave * 6;
        int mod = 20 + wave * 4;
        var style = (AttackType)_random.Next(0, 5);
        var mods = style switch
        {
            AttackType.Ranged => new ItemModifiers(RangedAttack: mod, StrengthBonus: mod),
            AttackType.Magic  => new ItemModifiers(MagicAttack: mod, StrengthBonus: mod),
            AttackType.Stab   => new ItemModifiers(StabAttack: mod, StrengthBonus: mod),
            AttackType.Crush  => new ItemModifiers(CrushAttack: mod, StrengthBonus: mod),
            _                 => new ItemModifiers(SlashAttack: mod, StrengthBonus: mod),
        };
        var template = new NpcTemplate(
            $"endless_w{wave}",
            $"Wave {wave} Fighter",
            $"A relentless wave {wave} challenger.",
            new CombatStats(99, 99, 99, hp),
            mods,
            style,
            [],
            goldReward: wave * 50,
            attackSpeedTicks: 4);
        return new NpcInstance(template);
    }
}
