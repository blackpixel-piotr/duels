using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

/// <summary>RS3-style action bar (UI bible §4): 4 manually-bound weapon slots,
/// never auto-filled. Locked when a fight starts (m1-plan Workstream G).</summary>
public sealed class Loadout
{
    private readonly string?[] _weaponSlots = new string?[4];
    private readonly string?[] _flaskSlots = new string?[2];
    private readonly Dictionary<string, AttackStyle> _defaultStylePerWeapon = new();

    public IReadOnlyList<string?> WeaponSlots => _weaponSlots;
    public IReadOnlyList<string?> FlaskSlots => _flaskSlots;
    public IReadOnlyDictionary<string, AttackStyle> DefaultStylePerWeapon => _defaultStylePerWeapon;

    public bool BindWeapon(int slot, string? weaponId)
    {
        if (slot is < 0 or > 3) return false;
        _weaponSlots[slot] = weaponId;
        return true;
    }

    public bool BindFlask(int slot, string? flaskId)
    {
        if (slot is < 0 or > 1) return false;
        _flaskSlots[slot] = flaskId;
        return true;
    }

    public void SetDefaultStyle(string weaponId, AttackStyle style) => _defaultStylePerWeapon[weaponId] = style;

    public void RestoreFromSave(IEnumerable<string?> weaponSlots, IEnumerable<string?> flaskSlots,
        IReadOnlyDictionary<string, AttackStyle>? defaultStyles = null)
    {
        int i = 0;
        foreach (var id in weaponSlots.Take(4)) _weaponSlots[i++] = id;
        i = 0;
        foreach (var id in flaskSlots.Take(2)) _flaskSlots[i++] = id;
        _defaultStylePerWeapon.Clear();
        foreach (var kv in defaultStyles ?? new Dictionary<string, AttackStyle>())
            _defaultStylePerWeapon[kv.Key] = kv.Value;
    }
}

/// <summary>One bound flask slot (m1-plan Workstream E / UI bible §3.2):
/// fixed sip charges per fight, free full refill on every duel start.</summary>
public sealed class FlaskSlotState
{
    public string? FlaskId { get; private set; }
    public int SipsRemaining { get; private set; }
    public int MaxSips { get; private set; }

    public void Bind(string? flaskId, int maxSips)
    {
        FlaskId = flaskId;
        MaxSips = maxSips;
        SipsRemaining = flaskId is null ? 0 : maxSips;
    }

    public void RefillFull() { if (FlaskId is not null) SipsRemaining = MaxSips; }

    public bool TrySip()
    {
        if (FlaskId is null || SipsRemaining <= 0) return false;
        SipsRemaining--;
        return true;
    }
}

/// <summary>The two bound flask slots for the current fight (bindings come
/// from <see cref="Loadout.FlaskSlots"/>; sip counts are duel-scoped).</summary>
public sealed class FlaskBelt
{
    public const int BaselineSips = 3;

    public FlaskSlotState[] Slots { get; } = { new(), new() };

    public void RefillForDuel(Loadout loadout)
    {
        for (int i = 0; i < Slots.Length; i++)
            Slots[i].Bind(loadout.FlaskSlots.ElementAtOrDefault(i), BaselineSips);
    }
}
