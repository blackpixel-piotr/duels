using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Application.GameSession;

namespace Duels.Application.Handlers;

public sealed class InspectHandler : ICommandHandler<InspectCommand>
{
    private readonly IGameStateRepository _stateRepo;
    private readonly INpcRepository _npcRepo;
    private readonly IItemRepository _itemRepo;

    public InspectHandler(IGameStateRepository stateRepo, INpcRepository npcRepo, IItemRepository itemRepo)
    {
        _stateRepo = stateRepo;
        _npcRepo = npcRepo;
        _itemRepo = itemRepo;
    }

    public async Task<CommandResult> HandleAsync(InspectCommand command, CancellationToken ct = default)
    {
        var state = await _stateRepo.GetAsync(command.PlayerId, ct);
        if (state is null) return CommandResult.Fail("No active game.");

        var messages = command.Target.ToLowerInvariant() switch
        {
            "me" or "self" or "player" => InspectPlayer(state),
            "npc" or "enemy" => InspectNpc(state),
            _ => InspectItem(command.Target, state)
        };

        foreach (var msg in messages)
            state.AppendLog(msg, LogEntryKind.Info);

        await _stateRepo.SaveAsync(state, ct);
        return CommandResult.Ok();
    }

    private static IEnumerable<string> InspectPlayer(GameState state)
    {
        var p = state.Player;
        yield return $"--- {p.Name} ---";
        yield return $"HP: {p.CurrentHp}/{p.MaxHp}  Gold: {p.Gold}  Special: {p.SpecialEnergy}%";
        yield return $"Attack: {p.AttackLevel}  Strength: {p.StrengthLevel}  Defence: {p.DefenceLevel}  HP: {p.HitpointsLevel}";
        if (p.Equipped.Count > 0)
        {
            yield return "Equipment:";
            foreach (var (slot, id) in p.Equipped)
                yield return $"  {slot}: {id}";
        }
    }

    private static IEnumerable<string> InspectNpc(GameState state)
    {
        if (!state.InDuel) { yield return "You are not in a duel."; yield break; }
        var npc = state.ActiveNpc!.Template;
        yield return $"--- {npc.Name} (level {npc.CombatLevel}) ---";
        yield return npc.ExamineText;
        yield return $"HP: {state.ActiveNpc.CurrentHp}/{npc.Stats.Hitpoints * 10}";
        yield return $"Attack: {npc.Stats.Attack}  Strength: {npc.Stats.Strength}  Defence: {npc.Stats.Defence}";
    }

    private IEnumerable<string> InspectItem(string itemId, GameState state)
    {
        var gear = _itemRepo.GetGear(itemId);
        if (gear is null) { yield return $"Unknown: '{itemId}'"; yield break; }
        yield return $"--- {gear.Name} ---";
        yield return gear.ExamineText;
        var m = gear.Modifiers;
        yield return $"Stab: {m.StabAttack:+0;-0}  Slash: {m.SlashAttack:+0;-0}  Crush: {m.CrushAttack:+0;-0}";
        yield return $"Str bonus: {m.StrengthBonus:+0;-0}";
    }
}
