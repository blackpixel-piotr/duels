using Duels.Domain.Services;
using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Player
{
    public string Id { get; }
    public string Name { get; }

    public int AttackXp { get; private set; }
    public int StrengthXp { get; private set; }
    public int DefenceXp { get; private set; }
    public int HitpointsXp { get; private set; }

    public int AttackLevel => ExperienceTable.LevelForXp(AttackXp);
    public int StrengthLevel => ExperienceTable.LevelForXp(StrengthXp);
    public int DefenceLevel => ExperienceTable.LevelForXp(DefenceXp);
    public int HitpointsLevel => ExperienceTable.LevelForXp(HitpointsXp);
    public int MaxHp => HitpointsLevel + (PrestigeLevel >= 2 ? 10 : 0);

    public AttackStyle ChosenStyle { get; private set; } = AttackStyle.Accurate;

    public int CurrentHp { get; private set; }
    public int Gold { get; private set; }
    public int SpecialEnergy { get; private set; }
    public int PrestigeLevel { get; private set; }
    public int CombatBoostRoundsLeft { get; private set; }
    public int PrayerPoints { get; private set; }
    public ProtectionPrayer ActiveProtection { get; private set; }
    public bool PietyActive { get; private set; }

    public string PhatPrefix => PrestigeLevel switch
    {
        >= 3 => "[White Phat] ",
        2    => "[Blue Phat] ",
        1    => "[Red Phat] ",
        _    => ""
    };

    private readonly Dictionary<EquipmentSlot, string> _equipped = new();
    private readonly List<string> _inventory = new();

    public IReadOnlyDictionary<EquipmentSlot, string> Equipped => _equipped;
    public IReadOnlyList<string> Inventory => _inventory;

    public Player(string id, string name)
    {
        Id = id;
        Name = name;
        CurrentHp = MaxHp;
        SpecialEnergy = 100;
        PrayerPoints = 99;
        Gold = 10_000;
    }

    public bool IsAlive => CurrentHp > 0;

    public void SetStyle(AttackStyle style) => ChosenStyle = style;

    /// <summary>Awards xp and returns any level-ups as (skill name, new level) for logging.</summary>
    public IReadOnlyList<(string Skill, int NewLevel)> GainXp(int atkXp, int strXp, int defXp, int hpXp)
    {
        var ups = new List<(string, int)>();

        int before = AttackLevel;
        AttackXp += Math.Max(0, atkXp);
        if (AttackLevel > before) ups.Add(("Attack", AttackLevel));

        before = StrengthLevel;
        StrengthXp += Math.Max(0, strXp);
        if (StrengthLevel > before) ups.Add(("Strength", StrengthLevel));

        before = DefenceLevel;
        DefenceXp += Math.Max(0, defXp);
        if (DefenceLevel > before) ups.Add(("Defence", DefenceLevel));

        before = HitpointsLevel;
        HitpointsXp += Math.Max(0, hpXp);
        if (HitpointsLevel > before) ups.Add(("Hitpoints", HitpointsLevel));

        return ups;
    }

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
    public void Heal(int amount) => CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    public void HealFood(int amount, bool canOverheal = false)
    {
        int cap = canOverheal ? MaxHp + 10 : MaxHp;
        CurrentHp = Math.Min(cap, CurrentHp + amount);
    }
    public void RestoreHp() => CurrentHp = MaxHp;
    public void RestoreSpecialEnergy() => SpecialEnergy = 100;
    public void RechargeSpecial(int amount) => SpecialEnergy = Math.Min(100, SpecialEnergy + amount);

    public bool DrainSpecialEnergy(int amount)
    {
        if (SpecialEnergy < amount) return false;
        SpecialEnergy -= amount;
        return true;
    }

    public void DrinkSuperCombat() => CombatBoostRoundsLeft = 4;
    public void TickCombatBoost() { if (CombatBoostRoundsLeft > 0) CombatBoostRoundsLeft--; }
    public void ClearCombatBoost() => CombatBoostRoundsLeft = 0;

    public void RestorePrayer() { PrayerPoints = 99; }
    public void DrainPrayer(int amount)
    {
        PrayerPoints = Math.Max(0, PrayerPoints - amount);
        if (PrayerPoints == 0)
        {
            ActiveProtection = ProtectionPrayer.None;
            PietyActive = false;
        }
    }
    public void ToggleProtection(ProtectionPrayer prayer)
        => ActiveProtection = ActiveProtection == prayer ? ProtectionPrayer.None : prayer;
    public void TogglePiety() => PietyActive = !PietyActive;

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

    public void AddGold(int amount) => Gold += amount;
    public bool SpendGold(int amount)
    {
        if (Gold < amount) return false;
        Gold -= amount;
        return true;
    }

    public void Prestige()
    {
        PrestigeLevel++;
        Gold = 0;
        _equipped.Clear();
        _inventory.Clear();
        if (PrestigeLevel >= 3) _inventory.Add("rune_scimitar");
        CurrentHp = MaxHp;
        SpecialEnergy = 100;
        PrayerPoints = 99;
        ActiveProtection = ProtectionPrayer.None;
        PietyActive = false;
        CombatBoostRoundsLeft = 0;
    }

    public void RestoreFromSave(int gold, int currentHp, int specialEnergy, int prestigeLevel,
        IEnumerable<string> inventory, IEnumerable<KeyValuePair<EquipmentSlot, string>> equipped,
        int attackXp = 0, int strengthXp = 0, int defenceXp = 0, int hitpointsXp = 0,
        AttackStyle chosenStyle = AttackStyle.Accurate)
    {
        Gold = gold;
        PrestigeLevel = prestigeLevel;
        AttackXp = Math.Max(0, attackXp);
        StrengthXp = Math.Max(0, strengthXp);
        DefenceXp = Math.Max(0, defenceXp);
        HitpointsXp = Math.Max(0, hitpointsXp);
        ChosenStyle = chosenStyle;
        _inventory.Clear();
        _inventory.AddRange(inventory);
        _equipped.Clear();
        foreach (var kv in equipped)
            _equipped[kv.Key] = kv.Value;
        CurrentHp = Math.Min(currentHp, MaxHp);
        SpecialEnergy = Math.Min(specialEnergy, 100);
    }
}
