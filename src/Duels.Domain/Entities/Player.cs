using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class Player
{
    public string Id { get; }
    public string Name { get; }

    public int AttackLevel => 99;
    public int StrengthLevel => 99;
    public int DefenceLevel => 99;
    public int MaxHp => 99;

    public int CurrentHp { get; private set; }
    public int Gold { get; private set; }
    public int SpecialEnergy { get; private set; }

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
    public void RestoreHp() => CurrentHp = MaxHp;
    public void RestoreSpecialEnergy() => SpecialEnergy = 100;
    public void RechargeSpecial(int amount) => SpecialEnergy = Math.Min(100, SpecialEnergy + amount);

    public bool DrainSpecialEnergy(int amount)
    {
        if (SpecialEnergy < amount) return false;
        SpecialEnergy -= amount;
        return true;
    }

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
    public bool HasItem(string itemId) => _inventory.Contains(itemId) || _equipped.ContainsValue(itemId);
    public string? GetEquippedWeaponId() => _equipped.GetValueOrDefault(EquipmentSlot.Weapon);

    public void AddGold(int amount) => Gold += amount;
    public bool SpendGold(int amount)
    {
        if (Gold < amount) return false;
        Gold -= amount;
        return true;
    }
}
