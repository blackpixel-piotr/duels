using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Tap the engagement indicator: break target lock explicitly
/// (M1 revision — "persistent target lock", UI bible §3.3/§3's
/// reticle+sheathed element). The only other action, besides Engage(),
/// that changes the lock — movement never does.</summary>
public sealed record DisengageCommand(string PlayerId) : IGameCommand;
