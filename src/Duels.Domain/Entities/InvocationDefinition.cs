namespace Duels.Domain.Entities;

/// <summary>An invocation (ToA-model pre-fight modifier) that raises Raid
/// Level. Schema only for M0 — the pipeline exists so M4 content is a data
/// commit, not a code change; <c>invocations.json</c> ships empty until then.</summary>
public sealed record InvocationDefinition(
    string Id,
    string Name,
    int RaidLevel,
    string Effect,
    string Tier,
    IReadOnlyList<string> Tags,
    string? ExclusiveGroup = null);
