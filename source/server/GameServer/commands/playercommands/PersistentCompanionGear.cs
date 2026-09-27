using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler.Client.v168;

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

        /// <summary>Worn slots in character-sheet order; ring and wrist pairs are adjacent.</summary>
        public static readonly eInventorySlot[] SheetSlots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.ArmsArmor,
            eInventorySlot.HandsArmor, eInventorySlot.LegsArmor, eInventorySlot.FeetArmor,
            eInventorySlot.Cloak, eInventorySlot.Neck, eInventorySlot.Jewelry, eInventorySlot.Waist,
            eInventorySlot.LeftBracer, eInventorySlot.RightBracer,
            eInventorySlot.LeftRing, eInventorySlot.RightRing,
            eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon,
            eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon, eInventorySlot.Mythical,
        ];

        public static bool IsBackpack(DbInventoryItem item) =>
            item?.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack;

        /// <summary>
        /// The companion's backpack items that it can equip in <paramref name="slot"/>, best first.
        /// Uses the same legality as a manual equip; slot locks do not hide an item.
        /// </summary>
        public static IReadOnlyList<DbInventoryItem> ItemsFitting(GameBot companion, eInventorySlot slot) =>
            companion?.Inventory == null
                ? Array.Empty<DbInventoryItem>()
                : companion.Inventory.AllItems.Where(IsBackpack)
                    .Where(item => FitsSlot(companion.GetManualEquipmentSlot(item), slot))
                    .OrderByDescending(AutonomousBotEconomy.EquipmentValue)
                    .ThenBy(item => item.SlotPosition)
                    .ToArray();

        /// <summary>True when an item that resolves to <paramref name="resolved"/> can go in <paramref name="slot"/>.</summary>
        public static bool FitsSlot(eInventorySlot resolved, eInventorySlot slot) =>
            resolved != eInventorySlot.Invalid && (resolved == slot || PairOf(resolved) == slot);

        /// <summary>The other half of a ring or wrist pair, or Invalid.</summary>
        public static eInventorySlot PairOf(eInventorySlot slot) => slot switch
        {
            eInventorySlot.LeftRing => eInventorySlot.RightRing,
            eInventorySlot.RightRing => eInventorySlot.LeftRing,
            eInventorySlot.LeftBracer => eInventorySlot.RightBracer,
            eInventorySlot.RightBracer => eInventorySlot.LeftBracer,
            _ => eInventorySlot.Invalid,
        };

        public static bool TryEquip(GamePlayer owner, string companionId, string itemId, out string message) =>
            TryEquip(owner, companionId, itemId, eInventorySlot.Invalid, out message);

        /// <param name="preferredSlot">The ring or wrist half to use; other slots follow the item.</param>
        public static bool TryEquip(GamePlayer owner, string companionId, string itemId, eInventorySlot preferredSlot,
            out string message)
        {
            if (!TryGetItem(owner, companionId, itemId, out GameBot companion, out DbInventoryItem item) || !IsBackpack(item))
            {
                message = "That item is no longer in the companion's backpack.";
                return false;
            }
            if (!companion.TryManuallyEquipPersistentCompanionItem(item, preferredSlot, out eInventorySlot slot))
            {
                message = "That item is not a legal choice, its slot is protected, or the companion is busy.";
                return false;
            }
            message = $"{item.Name} is equipped in the {SlotName(slot)} slot. It stays until you change it or " +
                      $"{companion.Name} finds something clearly better.";
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
                RefreshOwnerBackpack(owner);
            return transferred;
        }

        /// <summary>Pure pre-check for handing an owner item to a worn slot, before anything moves.</summary>
        public static bool CanGiveForSlot(eInventorySlot resolved, eInventorySlot slot, bool transferable, out string blocker)
        {
            blocker = resolved == eInventorySlot.Invalid ? "the companion cannot use that item"
                : !FitsSlot(resolved, slot) ? $"it does not fit the {SlotName(slot)} slot"
                : !transferable ? "that item cannot be traded"
                : string.Empty;
            return blocker.Length == 0;
        }

        /// <summary>The owner's backpack items the companion could wear in <paramref name="slot"/>, best first.</summary>
        public static IReadOnlyList<DbInventoryItem> OwnerItemsFitting(GamePlayer owner, GameBot companion, eInventorySlot slot) =>
            owner?.Inventory == null || companion == null
                ? Array.Empty<DbInventoryItem>()
                : owner.Inventory.AllItems.Where(IsBackpack)
                    .Where(item => PlayerCompanionRoster.CanTransferItem(item, out _) &&
                                   FitsSlot(companion.GetManualEquipmentSlot(item), slot))
                    .OrderByDescending(AutonomousBotEconomy.EquipmentValue)
                    .ThenBy(item => item.SlotPosition)
                    .ToArray();

        /// <summary>Owner item to the companion, then equipped in <paramref name="slot"/>; handed back if the equip fails.</summary>
        public static bool TryGiveAndEquip(GamePlayer owner, string companionId, string ownerItemId, eInventorySlot slot,
            out string message)
        {
            DbInventoryItem item = owner?.Inventory?.AllItems.FirstOrDefault(entry => entry.ObjectId == ownerItemId);
            if (item == null || !IsBackpack(item) ||
                !PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion) ||
                companion.Inventory == null)
            {
                message = "Choose an item from your backpack while the companion is near you.";
                return false;
            }
            if (!CanGiveForSlot(companion.GetManualEquipmentSlot(item), slot,
                    PlayerCompanionRoster.CanTransferItem(item, out _), out string blocker))
            {
                message = $"{item.Name}: {blocker}.";
                return false;
            }
            // Check room before anything moves, so a full backpack never triggers a surplus sale.
            int needed = FreeSlotsNeededToGive(DisplacedWeaponCount(companion, slot));
            int free = Enumerable.Range((int)eInventorySlot.FirstBackpack,
                    (int)eInventorySlot.LastBackpack - (int)eInventorySlot.FirstBackpack + 1)
                .Count(backpack => companion.Inventory.GetItem((eInventorySlot)backpack) == null);
            if (free < needed)
            {
                message = $"{companion.Name}'s backpack needs {needed} free slot{(needed == 1 ? string.Empty : "s")} " +
                          $"for {item.Name} and what it takes off; it has {free}.";
                return false;
            }
            if (!PlayerCompanionRoster.TryTransferItem(owner, companionId, ownerItemId, toCompanion: true, out message))
                return false;
            if (TryEquip(owner, companionId, ownerItemId, slot, out message))
            {
                RefreshOwnerBackpack(owner);
                return true;
            }
            string reason = message;
            bool returned = PlayerCompanionRoster.TryTransferItem(owner, companionId, ownerItemId, toCompanion: false, out _);
            RefreshOwnerBackpack(owner);
            message = GiveFailedMessage(item.Name, companion.Name, returned, reason);
            return false;
        }

        /// <summary>The incoming item needs a slot; each weapon the equip pushes out needs one more.</summary>
        public static int FreeSlotsNeededToGive(int displacedWeapons) => 1 + displacedWeapons;

        public static string GiveFailedMessage(string itemName, string companionName, bool returned, string reason) =>
            returned
                ? $"{itemName} was not equipped and came back to you: {reason}"
                : $"{itemName} was not equipped and is in {companionName}'s backpack; take it back from the Gear tab. {reason}";

        private static int DisplacedWeaponCount(GameBot companion, eInventorySlot slot)
        {
            eInventorySlot[] conflicting = slot switch
            {
                eInventorySlot.TwoHandWeapon => [eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon],
                eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon => [eInventorySlot.TwoHandWeapon],
                _ => [],
            };
            return conflicting.Count(worn => companion.Inventory.GetItem(worn) != null);
        }

        /// <summary>Worn item to the owner when they may take it; otherwise it stays in the companion's backpack.</summary>
        public static bool TryUnequipToOwner(GamePlayer owner, string companionId, eInventorySlot slot, string expectedItemId,
            out string message)
        {
            if (!TryUnequip(owner, companionId, slot, expectedItemId, out message))
                return false;
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion) ||
                companion.Inventory?.AllItems.FirstOrDefault(entry => entry.ObjectId == expectedItemId) is not DbInventoryItem item ||
                !PlayerCompanionRoster.CanReturnItemToOwner(item, companion.PlayerCompanionRecord, out _))
            {
                message += " It stays in the companion's backpack because it cannot be handed over.";
                return true;
            }
            if (TryReturnToOwner(owner, companionId, expectedItemId, out string returned))
                message = $"{item.Name} is unequipped and back in your backpack.";
            else
                message += $" {returned}";
            return true;
        }

        private static void RefreshOwnerBackpack(GamePlayer owner) =>
            owner.Out.SendInventorySlotsUpdate(Enumerable.Range((int)eInventorySlot.FirstBackpack, 40)
                .Select(slot => (eInventorySlot)slot).ToArray());

        /// <summary>Opens the same item info window a player gets by delving their own item.</summary>
        public static void ShowItemInfo(GamePlayer player, DbInventoryItem item)
        {
            if (player?.Client == null || item == null)
                return;
            var info = new List<string>();
            string caption = new DetailDisplayHandler().WriteInventoryItemInfo(player.Client, item, info);
            if (info.Count > 0)
                player.Out.SendCustomTextWindow(caption, info);
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
