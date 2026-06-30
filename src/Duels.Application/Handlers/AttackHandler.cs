using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
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

        var player = state.Player;
        var npc = state.ActiveNpc!;

        bool npcRetaliates;

        if (command.UseSpecial)
        {
            npcRetaliates = PerformSpecialAttack(state, player, npc);
        }
        else
        {
            var playerSnapshot = BuildPlayerSnapshot(player, command.Style);
            var npcSnapshot = BuildNpcSnapshot(npc);
            var roll = _combat.Roll(playerSnapshot, npcSnapshot);

            if (roll.Hit)
            {
                npc.TakeDamage(roll.Damage);
                state.AppendLog($"You hit {npc.Template.Name} for {roll.Damage} damage. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.PlayerHit);
                await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, roll.Damage), ct);
            }
            else
            {
                state.AppendLog($"You miss {npc.Template.Name}.", LogEntryKind.PlayerMiss);
                await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id), ct);
            }

            npcRetaliates = true;
        }

        player.RechargeSpecial(10);

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

        // NPC retaliates
        var npcAttackSnapshot = BuildNpcAttackSnapshot(npc);
        var playerDefSnapshot = BuildPlayerDefSnapshot(player);
        var npcRoll = _combat.Roll(npcAttackSnapshot, playerDefSnapshot);

        if (npcRoll.Hit)
        {
            player.TakeDamage(npcRoll.Damage);
            state.AppendLog($"{npc.Template.Name} hits you for {npcRoll.Damage} damage. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);
            await _events.PublishAsync(new AttackLanded(npc.Template.Id, player.Id, npcRoll.Damage), ct);
        }
        else
        {
            state.AppendLog($"{npc.Template.Name} misses.", LogEntryKind.NpcMiss);
            await _events.PublishAsync(new AttackMissed(npc.Template.Id, player.Id), ct);
        }

        if (!player.IsAlive)
        {
            state.AppendLog($"You have been defeated by {npc.Template.Name}! You respawn at full health.", LogEntryKind.System);
            state.AppendLog("═══ DUEL LOST ═══", LogEntryKind.System);
            player.RestoreHp();
            state.EndDuel();
            await _events.PublishAsync(new DuelLost(player.Id, npc.Template.Id, npc.Template.Name), ct);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
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
                npc.TakeDamage(damage);
                string healMsg = "";
                if (spec.HealOnHit)
                {
                    int healAmount = damage / 2;
                    player.Heal(healAmount);
                    healMsg = $" [healed {healAmount}]";
                }
                state.AppendLog($"Special! You hit {npc.Template.Name} for {damage}{healMsg}{suffix}. [{npc.CurrentHp}/{npc.MaxHp} HP]", LogEntryKind.PlayerHit);
            }
            else
            {
                state.AppendLog($"Special! You miss {npc.Template.Name}{suffix}.", LogEntryKind.PlayerMiss);
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

        player.AddGold(npc.Template.GoldReward);
        if (npc.Template.GoldReward > 0)
            state.AppendLog($"You receive {npc.Template.GoldReward} gold. (Total: {player.Gold}g)", LogEntryKind.Loot);

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
        await _events.PublishAsync(new DuelWon(player.Id, npc.Template.Id, npc.Template.Name, npc.Template.GoldReward), ct);
    }

    private CombatantSnapshot BuildPlayerSnapshot(Domain.Entities.Player player, AttackStyle style)
    {
        var mods = AggregatePlayerMods(player);
        var weaponId = player.GetEquippedWeaponId();
        var attackType = weaponId is not null
            ? (_itemRepo.GetWeapon(weaponId)?.AttackType ?? AttackType.Slash)
            : AttackType.Slash;

        return new CombatantSnapshot(player.AttackLevel, player.StrengthLevel, player.DefenceLevel, mods, attackType, style);
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
