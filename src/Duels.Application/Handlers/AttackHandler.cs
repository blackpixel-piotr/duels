using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Application.Services;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class AttackHandler : ICommandHandler<AttackCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly IItemRepository _itemRepo;
    private readonly ICombatCalculator _combat;
    private readonly IEventBus _events;
    private readonly ItemUnlockService _lootService;

    public AttackHandler(
        IGameStateRepository stateRepo,
        IItemRepository itemRepo,
        ICombatCalculator combat,
        IEventBus events,
        ItemUnlockService lootService)
    {
        _stateRepo = stateRepo;
        _itemRepo = itemRepo;
        _combat = combat;
        _events = events;
        _lootService = lootService;
    }

    public async Task<CommandResult> HandleAsync(AttackCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");
        if (!state.InDuel) return CommandResult.Fail("You are not in a duel. Type !duel <npc> to start one.");

        var player = state.Player;
        var npc = state.ActiveNpc!;

        // --- Player attacks NPC ---
        var playerSnapshot = BuildPlayerSnapshot(player, command.Style);
        var npcSnapshot = BuildNpcSnapshot(npc);

        var playerRoll = _combat.Roll(playerSnapshot, npcSnapshot);

        if (playerRoll.Hit)
        {
            npc.TakeDamage(playerRoll.Damage);
            state.AppendLog($"You hit {npc.Template.Name} for {playerRoll.Damage} damage. [{npc.CurrentHp}/{npc.Template.Stats.Hitpoints * 10} HP]", LogEntryKind.PlayerHit);
            await _events.PublishAsync(new AttackLanded(player.Id, npc.Template.Id, playerRoll.Damage), ct);
            GainXp(player, command.Style, playerRoll.Damage, state, _events, ct);
        }
        else
        {
            state.AppendLog($"You miss {npc.Template.Name}.", LogEntryKind.PlayerMiss);
            await _events.PublishAsync(new AttackMissed(player.Id, npc.Template.Id), ct);
        }

        // --- Check NPC death ---
        if (!npc.IsAlive)
        {
            await HandleVictory(state, ct);
            await _stateRepo.SaveAsync(state, ct);
            return CommandResult.Ok();
        }

        // --- NPC retaliates ---
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

        // --- Check player death ---
        if (!player.IsAlive)
        {
            state.AppendLog($"You have been defeated by {npc.Template.Name}! You respawn at full health.", LogEntryKind.System);
            state.AppendLog($"═══ DUEL LOST ═══", LogEntryKind.System);
            player.RestoreHp();
            state.EndDuel();
            await _events.PublishAsync(new DuelLost(player.Id, npc.Template.Id, npc.Template.Name), ct);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }

    private async Task HandleVictory(GameState state, CancellationToken ct)
    {
        var player = state.Player;
        var npc = state.ActiveNpc!;

        state.AppendLog($"You have defeated {npc.Template.Name}!", LogEntryKind.System);
        state.AppendLog($"═══ DUEL WON ═══", LogEntryKind.System);

        player.AddGold(npc.Template.GoldReward);
        if (npc.Template.GoldReward > 0)
            state.AppendLog($"You receive {npc.Template.GoldReward} gold.", LogEntryKind.Loot);

        var drops = _lootService.RollDrops(npc.Template, player);
        foreach (var (itemId, itemName) in drops)
        {
            player.AddToInventory(itemId);
            state.AppendLog($"You receive: {itemName}!", LogEntryKind.Loot);
            await _events.PublishAsync(new ItemUnlocked(player.Id, itemId, itemName), ct);
        }

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

    private static void GainXp(Domain.Entities.Player player, AttackStyle style, int damage, GameState state, IEventBus events, CancellationToken ct)
    {
        int xp = damage * 4;
        int hpXp = damage * 133 / 100;

        int prevAttack = player.AttackLevel;
        int prevStrength = player.StrengthLevel;
        int prevDefence = player.DefenceLevel;
        int prevHp = player.HitpointsLevel;

        switch (style)
        {
            case AttackStyle.Accurate: player.GainAttackXp(xp); break;
            case AttackStyle.Aggressive: player.GainStrengthXp(xp); break;
            case AttackStyle.Defensive: player.GainDefenceXp(xp); break;
        }
        player.GainHitpointsXp(hpXp);

        CheckLevelUp(player, "Attack", prevAttack, player.AttackLevel, state, events, ct);
        CheckLevelUp(player, "Strength", prevStrength, player.StrengthLevel, state, events, ct);
        CheckLevelUp(player, "Defence", prevDefence, player.DefenceLevel, state, events, ct);
        CheckLevelUp(player, "Hitpoints", prevHp, player.HitpointsLevel, state, events, ct);
    }

    private static void CheckLevelUp(Domain.Entities.Player player, string skill, int prevLevel, int newLevel, GameState state, IEventBus events, CancellationToken ct)
    {
        if (newLevel > prevLevel)
        {
            state.AppendLog($"*** {skill} level up! You are now level {newLevel}. ***", LogEntryKind.LevelUp);
            _ = events.PublishAsync(new LevelUp(player.Id, skill, newLevel), ct);
        }
    }
}
