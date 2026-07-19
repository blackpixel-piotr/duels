using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Sip a bound flask slot (0 or 1). Consumes the player's action for
/// the tick — heal or attack, never both (UI bible §3.2).</summary>
public sealed record SipFlaskCommand(string PlayerId, int Slot) : IGameCommand;
