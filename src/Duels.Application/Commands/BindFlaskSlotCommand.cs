using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Loadout Editor: bind (or clear, FlaskId=null) one of the 2 flask
/// belt slots.</summary>
public sealed record BindFlaskSlotCommand(string PlayerId, int Slot, string? FlaskId) : IGameCommand;
