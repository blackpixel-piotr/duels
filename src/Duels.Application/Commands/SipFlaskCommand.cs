using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Sip a bound flask slot (0 or 1). Costs tempo, not a full attack
/// slot: adds +1 tick to the current attack cooldown (weapon-speed
/// ratification), never resetting or replacing it.</summary>
public sealed record SipFlaskCommand(string PlayerId, int Slot) : IGameCommand;
