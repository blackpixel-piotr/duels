using Duels.Application.Abstractions;
using Duels.Application.Commands;
using Duels.Domain.ValueObjects;

namespace Duels.Application.Parsing;

public sealed record ParseResult(bool Success, IGameCommand? Command, string? Error);

public sealed class CommandParser
{
    private readonly INpcRepository _npcRepo;
    private readonly IItemRepository _itemRepo;

    private static readonly Dictionary<string, string> Shorthands = new()
    {
        ["ags"]    = "armadyl_godsword",
        ["bgs"]    = "bandos_godsword",
        ["zgs"]    = "zamorak_godsword",
        ["sgs"]    = "saradomin_godsword",
        ["dds"]    = "dragon_dagger",
        ["claws"]  = "dragon_claws",
        ["whip"]   = "abyssal_whip",
        ["scythe"] = "scythe_of_vitur",
    };

    private static readonly HashSet<string> FoodIds = ["shark", "karambwan", "anglerfish"];

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
        if (raw.StartsWith('!')) raw = raw[1..];

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new ParseResult(false, null, "Empty command.");

        var cmd = parts[0].ToLowerInvariant();
        var args = parts[1..];

        // Weapon shorthand or full weapon ID → auto-equip + attack
        if (Shorthands.TryGetValue(cmd, out var weaponId))
            return new ParseResult(true, new WeaponShortcutCommand(playerId, weaponId), null);
        if (_itemRepo.IsWeapon(cmd))
            return new ParseResult(true, new WeaponShortcutCommand(playerId, cmd), null);

