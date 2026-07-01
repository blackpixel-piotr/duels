using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Entities;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class AttackHandler : ICommandHandler<AttackCommand>
{
    private static readonly string[] Ladder =
        ["swashbuckler", "barbarian", "desert_bandit", "gladiator", "corsair", "berserker", "warlord", "champion"];

    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;
    private readonly INpcRepository _npcRepo;
    private readonly ICombatCalculator _combat;
    private readonly IEventBus _events;
    private readonly IRandomProvider _random;

    public AttackHandler(
        IGameStateRepository stateRepo,
        IItemRepository itemRepo,
        INpcRepository npcRepo,
        ICombatCalculator combat,
        IEventBus events,
        IRandomProvider random)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
        _npcRepo = npcRepo;
        _combat = combat;
        _events = events;
        _random = random;
    }

    public async Task<CommandResult> HandleAsync(AttackCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (!state.InDuel) return CommandResult.Fail("You are not in a duel. Type !duel <npc> to start one.");

        state.ResetTurnState();

        var player = state.Player;
        var npc = state.ActiveNpc!;

        bool npcRetaliates;
        bool actionTaken;

        if (command.UseSpecial)
        {
            actionTaken   = PerformSpecialAttack(state, player, npc);
            npcRetaliates = actionTaken;
        }
        else
        {
            var playerSnapshot = BuildPlayerSnapshot(player, command.Style);
            var npcSnapshot = BuildNpcSnapshot(npc);
            var roll = _combat.Roll(playerSnapshot, npcSnapshot);

            if (roll.Hit)
            {
                int damage = roll.Damage;

                // Warlord prayer flick: blocks 75% of player melee damage
                if (npc.Template.Id == "warlord" && npc.WarlordPrayerActive)
                {
                    int blocked = damage;
                    damage = (int)(damage * 0.25);
                    state.AppendLog($"⛉ {npc.Template.Name}'s prayer absorbs your strike. You hit {damage}. [{npc.CurrentHp - damage}/{npc.MaxHp} HP]", LogEntryKind.BossSpecial);
                }
                else
                {
                    int maxPossible = _combat.MaxHit(playerSnapshot);
                    bool isMax     = maxPossible > 0 && roll.Damage == maxPossible;
                    bool isTopTier = maxPossible > 0 && roll.Damage >= (int)(maxPossible * 0.80);
                    var kind   = isTopTier ? LogEntryKind.MaxHit : LogEntryKind.PlayerHit;
                    string prefix = isMax ? "MAX HIT! " : isTopTier ? "HEAVY HIT! " : "";
                    state.AppendLog($"{player.PhatPrefix}{prefix}You hit {npc.Template.Name} for {damage} damage. [{npc.CurrentHp - damage}/{npc.MaxHp} HP]", kind);
                }

                npc.TakeDamage(damage);
                await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, damage), ct);
            }
            else
            {
                state.AppendLog($"{player.PhatPrefix}You miss {npc.Template.Name}.", LogEntryKind.PlayerMiss);
                await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id), ct);
            }

            npcRetaliates = true;
            actionTaken   = true;
        }

        if (actionTaken)
        {
            // Piety drains 2 prayer per action
            if (player.PietyActive)
            {
                int before = player.PrayerPoints;
                player.DrainPrayer(2);
                if (player.PrayerPoints == 0 && before > 0)
                    state.AppendLog("Your prayer has run out!", LogEntryKind.System);
            }

            player.RechargeSpecial(10);
            player.TickCombatBoost();
        }

        if (!npc.IsAlive)
        {
            await HandleVictory(state, ct);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Ok();
        }

        if (!npcRetaliates)
        {
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Ok();
        }

        await NpcRetaliate(state, player, npc, ct);

        if (!player.IsAlive)
        {
            await HandleDefeat(state, player, npc, ct);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }

    private async Task NpcRetaliate(GameState state, Domain.Entities.Player player, Domain.Entities.NpcInstance npc, CancellationToken ct)
    {
        state.TickVeng();

        // Fire telegraphed special if pending from last turn
        if (npc.PendingSpecial is { } pending)
        {
            await ExecuteTelegraphedAttack(state, player, npc, pending, ct);
            npc.ConsumePendingSpecial();
        }
        else
        {
            // Normal NPC attack
            var npcAttackSnapshot = BuildNpcAttackSnapshot(npc);
            var playerDefSnapshot = BuildPlayerDefSnapshot(player);
            var npcRoll = _combat.Roll(npcAttackSnapshot, playerDefSnapshot);

            if (npcRoll.Hit)
            {
                int damage = npcRoll.Damage;

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

                // Protection prayer: 75% reduction if active and matches attack type
                double reduction = GetPrayerReduction(player, npc.Template.AttackType);
                if (reduction > 0)
                    damage = (int)(damage * (1.0 - reduction));

                player.TakeDamage(damage);
                state.AppendLog($"{npc.Template.Name} hits you for {damage} damage. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
                await _events.PublishAsync(new AttackLanded(npc.Template.Id, player.Id, damage), ct);

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
                state.AppendLog($"{npc.Template.Name} misses.", LogEntryKind.NpcMiss);
                await _events.PublishAsync(new AttackMissed(npc.Template.Id, player.Id), ct);
            }
        }

        // Drain protection prayer (1 per round it's active)
        if (player.ActiveProtection != ProtectionPrayer.None)
        {
            int before = player.PrayerPoints;
            player.DrainPrayer(1);
            if (player.PrayerPoints == 0 && before > 0)
                state.AppendLog("Your prayer has run out!", LogEntryKind.System);
        }

        // Boss: Warlord prayer flick every 3 rounds
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

        // Boss: Champion phase shift at ≤50% HP
        if (npc.Template.Id == "champion" && npc.IsAlive && npc.CurrentHp <= npc.MaxHp / 2)
        {
            if (npc.UsePhaseShift())
            {
                player.ClearCombatBoost();
                var phaseMove = new NpcSpecialMove(
                    "★ THE CHAMPION ENTERS PHASE 2! Your combat boost fades!", 2.0, 8);
                npc.SetPendingSpecial(phaseMove);
                state.AppendLog("★ THE CHAMPION ENTERS PHASE 2!", LogEntryKind.BossSpecial);
                state.AppendLog("Your combat potion boost fades. Brace for the ultimate strike!", LogEntryKind.BossSpecial);
            }
        }

        // Tick fight counter and possibly telegraph next special
        npc.TickFight();
        if (npc.PendingSpecial is null && npc.SpecialCooldown == 0 && npc.Template.TelegraphedMove is { } move)
        {
            npc.SetPendingSpecial(move);
            state.AppendLog($"⚠ {move.WarningText}", LogEntryKind.BossSpecial);
        }

        // Check if vengeance killed NPC
        if (!npc.IsAlive && player.IsAlive)
        {
            await HandleVictory(state, ct);
        }
    }

    private async Task ExecuteTelegraphedAttack(
        GameState state, Domain.Entities.Player player, Domain.Entities.NpcInstance npc,
        NpcSpecialMove spec, CancellationToken ct)
    {
        var npcAtk = BuildNpcAttackSnapshot(npc);
        int maxHit = _combat.MaxHit(npcAtk);
        int rawDamage = (int)(maxHit * spec.DamageMultiplier);

        double reduction = GetPrayerReduction(player, npc.Template.AttackType);
        int damage = reduction > 0 ? (int)(rawDamage * (1.0 - reduction)) : rawDamage;

        player.TakeDamage(damage);
        state.AppendLog($"★ {npc.Template.Name} unleashes their special attack for {damage} damage! [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.BossSpecial);
        await _events.PublishAsync(new AttackLanded(npc.Template.Id, player.Id, damage), ct);

        if (state.VengActive && damage > 0)
        {
            int vengDmg = (int)(damage * 0.75);
            npc.TakeDamage(vengDmg);
            state.ConsumeVeng();
            state.AppendLog($"VENGEANCE! {vengDmg} damage reflected!", LogEntryKind.Vengeance);
        }
    }

    private static double GetPrayerReduction(Domain.Entities.Player player, AttackType npcAttackType)
    {
        if (player.PrayerPoints <= 0) return 0.0;
        return player.ActiveProtection switch
        {
            ProtectionPrayer.Melee => npcAttackType is AttackType.Stab or AttackType.Slash or AttackType.Crush ? 0.75 : 0.0,
            ProtectionPrayer.Range => 0.0, // no ranged NPCs yet
            ProtectionPrayer.Magic => 0.0, // no magic NPCs yet
            _ => 0.0,
        };
    }

    private async Task HandleDefeat(GameState state, Domain.Entities.Player player, Domain.Entities.NpcInstance npc, CancellationToken ct)
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

        await _events.PublishAsync(new DuelLost(player.Id, npc.Template.Id, npc.Template.Name), ct);
    }

    private bool PerformSpecialAttack(GameState state, Domain.Entities.Player player, Domain.Entities.NpcInstance npc)
    {
        var weaponId = player.GetEquippedWeaponId();
        var weapon = weaponId is not null ? _itemRepo.GetWeapon(weaponId) : null;
        var spec = weapon?.Special;

        if (spec is null)
        {
            state.AppendLog("No special attack — equip a weapon with a special.", LogEntryKind.System);
            return false;
        }

        if (!player.DrainSpecialEnergy(spec.EnergyRequired))
        {
            state.AppendLog($"Not enough special energy ({player.SpecialEnergy}% / need {spec.EnergyRequired}%).", LogEntryKind.System);
            return false;
        }

        var baseSnapshot = BuildPlayerSnapshot(player, AttackStyle.Accurate);
        var boostedSnapshot = baseSnapshot with { AttackLevel = (int)(baseSnapshot.AttackLevel * spec.AccuracyMultiplier) };
        var npcSnapshot = BuildNpcSnapshot(npc);

        // Warlord prayer reduces special attack damage too
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
            }
            else
            {
                state.AppendLog($"{player.PhatPrefix}⚡ SPEC! You miss {npc.Template.Name}{suffix}.", LogEntryKind.PlayerMiss);
            }

            if (!npc.IsAlive) break;
        }

        return true;
    }

    private async Task HandleVictory(GameState state, CancellationToken ct)
    {
        var player = state.Player;
        var npc = state.ActiveNpc!;

        state.AppendLog($"You have defeated {npc.Template.Name}!", LogEntryKind.System);
        state.AppendLog("═══ DUEL WON ═══", LogEntryKind.System);

        int payout;
        if (state.CurrentWager > 0)
        {
            payout = (int)(state.CurrentWager * 2 * state.WinStreakMultiplier);
            state.SetWager(0);
        }
        else
        {
            double prestigeBonus = player.PrestigeLevel >= 1 ? 1.05 : 1.0;
            payout = (int)(npc.Template.GoldReward * prestigeBonus);
        }

        if (payout > 0)
        {
            player.AddGold(payout);
            state.AppendLog($"You receive {payout:N0} gold. (Total: {player.Gold:N0}g)", LogEntryKind.Loot);
        }

        state.IncrementWinStreak();
        if (state.WinStreak > 1)
            state.AppendLog($"Win streak: {state.WinStreak}! (×{state.WinStreakMultiplier:F1} multiplier)", LogEntryKind.System);

        if (npc.Template.Id == "rare_gladiator")
        {
            player.AddToInventory("corrupted_whip");
            state.AppendLog("The Corrupted Gladiator drops a Corrupted Whip!", LogEntryKind.Loot);
        }

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
            state.StartDuel(waveNpc); // also restores prayer
            state.AppendLog($"═══ WAVE {wave} ═══", LogEntryKind.System);
            state.AppendLog($"A wave {wave} fighter appears ({waveNpc.MaxHp} HP)!", LogEntryKind.System);
            state.AppendLog("Type !attack to fight.", LogEntryKind.System);
            await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, payout), ct);
            return;
        }

        int idx = Array.IndexOf(Ladder, npc.Template.Id);
        if (idx >= 0 && idx + 1 < Ladder.Length)
        {
            var nextId = Ladder[idx + 1];
            state.UnlockOpponent(nextId);
            var nextTemplate = _npcRepo.GetTemplate(nextId);
            var nextName = nextTemplate?.Name ?? nextId;
            state.AppendLog($"You have proven yourself — {nextName} now challenges you! (!duel {nextId})", LogEntryKind.System);
        }

        player.RestoreSpecialEnergy();
        state.EndDuel();
        await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, payout), ct);
    }

    private static NpcInstance BuildEndlessNpc(int wave)
    {
        int hp = 50 + wave * 6;
        int mod = 20 + wave * 4;
        var template = new NpcTemplate(
            $"endless_w{wave}",
            $"Wave {wave} Fighter",
            $"A relentless wave {wave} challenger.",
            new CombatStats(99, 99, 99, hp),
            new ItemModifiers(SlashAttack: mod, StrengthBonus: mod),
            AttackType.Slash,
            [],
            goldReward: wave * 50);
        return new NpcInstance(template);
    }

    private CombatantSnapshot BuildPlayerSnapshot(Domain.Entities.Player player, AttackStyle style)
    {
        var mods = AggregatePlayerMods(player);
        var weaponId = player.GetEquippedWeaponId();
        var attackType = weaponId is not null
            ? (_itemRepo.GetWeapon(weaponId)?.AttackType ?? AttackType.Slash)
            : AttackType.Slash;

        int atk = player.CombatBoostRoundsLeft > 0 ? (int)(player.AttackLevel * 1.15) + 5 : player.AttackLevel;
        int str = player.CombatBoostRoundsLeft > 0 ? (int)(player.StrengthLevel * 1.15) + 5 : player.StrengthLevel;

        // Piety: +20% to effective attack and strength
        if (player.PietyActive && player.PrayerPoints > 0)
        {
            atk = (int)(atk * 1.20);
            str = (int)(str * 1.20);
        }

        return new CombatantSnapshot(atk, str, player.DefenceLevel, mods, attackType, style);
    }

    private CombatantSnapshot BuildPlayerDefSnapshot(Domain.Entities.Player player)
    {
        var mods = AggregatePlayerMods(player);
        return new CombatantSnapshot(player.AttackLevel, player.StrengthLevel, player.DefenceLevel, mods, AttackType.Slash, AttackStyle.Defensive);
    }

    private ItemModifiers AggregatePlayerMods(Domain.Entities.Player player)
    {
        var mods = ItemModifiers.Zero;
        foreach (var (_, itemId) in player.Equipped)
        {
            var gear = _itemRepo.GetGear(itemId);
            if (gear is not null) mods = mods.Add(gear.Modifiers);
        }
        return mods;
    }

    private static CombatantSnapshot BuildNpcSnapshot(Domain.Entities.NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.Template.AttackType, AttackStyle.Accurate);
    }

    private static CombatantSnapshot BuildNpcAttackSnapshot(Domain.Entities.NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.Template.AttackType, AttackStyle.Aggressive);
    }
}
