namespace Duels.Domain.ValueObjects;

/// <summary>Attack range in arena tiles (Chebyshev distance) by combat style.
/// Melee must be adjacent; ranged/magic fire from across the arena.</summary>
public static class AttackRange
{
    public const int Melee = 1;
    // PROVISIONAL: dead path (backlog.md #28). No design doc sources this
    // value; kept only for the DummyStyle non-scripted movement fixture,
    // which is itself provably unreachable by real content now that every
    // fightable thing is a boss (m1-findings.md's M2 pre-plan addendum).
    // Real weapon ranges come from each weapon's own Range field instead.
    public const int Distant = 8;

    public static int ForStyle(AttackType t) =>
        t is AttackType.Ranged or AttackType.Magic ? Distant : Melee;
}