        return cmd switch
        {
            "duel" or "fight" or "challenge" => ParseDuel(playerId, args),
            "attack" or "hit" => ParseAttack(playerId, args, AttackStyle.Accurate),
            "spec" or "special" => new ParseResult(true, new AttackCommand(playerId, AttackStyle.Accurate, UseSpecial: true), null),

            "accurate" or "acc" => new ParseResult(true, new SetStyleCommand(playerId, AttackStyle.Accurate), null),
            "aggressive" or "aggro" or "str" => new ParseResult(true, new SetStyleCommand(playerId, AttackStyle.Aggressive), null),
            "defensive" or "def" => new ParseResult(true, new SetStyleCommand(playerId, AttackStyle.Defensive), null),
            "style" => ParseStyleCommand(playerId, args),

            "shop" or "store" => new ParseResult(true, new ShopCommand(playerId), null),
            "buy" or "purchase" => ParseBuy(playerId, args),

            "equip" or "wear" or "wield" => ParseEquip(playerId, args),
            "unequip" or "remove" => ParseUnequip(playerId, args),

            "eat" or "food" => ParseEat(playerId, args),
            "drink" or "potion" => ParseDrink(playerId, args),
            "veng" or "vengeance" => new ParseResult(true, new VengeanceCommand(playerId), null),

            "protect_melee" or "pm" or "protect" => new ParseResult(true, new PrayerCommand(playerId, "protect_melee"), null),
            "protect_range" or "pr" => new ParseResult(true, new PrayerCommand(playerId, "protect_range"), null),
            "protect_magic" or "pmagic" => new ParseResult(true, new PrayerCommand(playerId, "protect_magic"), null),
            "piety" or "pie" => new ParseResult(true, new PrayerCommand(playerId, "piety"), null),
            "pray" or "prayer" => ParsePray(playerId, args),
            "beg" => new ParseResult(true, new BegCommand(playerId), null),
            "prestige" => new ParseResult(true, new PrestigeCommand(playerId), null),

            "inspect" or "examine" or "check" => ParseInspect(playerId, args),
            "stats" or "me" => new ParseResult(true, new InspectCommand(playerId, "me"), null),
            "npc" or "enemy" => new ParseResult(true, new InspectCommand(playerId, "npc"), null),

            "npcs" or "enemies" or "list" => new ParseResult(true, new ListNpcsCommand(playerId), null),
            "inventory" or "inv" or "bag" => new ParseResult(true, new InspectCommand(playerId, "inventory"), null),

            "help" or "?" => new ParseResult(true, new HelpCommand(playerId), null),

            _ => new ParseResult(false, null, $"Unknown command: {cmd}. Type help for a list.")
        };
    }

    private static ParseResult ParseDuel(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: duel <enemy> [wager]  (e.g. duel swashbuckler 500)");

        // "duel endless" → endless mode
        if (args[0].Equals("endless", StringComparison.OrdinalIgnoreCase))
            return new ParseResult(true, new StartEndlessCommand(playerId), null);

        var npcId = args[0].ToLowerInvariant().Replace(" ", "_");
        int wager = args.Length > 1 && int.TryParse(args[1], out var w) && w > 0 ? w : 0;
        return new ParseResult(true, new StartDuelCommand(playerId, npcId, wager), null);
    }

    private static ParseResult ParseAttack(string playerId, string[] args, AttackStyle defaultStyle)
    {
        var style = args.Length > 0 ? ParseStyle(args[0]) ?? defaultStyle : defaultStyle;
        return new ParseResult(true, new AttackCommand(playerId, style), null);
    }

    private static ParseResult ParseDrink(string playerId, string[] args)
    {
        var itemId = args.Length > 0
            ? string.Join("_", args).ToLowerInvariant()
            : "super_combat_potion";
        if (itemId is "antidote" or "super_combat_potion")
            return new ParseResult(true, new DrinkPotionCommand(playerId, itemId), null);
        return new ParseResult(false, null, "Usage: drink <super_combat_potion|antidote>");
    }

    private static ParseResult ParseStyleCommand(string playerId, string[] args)
    {
        if (args.Length == 0 || ParseStyle(args[0]) is not { } style)
            return new ParseResult(false, null, "Usage: style <accurate|aggressive|defensive>");
        return new ParseResult(true, new SetStyleCommand(playerId, style), null);
    }

    private static AttackStyle? ParseStyle(string s) => s.ToLowerInvariant() switch
    {
        "accurate" or "acc" => AttackStyle.Accurate,
        "aggressive" or "aggro" or "str" => AttackStyle.Aggressive,
        "defensive" or "def" => AttackStyle.Defensive,
        _ => null
    };

    private static ParseResult ParseBuy(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: buy <item_id> [qty]  (e.g. buy shark 5)");

        int qty = 1;
        var parts = args;
        if (args.Length > 1 && int.TryParse(args[^1], out int parsed) && parsed > 0)
        {
            qty = parsed;
            parts = args[..^1];
        }
        var itemId = string.Join("_", parts).ToLowerInvariant();
        return new ParseResult(true, new BuyItemCommand(playerId, itemId, qty), null);
    }

    private ParseResult ParseEquip(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: equip <item_id>");

        var itemId = string.Join("_", args).ToLowerInvariant();
        return new ParseResult(true, new EquipItemCommand(playerId, itemId), null);
    }

    private static ParseResult ParseUnequip(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: unequip <slot>  (e.g. unequip weapon)");

        if (!Enum.TryParse<EquipmentSlot>(args[0], ignoreCase: true, out var slot))
            return new ParseResult(false, null, $"Unknown slot: '{args[0]}'. Slots: {string.Join(", ", Enum.GetNames<EquipmentSlot>())}");

        return new ParseResult(true, new UnequipItemCommand(playerId, slot), null);
    }

    private static ParseResult ParseEat(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: eat <food>  (e.g. eat shark)");

        var itemId = string.Join("_", args).ToLowerInvariant();
        return new ParseResult(true, new EatItemCommand(playerId, itemId), null);
    }

    private static ParseResult ParsePray(string playerId, string[] args)
    {
        if (args.Length == 0)
            return new ParseResult(false, null, "Usage: pray <type>  (protect_melee, piety, protect_range, protect_magic)");
        var name = string.Join("_", args).ToLowerInvariant();
        return new ParseResult(true, new PrayerCommand(playerId, name), null);
    }

    private static ParseResult ParseInspect(string playerId, string[] args)
    {
        var target = args.Length > 0 ? string.Join(" ", args) : "me";
        return new ParseResult(true, new InspectCommand(playerId, target), null);
    }
}
