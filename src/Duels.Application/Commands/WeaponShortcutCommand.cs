using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

public sealed record WeaponShortcutCommand(string PlayerId, string WeaponId) : IGameCommand;
