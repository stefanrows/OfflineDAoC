using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Owner-only native vault view of one companion's inventory: worn slots at positions
    /// 1-19 in character-sheet order, the backpack at positions 21-60. Drag from the owner's
    /// backpack onto a worn position to hand an item over and equip it.
    /// </summary>
    public sealed class PersistentCompanionInventoryView : GameVault
    {
        public const int BackpackFirstPosition = 21;
        private const int BackpackSize = 40;

        public enum Area { OwnerBackpack, Worn, CompanionBackpack, Other }

        public enum MoveKind { GiveAndEquip, UnequipToOwner, Equip, Unequip, WithinCompanion, GiveToCompanion, ReturnToOwner, Refused }

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

        /// <summary>1-based window position of a worn slot, or 0 when it is not shown.</summary>
        public static int WornPosition(eInventorySlot worn) => Array.IndexOf(PersistentCompanionGear.SheetSlots, worn) + 1;

        public static MoveKind ClassifyMove(Area from, Area to) => (from, to) switch
        {
            (Area.OwnerBackpack, Area.Worn) => MoveKind.GiveAndEquip,
            (Area.Worn, Area.OwnerBackpack) => MoveKind.UnequipToOwner,
            (Area.CompanionBackpack, Area.Worn) => MoveKind.Equip,
            (Area.Worn, Area.CompanionBackpack) => MoveKind.Unequip,
            (Area.CompanionBackpack, Area.CompanionBackpack) => MoveKind.WithinCompanion,
            (Area.OwnerBackpack, Area.CompanionBackpack) => MoveKind.GiveToCompanion,
            (Area.CompanionBackpack, Area.OwnerBackpack) => MoveKind.ReturnToOwner,
            _ => MoveKind.Refused,
        };

        /// <summary>
        /// Where a drop lands. A shift-right-click (GeneralHousing) sends the item to the
        /// other side: owner items to the companion's backpack, companion items to the owner.
        /// </summary>
        public static Area TargetArea(Area from, Area toArea, bool toGeneralHousing) =>
            !toGeneralHousing ? toArea
            : from == Area.OwnerBackpack ? Area.CompanionBackpack
            : from is Area.Worn or Area.CompanionBackpack ? Area.OwnerBackpack
            : Area.Other;

        public override string GetOwner(GamePlayer player) =>
            ReferenceEquals(player, _owner) ? PlayerCompanionRoster.InventoryOwnerId(_companionId) : string.Empty;

        public override IEnumerable<DbInventoryItem> GetDbItems(GamePlayer player) => GetClientInventory(player).Values;

        // Worn gear is always shown so the slots are complete; the backpack shows only what
        // the owner can take out. Starter and protected backpack items stay in the Gear tab.
        public override Dictionary<int, DbInventoryItem> GetClientInventory(GamePlayer player)
        {
            var items = new Dictionary<int, DbInventoryItem>();
            if (!TryGetCompanion(player, out GameBot companion) || companion.Inventory == null)
                return items;
            foreach (eInventorySlot worn in PersistentCompanionGear.SheetSlots)
                if (companion.Inventory.GetItem(worn) is DbInventoryItem item)
                    items[ClientSlotAt(WornPosition(worn))] = item;
            foreach (DbInventoryItem item in PlayerCompanionRoster.OwnerTakeableBackpack(companion.PlayerCompanionRecord,
                         companion.Inventory.AllItems.Where(entry => IsBackpack((eInventorySlot)entry.SlotPosition))))
                items[ClientSlotAt(BackpackFirstPosition + item.SlotPosition - (int)eInventorySlot.FirstBackpack)] = item;
            return items;
        }

        public void Open()
        {
            _owner.ActiveInventoryObject?.RemoveObserver(_owner);
            _owner.ActiveInventoryObject = this;
            AddObserver(_owner);
            Refresh(open: true);
        }

        /// <summary>
        /// Re-sends every slot. Only opening uses the house-vault window type:
        /// sent again after a move it made the client re-open the window and
        /// jump back to the top (bug 44), so updates are slot-only.
        /// </summary>
        public void Refresh(bool open = false)
        {
            if (!ReferenceEquals(_owner.ActiveInventoryObject, this))
                return;

            Dictionary<int, DbInventoryItem> items = GetClientInventory(_owner);
            var slots = new Dictionary<int, DbInventoryItem>(VaultSize);
            for (int slot = (int)FirstClientSlot; slot <= (int)LastClientSlot; slot++)
                slots[slot] = items.GetValueOrDefault(slot);
            _owner.Out.SendInventoryItemsUpdate(slots, open ? eInventoryWindowType.HouseVault : eInventoryWindowType.Update);
        }

        public override bool CanHandleMove(GamePlayer player, eInventorySlot fromSlot, eInventorySlot toSlot) =>
            ReferenceEquals(player, _owner) && ReferenceEquals(player.ActiveInventoryObject, this) &&
            (IsVaultWindowSlot(fromSlot) || IsVaultWindowSlot(toSlot) ||
             toSlot == eInventorySlot.GeneralHousing && IsBackpack(fromSlot));

        public override bool MoveItem(GamePlayer player, eInventorySlot fromSlot, eInventorySlot toSlot, ushort count)
        {
            if (!CanHandleMove(player, fromSlot, toSlot))
                return false;

            string blocker = "Invite the companion into your group to trade gear.";
            if (!TryGetCompanion(player, out GameBot companion) ||
                !PlayerCompanionRoster.CanManageInventory(companion, out blocker))
            {
                Tell(blocker);
                Refresh();
                return true;
            }

            Area from = AreaOf(fromSlot);
            Area to = TargetArea(from, AreaOf(toSlot), toSlot == eInventorySlot.GeneralHousing);
            DbInventoryItem item = from == Area.OwnerBackpack
                ? player.Inventory?.GetItem(fromSlot)
                : GetClientInventory(player).GetValueOrDefault((int)fromSlot);
            if (item == null || count > 0 && count < item.Count)
            {
                Tell("Choose one whole item in a valid slot.");
                Refresh();
                return true;
            }

            bool moved = false;
            string message;
            switch (ClassifyMove(from, to))
            {
                case MoveKind.GiveAndEquip:
                    moved = PersistentCompanionGear.TryGiveAndEquip(player, _companionId, item.ObjectId, WornSlotAt(toSlot), out message);
                    break;
                case MoveKind.UnequipToOwner:
                    moved = PersistentCompanionGear.TryUnequipToOwner(player, _companionId, WornSlotAt(fromSlot), item.ObjectId, out message);
                    break;
                case MoveKind.Equip:
                    eInventorySlot worn = WornSlotAt(toSlot);
                    if (!PersistentCompanionGear.FitsSlot(companion.GetManualEquipmentSlot(item), worn))
                        message = $"{item.Name} does not fit the {PersistentCompanionGear.SlotName(worn)} slot.";
                    else
                        moved = PersistentCompanionGear.TryEquip(player, _companionId, item.ObjectId, worn, out message);
                    break;
                case MoveKind.Unequip:
                    moved = PersistentCompanionGear.TryUnequip(player, _companionId, WornSlotAt(fromSlot), item.ObjectId, out message);
                    break;
                case MoveKind.WithinCompanion:
                    eInventorySlot destination = BackpackSlotAt(toSlot);
                    if (fromSlot == toSlot || companion.Inventory.GetItem(destination) != null)
                        message = companion.Inventory.GetItem(destination) != null && !GetClientInventory(player).ContainsKey((int)toSlot)
                            ? "That slot holds gear that stays with the companion; see the Gear tab. Choose another slot."
                            : "Choose an empty companion backpack slot to move this item.";
                    else
                        moved = PlayerCompanionRoster.TryApplyEquipmentMutation(companion,
                            () => companion.Inventory.GetItem((eInventorySlot)item.SlotPosition)?.ObjectId == item.ObjectId &&
                                  companion.Inventory.GetItem(destination) == null &&
                                  companion.Inventory.MoveItem((eInventorySlot)item.SlotPosition, destination, item.Count),
                            out message);
                    if (moved && string.IsNullOrWhiteSpace(message))
                        message = $"Moved {item.Name} within {companion.Name}'s backpack.";
                    break;
                case MoveKind.GiveToCompanion:
                    if (IsVaultWindowSlot(toSlot) && GetClientInventory(player).ContainsKey((int)toSlot))
                        message = "Choose an empty companion backpack slot.";
                    else
                        moved = PlayerCompanionRoster.TryTransferItem(player, _companionId, item.ObjectId,
                            toCompanion: true, out message);
                    break;
                case MoveKind.ReturnToOwner:
                    if (IsBackpack(toSlot) && player.Inventory.GetItem(toSlot) != null)
                        message = "Choose an empty slot in your backpack.";
                    else
                        moved = PlayerCompanionRoster.TryTransferItem(player, _companionId, item.ObjectId,
                            toCompanion: false, out message);
                    break;
                default:
                    message = "Drag between your backpack, the worn slots (1-19) and the companion's backpack (21-60).";
                    break;
            }

            Tell(message);
            if (moved)
                player.Out.SendInventorySlotsUpdate(Enumerable.Range((int)eInventorySlot.FirstBackpack, BackpackSize)
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

        private Area AreaOf(eInventorySlot slot)
        {
            if (IsBackpack(slot))
                return Area.OwnerBackpack;
            if (!IsVaultWindowSlot(slot))
                return Area.Other;
            int position = PositionOf(slot);
            if (position >= 1 && position <= PersistentCompanionGear.SheetSlots.Length)
                return Area.Worn;
            return position >= BackpackFirstPosition && position < BackpackFirstPosition + BackpackSize
                ? Area.CompanionBackpack
                : Area.Other;
        }

        private int PositionOf(eInventorySlot clientSlot) => (int)clientSlot - (int)FirstClientSlot + 1;

        private int ClientSlotAt(int position) => (int)FirstClientSlot + position - 1;

        private eInventorySlot WornSlotAt(eInventorySlot clientSlot) => PersistentCompanionGear.SheetSlots[PositionOf(clientSlot) - 1];

        private eInventorySlot BackpackSlotAt(eInventorySlot clientSlot) =>
            (eInventorySlot)((int)eInventorySlot.FirstBackpack + PositionOf(clientSlot) - BackpackFirstPosition);

        private static bool IsVaultWindowSlot(eInventorySlot slot) =>
            slot >= eInventorySlot.HousingInventory_First && slot <= eInventorySlot.HousingInventory_Last;

        private static bool IsBackpack(eInventorySlot slot) =>
            slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack;

        private void Tell(string message) =>
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
