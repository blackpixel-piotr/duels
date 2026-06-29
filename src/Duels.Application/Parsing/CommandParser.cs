using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Parsing;

public sealed record ParseResult(bool Success, IGameCommand? Command, string? Error);

public sealed class CommandParser
{
    private readonly INpcRepository _npcRepo;
    private readonly IItemRepository _itemRepo;

    public CommandParser(INpcRepository npcRepo, IItemRepository itemRepo)
    {
        _npcRepo = npcRepo;
        _itemRepo = itemRepo;
    }

    public ParseResult Parse(string playerId, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParseResult(false, null, "Empty command.");

        var raw = input.Trim();
        if (!raw.StartsWith('!'))
            return new ParseResult(false, null, "Commands start with !  (e.g. !attack, !duel goblin)");

        var parts = raw[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new ParseResult(false, null, "Empty command.");

        var cmd = parts[0].ToLowerInvariant();
        var args = parts[1..];

        return cmd switch
        {
            "duel" or "fight" or "challenge" => ParseDuel(playerId, args),
            "attack" or "hit" or "fight" => ParseAttack(playerId, args, AttackStyle.Accurate),

            // weapon aliases — mIRC style
            "whip" => ParseAttack(playerId, [], AttackStyle.Accurate),
            "dds" => new ParseResult(true, new AttackCommand(playerId, AttackStyle.Aggressive, UseSpecial: true), null),
            "spec" or "special" => new ParseResult(true, new AttackCommand(playerId, AttackStyle.Accurate, UseSpecial: true), null),

            "accurate" => ParseAttack(playerId, [], AttackStyle.Accurate),
            "aggressive" or "aggro" => ParseAttack(playerId, [], AttackStyle.Aggressive),
            "defensive" or "def" => ParseAttack(playerId, [], AttackStyle.Defensive),

            "equip" or "wear" or "wield" => ParseEquip(playerId, args),
            "unequip" or "remove" => ParseUnequip(playerId, args),

            "inspect" or "examine" or "check" => ParseInspect(playerId, args),
            "stats" or "me" => new ParseResult(true, new InspectCommand(playerId, "me"), null),
            "npc" or "enemy" => new ParseResult(true, new InspectCommand(playerId, "npc"), null),

            "npcs" or "enemies" or "list" => new ParseResult(true, new ListNpcsCommand(playerId), null),
            "inventory" or "inv" or "bag" => new ParseResult(true, new InspectCommand(playerId, "inventory"), null),

            "help" or "?" => new ParseResult(true, new HelpCommand(playerId), null),

            _ => new ParseResult(false, null, $"Unknown command: !{cmd}. Type !help for a list.")
        };
    }

    private ParseResult ParseDuel(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: !duel <enemy>  (e.g. !duel goblin)");

        var npcId = string.Join("_", args).ToLowerInvariant().Replace(" ", "_");
        return new ParseResult(true, new StartDuelCommand(playerId, npcId), null);
    }

    private static ParseResult ParseAttack(string playerId, string[] args, AttackStyle defaultStyle)
    {
        var style = args.Length > 0 ? ParseStyle(args[0]) ?? defaultStyle : defaultStyle;
        return new ParseResult(true, new AttackCommand(playerId, style), null);
    }

    private static AttackStyle? ParseStyle(string s) => s.ToLowerInvariant() switch
    {
        "accurate" or "acc" => AttackStyle.Accurate,
        "aggressive" or "aggro" or "str" => AttackStyle.Aggressive,
        "defensive" or "def" => AttackStyle.Defensive,
        _ => null
    };

    private ParseResult ParseEquip(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: !equip <item_id>");

        var itemId = string.Join("_", args).ToLowerInvariant();
        return new ParseResult(true, new EquipItemCommand(playerId, itemId), null);
    }

    private static ParseResult ParseUnequip(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: !unequip <slot>  (e.g. !unequip weapon)");

        if (!Enum.TryParse<EquipmentSlot>(args[0], ignoreCase: true, out var slot))
            return new ParseResult(false, null, $"Unknown slot: '{args[0]}'. Slots: {string.Join(", ", Enum.GetNames<EquipmentSlot>())}");

        return new ParseResult(true, new UnequipItemCommand(playerId, slot), null);
    }

    private static ParseResult ParseInspect(string playerId, string[] args)
    {
        var target = args.Length > 0 ? string.Join(" ", args) : "me";
        return new ParseResult(true, new InspectCommand(playerId, target), null);
    }
}
