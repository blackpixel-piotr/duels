using Duels.Application.Abstractions;

namespace Duels.Application.Commands;

/// <summary>Dev/debug menu (m1-plan Workstream B): one-tap grant of a T1 or T2
/// preset — 3 weapons on the bar, full matching-line armour, and Health +
/// Prayer flasks on the belt. No shop, no bank, no drops.</summary>
public sealed record GrantDevLoadoutCommand(string PlayerId, int Tier, string Line) : IGameCommand;
