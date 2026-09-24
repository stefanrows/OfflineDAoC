using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Owner-only native vault view of one companion's backpack (positions 1-40) and worn
    /// slots (positions 51-69, two per row). Dropping a bag item on a worn position equips it;
    /// dropping a worn item on an empty bag position unequips it.
    /// </summary>
    internal sealed class PersistentCompanionInventoryView : GameVault
    {
        private const int BackpackSize = 40;
        private const int WornOffset = 50;

        /// <summary>Worn slots in vault order; ring and wrist pairs share a row.</summary>
        internal static readonly eInventorySlot[] WornSlots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor,
            eInventorySlot.ArmsArmor, eInventorySlot.HandsArmor,
            eInventorySlot.LegsArmor, eInventorySlot.FeetArmor,
            eInventorySlot.Cloak, eInventorySlot.Neck,
            eInventorySlot.Jewelry, eInventorySlot.Waist,
            eInventorySlot.LeftBracer, eInventorySlot.RightBracer,
            eInventorySlot.LeftRing, eInventorySlot.RightRing,
            eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon,
            eInventorySlot.TwoHandWeapon, eInventorySlot.DistanceWeapon,
            eInventorySlot.Mythical,
        ];

        /// <summary>First and last 1-based vault positions of the worn slots.</summary>
        internal const int FirstWornPosition = WornOffset + 1;
        internal static int LastWornPosition => WornOffset + WornSlots.Length;

        private readonly GamePlayer _owner;
        private readonly string _companionId;

        public PersistentCompanionInventoryView(GamePlayer owner, string companionId)
        {
            _owner = owner;
            _companionId = companionId;
        }

        public override int VaultSize => 100;
        public override int FirstDbSlot => (int)eInventorySlot.FirstBackpack;
        public override int LastDbSlot => (int)eInventorySlot.LastBackpack;
        public override eInventorySlot LastClientSlot => (eInventorySlot)((int)FirstClientSlot + VaultSize - 1);

        public override string GetOwner(GamePlayer player) =>
            ReferenceEquals(player, _owner) ? PlayerCompanionRoster.InventoryOwnerId(_companionId) : string.Empty;

        public override IEnumerable<DbInventoryItem> GetDbItems(GamePlayer player) =>
            TryGetCompanion(player, out GameBot companion) && companion.Inventory != null
                ? companion.Inventory.AllItems.Where(item => ClientSlotOf((eInventorySlot)item.SlotPosition) != null)
                    .ToArray()
                : Array.Empty<DbInventoryItem>();

        public override Dictionary<int, DbInventoryItem> GetClientInventory(GamePlayer player) =>
            GetDbItems(player).ToDictionary(item => ClientSlotOf((eInventorySlot)item.SlotPosition).Value);

        /// <summary>The vault client slot that shows a companion inventory slot, if any.</summary>
        internal static int? ClientSlotOf(eInventorySlot slot)
        {
            if (IsBackpack(slot))
                return (int)eInventorySlot.HousingInventory_First + (int)slot - (int)eInventorySlot.FirstBackpack;
            int worn = Array.IndexOf(WornSlots, slot);
            return worn < 0 ? null : (int)eInventorySlot.HousingInventory_First + WornOffset + worn;
        }

        /// <summary>The worn slot shown at a vault client slot, or Invalid.</summary>
        internal static eInventorySlot WornSlotAt(eInventorySlot clientSlot)
        {
            int index = (int)clientSlot - (int)eInventorySlot.HousingInventory_First - WornOffset;
            return index >= 0 && index < WornSlots.Length ? WornSlots[index] : eInventorySlot.Invalid;
        }

        public void Open()
        {
            _owner.ActiveInventoryObject?.RemoveObserver(_owner);
            _owner.ActiveInventoryObject = this;
            AddObserver(_owner);
            Refresh();
        }

        public void Refresh()
        {
            if (!ReferenceEquals(_owner.ActiveInventoryObject, this))
                return;

            Dictionary<int, DbInventoryItem> items = GetClientInventory(_owner);
            var slots = new Dictionary<int, DbInventoryItem>(VaultSize);
            for (int slot = (int)FirstClientSlot; slot <= (int)LastClientSlot; slot++)
                slots[slot] = items.GetValueOrDefault(slot);
            _owner.Out.SendInventoryItemsUpdate(slots, eInventoryWindowType.HouseVault);
        }

        public override bool CanHandleMove(GamePlayer player, eInventorySlot fromSlot, eInventorySlot toSlot) =>
            ReferenceEquals(player, _owner) && ReferenceEquals(player.ActiveInventoryObject, this) &&
            (IsVaultWindowSlot(fromSlot) || IsVaultWindowSlot(toSlot) ||
             toSlot == eInventorySlot.GeneralHousing && IsBackpack(fromSlot));

        public override bool MoveItem(GamePlayer player, eInventorySlot fromSlot, eInventorySlot toSlot, ushort count)
        {
            if (!CanHandleMove(player, fromSlot, toSlot))
                return false;

            if (!TryGetCompanion(player, out GameBot companion) ||
                !PlayerCompanionRoster.CanManageInventory(companion))
            {
                Tell("Inventory changes require an active, nearby companion while both of you are out of combat.");
                Refresh();
                return true;
            }

            eInventorySlot fromWorn = WornSlotAt(fromSlot);
            eInventorySlot toWorn = WornSlotAt(toSlot);
            bool fromCompanion = IsCompanionSlot(fromSlot) || fromWorn != eInventorySlot.Invalid;
            bool toCompanion = IsCompanionSlot(toSlot) || toSlot == eInventorySlot.GeneralHousing && IsBackpack(fromSlot);
            DbInventoryItem item = fromCompanion
                ? GetClientInventory(player).GetValueOrDefault((int)fromSlot)
                : IsBackpack(fromSlot) ? player.Inventory?.GetItem(fromSlot) : null;
            if (item == null || count > 0 && count < item.Count)
            {
                Tell("Choose one whole item in a valid backpack slot.");
                Refresh();
                return true;
            }

            bool moved = false;
            string message;
            if (fromWorn != eInventorySlot.Invalid)
            {
                if (toWorn != eInventorySlot.Invalid)
                    message = "Drag a worn item to an empty slot in the companion's backpack to unequip it.";
                else if (!IsCompanionSlot(toSlot))
                    message = "Unequip it into the companion's backpack first; from there it can move to yours.";
                else if (GetClientInventory(player).ContainsKey((int)toSlot))
                    message = "Choose an empty companion backpack slot to unequip this item.";
                else
                    moved = PersistentCompanionGear.TryUnequip(player, _companionId, fromWorn, item.ObjectId,
                        BackpackSlotAt(toSlot), out message);
            }
            else if (toWorn != eInventorySlot.Invalid)
            {
                if (!fromCompanion)
                    message = "Drag it into the companion's backpack first, then onto a worn slot.";
                else
                    moved = PersistentCompanionGear.TryEquip(player, _companionId, item.ObjectId, toWorn, out message);
            }
            else if (fromCompanion && toCompanion)
            {
                eInventorySlot destination = BackpackSlotAt(toSlot);
                if (fromSlot == toSlot || companion.Inventory.GetItem(destination) != null)
                    message = "Choose an empty companion backpack slot to move this item.";
                else
                    moved = PlayerCompanionRoster.TryApplyEquipmentMutation(companion,
                        () => companion.Inventory.GetItem((eInventorySlot)item.SlotPosition)?.ObjectId == item.ObjectId &&
                              companion.Inventory.GetItem(destination) == null &&
                              companion.Inventory.MoveItem((eInventorySlot)item.SlotPosition, destination, item.Count),
                        out message);
            }
            else if (!fromCompanion && toCompanion)
            {
                if (IsCompanionSlot(toSlot) && GetClientInventory(player).ContainsKey((int)toSlot))
                    message = "Choose an empty companion backpack slot.";
                else
                    moved = PlayerCompanionRoster.TryTransferItem(player, _companionId, item.ObjectId,
                        toCompanion: true, out message);
            }
            else if (fromCompanion && (IsBackpack(toSlot) || toSlot == eInventorySlot.GeneralHousing))
            {
                if (IsBackpack(toSlot) && player.Inventory.GetItem(toSlot) != null)
                    message = "Choose an empty slot in your backpack.";
                else
                    moved = PlayerCompanionRoster.TryTransferItem(player, _companionId, item.ObjectId,
                        toCompanion: false, out message);
            }
            else
                message = $"Use an empty backpack slot (positions 1-{BackpackSize}) or a worn slot " +
                          $"({FirstWornPosition}-{LastWornPosition}).";

            Tell(moved && string.IsNullOrWhiteSpace(message)
                ? $"Moved {item.Name} within {companion.Name}'s backpack."
                : message);
            if (moved)
                player.Out.SendInventorySlotsUpdate(Enumerable.Range((int)eInventorySlot.FirstBackpack, 40)
                    .Select(slot => (eInventorySlot)slot).ToArray());
            Refresh();
            return true;
        }

        private bool TryGetCompanion(GamePlayer player, out GameBot companion)
        {
            companion = null;
            return ReferenceEquals(player, _owner) &&
                   PlayerCompanionRoster.TryGetActiveCompanionById(player, _companionId, out companion);
        }

        /// <summary>True for the vault positions that show the companion's backpack.</summary>
        private bool IsCompanionSlot(eInventorySlot slot) =>
            slot >= FirstClientSlot && (int)slot < (int)FirstClientSlot + BackpackSize;

        private eInventorySlot BackpackSlotAt(eInventorySlot clientSlot) =>
            (eInventorySlot)((int)eInventorySlot.FirstBackpack + (int)clientSlot - (int)FirstClientSlot);

        private static bool IsVaultWindowSlot(eInventorySlot slot) =>
            slot >= eInventorySlot.HousingInventory_First && slot <= eInventorySlot.HousingInventory_Last;

        private static bool IsBackpack(eInventorySlot slot) =>
            slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack;

        private void Tell(string message) =>
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
