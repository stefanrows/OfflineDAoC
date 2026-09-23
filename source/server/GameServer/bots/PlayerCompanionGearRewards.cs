using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Personal, persistent equipment rewards for owned companions.</summary>
    public static class PlayerCompanionGearRewards
    {
        private const string NpcClaimPrefix = "OfflineDAoC.CompanionPveGear.";
        private const string PlayerClaimPrefix = "OfflineDAoC.CompanionPvpGear.";
        private const string PlayerDeathAnchorProperty = "OfflineDAoC.CompanionPvpGear.DeathAnchor";
        private const string PlayerDeathCycleProperty = "OfflineDAoC.CompanionPvpGear.DeathCycle";

        /// <summary>25% at 1x XP, scaling linearly to certainty at 4x.</summary>
        public static double PveDropChance(double xpRate) =>
            double.IsFinite(xpRate) && xpRate > 0 ? Math.Min(1d, xpRate * 0.25d) : 0d;

        public static bool PassesPveRoll(double xpRate, double roll) =>
            double.IsFinite(roll) && roll >= 0 && roll < 1 && roll < PveDropChance(xpRate);

        public static bool TryRewardPve(GameBot companion, GameNPC victim, double roll)
        {
            if (!IsEligible(companion, victim) || !double.IsFinite(roll) || roll < 0 || roll >= 1 ||
                !TryClaim(companion, victim, NpcClaimPrefix + companion.PlayerCompanionRecord.CompanionId,
                    victim.SpawnTick.ToString(System.Globalization.CultureInfo.InvariantCulture)) ||
                !PassesPveRoll(ServerProperties.Properties.XP_RATE, roll))
                return false;

            return Grant(companion, victim, "PvE");
        }

        internal static int AwardPvePartyGear(GamePlayer owner, GameNPC victim)
        {
            if (owner?.ObjectState != GameObject.eObjectState.Active || !owner.GainXP || owner.Group == null ||
                victim == null || !owner.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                return 0;

            int rewards = 0;
            foreach (GameBot companion in owner.Group.GetMembersInTheGroup().OfType<GameBot>())
            {
                if (companion.Owner == owner &&
                    TryRewardPve(companion, victim, Random.Shared.NextDouble()))
                    rewards++;
            }
            return rewards;
        }

        public static bool TryRewardPvp(GameBot companion, GamePlayer victim)
        {
            string cycle = GetPlayerDeathCycle(victim);
            return TryRewardPvp(companion, victim, cycle);
        }

        internal static bool TryRewardPvp(GameBot companion, GameBot victim, long deathTick)
        {
            string cycle = deathTick < 0 ? string.Empty : deathTick.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return TryRewardPvp(companion, victim, cycle);
        }

        internal static int AwardPvpPartyGear(GameLiving victim)
        {
            if (victim is not GamePlayer && victim is not GameBot { IsAutonomousWorldBot: true })
                return 0;

            string cycle = victim is GamePlayer player
                ? GetPlayerDeathCycle(player)
                : victim.TempProperties.GetProperty<long>(AutonomousBotRealmPointRewards.LastRealmPointDeathTickProperty, -1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(cycle) || cycle == "-1")
                return 0;

            KeyValuePair<GameLiving, double>[] contributors;
            lock (victim.XpGainersLock)
                contributors = victim.XPGainers.ToArray();

            var owners = new HashSet<GamePlayer>();
            foreach (KeyValuePair<GameLiving, double> entry in contributors)
            {
                GameLiving attacker = PvpCombatant.Resolve(entry.Key);
                if (attacker == null || attacker.Realm == eRealm.None || PvpCombatant.AreAllied(attacker, victim))
                    continue;

                if (attacker is GamePlayer playerAttacker)
                    owners.Add(playerAttacker);
                else if (attacker is GameBot { IsPersistentPlayerCompanion: true } companionAttacker &&
                         companionAttacker.Owner != null)
                    owners.Add(companionAttacker.Owner);

                if (attacker.Group != null)
                    foreach (GamePlayer groupPlayer in attacker.Group.GetMembersInTheGroup().OfType<GamePlayer>())
                        owners.Add(groupPlayer);
            }

            int rewards = 0;
            foreach (GamePlayer owner in owners)
            {
                if (owner?.ObjectState != GameObject.eObjectState.Active || owner.Group == null ||
                    !owner.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) ||
                    owner.IsObjectGreyCon(victim) || PvpCombatant.AreAllied(owner, victim))
                    continue;

                foreach (GameBot companion in owner.Group.GetMembersInTheGroup().OfType<GameBot>())
                {
                    if (companion.Owner == owner && TryRewardPvp(companion, victim, cycle))
                        rewards++;
                }
            }

            return rewards;
        }

        private static string GetPlayerDeathCycle(GamePlayer victim)
        {
            if (victim?.TempProperties == null)
                return string.Empty;

            string anchor = victim.DeathTime.ToString(System.Globalization.CultureInfo.InvariantCulture);
            lock (victim.TempProperties)
            {
                string previousAnchor = victim.TempProperties.GetProperty<string>(PlayerDeathAnchorProperty);
                string previousCycle = victim.TempProperties.GetProperty<string>(PlayerDeathCycleProperty);
                if (string.Equals(anchor, previousAnchor, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(previousCycle))
                    return previousCycle;

                string cycle = anchor + ":" + (victim.DeathsPvP + 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                victim.TempProperties.SetProperty(PlayerDeathAnchorProperty, anchor);
                victim.TempProperties.SetProperty(PlayerDeathCycleProperty, cycle);
                return cycle;
            }
        }

        private static bool TryRewardPvp(GameBot companion, GameLiving victim, string cycle)
        {
            if (!IsEligiblePvp(companion, victim) || string.IsNullOrWhiteSpace(cycle) ||
                !TryClaim(companion, victim, PlayerClaimPrefix + companion.PlayerCompanionRecord.CompanionId, cycle))
                return false;

            return Grant(companion, victim, "PvP");
        }

        private static bool IsEligible(GameBot companion, GameLiving victim)
        {
            GamePlayer owner = companion?.Owner;
            return companion?.IsPersistentPlayerCompanion == true &&
                   companion.ObjectState == GameObject.eObjectState.Active &&
                   owner?.ObjectState == GameObject.eObjectState.Active && owner.GainXP &&
                   owner.Group != null && companion.Group == owner.Group &&
                   owner.Group.IsInTheGroup(companion) && victim != null &&
                   companion.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) &&
                   owner.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) &&
                   companion.Realm != victim.Realm &&
                   (victim is not GameNPC npc || !companion.IsObjectGreyCon(npc));
        }

        private static bool IsEligiblePvp(GameBot companion, GameLiving victim)
        {
            GamePlayer owner = companion?.Owner;
            return companion?.IsPersistentPlayerCompanion == true &&
                   companion.ObjectState == GameObject.eObjectState.Active &&
                   owner?.ObjectState == GameObject.eObjectState.Active &&
                   owner.Group != null && companion.Group == owner.Group &&
                   owner.Group.IsInTheGroup(companion) && victim != null &&
                   victim.ObjectState == GameObject.eObjectState.Active &&
                   companion.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) &&
                   owner.IsWithinRadius(victim, WorldMgr.MAX_EXPFORKILL_DISTANCE) &&
                   companion.Realm != victim.Realm && !PvpCombatant.AreAllied(owner, victim);
        }

        private static bool TryClaim(GameBot companion, GameLiving victim, string propertyKey, string cycle)
        {
            if (string.IsNullOrWhiteSpace(cycle))
                return false;
            lock (victim.TempProperties)
            {
                if (string.Equals(victim.TempProperties.GetProperty<string>(propertyKey), cycle,
                        StringComparison.Ordinal))
                    return false;
                victim.TempProperties.SetProperty(propertyKey, cycle);
                return true;
            }
        }

        private static bool Grant(GameBot companion, GameLiving victim, string source)
        {
            if (companion.Inventory is not BotInventory inventory || companion.Owner?.DBCharacter == null)
                return false;

            GeneratedUniqueItem template = AtlasROGManager.GenerateMonsterLootROG(
                companion.Realm, (eCharacterClass)companion.CharacterClass.ID,
                (byte)Math.Clamp((int)companion.Level, 1, 50),
                victim.CurrentZone?.IsOF == true);
            if (template == null)
                return false;

            template.AllowAdd = true;
            template.IsTradable = true;
            template.Level = Math.Min(template.Level, companion.Level);
            template.LevelRequirement = Math.Min(template.LevelRequirement, companion.Level);
            GameInventoryItem item = GameInventoryItem.Create(template);
            item.IsCrafted = false;
            item.IsROG = true;
            item.Creator = victim.Name;

            long soldCopper = 0;
            long creditedMoney = 0;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            lock (inventory.Lock)
            {
                if (companion.ObjectState != GameObject.eObjectState.Active ||
                    companion.Owner?.ObjectState != GameObject.eObjectState.Active ||
                    companion.Owner.DBCharacter == null)
                    return false;

                if (GameServer.Database is not SqlObjectDatabase database)
                    return false;

                eInventorySlot backpack = inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                DbInventoryItem candidate = null;
                if (backpack == eInventorySlot.Invalid)
                {
                    candidate = FindSurplusForSpace(companion, inventory, out soldCopper);
                    if (candidate == null)
                        return false;
                    backpack = (eInventorySlot)candidate.SlotPosition;
                    if (companion.Owner.GetCurrentMoney() > PlayerCompanionRoster.MaximumOwnerMoney - soldCopper)
                        return false;
                }

                PlayerCompanionRecord record = companion.PlayerCompanionRecord;
                GamePlayer owner = companion.Owner;
                string previousState = record.SerializedEquipmentState;
                string previousUpdatedUtc = record.UpdatedUtc;
                int previousCopper = owner.DBCharacter.Copper;
                int previousSilver = owner.DBCharacter.Silver;
                int previousGold = owner.DBCharacter.Gold;
                int previousPlatinum = owner.DBCharacter.Platinum;
                int previousMithril = owner.DBCharacter.Mithril;
                var originalItems = inventory.AllItems.Select(existing =>
                    (Item: existing, Slot: existing.SlotPosition, OwnerId: existing.OwnerID)).ToArray();

                if (candidate != null && !inventory.RemoveItemWithoutDbDeletion(candidate))
                    return false;
                if (!inventory.AddItemWithoutDbAddition(backpack, item))
                {
                    RestoreInventory(inventory, originalItems);
                    return false;
                }
                item.OwnerID = PlayerCompanionRoster.InventoryOwnerId(record.CompanionId);
                item.SlotPosition = (int)backpack;
                PlayerCompanionRoster.SetEquipmentItemFlags(record, item.ObjectId, "E");
                if (candidate != null)
                {
                    creditedMoney = owner.GetCurrentMoney() + soldCopper;
                    owner.DBCharacter.Copper = Money.GetCopper(creditedMoney);
                    owner.DBCharacter.Silver = Money.GetSilver(creditedMoney);
                    owner.DBCharacter.Gold = Money.GetGold(creditedMoney);
                    owner.DBCharacter.Platinum = Money.GetPlatinum(creditedMoney);
                    owner.DBCharacter.Mithril = Money.GetMithril(creditedMoney);
                    PlayerCompanionRoster.SetEquipmentItemFlags(record, candidate.ObjectId, string.Empty);
                }
                record.UpdatedUtc = DateTime.UtcNow.ToString("O");
                record.Dirty = true;

                DbItemUnique definition = item.IUWrapper;
                DataObject[] inserts = definition == null
                    ? [item]
                    : [definition, item];
                IEnumerable<DataObject> updates = candidate == null
                    ? [record]
                    : [record, owner.DBCharacter];
                IEnumerable<DataObject> deletes = candidate == null ? [] : [candidate];
                if (!database.InsertUpdateAndDeleteObjectsAtomically(inserts, updates, deletes))
                {
                    RestoreInventory(inventory, originalItems);
                    record.SerializedEquipmentState = previousState;
                    record.UpdatedUtc = previousUpdatedUtc;
                    record.Dirty = true;
                    owner.DBCharacter.Copper = previousCopper;
                    owner.DBCharacter.Silver = previousSilver;
                    owner.DBCharacter.Gold = previousGold;
                    owner.DBCharacter.Platinum = previousPlatinum;
                    owner.DBCharacter.Mithril = previousMithril;
                    return false;
                }
            }

            if (soldCopper > 0)
                companion.Owner.SetCurrentMoneyAfterAtomicPersistence(creditedMoney);
            companion.RefreshItemBonuses();
            companion.UpdateNPCEquipmentAppearance();
            if (soldCopper > 0)
                companion.Owner.Out.SendUpdateMoney();

            companion.Owner.Out.SendMessage(
                soldCopper > 0
                    ? $"{companion.Name} received {item.Name} from a {source} encounter; surplus gear sold for {Money.GetString(soldCopper)}."
                    : $"{companion.Name} received {item.Name} from a {source} encounter.",
                DOL.GS.PacketHandler.eChatType.CT_Loot,
                DOL.GS.PacketHandler.eChatLoc.CL_SystemWindow);
            return true;
        }

        internal static DbInventoryItem FindSurplusForSpace(GameBot companion, BotInventory inventory,
            out long copper, string excludeItemId = null)
        {
            copper = 0;
            var candidate = inventory.AllItems
                .Where(item => !string.Equals(item.ObjectId, excludeItemId, StringComparison.Ordinal) &&
                               CanSellForSpace(companion, item))
                .OrderBy(AutonomousBotEconomy.EquipmentValue)
                .ThenBy(item => item.Price)
                .FirstOrDefault();
            copper = candidate == null ? 0 : AutonomousBotEconomy.CalculateStandardVendorSaleCopper(candidate);
            return copper > 0 ? candidate : null;
        }

        internal static bool CanSellForSpace(GameBot companion, DbInventoryItem item)
        {
            if (companion?.PlayerCompanionRecord == null || item == null ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack ||
                !item.IsPersisted || !item.IsDropable || item.OwnerLot != 0 || item is GameInventoryRelic ||
                BotSiegeRuntime.IsSupply(item.Id_nb))
                return false;
            string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, item.ObjectId);
            return flags.Contains('E') && !flags.Contains('S') && !flags.Contains('P') && !flags.Contains('K') &&
                   AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item) > 0;
        }

        private static void RestoreInventory(BotInventory inventory,
            (DbInventoryItem Item, int Slot, string OwnerId)[] originalItems)
        {
            foreach (DbInventoryItem current in inventory.AllItems.ToArray())
                inventory.RemoveItemWithoutDbDeletion(current);
            foreach ((DbInventoryItem item, int slot, string ownerId) in originalItems)
            {
                item.OwnerID = ownerId;
                item.SlotPosition = slot;
                inventory.AddItemWithoutDbAddition((eInventorySlot)slot, item);
            }
        }
    }
}
