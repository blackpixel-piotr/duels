using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;
using Duels.Domain.Events;
using Duels.Domain.Interfaces;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Handlers;

public sealed class VengeanceHandler : ICommandHandler<VengeanceCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly ICombatCalculator _combat;
    private readonly IRandomProvider _random;

    public VengeanceHandler(IGameStateRepository stateRepo, ICombatCalculator combat, IRandomProvider random)
    {
        _stateRepo = stateRepo;
        _combat = combat;
        _random = random;
    }

    public async Task<CommandResult> HandleAsync(VengeanceCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        if (state.VengActive)
            return CommandResult.Fail("Vengeance is already active — waiting to proc.");

        if (state.VengCooldownRounds > 0)
            return CommandResult.Fail($"Vengeance is on cooldown ({state.VengCooldownRounds} rounds remaining).");

        state.ResetTurnState();
        state.ActivateVeng();
        state.AppendLog("Vengeance is ready! The next hit you take will be reflected for 75%.", LogEntryKind.Info);

        // Casting veng is an Action — NPC retaliates
        if (state.InDuel)
        {
            var npc = state.ActiveNpc!;
            var player = state.Player;

            state.TickVeng();

            var npcAtk = BuildNpcAttackSnapshot(npc);
            var playerDef = BuildPlayerDefSnapshot(player);
            var roll = _combat.Roll(npcAtk, playerDef);

            if (roll.Hit)
            {
                int damage = roll.Damage;

                // Prayer reduction
                double reduction = GetPrayerReduction(player, npc.Template.AttackType);
                if (reduction > 0)
                    damage = (int)(damage * (1.0 - reduction));

                player.TakeDamage(damage);
                state.AppendLog($"{npc.Template.Name} hits you for {damage} damage while you cast. [{player.CurrentHp}/{player.MaxHp} HP]", LogEntryKind.NpcHit);

                // Veng procs immediately if we just got hit (rare self-proc)
                if (state.VengActive && damage > 0)
                {
                    int vengDmg = (int)(damage * 0.75);
                    npc.TakeDamage(vengDmg);
                    state.ConsumeVeng();
                    state.AppendLog($"VENGEANCE! {vengDmg} damage reflected!", LogEntryKind.Vengeance);

                    if (!npc.IsAlive && player.IsAlive)
                    {
                        state.AppendLog($"You have defeated {npc.Template.Name} via Vengeance!", LogEntryKind.System);
                        state.EndDuel();
                    }
                }

                if (!player.IsAlive)
                {
                    state.AppendLog($"You have been defeated by {npc.Template.Name}!", LogEntryKind.System);
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
                state.AppendLog($"{npc.Template.Name} misses while you cast.", LogEntryKind.NpcMiss);
            }

            // Drain protection prayer
            if (player.ActiveProtection != ProtectionPrayer.None)
                player.DrainPrayer(1);
        }

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }

    private static double GetPrayerReduction(Domain.Entities.Player player, AttackType npcAttackType)
    {
        if (player.PrayerPoints <= 0) return 0.0;
        return player.ActiveProtection switch
        {
            ProtectionPrayer.Melee => npcAttackType is AttackType.Stab or AttackType.Slash or AttackType.Crush ? 0.75 : 0.0,
            _ => 0.0,
        };
    }

    private static CombatantSnapshot BuildNpcAttackSnapshot(Domain.Entities.NpcInstance npc)
    {
        var s = npc.Template.Stats;
        return new CombatantSnapshot(s.Attack, s.Strength, s.Defence, npc.Template.Modifiers, npc.Template.AttackType, AttackStyle.Aggressive);
    }

    private static CombatantSnapshot BuildPlayerDefSnapshot(Domain.Entities.Player player)
        => new(player.AttackLevel, player.StrengthLevel, player.DefenceLevel, Domain.ValueObjects.ItemModifiers.Zero, AttackType.Slash, AttackStyle.Defensive);
}
