using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Player
{
    public string Id { get; }
    public string Name { get; }

    public int AttackLevel => 99;
    public int StrengthLevel => 99;
    public int DefenceLevel => 99;
    public int MaxHp => PrestigeLevel >= 2 ? 109 : 99;

    public int CurrentHp { get; private set; }
    public int Gold { get; private set; }
    public int SpecialEnergy { get; private set; }
    public int PrestigeLevel { get; private set; }
    public int CombatBoostRoundsLeft { get; private set; }

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
        Gold = 10_000;
    }

    public bool IsAlive => CurrentHp > 0;

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
        CombatBoostRoundsLeft = 0;
    }

    public void RestoreFromSave(int gold, int currentHp, int specialEnergy, int prestigeLevel,
        IEnumerable<string> inventory, IEnumerable<KeyValuePair<EquipmentSlot, string>> equipped)
    {
        Gold = gold;
        PrestigeLevel = prestigeLevel;
        _inventory.Clear();
        _inventory.AddRange(inventory);
        _equipped.Clear();
        foreach (var kv in equipped)
            _equipped[kv.Key] = kv.Value;
        CurrentHp = Math.Min(currentHp, MaxHp);
        SpecialEnergy = Math.Min(specialEnergy, 100);
    }
}
