using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class EatItemHandler : ICommandHandler<EatItemCommand>
{
    private static readonly Dictionary<string, (int Heal, bool CanOverheal, bool FreeAction)> FoodData = new()
    {
        ["shark"]       = (20, false, false),
        ["karambwan"]   = (18, false, true),
        ["anglerfish"]  = (22, true,  false),
    };

    private readonly IGameStateRepository _stateRepo;
    private readonly ICombatCalculator _combat;
    private readonly IRandomProvider _random;

    public EatItemHandler(IGameStateRepository stateRepo, ICombatCalculator combat, IRandomProvider random)
    {
        _stateRepo = stateRepo;
        _combat = combat;
        _random = random;
    }

    public async Task<CommandResult> HandleAsync(EatItemCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var player = state.Player;

        if (!FoodData.TryGetValue(command.ItemId, out var food))
            return CommandResult.Fail($"'{command.ItemId}' is not food. Try: shark, karambwan, anglerfish.");

        if (!player.RemoveFromInventory(command.ItemId))
            return CommandResult.Fail($"You don't have any {command.ItemId}.");

        if (player.CurrentHp >= player.MaxHp && !food.CanOverheal)
        {
            player.AddToInventory(command.ItemId);
            return CommandResult.Fail("You are already at full HP.");
        }

        int before = player.CurrentHp;
        player.HealFood(food.Heal, food.CanOverheal);
        int healed = player.CurrentHp - before;
        state.AppendLog($"You eat the {command.ItemId}. HP: {before} → {player.CurrentHp}/{player.MaxHp} (+{healed})", LogEntryKind.Info);

        // NPC retaliates unless free action (karambwan) or not in duel
        if (!food.FreeAction && state.InDuel)
        {
            var npc = state.ActiveNpc!;
            state.TickVeng();
            var npcAtk = BuildNpcAttackSnapshot(npc);
            var playerDef = BuildPlayerDefSnapshot(player);
            var roll = _combat.Roll(npcAtk, playerDef);

            if (roll.Hit)
            {
                player.TakeDamage(roll.Damage);
                state.AppendLog($"{npc.Template.Name} hits you for {roll.Damage} while you eat. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);

                if (state.VengActive && roll.Damage > 0)
                {
                    int vengDmg = (int)(roll.Damage * 0.75);
                    npc.TakeDamage(vengDmg);
                    state.ConsumeVeng();
                    state.AppendLog($"VENGEANCE! {vengDmg} damage reflected!", LogEntryKind.Vengeance);
                }

                if (!player.IsAlive)
                {
                    state.AppendLog($"You have been defeated by {npc.Template.Name}! You respawn at full health.", LogEntryKind.System);
                    state.AppendLog("═══ DUEL LOST ═══", LogEntryKind.System);
                    state.ResetWinStreak();
                    if (state.InEndlessMode) { state.AppendLog($"Endless run over! Wave {state.EndlessWave}.", LogEntryKind.System); state.EndEndless(); }
                    player.RestoreHp();
                    state.EndDuel();
                    if (player.Gold == 0) state.AppendLog("You've been cleaned. Type 'beg' for emergency coin.", LogEntryKind.System);
                }
            }
            else
            {
                state.AppendLog($"{npc.Template.Name} misses while you eat.", LogEntryKind.NpcMiss);
            }
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }

    private static CombatantSnapshot BuildNpcAttackSnapshot(Domain.Entities.NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.Template.AttackType, AttackStyle.Aggressive);
    }

    private static CombatantSnapshot BuildPlayerDefSnapshot(Domain.Entities.Player player)
        => new(player.AttackLevel, player.StrengthLevel, player.DefenceLevel, Domain.ValueObjects.ItemModifiers.Zero, AttackType.Slash, AttackStyle.Defensive);
}
