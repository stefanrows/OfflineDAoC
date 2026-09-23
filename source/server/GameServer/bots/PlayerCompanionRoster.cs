using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
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
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<string, GameBot> ActiveCompanions = new(StringComparer.OrdinalIgnoreCase);

        public static string InventoryOwnerId(string companionId)
        {
            return InventoryOwnerPrefix + companionId;
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

                message = $"{record.Name} is in manual training mode. Earned specialization points stay unspent until you train them.";
                return true;
            }
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
            out PlayerCompanionRecord record, out string message)
        {
            record = null;
            message = "The companion could not be recruited.";
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

                var reservedNames = new HashSet<string>(roster.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
                eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
                AutonomousBotIdentityGenerator.Identity identity;
                try
                {
                    identity = AutonomousBotIdentityGenerator.GenerateForClass(realm, gender, characterClass, reservedNames);
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not generate a {realm} {characterClass} companion identity.", exception);
                    message = $"A {characterClass} recruit could not be created. Try again.";
                    return false;
                }

                string now = DateTime.UtcNow.ToString("O");
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
                    RecruitType = "generated",
                    AuthoredRecruitKey = string.Empty,
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

            message = $"{record.Name}, level 1 {characterClass}, joined your roster. Invite with /companions invite {record.Name}.";
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

            message = $"{record.Name}, level {companion.Level} {(eCharacterClass)companion.ClassId}, joined your group.";
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
                    message = $"{record.Name} was benched. Their roster state was saved.";
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
            {
                string ownerId = InventoryOwnerId(record.CompanionId);
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    saved = inventory.SaveIntoDatabase(ownerId);
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
                foreach (DbInventoryItem item in initial.AllItems.ToArray())
                {
                    eInventorySlot slot = (eInventorySlot)item.SlotPosition;
                    if (item.IUWrapper is { IsPersisted: false } definition)
                        definition.AllowAdd = true;
                    if (!initial.RemoveItemWithoutDbDeletion(item) || !persistent.AddItem(slot, item))
                        return false;
                }
            }

            companion.Inventory = persistent;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                if (!persistent.SaveIntoDatabase(ownerId))
                    return false;
            }

            record.InventoryInitialized = true;
            record.Dirty = true;
            bool saved = SaveRecord(record);
            if (saved)
                companion.RefreshItemBonuses();
            return saved;
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