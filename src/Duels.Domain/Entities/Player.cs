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

    public int AttackLevel => LevelForXp(AttackXp);
    public int StrengthLevel => LevelForXp(StrengthXp);
    public int DefenceLevel => LevelForXp(DefenceXp);
    public int HitpointsLevel => LevelForXp(HitpointsXp);

    public int MaxHp => HitpointsLevel * 10;
    public int CurrentHp { get; private set; }
    public int Gold { get; private set; }
    public int SpecialEnergy { get; private set; }

    private readonly Dictionary<EquipmentSlot, string> _equipped = new();
    private readonly List<string> _inventory = new();

    public IReadOnlyDictionary<EquipmentSlot, string> Equipped => _equipped;
    public IReadOnlyList<string> Inventory => _inventory;

    public Player(string id, string name, int attackXp = 0, int strengthXp = 0, int defenceXp = 0, int hitpointsXp = 1154)
    {
        Id = id;
        Name = name;
        AttackXp = attackXp;
        StrengthXp = strengthXp;
        DefenceXp = defenceXp;
        HitpointsXp = hitpointsXp; // level 10 by default
        CurrentHp = MaxHp;
        SpecialEnergy = 100;
    }

    public bool IsAlive => CurrentHp > 0;

    public void TakeDamage(int amount) => CurrentHp = Math.Max(0, CurrentHp - amount);
    public void Heal(int amount) => CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    public void RestoreHp() => CurrentHp = MaxHp;
    public void RestoreSpecialEnergy() => SpecialEnergy = 100;

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

    public void GainAttackXp(int xp) => AttackXp += xp;
    public void GainStrengthXp(int xp) => StrengthXp += xp;
    public void GainDefenceXp(int xp) => DefenceXp += xp;
    public void GainHitpointsXp(int xp) => HitpointsXp += xp;

    public void AddGold(int amount) => Gold += amount;

    public CombatStats ToCombatStats() => new(AttackLevel, StrengthLevel, DefenceLevel, HitpointsLevel);

    public static int LevelForXp(int xp)
    {
        for (int level = 99; level >= 1; level--)
            if (xp >= XpForLevel(level)) return level;
        return 1;
    }

    public static int XpForLevel(int level)
    {
        if (level <= 1) return 0;
        int total = 0;
        for (int i = 1; i < level; i++)
            total += (int)(i + 300 * Math.Pow(2, i / 7.0));
        return total / 4;
    }
}
