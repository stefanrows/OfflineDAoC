using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS
{

    /// <summary>Owns the durable roster of companions recruited by player characters.</summary>
    public static class PlayerCompanionRoster
    {
        public const int MaximumRosterSize = 78;
        private const string InventoryOwnerPrefix = "playercompanion:";
        internal static readonly long MaximumOwnerMoney = Money.GetMoney(999, 999, 999, 99, 99);
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<string, GameBot> ActiveCompanions = new(StringComparer.OrdinalIgnoreCase);

        public static string GetEquipmentItemFlags(PlayerCompanionRecord record, string itemId)
        {
            if (record == null || string.IsNullOrWhiteSpace(itemId))
                return string.Empty;
            lock (record)
            {
                return ParseEquipmentState(record.SerializedEquipmentState)
                    .TryGetValue("i:" + itemId, out string flags) ? flags : string.Empty;
            }
        }

        public static bool SetEquipmentItemFlags(PlayerCompanionRecord record, string itemId, string flags)
        {
            if (record == null || string.IsNullOrWhiteSpace(itemId))
                return false;
            lock (record)
            {
                Dictionary<string, string> state = ParseEquipmentState(record.SerializedEquipmentState);
                string key = "i:" + itemId;
                if (string.IsNullOrEmpty(flags))
                    state.Remove(key);
                else
                    state[key] = flags;
                WriteEquipmentState(record, state);
                return true;
            }
        }

        public static bool IsEquipmentSlotLocked(PlayerCompanionRecord record, eInventorySlot slot)
        {
            if (record == null)
                return false;
            lock (record)
                return ParseEquipmentState(record.SerializedEquipmentState).ContainsKey("s:" + (int)slot);
        }

        public static bool SetEquipmentSlotLocked(PlayerCompanionRecord record, eInventorySlot slot, bool locked)
        {
            if (record == null)
                return false;
            lock (record)
            {
                Dictionary<string, string> state = ParseEquipmentState(record.SerializedEquipmentState);
                string key = "s:" + (int)slot;
                if (locked)
                    state[key] = "L";
                else
                    state.Remove(key);
                WriteEquipmentState(record, state);
                return true;
            }
        }

        public static bool TryApplyEquipmentMutation(GameBot companion, Func<bool> mutation, out string error,
            int requiredFreeBackpackSlots = 0, string excludeItemId = null)
        {
            error = "The equipment change could not be saved.";
            if (!CanManageInventory(companion) || companion.Inventory is not BotInventory inventory || mutation == null ||
                companion.Owner?.DBCharacter == null ||
                GameServer.Database is not SqlObjectDatabase database)
            {
                error = "Inventory changes require an active, nearby companion while both of you are out of combat.";
                return false;
            }

            PlayerCompanionRecord record = companion.PlayerCompanionRecord;
            GamePlayer owner = companion.Owner;
            long saleCopper = 0;
            long creditedMoney = owner.GetCurrentMoney();
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            lock (inventory.Lock)
            {
                if (!CanManageInventory(companion))
                {
                    error = "The companion moved or combat started before the equipment change.";
                    return false;
                }

                var originalItems = inventory.AllItems.Select(item =>
                    (Item: item, Slot: item.SlotPosition, OwnerId: item.OwnerID)).ToArray();
                string originalEquipmentState = record.SerializedEquipmentState;
                string originalUpdatedUtc = record.UpdatedUtc;
                int previousCopper = owner.DBCharacter.Copper;
                int previousSilver = owner.DBCharacter.Silver;
                int previousGold = owner.DBCharacter.Gold;
                int previousPlatinum = owner.DBCharacter.Platinum;
                int previousMithril = owner.DBCharacter.Mithril;
                var soldItems = new List<DbInventoryItem>();

                while (CountEmptyBackpackSlots(inventory) < requiredFreeBackpackSlots)
                {
                    DbInventoryItem candidate = PlayerCompanionGearRewards.FindSurplusForSpace(
                        companion, inventory, out long candidateCopper, excludeItemId);
                    if (candidate == null || !candidate.IsPersisted || candidateCopper <= 0 ||
                        creditedMoney > MaximumOwnerMoney - candidateCopper ||
                        !inventory.RemoveItemWithoutDbDeletion(candidate))
                    {
                        RestoreInventory(inventory, originalItems);
                        record.SerializedEquipmentState = originalEquipmentState;
                        record.UpdatedUtc = originalUpdatedUtc;
                        record.Dirty = true;
                        error = "The backpack needs space, but no sellable companion-earned item can safely make it.";
                        return false;
                    }

                    soldItems.Add(candidate);
                    saleCopper += candidateCopper;
                    creditedMoney += candidateCopper;
                    SetEquipmentItemFlags(record, candidate.ObjectId, string.Empty);
                }

                bool changed;
                try
                {
                    changed = mutation();
                }
                catch
                {
                    changed = false;
                }

                if (!changed)
                {
                    RestoreInventory(inventory, originalItems);
                    record.SerializedEquipmentState = originalEquipmentState;
                    record.UpdatedUtc = originalUpdatedUtc;
                    record.Dirty = true;
                    if (saleCopper > 0)
                        RestoreOwnerCoins(owner, previousCopper, previousSilver, previousGold, previousPlatinum, previousMithril);
                    return false;
                }

                DbInventoryItem[] movedItems = inventory.AllItems.Where(item =>
                        originalItems.Any(original => ReferenceEquals(original.Item, item) &&
                            (original.Slot != item.SlotPosition || !string.Equals(original.OwnerId, item.OwnerID, StringComparison.Ordinal))))
                    .ToArray();
                if (movedItems.Any(item => !item.IsPersisted))
                {
                    RestoreInventory(inventory, originalItems);
                    record.SerializedEquipmentState = originalEquipmentState;
                    record.UpdatedUtc = originalUpdatedUtc;
                    record.Dirty = true;
                    if (saleCopper > 0)
                        RestoreOwnerCoins(owner, previousCopper, previousSilver, previousGold, previousPlatinum, previousMithril);
                    error = "The equipment change includes an unsaved item; try again after its save completes.";
                    return false;
                }

                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                if (saleCopper > 0)
                {
                    owner.DBCharacter.Copper = Money.GetCopper(creditedMoney);
                    owner.DBCharacter.Silver = Money.GetSilver(creditedMoney);
                    owner.DBCharacter.Gold = Money.GetGold(creditedMoney);
                    owner.DBCharacter.Platinum = Money.GetPlatinum(creditedMoney);
                    owner.DBCharacter.Mithril = Money.GetMithril(creditedMoney);
                }

                DataObject[] updates = new DataObject[] { record }.Concat(movedItems)
                    .Concat(saleCopper > 0 ? [owner.DBCharacter] : []).ToArray();
                bool saved = soldItems.Count == 0
                    ? database.SaveObjectsAtomically(updates)
                    : database.UpdateAndDeleteObjectsAtomically(updates, soldItems);
                if (!saved)
                {
                    RestoreInventory(inventory, originalItems);
                    record.SerializedEquipmentState = originalEquipmentState;
                    record.UpdatedUtc = originalUpdatedUtc;
                    record.Dirty = true;
                    if (saleCopper > 0)
                        RestoreOwnerCoins(owner, previousCopper, previousSilver, previousGold, previousPlatinum, previousMithril);
                    error = "The equipment change failed its atomic save and was rolled back.";
                    return false;
                }
            }

            if (saleCopper > 0)
            {
                owner.SetCurrentMoneyAfterAtomicPersistence(creditedMoney);
                owner.Out.SendUpdateMoney();
                owner.Out.SendMessage($"Surplus companion gear sold for {Money.GetString(saleCopper)} to make backpack space.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
            error = string.Empty;
            return true;
        }

        private static int CountEmptyBackpackSlots(IGameInventory inventory) =>
            Enumerable.Range((int)eInventorySlot.FirstBackpack,
                    (int)eInventorySlot.LastBackpack - (int)eInventorySlot.FirstBackpack + 1)
                .Count(value => inventory.GetItem((eInventorySlot)value) == null);

        private static void RestoreOwnerCoins(GamePlayer owner, int copper, int silver, int gold,
            int platinum, int mithril)
        {
            owner.DBCharacter.Copper = copper;
            owner.DBCharacter.Silver = silver;
            owner.DBCharacter.Gold = gold;
            owner.DBCharacter.Platinum = platinum;
            owner.DBCharacter.Mithril = mithril;
        }

        public static bool CanManageInventory(GameBot companion)
        {
            GamePlayer owner = companion?.Owner;
            return companion?.IsPersistentPlayerCompanion == true &&
                   companion.ObjectState == GameObject.eObjectState.Active &&
                   owner?.ObjectState == GameObject.eObjectState.Active &&
                   owner.Group != null && owner.Group == companion.Group &&
                   owner.Group.IsInTheGroup(companion) &&
                   owner.CurrentRegion == companion.CurrentRegion &&
                   owner.IsWithinRadius(companion, ServerProperties.Properties.WORLD_PICKUP_DISTANCE) &&
                   !owner.InCombat && !companion.InCombat && !companion.IsAttacking && !companion.IsCasting &&
                   !companion.IsOnStableMasterRoute && companion.Brain is not BotBrain { HasAggro: true } &&
                   companion.ControlledBrain?.Body is not { InCombat: true };
        }

        private static void RestoreInventory(IGameInventory inventory,
            (DbInventoryItem Item, int Slot, string OwnerId)[] originalItems)
        {
            foreach (DbInventoryItem item in inventory.AllItems.ToArray())
                inventory.RemoveItemWithoutDbDeletion(item);
            foreach ((DbInventoryItem item, int slot, string ownerId) in originalItems)
            {
                item.OwnerID = ownerId;
                item.SlotPosition = slot;
                inventory.AddItemWithoutDbAddition((eInventorySlot)slot, item);
            }
        }

        public static bool TryTransferItem(GamePlayer owner, string companionNameOrId, string itemId,
            bool toCompanion, out string message)
        {
            message = "The item could not be transferred.";
            if (owner == null || string.IsNullOrWhiteSpace(itemId) ||
                !TryGetActiveCompanion(owner, companionNameOrId, out GameBot companion) ||
                companion.Owner != owner || owner.Inventory == null || owner.DBCharacter == null ||
                companion.Inventory is not BotInventory botInventory)
            {
                message = "Invite the companion first and keep them nearby to transfer gear.";
                return false;
            }

            if (!CanManageInventory(companion))
            {
                message = "Gear transfers require a nearby companion while you are out of combat.";
                return false;
            }

            PlayerCompanionRecord record = companion.PlayerCompanionRecord;
            IGameInventory source = toCompanion ? owner.Inventory : botInventory;
            IGameInventory destination = toCompanion ? botInventory : owner.Inventory;
            string expectedOwnerId = toCompanion ? owner.InternalID : InventoryOwnerId(record.CompanionId);
            DbInventoryItem item = source.AllItems.FirstOrDefault(candidate =>
                string.Equals(candidate?.ObjectId, itemId, StringComparison.Ordinal));
            if (item == null || item.OwnerID != expectedOwnerId || !CanTransferItem(item, out _))
            {
                message = CanTransferItem(item, out string blocker)
                    ? "That item is not owned by the expected backpack."
                    : $"That item cannot be transferred: {blocker}.";
                return false;
            }

            if (!toCompanion && !CanReturnItemToOwner(item, record, out string returnBlocker))
            {
                message = returnBlocker;
                return false;
            }

            if (!item.IsPersisted)
            {
                message = "Save this item to your inventory before transferring it.";
                return false;
            }

            eInventorySlot destinationSlot = destination.FindFirstEmptySlot(
                eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            long saleProceeds = 0;
            if (destinationSlot == eInventorySlot.Invalid && !toCompanion)
            {
                message = "The destination backpack is full.";
                return false;
            }

            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            lock (owner.Inventory.Lock)
            lock (botInventory.Lock)
            {
                if (!CanManageInventory(companion) || companion.Owner != owner ||
                    !source.AllItems.Contains(item) || item.OwnerID != expectedOwnerId ||
                    !CanTransferItem(item, out _))
                {
                    message = CanTransferItem(item, out string blocker)
                        ? "The item or companion changed before the transfer could finish. Check ownership, range, and combat state."
                        : $"The item became ineligible for transfer: {blocker}.";
                    return false;
                }

                if (!toCompanion && !CanReturnItemToOwner(item, record, out returnBlocker))
                {
                    message = returnBlocker;
                    return false;
                }

                destinationSlot = destination.FindFirstEmptySlot(
                    eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                DbInventoryItem soldItem = null;
                long soldCopper = 0;
                if (destinationSlot == eInventorySlot.Invalid && toCompanion)
                {
                    soldItem = PlayerCompanionGearRewards.FindSurplusForSpace(
                        companion, botInventory, out soldCopper);
                    if (soldItem == null || !soldItem.IsPersisted || owner.DBCharacter == null ||
                        soldCopper <= 0 || owner.GetCurrentMoney() > MaximumOwnerMoney - soldCopper)
                    {
                        message = "The companion backpack is full and has no sellable earned gear. Keep or protected items were left untouched.";
                        return false;
                    }
                    destinationSlot = (eInventorySlot)soldItem.SlotPosition;
                }
                if (destinationSlot == eInventorySlot.Invalid)
                {
                    message = "The destination backpack is full.";
                    return false;
                }

                var ownerItems = owner.Inventory.AllItems.Select(existing =>
                    (Item: existing, Slot: existing.SlotPosition, OwnerId: existing.OwnerID)).ToArray();
                var companionItems = botInventory.AllItems.Select(existing =>
                    (Item: existing, Slot: existing.SlotPosition, OwnerId: existing.OwnerID)).ToArray();
                string previousEquipmentState = record.SerializedEquipmentState;
                string previousUpdatedUtc = record.UpdatedUtc;
                int previousCopper = owner.DBCharacter.Copper;
                int previousSilver = owner.DBCharacter.Silver;
                int previousGold = owner.DBCharacter.Gold;
                int previousPlatinum = owner.DBCharacter.Platinum;
                int previousMithril = owner.DBCharacter.Mithril;

                bool removedSaleItem = soldItem == null || botInventory.RemoveItemWithoutDbDeletion(soldItem);
                if (!removedSaleItem || !source.RemoveItemWithoutDbDeletion(item) ||
                    !destination.AddItemWithoutDbAddition(destinationSlot, item))
                {
                    RestoreInventory(owner.Inventory, ownerItems);
                    RestoreInventory(botInventory, companionItems);
                    if (soldItem != null)
                        RestoreOwnerCoins(owner, previousCopper, previousSilver, previousGold, previousPlatinum, previousMithril);
                    message = "The inventories changed before the transfer could finish. Try again.";
                    return false;
                }

                item.OwnerID = toCompanion ? InventoryOwnerId(record.CompanionId) : owner.InternalID;
                item.SlotPosition = (int)destinationSlot;
                if (toCompanion)
                    SetEquipmentItemFlags(record, item.ObjectId, "P");
                else
                    SetEquipmentItemFlags(record, item.ObjectId, string.Empty);
                if (soldItem != null)
                    SetEquipmentItemFlags(record, soldItem.ObjectId, string.Empty);
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");

                long creditedMoney = 0;
                if (soldItem != null)
                {
                    saleProceeds = soldCopper;
                    creditedMoney = owner.GetCurrentMoney() + soldCopper;
                    owner.DBCharacter.Copper = Money.GetCopper(creditedMoney);
                    owner.DBCharacter.Silver = Money.GetSilver(creditedMoney);
                    owner.DBCharacter.Gold = Money.GetGold(creditedMoney);
                    owner.DBCharacter.Platinum = Money.GetPlatinum(creditedMoney);
                    owner.DBCharacter.Mithril = Money.GetMithril(creditedMoney);
                }

                DataObject[] updates = soldItem == null
                    ? [item, record]
                    : [item, record, owner.DBCharacter];
                bool saved = GameServer.Database is SqlObjectDatabase database &&
                    (soldItem == null
                        ? database.SaveObjectsAtomically(updates)
                        : database.UpdateAndDeleteObjectsAtomically(updates, [soldItem]));
                if (!saved)
                {
                    RestoreInventory(owner.Inventory, ownerItems);
                    RestoreInventory(botInventory, companionItems);
                    record.SerializedEquipmentState = previousEquipmentState;
                    record.UpdatedUtc = previousUpdatedUtc;
                    record.Dirty = true;
                    if (soldItem != null)
                        RestoreOwnerCoins(owner, previousCopper, previousSilver, previousGold, previousPlatinum, previousMithril);
                    message = "The transfer could not be saved atomically. The item was returned to its original backpack.";
                    return false;
                }

                if (soldItem != null)
                    owner.SetCurrentMoneyAfterAtomicPersistence(creditedMoney);
            }

            companion.RefreshItemBonuses();
            if (saleProceeds > 0)
                owner.Out.SendUpdateMoney();
            message = toCompanion
                ? saleProceeds > 0
                    ? $"Transferred {item.Name} to {companion.Name}; surplus gear sold for {Money.GetString(saleProceeds)}. The transferred item is protected as player-supplied gear."
                    : $"Transferred {item.Name} to {companion.Name}. It is protected as player-supplied gear."
                : $"Returned {item.Name} from {companion.Name} to your backpack.";
            return true;
        }

        private static Dictionary<string, string> ParseEquipmentState(string serialized)
        {
            var state = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string entry in (serialized ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator > 0 && separator < entry.Length - 1)
                    state[entry[..separator]] = entry[(separator + 1)..];
            }
            return state;
        }

        private static void WriteEquipmentState(PlayerCompanionRecord record, Dictionary<string, string> state)
        {
            record.SerializedEquipmentState = string.Join(';', state.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
            record.UpdatedUtc = DateTime.UtcNow.ToString("O");
            record.Dirty = true;
        }

        public static string InventoryOwnerId(string companionId)
        {
            return InventoryOwnerPrefix + companionId;
        }

        public static bool CanTransferItem(DbInventoryItem item, out string blocker)
        {
            if (item == null)
                blocker = "the item reference is no longer valid";
            else if (item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
                blocker = "only unequipped backpack items can be transferred";
            else if (!item.IsPersisted)
                blocker = "the item must finish saving before it can be transferred";
            else if (item.OwnerLot != 0 || !item.IsDropable || !item.IsTradable || item is GameInventoryRelic ||
                     BotSiegeRuntime.IsSupply(item.Id_nb))
                blocker = "special, quest, relic, siege, or otherwise restricted items cannot use this transfer path";
            else
                blocker = string.Empty;
            return blocker.Length == 0;
        }

        public static bool CanReturnItemToOwner(DbInventoryItem item, PlayerCompanionRecord record, out string blocker)
        {
            if (!CanTransferItem(item, out blocker))
                return false;
            string flags = GetEquipmentItemFlags(record, item.ObjectId);
            if (flags.Contains('S'))
                blocker = "recruitment starter gear stays with its original companion";
            else if (!flags.Contains('E') && !flags.Contains('P'))
                blocker = "legacy ownership is unknown, so this item is protected from outward transfer";
            return blocker.Length == 0;
        }

        public static bool TrySetTactics(GamePlayer owner, string nameOrId, string kind, string value,
            out string message)
        {
            message = "That companion name or ID is not in your roster.";
            if (owner == null)
                return false;
            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                    return false;
                if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) &&
                    active?.PlayerCompanionRecord != null)
                    record = active.PlayerCompanionRecord;
                string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
                string previous;
                string previousUpdatedUtc = record.UpdatedUtc;
                if (kind == "role")
                {
                    if (normalized is not ("tank" or "healer" or "buffer" or "attacker") ||
                        !Enum.TryParse(normalized, true, out BotPveGroupRole role) ||
                        !BotPartyRoles.CanFill((eCharacterClass)record.ClassId, role))
                    {
                        message = $"{record.Name} cannot fill that role. Choose a class-legal tank, healer, buffer, or attacker role.";
                        return false;
                    }
                    previous = record.TacticalRole;
                    record.TacticalRole = normalized;
                }
                else if (kind == "stance")
                {
                    if (normalized is not ("aggressive" or "defensive" or "passive"))
                    {
                        message = "Choose aggressive, defensive, or passive stance.";
                        return false;
                    }
                    previous = record.EngagementPreference;
                    record.EngagementPreference = normalized;
                }
                else
                    return false;
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                if (!SaveRecord(record))
                {
                    if (kind == "role") record.TacticalRole = previous;
                    else record.EngagementPreference = previous;
                    record.UpdatedUtc = previousUpdatedUtc;
                    record.Dirty = true;
                    message = "The preference could not be saved; the previous setting remains active.";
                    return false;
                }
                if (kind == "stance") CompanionPvpEngagement.Reset(owner);
                if (active?.Brain is DOL.AI.Brain.BotBrain brain)
                {
                    brain.EnforceCompanionEngagementRange();
                    brain.RegroupWithLeader();
                }
                message = $"{record.Name}: {kind} set to {normalized}.";
                return true;
            }
        }

        public static List<PlayerCompanionRecord> GetRoster(GamePlayer owner)
        {
            return TryGetRoster(owner, out List<PlayerCompanionRecord> records)
                ? records
                : new List<PlayerCompanionRecord>();
        }

        public static bool TryGetActiveCompanion(GamePlayer owner, string nameOrId, out GameBot companion)
        {
            companion = null;
            if (owner == null)
                return false;

            PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
            if (record == null || !ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) ||
                active?.Owner != owner || active.ObjectState != GameObject.eObjectState.Active)
            {
                return false;
            }

            companion = active;
            return true;
        }

        public static bool TryGetActiveCompanionById(GamePlayer owner, string companionId, out GameBot companion) =>
            TryGetActiveCompanion(owner, companionId, out companion);

        public static bool TryMatchOwnedCompanionPrefix(GamePlayer owner, string[] arguments, int startIndex,
            int endExclusive, out PlayerCompanionRecord record, out int consumedTokens)
        {
            record = null;
            consumedTokens = 0;
            if (arguments == null || startIndex < 0 || endExclusive > arguments.Length || startIndex >= endExclusive ||
                !TryGetRoster(owner, out List<PlayerCompanionRecord> roster))
            {
                return false;
            }

            for (int tokenCount = endExclusive - startIndex; tokenCount > 0; tokenCount--)
            {
                string candidate = string.Join(' ', arguments.Skip(startIndex).Take(tokenCount));
                PlayerCompanionRecord match = roster.FirstOrDefault(entry =>
                    string.Equals(entry.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.CompanionId, candidate, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    continue;

                record = match;
                consumedTokens = tokenCount;
                return true;
            }

            return false;
        }

        public static bool TrySetManualTrainingMode(GamePlayer owner, string nameOrId, out string message)
        {
            if (owner == null)
            {
                message = "Your character could not be found.";
                return false;
            }

            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                {
                    message = "That companion name or ID is not in your roster. Type /companions list to see names.";
                    return false;
                }

                if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) && active?.Owner == owner)
                    record = active.PlayerCompanionRecord;

                if (record.TrainingMode == "manual")
                {
                    message = $"{record.Name} is already in manual training mode. Earned specialization points stay unspent until you train them.";
                    return true;
                }

                string previousMode = record.TrainingMode;
                string previousPlan = record.TrainingPlanId;
                record.TrainingMode = "manual";
                record.TrainingPlanId = string.Empty;
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                bool saved = SaveRecord(record);
                if (!saved)
                {
                    record.TrainingMode = previousMode;
                    record.TrainingPlanId = previousPlan;
                    record.Dirty = true;
                }
                message = saved
                    ? $"{record.Name} is in manual training mode. Existing specialization allocations were preserved."
                    : $"{record.Name}'s training mode could not be saved. No mode change was applied.";
                return saved;
            }
        }

        public static string AutomaticTrainingBlocker(eCharacterClass characterClass) =>
            CompanionBuildPlanCatalog.GetBlocker(characterClass);

        public static bool TrySetAutomaticTrainingMode(GamePlayer owner, string nameOrId, out string message)
        {
            if (owner == null)
            {
                message = "Your character could not be found.";
                return false;
            }

            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                {
                    message = "That companion name or ID is not in your roster.";
                    return false;
                }

                eCharacterClass characterClass = (eCharacterClass)record.ClassId;
                string savedPlanId = ActiveCompanions.TryGetValue(record.CompanionId, out GameBot live) && live?.Owner == owner
                    ? live.PlayerCompanionRecord.TrainingPlanId
                    : record.TrainingPlanId;
                // Keep a still-valid saved build; otherwise follow the class default.
                if (!CompanionBuildPlanCatalog.TryGetPlanById(characterClass, savedPlanId, out CompanionBuildPlan plan) &&
                    !CompanionBuildPlanCatalog.TryGetPlan(characterClass, out plan))
                {
                    message = $"Automatic training is unavailable for {record.Name}: {AutomaticTrainingBlocker(characterClass)}";
                    return false;
                }

                ICharacterClass runtimeClass = ScriptMgr.FindCharacterClass(record.ClassId);
                if (runtimeClass == null || runtimeClass.SpecPointsMultiplier != plan.ExpectedSpecPointsMultiplier)
                {
                    message = $"Automatic training is unavailable for {record.Name}: runtime class multiplier does not match the validated {plan.ExpectedSpecPointsMultiplier}-point plan.";
                    return false;
                }
                if (!CompanionBuildPlanCatalog.TryValidateRuntimePlan(characterClass, plan, out string runtimeBlocker))
                {
                    message = $"Automatic training is unavailable for {record.Name}: {runtimeBlocker}.";
                    return false;
                }

                if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) && active?.Owner == owner)
                {
                    return active.TryEnableAutomaticCompanionPlan(plan, out message);
                }

                string previousMode = record.TrainingMode;
                string previousPlan = record.TrainingPlanId;
                string previousSpecs = record.SerializedSpecs;
                int previousUnspent = record.UnspentSpecPoints;
                var currentSpecs = ParseSerializedCompanionSpecs(record.SerializedSpecs);
                IReadOnlyDictionary<string, int> targets = plan.GetTargetsAtLevel(record.Level,
                    plan.ExpectedSpecPointsMultiplier);
                foreach (KeyValuePair<string, int> current in currentSpecs)
                {
                    int target = targets.TryGetValue(current.Key, out int planned) ? planned : 1;
                    if (current.Value > target)
                    {
                        message = $"{record.Name}'s {current.Key} {current.Value} allocation is above the validated level-{record.Level} schedule ({target}). Keep manual mode or use the explicit respec flow before switching.";
                        return false;
                    }
                }

                int pointsNeeded = 0;
                foreach (CompanionBuildRank rank in plan.TargetAllocations)
                {
                    int current = currentSpecs.TryGetValue(rank.Specialization, out int value) ? value : 1;
                    pointsNeeded += CompanionBuildPlan.CostToReach(current, targets[rank.Specialization]);
                }
                if (pointsNeeded > record.UnspentSpecPoints)
                {
                    message = $"{record.Name}'s current build needs {pointsNeeded} more points to reach the validated schedule, but only {record.UnspentSpecPoints} are available. Keep manual mode or use the explicit respec flow.";
                    return false;
                }

                foreach (CompanionBuildRank rank in plan.TargetAllocations)
                    currentSpecs[rank.Specialization] = targets[rank.Specialization];
                record.SerializedSpecs = string.Join(';', currentSpecs.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key}|{pair.Value}"));
                record.UnspentSpecPoints -= pointsNeeded;
                record.TrainingMode = "automatic";
                record.TrainingPlanId = plan.Id;
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                bool saved = SaveRecord(record);
                if (!saved)
                {
                    record.TrainingMode = previousMode;
                    record.TrainingPlanId = previousPlan;
                    record.SerializedSpecs = previousSpecs;
                    record.UnspentSpecPoints = previousUnspent;
                    record.Dirty = true;
                }
                message = saved
                    ? $"{record.Name} is following the {plan.Name} build ({plan.Role}); spent {pointsNeeded} points, {record.UnspentSpecPoints} remain."
                    : $"{record.Name}'s training mode could not be saved; the previous mode remains active.";
                return saved;
            }
        }

        /// <summary>
        /// Selects a companion's build and switches it to automatic training.
        /// Owner decision (M1): the switch is free, needs no trainer, and
        /// retrains the new build to the companion's current level.
        /// </summary>
        public static bool TrySelectBuild(GamePlayer owner, string nameOrId, string buildQuery, out string message)
        {
            if (owner == null)
            {
                message = "Your character could not be found.";
                return false;
            }

            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                {
                    message = "That companion name or ID is not in your roster. Type /companions list to see names.";
                    return false;
                }

                eCharacterClass characterClass = (eCharacterClass)record.ClassId;
                if (CompanionBuildPlanCatalog.GetPlans(characterClass).Count == 0)
                {
                    message = $"{record.Name} has no automatic builds: {AutomaticTrainingBlocker(characterClass)}.";
                    return false;
                }
                if (!CompanionBuildPlanCatalog.TryFindPlan(characterClass, buildQuery, out CompanionBuildPlan plan))
                {
                    message = $"'{buildQuery}' is not a {characterClass} build. Choose one of: {FormatBuildChoices(characterClass)}.";
                    return false;
                }
                if (!CompanionBuildPlanCatalog.TryValidateRuntimeBuild(characterClass, plan, out string runtimeBlocker))
                {
                    message = $"The {plan.Name} build is unavailable for {record.Name}: {runtimeBlocker}.";
                    return false;
                }

                if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) && active?.Owner == owner)
                    return active.TrySwitchCompanionBuild(plan, out message);

                if (string.Equals(record.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.TrainingPlanId, plan.Id, StringComparison.Ordinal))
                {
                    message = $"{record.Name} already follows the {plan.Name} build.";
                    return true;
                }

                if (!plan.TryGetSwitchedAllocation(ParseSerializedCompanionSpecs(record.SerializedSpecs).Keys,
                        record.Level, plan.ExpectedSpecPointsMultiplier, out Dictionary<string, int> allocation,
                        out int unspentPoints))
                {
                    message = $"The {plan.Name} build does not fit {record.Name}'s level-{record.Level} point budget. Nothing was changed.";
                    return false;
                }

                string previousMode = record.TrainingMode;
                string previousPlan = record.TrainingPlanId;
                string previousSpecs = record.SerializedSpecs;
                int previousUnspent = record.UnspentSpecPoints;
                record.SerializedSpecs = string.Join(';', allocation.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key}|{pair.Value}"));
                record.UnspentSpecPoints = unspentPoints;
                record.TrainingMode = "automatic";
                record.TrainingPlanId = plan.Id;
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                bool saved = SaveRecord(record);
                if (!saved)
                {
                    record.TrainingMode = previousMode;
                    record.TrainingPlanId = previousPlan;
                    record.SerializedSpecs = previousSpecs;
                    record.UnspentSpecPoints = previousUnspent;
                    record.Dirty = true;
                }
                message = saved
                    ? $"{record.Name} now follows the {plan.Name} build ({plan.Role}) and was retrained to level {record.Level}: " +
                      $"{string.Join(", ", plan.TargetAllocations.Select(rank => $"{rank.Specialization} {allocation[rank.Specialization]}"))}. {unspentPoints} points remain."
                    : $"{record.Name}'s build change could not be saved; their previous build and allocations were kept.";
                return saved;
            }
        }

        public static string FormatBuildChoices(eCharacterClass characterClass) =>
            string.Join(", ", CompanionBuildPlanCatalog.GetPlans(characterClass).Select(plan => $"{plan.Key} ({plan.Name})"));

        private static Dictionary<string, int> ParseSerializedCompanionSpecs(string serialized)
        {
            var specs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in (serialized ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('|', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && int.TryParse(parts[1], out int level))
                    specs[parts[0]] = Math.Max(1, level);
            }
            return specs;
        }

        public static bool TryGetRoster(GamePlayer owner, out List<PlayerCompanionRecord> records)
        {
            records = new List<PlayerCompanionRecord>();
            if (owner == null || string.IsNullOrWhiteSpace(owner.ObjectId))
                return false;

            try
            {
                records = GameServer.Database.SelectObjects<PlayerCompanionRecord>(
                        DB.Column(nameof(PlayerCompanionRecord.OwnerCharacterId)).IsEqualTo(owner.ObjectId))
                    .OrderBy(record => record.CreatedUtc, StringComparer.Ordinal)
                    .ThenBy(record => record.CompanionId, StringComparer.Ordinal)
                    .ToList();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"Could not load the companion roster for {owner.Name}.", exception);
                return false;
            }
        }

        public static bool TryRecruit(GamePlayer owner, eRealm realm, eCharacterClass characterClass,
            out PlayerCompanionRecord record, out string message) =>
            TryRecruitInternal(owner, realm, characterClass, null, null, out record, out message);

        /// <summary>Recruits with a chosen build; an empty build uses the class default.</summary>
        public static bool TryRecruit(GamePlayer owner, eRealm realm, eCharacterClass characterClass,
            string buildQuery, out PlayerCompanionRecord record, out string message) =>
            TryRecruitInternal(owner, realm, characterClass, null, buildQuery, out record, out message);

        public static bool TryRecruitAuthored(GamePlayer owner, string name, out PlayerCompanionRecord record,
            out string message) =>
            TryRecruitAuthored(owner, name, null, out record, out message);

        /// <summary>Recruits an authored person with a chosen build; an empty build uses the class default.</summary>
        public static bool TryRecruitAuthored(GamePlayer owner, string name, string buildQuery,
            out PlayerCompanionRecord record, out string message)
        {
            CompanionCharacterCatalog.Character character = CompanionCharacterCatalog.FindByName(name);
            if (character == null)
            {
                record = null;
                message = "That authored companion was not found. Browse the authored cast in /companions.";
                return false;
            }
            return TryRecruitInternal(owner, character.Realm, character.Class, character, buildQuery, out record, out message);
        }

        private static bool TryRecruitInternal(GamePlayer owner, eRealm realm, eCharacterClass characterClass,
            CompanionCharacterCatalog.Character authored, string buildQuery, out PlayerCompanionRecord record,
            out string message)
        {
            record = null;
            message = "The companion could not be recruited.";
            CompanionBuildPlan requestedPlan = null;
            if (!string.IsNullOrWhiteSpace(buildQuery))
            {
                if (!CompanionBuildPlanCatalog.TryFindPlan(characterClass, buildQuery, out requestedPlan))
                {
                    message = CompanionBuildPlanCatalog.GetPlans(characterClass).Count == 0
                        ? $"{characterClass} companions have no automatic builds yet; recruit without a build to train them manually."
                        : $"'{buildQuery}' is not a {characterClass} build. Choose one of: {FormatBuildChoices(characterClass)}.";
                    return false;
                }
                if (!CompanionBuildPlanCatalog.TryValidateRuntimeBuild(characterClass, requestedPlan, out string blocker))
                {
                    message = $"The {requestedPlan.Name} build is unavailable: {blocker}. Nothing was recruited.";
                    return false;
                }
            }
            if (owner == null || !TemporaryGroupClassCatalog.ForRealm(realm)
                    .Any(entry => entry.CharacterClass == characterClass))
            {
                message = "Choose a supported Classic + SI class and realm.";
                return false;
            }

            lock (owner)
            {
                if (!TryGetRoster(owner, out List<PlayerCompanionRecord> roster))
                {
                    message = "Your companion roster could not be loaded. Nothing was recruited.";
                    return false;
                }
                if (roster.Count >= MaximumRosterSize)
                {
                    message = $"Your roster is full ({MaximumRosterSize} companions).";
                    return false;
                }

                if (authored != null && roster.Any(entry =>
                        string.Equals(entry.AuthoredRecruitKey, authored.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    message = $"{authored.Name} is already in your roster; invite that individual instead.";
                    return false;
                }
                var reservedNames = new HashSet<string>(roster.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
                if (authored == null)
                    foreach (CompanionCharacterCatalog.Character character in CompanionCharacterCatalog.All)
                        reservedNames.Add(character.Name);
                else if (reservedNames.Contains(authored.Name))
                {
                    message = $"A companion with the name {authored.Name} is already in your roster.";
                    return false;
                }
                AutonomousBotIdentityGenerator.Identity identity;
                try
                {
                    eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
                    identity = authored == null
                        ? AutonomousBotIdentityGenerator.GenerateForClass(realm, gender, characterClass, reservedNames)
                        : new AutonomousBotIdentityGenerator.Identity(authored.Name, realm, authored.Gender,
                            characterClass, authored.Race);
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not create a {realm} {characterClass} companion identity.", exception);
                    message = $"A {characterClass} recruit could not be created. Try again.";
                    return false;
                }

                string now = DateTime.UtcNow.ToString("O");
                string trainingMode = "manual";
                string trainingPlanId = string.Empty;
                CompanionBuildPlan plan = requestedPlan;
                if ((plan != null || CompanionBuildPlanCatalog.TryGetPlan(characterClass, out plan)) &&
                    CompanionBuildPlanCatalog.TryValidateRuntimeBuild(characterClass, plan, out _))
                {
                    trainingMode = "automatic";
                    trainingPlanId = plan.Id;
                }
                string personality = authored?.Personality ?? CompanionPersonality.RandomKey();
                record = new PlayerCompanionRecord
                {
                    CompanionId = Guid.NewGuid().ToString("D"),
                    OwnerCharacterId = owner.ObjectId,
                    Name = identity.Name,
                    Realm = (int)identity.Realm,
                    ClassId = (int)identity.CharacterClass,
                    RaceId = (int)identity.Race,
                    GenderId = (int)identity.Gender,
                    Level = 1,
                    Experience = 0,
                    IsActive = false,
                    InventoryInitialized = false,
                    RecruitType = authored == null ? "generated" : "authored",
                    AuthoredRecruitKey = authored?.Key ?? string.Empty,
                    PersonalityKey = personality,
                    TacticalRole = BotPartyRoles.DefaultPreference(characterClass),
                    EngagementPreference = CompanionPersonality.DefaultEngagement(personality),
                    AppearanceSize = authored?.Size ?? Random.Shared.Next(46, 61),
                    TrainingMode = trainingMode,
                    TrainingPlanId = trainingPlanId,
                    StateVersion = 1,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    Dirty = true,
                };

                try
                {
                    lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    {
                        if (!GameServer.Database.AddObject(record))
                        {
                            message = "The companion roster could not be saved. Nothing was recruited.";
                            return false;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not save companion {record.CompanionId} for {owner.Name}.", exception);
                    message = "The companion roster could not be saved. Nothing was recruited.";
                    return false;
                }
            }

            message = record.TrainingMode == "automatic" &&
                      CompanionBuildPlanCatalog.TryGetPlanById(characterClass, record.TrainingPlanId, out CompanionBuildPlan chosen)
                ? $"{record.Name}, level 1 {characterClass}, joined your roster with automatic training in the {chosen.Name} build. Invite with /companions invite {record.Name}; change builds with /companions build {record.Name} <build>."
                : $"{record.Name}, level 1 {characterClass}, joined your roster in manual training mode. Invite with /companions invite {record.Name}.";
            message += " " + CompanionPersonality.Dialogue(record, "recruit");
            return true;
        }

        public static bool TryInvite(GamePlayer owner, string nameOrId, out string message)
        {
            if (owner == null)
            {
                message = "Your character could not be found.";
                return false;
            }

            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                {
                    message = "That companion name or ID is not in your roster. Type /companions list to see names.";
                    return false;
                }

                return TryInvite(owner, record, out message);
            }
        }

        private static bool TryInvite(GamePlayer owner, PlayerCompanionRecord record, out string message)
        {
            if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active))
            {
                if (active?.ObjectState == GameObject.eObjectState.Active)
                {
                    message = $"{record.Name} is already active. Use /companions bench {record.Name} to bench them.";
                    return false;
                }
                ActiveCompanions.TryRemove(record.CompanionId, out _);
            }

            if (owner.CurrentRegion == null || owner.CurrentZone == null)
            {
                message = "You must be in the world before inviting a companion.";
                return false;
            }

            eRealm realm = (eRealm)record.Realm;
            ICharacterClass characterClass = ScriptMgr.FindCharacterClass(record.ClassId);
            bool validRace = characterClass?.EligibleRaces?.Any(race => (int)race.ID == record.RaceId) == true;
            bool validGender = record.GenderId == (int)eGender.Male || record.GenderId == (int)eGender.Female;
            if (!TemporaryGroupClassCatalog.ForRealm(realm)
                    .Any(entry => (int)entry.CharacterClass == record.ClassId) ||
                !Guid.TryParse(record.CompanionId, out _) || !validRace || !validGender)
            {
                message = $"{record.Name}'s saved class identity is invalid; the roster record was kept.";
                return false;
            }

            if (owner.Group != null && owner.Group.MemberCount >= owner.Group.MaximumMemberCount)
            {
                message = "Your group is full.";
                return false;
            }

            // An active record without a live actor is a stale login state. Clear it
            // before respawning so a failed world/group insertion cannot duplicate it.
            if (record.IsActive)
            {
                record.IsActive = false;
                record.Dirty = true;
                if (!SaveRecord(record))
                {
                    message = "The companion's roster state could not be updated. Try again.";
                    return false;
                }
            }

            GameBot companion;
            try
            {
                companion = new GameBot(owner, (byte)record.ClassId, record.Name,
                    (byte)record.RaceId, (byte)record.GenderId,
                    botLevel: (byte)Math.Clamp(record.Level, 1, 50),
                    playerCompanionRecord: record);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not load companion {record.CompanionId} for {owner.Name}.", exception);
                message = $"{record.Name} could not be loaded; the roster record was kept.";
                return false;
            }

            bool inventoryInitialized;
            try
            {
                inventoryInitialized = InitializeInventory(companion, record);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not load companion inventory {record.CompanionId} for {owner.Name}.", exception);
                inventoryInitialized = false;
            }

            if (!inventoryInitialized)
            {
                companion.Delete();
                message = $"{record.Name}'s saved inventory could not be loaded; the roster record was kept.";
                return false;
            }

            Vector3 current = new(owner.X, owner.Y, owner.Z);
            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            int distance = Random.Shared.Next(110, 221);
            Vector3 desired = new(owner.X + (float)(Math.Cos(angle) * distance),
                owner.Y + (float)(Math.Sin(angle) * distance), owner.Z);
            Vector3 spawn = PathfindingProvider.Instance.GetMoveAlongSurface(owner.CurrentZone, current, desired,
                PathfindingProvider.Instance.DefaultFilters) ?? current;
            if (record.AppearanceSize is >= 40 and <= 70)
                companion.Size = (byte)record.AppearanceSize;
            companion.X = (int)Math.Round(spawn.X);
            companion.Y = (int)Math.Round(spawn.Y);
            companion.Z = (int)Math.Round(spawn.Z);
            companion.Heading = owner.Heading;
            companion.CurrentRegionID = owner.CurrentRegionID;

            if (!companion.AddToWorld())
            {
                companion.Delete();
                message = $"{record.Name} could not enter this region. The roster record was kept.";
                return false;
            }

            bool createdGroup = false;
            if (owner.Group == null)
            {
                var group = new Group(owner);
                if (!GroupMgr.AddGroup(group) || !group.AddMember(owner))
                {
                    GroupMgr.RemoveGroup(group);
                    companion.Delete();
                    message = "A group could not be created. The roster record was kept.";
                    return false;
                }
                createdGroup = true;
            }

            if (!owner.Group.AddMember(companion))
            {
                companion.Delete();
                if (createdGroup && owner.Group?.MemberCount == 1 && owner.Group.Leader == owner)
                    owner.Group.RemoveMember(owner);
                message = "The companion could not join your group. The roster record was kept.";
                return false;
            }

            companion.EnterPlayerLedGroup(owner);
            companion.Follow(owner, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            if (companion.Brain is DOL.AI.Brain.BotBrain brain)
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);

            if (!ActiveCompanions.TryAdd(record.CompanionId, companion))
            {
                // The owner lock makes this an exceptional duplicate rather than a
                // normal race. Remove only the actor created by this attempt.
                owner.Group.RemoveMember(companion);
                companion.Delete();
                message = "That companion is already active.";
                return false;
            }

            if (!SaveBotState(companion, active: true))
            {
                BenchActiveActor(companion, notifyOwner: false);
                message = "The companion could not be saved after joining; they were returned to the roster.";
                return false;
            }

            message = $"{record.Name}, level {companion.Level} {(eCharacterClass)companion.ClassId}, joined your group. " +
                CompanionPersonality.Dialogue(record, "invite");
            return true;
        }

        public static bool TryBench(GamePlayer owner, string nameOrId, out string message)
        {
            if (owner == null)
            {
                message = "Your character could not be found.";
                return false;
            }

            lock (owner)
            {
                PlayerCompanionRecord record = FindOwnedRecord(owner, nameOrId);
                if (record == null)
                {
                    message = "That companion name or ID is not in your roster. Type /companions list to see names.";
                    return false;
                }

                if (ActiveCompanions.TryGetValue(record.CompanionId, out GameBot active) && active?.Owner == owner)
                {
                    if (!SaveBotState(active, active: false))
                    {
                        message = $"{record.Name} could not be saved, so they remain active.";
                        return false;
                    }

                    DetachAndDelete(active);
                    message = $"{record.Name} was benched. Their roster state was saved. " +
                        CompanionPersonality.Dialogue(record, "bench");
                    return true;
                }

                record.IsActive = false;
                record.Dirty = true;
                if (!SaveRecord(record))
                {
                    message = $"{record.Name}'s roster state could not be saved.";
                    return false;
                }

                message = $"{record.Name} is already benched.";
                return true;
            }
        }

        public static bool SaveBotState(GameBot companion, bool active)
        {
            PlayerCompanionRecord record = companion?.PlayerCompanionRecord;
            if (record == null)
                return false;

            bool saved;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                lock (record)
                {
                    CopyProgressToRecord(companion, record);
                    record.IsActive = active;
                    record.StateVersion = 1;
                    record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                    record.Dirty = true;
                    saved = SaveRecord(record);
                }

                if (saved && record.InventoryInitialized && companion.Inventory is BotInventory inventory)
                    saved = inventory.SaveIntoDatabase(InventoryOwnerId(record.CompanionId));
            }

            if (!saved)
                Log.Error($"Could not persist companion {record.CompanionId} ({record.Name}).");
            return saved;
        }

        public static bool SaveProgress(GameBot companion)
        {
            PlayerCompanionRecord record = companion?.PlayerCompanionRecord;
            if (record == null)
                return false;

            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            lock (record)
            {
                CopyProgressToRecord(companion, record);
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                return SaveRecord(record);
            }
        }

        private static void CopyProgressToRecord(GameBot companion, PlayerCompanionRecord record)
        {
            record.Level = Math.Clamp((int)companion.Level, 1, 50);
            record.Experience = Math.Max(0, companion.Experience);
            record.SerializedSpecs = string.Join(';', companion.GetSpecList()
                .Where(spec => spec.Trainable).Select(spec => $"{spec.KeyName}|{spec.Level}"));
            record.SerializedBuildPlan = companion.BotSpec == null ? string.Empty : BotLifetimeBuild.Encode(companion.BotSpec);
            record.UnspentSpecPoints = companion.UnspentSpecPoints;
            record.LastTrainedLevel = companion.LastTrainedLevel;
        }

        public static void RestoreActiveForPlayer(GamePlayer owner)
        {
            if (owner == null || owner.CurrentRegion == null)
                return;

            if (!TryGetRoster(owner, out List<PlayerCompanionRecord> roster))
                return;

            lock (owner)
            {
                foreach (PlayerCompanionRecord record in roster.Where(entry => entry.IsActive))
                {
                    if (owner.Group != null && owner.Group.MemberCount >= owner.Group.MaximumMemberCount)
                    {
                        record.IsActive = false;
                        record.Dirty = true;
                        SaveRecord(record);
                        owner.Out.SendMessage($"{record.Name} stayed benched because your group is full. Invite them with /companions invite {record.Name}.",
                            eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        continue;
                    }

                    if (!TryInvite(owner, record, out string message))
                    {
                        record.IsActive = false;
                        record.Dirty = true;
                        SaveRecord(record);
                        Log.Warn($"Could not restore active companion {record.CompanionId} for {owner.Name}: {message}");
                        owner.Out.SendMessage($"{record.Name} stayed in your roster but could not rejoin. Use /companions invite {record.Name} after correcting the issue.",
                            eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    }
                }
            }
        }

        public static void OnOwnerQuit(GameBot companion)
        {
            if (companion?.IsPersistentPlayerCompanion != true)
                return;

            bool active = companion.Group != null && companion.Group.IsInTheGroup(companion);
            SaveBotState(companion, active);
            companion.SuppressRosterBenchOnGroupRemoval = true;
            ActiveCompanions.TryRemove(companion.PlayerCompanionRecord.CompanionId, out _);
            companion.Delete();
        }

        public static void OnGroupMemberRemoved(GameBot companion)
        {
            if (companion?.IsPersistentPlayerCompanion != true || companion.SuppressRosterBenchOnGroupRemoval)
                return;

            BenchActiveActor(companion, notifyOwner: true);
        }

        private static void BenchActiveActor(GameBot companion, bool notifyOwner)
        {
            if (companion?.PlayerCompanionRecord == null)
                return;

            bool saved = SaveBotState(companion, active: false);
            if (!saved && notifyOwner && companion.Owner?.ObjectState == GameObject.eObjectState.Active)
                companion.Owner.Out.SendMessage($"{companion.Name} left the group, but the roster save failed. The last saved state was kept.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);

            DetachAndDelete(companion);
        }

        private static void DetachAndDelete(GameBot companion)
        {
            if (companion == null)
                return;

            companion.SuppressRosterBenchOnGroupRemoval = true;
            ActiveCompanions.TryRemove(companion.PlayerCompanionRecord.CompanionId, out _);
            if (companion.Group != null)
                companion.Group.RemoveMember(companion);
            companion.RemoveFromWorld();
            companion.Delete();
        }

        private static bool InitializeInventory(GameBot companion, PlayerCompanionRecord record)
        {
            string ownerId = InventoryOwnerId(record.CompanionId);
            var persistent = new BotInventory(ownerId);
            if (record.InventoryInitialized)
            {
                if (!persistent.LoadFromDatabase(ownerId))
                    return false;
                companion.Inventory = persistent;
                companion.RefreshItemBonuses();
                return true;
            }

            // Recover an interrupted first save before creating another starter kit.
            // The inventory row IDs are stable, so any rows already written are reused.
            if (!persistent.LoadFromDatabase(ownerId))
                return false;
            if (persistent.AllItems.Count > 0)
            {
                record.InventoryInitialized = true;
                record.Dirty = true;
                if (!SaveRecord(record))
                    return false;
                companion.Inventory = persistent;
                companion.RefreshItemBonuses();
                return true;
            }

            if (companion.Inventory is BotInventory initial)
            {
                DbInventoryItem[] initialItems = initial.AllItems.ToArray();
                string previousEquipmentState = record.SerializedEquipmentState;
                string previousUpdatedUtc = record.UpdatedUtc;
                bool previousInitialized = record.InventoryInitialized;
                bool movedAll = true;
                foreach (DbInventoryItem item in initialItems)
                {
                    eInventorySlot slot = (eInventorySlot)item.SlotPosition;
                    if (item.IUWrapper is { IsPersisted: false } definition)
                        definition.AllowAdd = true;
                    if (!initial.RemoveItemWithoutDbDeletion(item) || !persistent.AddItemWithoutDbAddition(slot, item))
                    {
                        movedAll = false;
                        break;
                    }
                    SetEquipmentItemFlags(record, item.ObjectId, "S");
                }
                if (!movedAll)
                {
                    RestoreInitialItems(initial, persistent, initialItems);
                    record.SerializedEquipmentState = previousEquipmentState;
                    record.UpdatedUtc = previousUpdatedUtc;
                    record.InventoryInitialized = previousInitialized;
                    record.Dirty = true;
                    return false;
                }

                record.InventoryInitialized = true;
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;
                if (GameServer.Database is not SqlObjectDatabase database)
                    return false;

                DbInventoryItem[] itemsToInsert = persistent.AllItems.Where(item => !item.IsPersisted).ToArray();
                DataObject[] definitions = itemsToInsert.Select(item => item.IUWrapper)
                    .Where(definition => definition != null && !definition.IsPersisted)
                    .Distinct<DbItemUnique>(ReferenceEqualityComparer.Instance)
                    .Cast<DataObject>().ToArray();
                DataObject[] inserts = definitions.Concat(itemsToInsert).ToArray();
                DataObject[] updates = persistent.AllItems.Where(item => item.IsPersisted)
                    .Cast<DataObject>().Concat(record.IsPersisted ? [record] : []).ToArray();
                if (!record.IsPersisted)
                    inserts = inserts.Append(record).ToArray();

                bool saved;
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                lock (persistent.Lock)
                {
                    saved = inserts.Length == 0
                        ? database.SaveObjectsAtomically(updates)
                        : database.InsertUpdateAndDeleteObjectsAtomically(inserts, updates, []);
                }
                if (!saved)
                {
                    RestoreInitialItems(initial, persistent, initialItems);
                    record.SerializedEquipmentState = previousEquipmentState;
                    record.UpdatedUtc = previousUpdatedUtc;
                    record.InventoryInitialized = previousInitialized;
                    record.Dirty = true;
                    companion.Inventory = initial;
                    return false;
                }

                companion.Inventory = persistent;
                companion.RefreshItemBonuses();
                return true;
            }

            return false;
        }

        private static void RestoreInitialItems(BotInventory initial, BotInventory persistent,
            DbInventoryItem[] originalItems)
        {
            foreach (DbInventoryItem item in persistent.AllItems.ToArray())
                persistent.RemoveItemWithoutDbDeletion(item);
            foreach (DbInventoryItem item in initial.AllItems.ToArray())
                initial.RemoveItemWithoutDbDeletion(item);
            foreach (DbInventoryItem item in originalItems)
            {
                item.OwnerID = null;
                initial.AddItem((eInventorySlot)item.SlotPosition, item);
            }
        }

        private static bool SaveRecord(PlayerCompanionRecord record)
        {
            if (record == null)
                return false;

            try
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    return record.IsPersisted
                        ? GameServer.Database.SaveObject(record)
                        : GameServer.Database.AddObject(record);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not save companion record {record.CompanionId}.", exception);
                return false;
            }
        }

        private static PlayerCompanionRecord FindOwnedRecord(GamePlayer owner, string nameOrId)
        {
            if (owner == null || string.IsNullOrWhiteSpace(owner.ObjectId) || string.IsNullOrWhiteSpace(nameOrId))
                return null;

            try
            {
                if (Guid.TryParse(nameOrId, out Guid companionGuid))
                {
                    return GameServer.Database.SelectObjects<PlayerCompanionRecord>(
                            DB.Column(nameof(PlayerCompanionRecord.OwnerCharacterId)).IsEqualTo(owner.ObjectId))
                        .FirstOrDefault(record => string.Equals(record.OwnerCharacterId, owner.ObjectId, StringComparison.Ordinal) &&
                                                  Guid.TryParse(record.CompanionId, out Guid storedGuid) &&
                                                  storedGuid == companionGuid);
                }

                List<PlayerCompanionRecord> nameMatches = GameServer.Database.SelectObjects<PlayerCompanionRecord>(
                        DB.Column(nameof(PlayerCompanionRecord.OwnerCharacterId)).IsEqualTo(owner.ObjectId))
                    .Where(record => string.Equals(record.OwnerCharacterId, owner.ObjectId, StringComparison.Ordinal) &&
                                     string.Equals(record.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();
                return nameMatches.Count == 1 ? nameMatches[0] : null;
            }
            catch (Exception exception)
            {
                Log.Error($"Could not find companion {nameOrId} for {owner.Name}.", exception);
                return null;
            }
        }
    }
}