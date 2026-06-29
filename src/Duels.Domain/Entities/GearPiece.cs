using Duels.Domain.ValueObjects;

namespace Duels.Domain.Entities;

public sealed class GearPiece
{
    public string Id { get; }
    public string Name { get; }
    public EquipmentSlot Slot { get; }
    public ItemModifiers Modifiers { get; }
    public string ExamineText { get; }

    public GearPiece(string id, string name, EquipmentSlot slot, ItemModifiers modifiers, string examineText = "")
    {
        Id = id;
        Name = name;
        Slot = slot;
        Modifiers = modifiers;
        ExamineText = examineText;
    }
}
