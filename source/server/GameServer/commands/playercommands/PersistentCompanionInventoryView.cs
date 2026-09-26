using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>Owner-only native vault view of one companion's backpack.</summary>
    internal sealed class PersistentCompanionInventoryView : GameVault
    {
        private readonly GamePlayer _owner;
        private readonly string _companionId;

        public PersistentCompanionInventoryView(GamePlayer owner, string companionId)
        {
            _owner = owner;
            _companionId = companionId;
        }

        public override int VaultSize => 40;
        public override int FirstDbSlot => (int)eInventorySlot.FirstBackpack;
        public override int LastDbSlot => (int)eInventorySlot.LastBackpack;
        public override eInventorySlot LastClientSlot => (eInventorySlot)((int)FirstClientSlot + VaultSize - 1);

        public override string GetOwner(GamePlayer player) =>
            ReferenceEquals(player, _owner) ? PlayerCompanionRoster.InventoryOwnerId(_companionId) : string.Empty;

        // Only what the owner can take out is shown; starter and protected gear
        // stays visible in the manager's Gear tab.
        public override IEnumerable<DbInventoryItem> GetDbItems(GamePlayer player) =>
            TryGetCompanion(player, out GameBot companion) && companion.Inventory != null
                ? PlayerCompanionRoster.OwnerTakeableBackpack(companion.PlayerCompanionRecord,
                    companion.Inventory.AllItems.Where(item => IsBackpack((eInventorySlot)item.SlotPosition))).ToArray()
                : Array.Empty<DbInventoryItem>();

        public override Dictionary<int, DbInventoryItem> GetClientInventory(GamePlayer player) =>
            GetDbItems(player).ToDictionary(item => (int)FirstClientSlot + item.SlotPosition - FirstDbSlot);

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

            string blocker = "Invite the companion into your group to trade gear.";
            if (!TryGetCompanion(player, out GameBot companion) ||
                !PlayerCompanionRoster.CanManageInventory(companion, out blocker))
            {
                Tell(blocker);
                Refresh();
                return true;
            }

            bool fromCompanion = IsCompanionSlot(fromSlot);
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
            if (fromCompanion && toCompanion)
            {
                eInventorySlot destination = (eInventorySlot)((int)eInventorySlot.FirstBackpack + (int)toSlot - (int)FirstClientSlot);
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
                message = "Use an empty backpack slot to transfer this item.";

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

        private bool IsCompanionSlot(eInventorySlot slot) => slot >= FirstClientSlot && slot <= LastClientSlot;

        private static bool IsVaultWindowSlot(eInventorySlot slot) =>
            slot >= eInventorySlot.HousingInventory_First && slot <= eInventorySlot.HousingInventory_Last;

        private static bool IsBackpack(eInventorySlot slot) =>
            slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack;

        private void Tell(string message) =>
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
