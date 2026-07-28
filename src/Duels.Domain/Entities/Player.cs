using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Player
{
    public string Id { get; }
    public string Name { get; }

    // Combat-math-v2 (items doc §1): flat 100 HP, no attack/strength/defence
    // levels driving combat. XP/levels retired in the M1 ladder sweep (D1/D3)
    // — Attack/Strength/Defence/Hitpoints no longer exist as concepts.
    public const int BaseMaxHp = 100;
    public int MaxHp => BaseMaxHp;

    public AttackStyle ChosenStyle { get; private set; } = AttackStyle.Accurate;

    public int CurrentHp { get; private set; }
    public int Gold { get; private set; }
    public int SpecialEnergy { get; private set; }
    public int PrayerPoints { get; private set; }
    public ProtectionPrayer ActiveProtection { get; private set; }

    /// <summary>Generic boost prayer (UI bible §3.2 "Boost prayer"): +20%
    /// Power while active, drains prayer points while on.</summary>
    public bool BoostPrayerActive { get; private set; }

    public Loadout Loadout { get; } = new();
    public FlaskBelt FlaskBelt { get; } = new();

    /// <summary>Best kill time recorded against the Maggot King specifically,
    /// in ticks — M1's only boss at the time this shipped. M3 adds the real
    /// per-boss <see cref="BossRecords"/> map alongside it (this field stays,
    /// unmigrated, purely so old saves' data isn't silently dropped — see
    /// SaveData v4's migration in GameService).</summary>
    public int? PersonalBestKillTicks { get; private set; }
    public void RecordKillTime(int ticks)
    {
        if (PersonalBestKillTicks is null || ticks < PersonalBestKillTicks) PersonalBestKillTicks = ticks;
    }

    // M3 Workstream F: per-boss kc/deaths/best-time (UI bible §6's roster
    // and pre-fight screens). Keyed by NpcTemplate.Id.
    private readonly Dictionary<string, BossRecord> _bossRecords = new();
    public IReadOnlyDictionary<string, BossRecord> BossRecords => _bossRecords;

    public void RecordBossKill(string bossId, int ticks)
    {
        var existing = _bossRecords.GetValueOrDefault(bossId, new BossRecord(0, 0, null));
        int? best = existing.BestTimeTicks is null || ticks < existing.BestTimeTicks ? ticks : existing.BestTimeTicks;
        _bossRecords[bossId] = existing with { Kills = existing.Kills + 1, BestTimeTicks = best };
    }

    public void RecordBossDeath(string bossId)
    {
        var existing = _bossRecords.GetValueOrDefault(bossId, new BossRecord(0, 0, null));
        _bossRecords[bossId] = existing with { Deaths = existing.Deaths + 1 };
    }

    public void RestoreBossRecords(IEnumerable<KeyValuePair<string, BossRecord>> records)
    {
        _bossRecords.Clear();
        foreach (var kv in records) _bossRecords[kv.Key] = kv.Value;
    }

    private readonly Dictionary<EquipmentSlot, string> _equipped = new();
    private readonly List<string> _inventory = new();
    private readonly List<string> _bankedItems = new();

    public IReadOnlyDictionary<EquipmentSlot, string> Equipped => _equipped;
    public IReadOnlyList<string> Inventory => _inventory;

    /// <summary>UI bible §7: unbounded bank storage, distinct from the
    /// 28-slot carried bag (<see cref="Inventory"/>).</summary>
    public IReadOnlyList<string> BankedItems => _bankedItems;

    public Player(string id, string name)
    {
        Id = id;
        Name = name;
        CurrentHp = MaxHp;
        SpecialEnergy = 100;
        PrayerPoints = 99; // Ratified, items doc §1 (backlog resolution batch 1 §9) -- flat 99-point pool.
        // Cold-start ruling (backlog resolution batch 1 §4, economy doc §1):
        // 600g start + one free T1 weapon (granted by StartGameHandler) —
        // resolves the M2 pre-plan's flagged cold-start gap (0 gold + no
        // regular-duel income meant a fresh player couldn't afford gear
        // without already having gear).
        Gold = 600;
    }

    /// <summary>UI bible §7 (ratified M2 pre-plan): "The carried bag is 28
    /// slots (fixed)."</summary>
    public const int BagCapacity = 28;

    public bool IsAlive => CurrentHp > 0;

    public void SetStyle(AttackStyle style) => ChosenStyle = style;

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
    public void Heal(int amount) => CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    public void RestoreHp() => CurrentHp = MaxHp;

    public void RestoreSpecialEnergy(int cap = 100) => SpecialEnergy = cap;
    public void RechargeSpecial(int amount, int cap = 100) => SpecialEnergy = Math.Min(cap, SpecialEnergy + amount);

    public bool DrainSpecialEnergy(int amount)
    {
        if (SpecialEnergy < amount) return false;
        SpecialEnergy -= amount;
        return true;
    }

    public void RestorePrayer() => PrayerPoints = 99;
    public void RestorePrayerPoints(int amount) => PrayerPoints = Math.Min(99, PrayerPoints + amount);
    public void DrainPrayer(int amount)
    {
        PrayerPoints = Math.Max(0, PrayerPoints - amount);
        if (PrayerPoints == 0)
        {
            ActiveProtection = ProtectionPrayer.None;
            BoostPrayerActive = false;
        }
    }
    public void ToggleProtection(ProtectionPrayer prayer)
        => ActiveProtection = ActiveProtection == prayer ? ProtectionPrayer.None : prayer;
    public void ToggleBoostPrayer() => BoostPrayerActive = !BoostPrayerActive;

    public void Equip(string itemId, EquipmentSlot slot)
    {
        if (_equipped.TryGetValue(slot, out var existing))
            _inventory.Add(existing);
        _equipped[slot] = itemId;
        _inventory.Remove(itemId);
    }

    public string? Unequip(EquipmentSlot slot)
    {
        if (!_equipped.Remove(slot, out var itemId)) return null;
        _inventory.Add(itemId);
        return itemId;
    }

    public void AddToInventory(string itemId) => _inventory.Add(itemId);
    public bool RemoveFromInventory(string itemId)
    {
        int idx = _inventory.LastIndexOf(itemId);
        if (idx < 0) return false;
        _inventory.RemoveAt(idx);
        return true;
    }
    public bool HasItem(string itemId) => _inventory.Contains(itemId) || _equipped.ContainsValue(itemId);
    public string? GetEquippedWeaponId() => _equipped.GetValueOrDefault(EquipmentSlot.Weapon);

    public void AddToBank(string itemId) => _bankedItems.Add(itemId);
    public bool RemoveFromBank(string itemId)
    {
        int idx = _bankedItems.LastIndexOf(itemId);
        if (idx < 0) return false;
        _bankedItems.RemoveAt(idx);
        return true;
    }

    /// <summary>Moves one unit bag -> bank. No cap on the bank side (UI bible
    /// §7: unbounded store).</summary>
    public bool Deposit(string itemId)
    {
        if (!RemoveFromInventory(itemId)) return false;
        AddToBank(itemId);
        return true;
    }

    /// <summary>Moves one unit bank -> bag, respecting the 28-slot bag cap.</summary>
    public bool Withdraw(string itemId)
    {
        if (_inventory.Count >= BagCapacity) return false;
        if (!RemoveFromBank(itemId)) return false;
        AddToInventory(itemId);
        return true;
    }

    public void AddGold(int amount) => Gold += amount;
    public bool SpendGold(int amount)
    {
        if (Gold < amount) return false;
        Gold -= amount;
        return true;
    }

    public void RestoreFromSave(int gold, int currentHp, int specialEnergy,
        IEnumerable<string> inventory, IEnumerable<KeyValuePair<EquipmentSlot, string>> equipped,
        AttackStyle chosenStyle = AttackStyle.Accurate, int? personalBestKillTicks = null,
        IEnumerable<string>? bankedItems = null)
    {
        Gold = gold;
        ChosenStyle = chosenStyle;
        _inventory.Clear();
        _inventory.AddRange(inventory);
        _equipped.Clear();
        foreach (var kv in equipped)
            _equipped[kv.Key] = kv.Value;
        _bankedItems.Clear();
        _bankedItems.AddRange(bankedItems ?? []);
        CurrentHp = Math.Min(currentHp, MaxHp);
        SpecialEnergy = Math.Min(specialEnergy, 100);
        PersonalBestKillTicks = personalBestKillTicks;
    }
}

/// <summary>Per-boss stats for the roster/pre-fight screens (UI bible §6):
/// kill count, death count, and best kill time in ticks. Not "highest raid
/// level cleared" — that's M4 (raid level doesn't exist yet).</summary>
public sealed record BossRecord(int Kills, int Deaths, int? BestTimeTicks);
