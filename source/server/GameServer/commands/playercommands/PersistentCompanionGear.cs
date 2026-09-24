using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Owner gear operations shared by the Companion Manager and the legacy menu.
    /// Every call re-resolves the active companion and the item's current slot,
    /// then mutates only through PlayerCompanionRoster's protected paths.
    /// </summary>
    public static class PersistentCompanionGear
    {
        private const string BusyMessage = "Gear changes need an active companion near you while both of you are out of combat.";

        public static bool IsBackpack(DbInventoryItem item) =>
            item?.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack;

        public static bool TryEquip(GamePlayer owner, string companionId, string itemId, out string message)
        {
            if (!TryGetItem(owner, companionId, itemId, out GameBot companion, out DbInventoryItem item) || !IsBackpack(item))
            {
                message = "That item is no longer in the companion's backpack.";
                return false;
            }
            if (!companion.TryManuallyEquipPersistentCompanionItem(item))
            {
                message = "That item is not a legal choice, its slot is protected, or the companion is busy.";
                return false;
            }
            message = $"{item.Name} is equipped and its slot is locked against automatic replacement.";
            return true;
        }

        public static bool TryUnequip(GamePlayer owner, string companionId, eInventorySlot slot, string expectedItemId,
            out string message)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion) ||
                companion.Inventory == null)
            {
                message = BusyMessage;
                return false;
            }
            DbInventoryItem item = companion.Inventory.GetItem(slot);
            if (item?.ObjectId != expectedItemId || !PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    if (!ReferenceEquals(companion.Inventory.GetItem(slot), item))
                        return false;
                    eInventorySlot backpack = companion.Inventory.FindFirstEmptySlot(
                        eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (backpack == eInventorySlot.Invalid ||
                        !companion.Inventory.MoveItem(slot, backpack, Math.Max(1, item.Count)))
                        return false;
                    PlayerCompanionRoster.SetEquipmentSlotLocked(companion.PlayerCompanionRecord, slot, false);
                    return true;
                }, out _, requiredFreeBackpackSlots: 1))
            {
                message = "The item could not be moved to the companion's backpack.";
                return false;
            }
            companion.RefreshPersistentCompanionEquipment(
                slot is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon);
            message = $"{item.Name} moved to {companion.Name}'s backpack; the {SlotName(slot)} slot is unlocked.";
            return true;
        }

        public static bool TrySetSlotLock(GamePlayer owner, string companionId, eInventorySlot slot, bool locked,
            string expectedItemId, out string message)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion) ||
                companion.Inventory?.GetItem(slot)?.ObjectId != expectedItemId ||
                !PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    if (companion.Inventory?.GetItem(slot)?.ObjectId != expectedItemId)
                        return false;
                    PlayerCompanionRoster.SetEquipmentSlotLocked(companion.PlayerCompanionRecord, slot, locked);
                    return true;
                }, out _))
            {
                message = "Slot locks can only be changed near an idle companion.";
                return false;
            }
            message = locked
                ? $"The {SlotName(slot)} slot is locked against automatic replacement."
                : $"The {SlotName(slot)} slot accepts automatic upgrades again.";
            return true;
        }

        public static bool TrySetKeep(GamePlayer owner, string companionId, string itemId, bool keep, out string message)
        {
            if (!TryGetItem(owner, companionId, itemId, out GameBot companion, out DbInventoryItem item) ||
                !PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    if (companion.Inventory?.AllItems.Any(entry => entry.ObjectId == itemId) != true)
                        return false;
                    string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, itemId);
                    flags = keep ? string.Concat(flags.Replace("K", string.Empty, StringComparison.Ordinal), "K")
                        : flags.Replace("K", string.Empty, StringComparison.Ordinal);
                    PlayerCompanionRoster.SetEquipmentItemFlags(companion.PlayerCompanionRecord, itemId, flags);
                    return true;
                }, out _))
            {
                message = "Keep flags can only be changed for inventory owned by an idle companion nearby.";
                return false;
            }
            message = keep
                ? $"{item.Name} is kept and will not be sold for space."
                : $"{item.Name} may be sold automatically when space is needed, if eligible.";
            return true;
        }

        public static bool TryReturnToOwner(GamePlayer owner, string companionId, string itemId, out string message)
        {
            bool transferred = PlayerCompanionRoster.TryTransferItem(owner, companionId, itemId, toCompanion: false,
                out message);
            if (transferred)
                owner.Out.SendInventorySlotsUpdate(Enumerable.Range((int)eInventorySlot.FirstBackpack, 40)
                    .Select(slot => (eInventorySlot)slot).ToArray());
            return transferred;
        }

        public static string DescribeFlags(string flags)
        {
            if (flags.Contains('S')) return "starter gear; protected";
            if (flags.Contains('P')) return "player-supplied; protected";
            if (flags.Contains('E')) return flags.Contains('K') ? "companion gear; kept" : "companion gear";
            return "legacy ownership unknown; protected";
        }

        public static string DescribeStats(DbInventoryItem item)
        {
            var stats = new List<string>();
            AddStat("Bonus", item.Bonus);
            AddStat((eProperty)item.Bonus1Type, item.Bonus1);
            AddStat((eProperty)item.Bonus2Type, item.Bonus2);
            AddStat((eProperty)item.Bonus3Type, item.Bonus3);
            AddStat((eProperty)item.Bonus4Type, item.Bonus4);
            AddStat((eProperty)item.Bonus5Type, item.Bonus5);
            AddStat((eProperty)item.Bonus6Type, item.Bonus6);
            AddStat((eProperty)item.Bonus7Type, item.Bonus7);
            AddStat((eProperty)item.Bonus8Type, item.Bonus8);
            AddStat((eProperty)item.Bonus9Type, item.Bonus9);
            AddStat((eProperty)item.Bonus10Type, item.Bonus10);
            AddStat((eProperty)item.ExtraBonusType, item.ExtraBonus);
            if (item.DPS_AF > 0)
                stats.Add($"DPS/AF {item.DPS_AF}");
            if (item.SPD_ABS > 0)
                stats.Add($"speed {item.SPD_ABS}");
            return stats.Count == 0 ? "Stats: none" : "Stats: " + string.Join(", ", stats);

            void AddStat(object type, int value)
            {
                if (value != 0 && !string.Equals(type?.ToString(), "Undefined", StringComparison.Ordinal))
                    stats.Add($"{type} {value}");
            }
        }

        public static string SlotName(eInventorySlot slot) => slot switch
        {
            eInventorySlot.RightHandWeapon => "right hand",
            eInventorySlot.LeftHandWeapon => "left hand",
            eInventorySlot.TwoHandWeapon => "two-handed",
            eInventorySlot.DistanceWeapon => "ranged",
            eInventorySlot.HeadArmor => "helm",
            eInventorySlot.HandsArmor => "gloves",
            eInventorySlot.FeetArmor => "boots",
            eInventorySlot.TorsoArmor => "chest",
            eInventorySlot.LegsArmor => "legs",
            eInventorySlot.ArmsArmor => "arms",
            eInventorySlot.Cloak => "cloak",
            eInventorySlot.Neck => "neck",
            eInventorySlot.Waist => "belt",
            eInventorySlot.Jewelry => "jewel",
            eInventorySlot.LeftBracer => "left wrist",
            eInventorySlot.RightBracer => "right wrist",
            eInventorySlot.LeftRing => "left ring",
            eInventorySlot.RightRing => "right ring",
            eInventorySlot.Mythical => "mythical",
            _ => slot.ToString(),
        };

        private static bool TryGetItem(GamePlayer owner, string companionId, string itemId, out GameBot companion,
            out DbInventoryItem item)
        {
            item = null;
            return PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out companion) &&
                   (item = companion.Inventory?.AllItems.FirstOrDefault(entry => entry.ObjectId == itemId)) != null;
        }
    }
}
