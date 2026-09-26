using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionBagVisibility
{
    [Test]
    public void OwnerBagShowsOnlyItemsTheOwnerCanTakeOut()
    {
        var record = new PlayerCompanionRecord();
        DbInventoryItem earned = Item(record, "earned", "E");
        DbInventoryItem given = Item(record, "given", "P");
        DbInventoryItem starter = Item(record, "starter", "S");
        DbInventoryItem legacy = Item(record, "legacy", null);
        DbInventoryItem bound = Item(record, "bound", "E"); bound.IsTradable = false;
        DbInventoryItem worn = Item(record, "worn", "E"); worn.SlotPosition = (int)eInventorySlot.TorsoArmor;

        DbInventoryItem[] visible = PlayerCompanionRoster
            .OwnerTakeableBackpack(record, [earned, given, starter, legacy, bound, worn]).ToArray();

        Assert.That(visible, Is.EquivalentTo(new[] { earned, given }));
    }

    private static DbInventoryItem Item(PlayerCompanionRecord record, string name, string flags)
    {
        var template = new DbItemTemplate
        {
            Id_nb = "test_" + name, Name = name, Level = 1, IsDropable = true, IsTradable = true,
            IsPickable = true, Object_Type = (int)eObjectType.Cloth, Item_Type = (int)eInventorySlot.TorsoArmor,
        };
        DbInventoryItem item = GameInventoryItem.Create(template);
        item.Name = name;
        item.ObjectId = "item-" + name;
        item.SlotPosition = (int)eInventorySlot.FirstBackpack;
        item.IsPersisted = true;
        if (flags != null)
            PlayerCompanionRoster.SetEquipmentItemFlags(record, item.ObjectId, flags);
        return item;
    }
}
