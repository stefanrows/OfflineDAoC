using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Owns the durable identity of autonomous guilds. Generated guilds are real
/// guilds so the existing PvP alliance rules protect mixed-realm members.
/// </summary>
public static class AutonomousCrewManager
{
    public const string CrewNamePrefix = "Camlann Crew ";
    public const int MaximumCrewSize = 8; // Ordinary party/warband capacity; guilds may be larger.
    public const int MaximumManagedGuilds = 15;
    public const int BotsPerSizeTriplet = 56;

    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousCrewManager));
    private static readonly string[] CrewNames =
    {
        "Ashen Concord", "Blackwood Pact", "Duskbound", "Emberwake", "Frostfall",
        "Gravewind", "Ironroot", "Moonlit Fang", "Ravenshade", "Stormwake",
    };

    public readonly record struct ReconcileResult(bool Succeeded, int ChangedBots, int ManagedGuilds, string Error)
    {
        public static ReconcileResult Failure(string error) => new(false, 0, 0, error);
    }

    public readonly record struct AssignmentCandidate(long BotId, eRealm Realm, int Level,
        eCharacterClass CharacterClass, string GuildId);

    public readonly record struct GuildTarget(string GuildId, int Weight, int TargetSize);

    public static bool IsCrewGuild(Guild guild) =>
        guild != null && guild != Guild.DummyGuild &&
        guild.Name?.StartsWith(CrewNamePrefix, StringComparison.Ordinal) == true;

    public static bool IsManagedMembership(string guildId, bool isGeneratedGuild, bool containsHuman) =>
        string.IsNullOrWhiteSpace(guildId) || isGeneratedGuild && !containsHuman;

    public static string MappedKeepOwner(string claimedGuildName, string sourceGuildName, string targetGuildName) =>
        string.Equals(claimedGuildName, sourceGuildName, StringComparison.Ordinal)
            ? targetGuildName
            : claimedGuildName;

    public static bool MappingNeedsCompletion(string state, bool sourceStillExists = false) =>
        sourceStillExists || !string.Equals(state, "Completed", StringComparison.Ordinal);

    public static bool AreInSameCrew(GameBot first, GameBot second) =>
        first?.Guild != null && second?.Guild != null &&
        first.Guild != Guild.DummyGuild && second.Guild != Guild.DummyGuild &&
        string.Equals(first.Guild.GuildID, second.Guild.GuildID, StringComparison.Ordinal);

    public static string NameForOrdinal(int ordinal)
    {
        ordinal = Math.Max(0, ordinal);
        string baseName = CrewNamePrefix + CrewNames[ordinal % CrewNames.Length];
        int suffix = ordinal / CrewNames.Length;
        return suffix == 0 ? baseName : $"{baseName} {suffix + 1}";
    }

    public static int DesiredManagedGuildCount(int botCount)
    {
        botCount = Math.Max(0, botCount);
        if (botCount == 0)
            return 0;
        int triplets = Math.Min(5, (int)Math.Ceiling(botCount / (double)BotsPerSizeTriplet));
        return Math.Min(botCount, triplets * 3);
    }

    /// <summary>Returns deterministic 1:2:4 small/medium/large targets.</summary>
    public static GuildTarget[] BuildTargets(IReadOnlyList<string> guildIds, int botCount)
    {
        if (guildIds == null || guildIds.Count == 0 || botCount <= 0)
            return [];

        int[] weights = Enumerable.Range(0, guildIds.Count)
            .Select(index => (index % 3) switch { 0 => 1, 1 => 2, _ => 4 })
            .ToArray();
        double unit = botCount / (double)weights.Sum();
        int[] sizes = weights.Select(weight => (int)Math.Floor(weight * unit)).ToArray();
        foreach (int index in Enumerable.Range(0, sizes.Length)
                     .OrderByDescending(index => weights[index] * unit - sizes[index])
                     .ThenBy(index => index)
                     .Take(botCount - sizes.Sum()))
            sizes[index]++;

        for (int index = 0; index < sizes.Length && botCount >= sizes.Length; index++)
        {
            if (sizes[index] > 0)
                continue;
            int donor = Enumerable.Range(0, sizes.Length)
                .Where(candidate => sizes[candidate] > 1)
                .OrderByDescending(candidate => sizes[candidate])
                .ThenBy(candidate => candidate)
                .First();
            sizes[donor]--;
            sizes[index]++;
        }

        return guildIds.Select((id, index) => new GuildTarget(id, weights[index], sizes[index])).ToArray();
    }

    /// <summary>
    /// Deterministically fills weighted size, realm, level-band and role
    /// deficits. Existing survivor assignments are fixed and never reshuffled
    /// merely because a new bot is added.
    /// </summary>
    public static Dictionary<long, string> PlanAssignments(
        IEnumerable<AssignmentCandidate> fixedMembers,
        IEnumerable<AssignmentCandidate> candidates,
        IReadOnlyList<GuildTarget> targets)
    {
        var result = new Dictionary<long, string>();
        if (targets == null || targets.Count == 0)
            return result;

        var states = targets.ToDictionary(target => target.GuildId,
            target => new AssignmentState(target), StringComparer.Ordinal);
        foreach (AssignmentCandidate member in fixedMembers ?? [])
            if (states.TryGetValue(member.GuildId ?? string.Empty, out AssignmentState state))
                state.Add(member);

        foreach (AssignmentCandidate candidate in (candidates ?? []).OrderBy(entry => entry.BotId))
        {
            AssignmentState selected = states.Values
                .OrderBy(state => state.SizeDeficitScore)
                .ThenBy(state => state.RealmCount(candidate.Realm))
                .ThenBy(state => state.LevelBandCount(LevelBand(candidate.Level)))
                .ThenBy(state => state.RoleCount(Role(candidate.CharacterClass)))
                .ThenBy(state => state.Target.GuildId, StringComparer.Ordinal)
                .First();
            selected.Add(candidate);
            result[candidate.BotId] = selected.Target.GuildId;
        }
        return result;
    }

    /// <summary>
    /// Consolidates generated guilds before autonomous login. A persisted
    /// source-to-survivor map makes every step safe to repeat after interruption.
    /// Player guilds and generated guilds containing human characters are
    /// protected exceptions and do not count toward the managed cap.
    /// </summary>
    public static ReconcileResult Reconcile()
    {
        try
        {
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                OfflineWorldBotRecord[] roster = DOLDB<OfflineWorldBotRecord>
                    .SelectObjects(DB.Column("IsRetired").IsEqualTo(false))
                    .OrderBy(record => record.BotId)
                    .ToArray();

                HashSet<string> humanGuildIds = DOLDB<DbCoreCharacter>.SelectAllObjects()
                    .Select(character => character.GuildID)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
                List<Guild> generated = GuildMgr.GetGuilds().Where(IsCrewGuild)
                    .OrderBy(guild => guild.Name, StringComparer.Ordinal)
                    .ThenBy(guild => guild.GuildID, StringComparer.Ordinal)
                    .ToList();
                HashSet<string> protectedGuildIds = generated
                    .Where(guild => humanGuildIds.Contains(guild.GuildID))
                    .Select(guild => guild.GuildID)
                    .ToHashSet(StringComparer.Ordinal);

                OfflineWorldBotRecord[] managedRoster = roster.Where(record =>
                {
                    Guild guild = GuildMgr.GetGuildByGuildID(record.GuildId);
                    return IsManagedMembership(record.GuildId, IsCrewGuild(guild),
                        guild != null && protectedGuildIds.Contains(guild.GuildID));
                }).ToArray();
                int desiredCount = DesiredManagedGuildCount(managedRoster.Length);

                var persistedMappings = DOLDB<AutonomousCrewMappingRecord>.SelectAllObjects()
                    .ToDictionary(mapping => mapping.SourceGuildId, StringComparer.Ordinal);
                HashSet<string> requiredSurvivors = persistedMappings.Values
                    .Select(mapping => mapping.TargetGuildId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
                List<Guild> managedGuilds = generated.Where(guild => !protectedGuildIds.Contains(guild.GuildID)).ToList();
                List<Guild> survivors = managedGuilds.Where(guild => requiredSurvivors.Contains(guild.GuildID)).ToList();
                foreach (Guild guild in managedGuilds)
                    if (survivors.Count < desiredCount && !survivors.Contains(guild))
                        survivors.Add(guild);

                for (int ordinal = 0; survivors.Count < desiredCount; ordinal++)
                {
                    string name = NameForOrdinal(ordinal);
                    Guild existing = GuildMgr.GetGuildByName(name);
                    if (existing != null)
                    {
                        if (IsCrewGuild(existing) && !protectedGuildIds.Contains(existing.GuildID) && !survivors.Contains(existing))
                            survivors.Add(existing);
                        continue;
                    }
                    Guild created = GuildMgr.CreateGuild(eRealm.None, name);
                    if (created == null)
                        throw new InvalidOperationException($"Could not create generated guild '{name}'.");
                    survivors.Add(created);
                }
                survivors = survivors.Take(desiredCount).ToList();
                if (desiredCount > 0 && survivors.Count != desiredCount)
                    throw new InvalidOperationException($"Expected {desiredCount} managed guilds but only {survivors.Count} are available.");

                GuildTarget[] targets = BuildTargets(survivors.Select(guild => guild.GuildID).ToArray(), managedRoster.Length);
                HashSet<string> survivorIds = survivors.Select(guild => guild.GuildID).ToHashSet(StringComparer.Ordinal);
                AssignmentCandidate[] fixedMembers = managedRoster
                    .Where(record => survivorIds.Contains(record.GuildId))
                    .Select(ToCandidate).ToArray();
                OfflineWorldBotRecord[] candidates = managedRoster
                    .Where(record => !survivorIds.Contains(record.GuildId))
                    .ToArray();
                Dictionary<long, string> assignments = PlanAssignments(fixedMembers, candidates.Select(ToCandidate), targets);

                foreach (IGrouping<string, OfflineWorldBotRecord> source in candidates
                             .Where(record => !string.IsNullOrWhiteSpace(record.GuildId))
                             .GroupBy(record => record.GuildId, StringComparer.Ordinal))
                {
                    string targetId = source.Select(record => assignments[record.BotId]).First();
                    if (source.Any(record => assignments[record.BotId] != targetId))
                    {
                        targetId = SelectTargetForWholeGuild(source.Select(ToCandidate), fixedMembers, targets);
                        foreach (OfflineWorldBotRecord record in source)
                            assignments[record.BotId] = targetId;
                    }
                    Guild sourceGuild = GuildMgr.GetGuildByGuildID(source.Key);
                    Guild targetGuild = GuildMgr.GetGuildByGuildID(targetId);
                    if (sourceGuild == null || targetGuild == null)
                        throw new InvalidOperationException($"Generated guild mapping {source.Key} -> {targetId} cannot resolve both guilds.");
                    if (!persistedMappings.TryGetValue(source.Key, out AutonomousCrewMappingRecord mapping))
                    {
                        mapping = new AutonomousCrewMappingRecord
                        {
                            SourceGuildId = source.Key,
                            SourceGuildName = sourceGuild.Name,
                            TargetGuildId = targetId,
                            TargetGuildName = targetGuild.Name,
                            State = "Planned",
                            UpdatedUtc = DateTime.UtcNow.ToString("O"),
                        };
                        if (!GameServer.Database.AddObject(mapping))
                            throw new InvalidOperationException($"Could not persist generated guild mapping {sourceGuild.Name} -> {targetGuild.Name}.");
                        persistedMappings[source.Key] = mapping;
                    }
                    else if (!string.Equals(mapping.TargetGuildId, targetId, StringComparison.Ordinal))
                    {
                        targetId = mapping.TargetGuildId;
                        targetGuild = GuildMgr.GetGuildByGuildID(targetId) ??
                            throw new InvalidOperationException($"Persisted target guild {targetId} for {sourceGuild.Name} no longer exists.");
                        foreach (OfflineWorldBotRecord record in source)
                            assignments[record.BotId] = targetId;
                    }
                }

                // Old generated guilds may already be empty. They still need a
                // durable mapping so keep/alliance references are reconciled and
                // the guild can be removed instead of permanently exceeding the cap.
                int obsoleteOrdinal = 0;
                foreach (Guild sourceGuild in managedGuilds.Where(guild => !survivorIds.Contains(guild.GuildID)))
                {
                    if (persistedMappings.ContainsKey(sourceGuild.GuildID))
                        continue;
                    if (targets.Length == 0)
                        throw new InvalidOperationException($"No surviving generated guild is available for obsolete guild '{sourceGuild.Name}'.");
                    Guild targetGuild = GuildMgr.GetGuildByGuildID(targets[obsoleteOrdinal++ % targets.Length].GuildId) ??
                        throw new InvalidOperationException($"Generated guild target for '{sourceGuild.Name}' no longer exists.");
                    var mapping = new AutonomousCrewMappingRecord
                    {
                        SourceGuildId = sourceGuild.GuildID,
                        SourceGuildName = sourceGuild.Name,
                        TargetGuildId = targetGuild.GuildID,
                        TargetGuildName = targetGuild.Name,
                        State = "Planned",
                        UpdatedUtc = DateTime.UtcNow.ToString("O"),
                    };
                    if (!GameServer.Database.AddObject(mapping))
                        throw new InvalidOperationException($"Could not persist empty generated guild mapping {sourceGuild.Name} -> {targetGuild.Name}.");
                    persistedMappings[sourceGuild.GuildID] = mapping;
                }

                var changed = new List<DataObject>();
                foreach (OfflineWorldBotRecord record in candidates)
                {
                    if (!assignments.TryGetValue(record.BotId, out string targetId))
                        throw new InvalidOperationException($"No generated guild target was planned for bot {record.BotId}.");
                    if (record.GuildId == targetId)
                        continue;
                    record.GuildId = targetId;
                    record.GuildRank = 9;
                    record.Dirty = true;
                    changed.Add(record);
                }
                if (changed.Count > 0 && !GameServer.Database.SaveObject(changed))
                    throw new InvalidOperationException("Could not persist autonomous guild assignments.");

                foreach (AutonomousCrewMappingRecord mapping in persistedMappings.Values.Where(mapping =>
                             MappingNeedsCompletion(mapping.State, GuildMgr.GetGuildByGuildID(mapping.SourceGuildId) != null)))
                    CompleteMapping(mapping, protectedGuildIds);

                int remainingManaged = GuildMgr.GetGuilds().Count(guild => IsCrewGuild(guild) &&
                    !humanGuildIds.Contains(guild.GuildID));
                if (remainingManaged != desiredCount || remainingManaged > MaximumManagedGuilds)
                    throw new InvalidOperationException($"Generated guild consolidation left {remainingManaged} managed guilds; expected {desiredCount} with an absolute cap of {MaximumManagedGuilds}.");

                Log.Info($"Autonomous guild reconciliation completed: bots={managedRoster.Length}, changed={changed.Count}, " +
                         $"managedGuilds={remainingManaged}, protectedGeneratedGuilds={protectedGuildIds.Count}.");
                return new(true, changed.Count, remainingManaged, string.Empty);
            }
        }
        catch (Exception exception)
        {
            string error = "Autonomous guild consolidation failed before login. Back up the save, inspect generated guild/keep/alliance references, and restart after correcting the reported error: " + exception.Message;
            Log.Error(error, exception);
            return ReconcileResult.Failure(error);
        }
    }

    public static bool Bind(GameBot bot)
    {
        OfflineWorldBotRecord record = bot?.PersistentRecord;
        Guild guild = GuildMgr.GetGuildByGuildID(record?.GuildId);
        if (bot == null || record == null || guild == null)
            return false;

        DbGuildRank rank = guild.GetRankByID(Math.Clamp(record.GuildRank, 0, 9)) ?? guild.GetRankByID(9);
        if (rank == null || !guild.AddBotMember(bot, rank))
            return false;

        record.GuildId = guild.GuildID;
        record.GuildRank = rank.RankLevel;
        return true;
    }

    /// <summary>Accepts a nearby player invitation for an unassigned live bot.</summary>
    public static bool TryAcceptGuildInvite(GamePlayer inviter, GameBot bot)
    {
        if (inviter?.Guild == null || bot == null || !bot.IsAlive || bot.Guild != null ||
            (!bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper) || !AreLevelsCompatible(inviter.Level, bot.Level))
            return false;

        DbGuildRank rank = inviter.Guild.GetRankByID(9);
        if (rank == null || !inviter.Guild.AddBotMember(bot, rank))
            return false;

        if (bot.PersistentRecord != null)
        {
            bot.PersistentRecord.GuildId = inviter.Guild.GuildID;
            bot.PersistentRecord.GuildRank = rank.RankLevel;
            bot.PersistentRecord.Dirty = true;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                GameServer.Database.SaveObject(bot.PersistentRecord);
        }

        return true;
    }

    private sealed class AssignmentState
    {
        public GuildTarget Target { get; }
        private int Count { get; set; }
        private readonly Dictionary<eRealm, int> _realms = new();
        private readonly Dictionary<int, int> _levels = new();
        private readonly Dictionary<BotPartyRole, int> _roles = new();

        public AssignmentState(GuildTarget target) => Target = target;
        public double SizeDeficitScore => Target.TargetSize <= 0 ? Count : Count / (double)Target.TargetSize;
        public int RealmCount(eRealm realm) => _realms.GetValueOrDefault(realm);
        public int LevelBandCount(int band) => _levels.GetValueOrDefault(band);
        public int RoleCount(BotPartyRole role) => _roles.GetValueOrDefault(role);
        public void Add(AssignmentCandidate candidate)
        {
            Count++;
            _realms[candidate.Realm] = RealmCount(candidate.Realm) + 1;
            int band = LevelBand(candidate.Level);
            _levels[band] = LevelBandCount(band) + 1;
            BotPartyRole role = Role(candidate.CharacterClass);
            _roles[role] = RoleCount(role) + 1;
        }
    }

    private static AssignmentCandidate ToCandidate(OfflineWorldBotRecord record) =>
        new(record.BotId, (eRealm)record.Realm, record.Level, (eCharacterClass)record.ClassId, record.GuildId ?? string.Empty);

    private static int LevelBand(int level) => level >= 50 ? 50 : level >= 20 ? 20 : 0;
    private static BotPartyRole Role(eCharacterClass characterClass) => BotPartyRoles.For(characterClass);

    private static string SelectTargetForWholeGuild(IEnumerable<AssignmentCandidate> source,
        IEnumerable<AssignmentCandidate> fixedMembers, IReadOnlyList<GuildTarget> targets)
    {
        AssignmentCandidate[] fixedArray = (fixedMembers ?? []).ToArray();
        AssignmentCandidate[] group = source.OrderBy(candidate => candidate.BotId).ToArray();
        return targets
            .OrderBy(target => fixedArray.Count(member => member.GuildId == target.GuildId) /
                               (double)Math.Max(1, target.TargetSize))
            .ThenBy(target => group.Sum(candidate => fixedArray.Count(member =>
                member.GuildId == target.GuildId && member.Realm == candidate.Realm)))
            .ThenBy(target => target.GuildId, StringComparer.Ordinal)
            .First().GuildId;
    }

    private static void CompleteMapping(AutonomousCrewMappingRecord mapping, HashSet<string> protectedGuildIds)
    {
        Guild source = GuildMgr.GetGuildByGuildID(mapping.SourceGuildId);
        Guild target = GuildMgr.GetGuildByGuildID(mapping.TargetGuildId);
        if (source == null)
        {
            mapping.State = "Completed";
            mapping.UpdatedUtc = DateTime.UtcNow.ToString("O");
            GameServer.Database.SaveObject(mapping);
            return;
        }
        if (target == null)
            throw new InvalidOperationException($"Mapped survivor {mapping.TargetGuildId} for {source.Name} does not exist.");
        if (protectedGuildIds.Contains(source.GuildID) ||
            DOLDB<DbCoreCharacter>.SelectObjects(DB.Column("GuildID").IsEqualTo(source.GuildID)).Count > 0)
            throw new InvalidOperationException($"Generated guild '{source.Name}' gained a human member during consolidation and is now protected.");
        if (DOLDB<OfflineWorldBotRecord>.SelectObjects(DB.Column("GuildId").IsEqualTo(source.GuildID)
                .And(DB.Column("IsRetired").IsEqualTo(false))).Count > 0)
            throw new InvalidOperationException($"Generated guild '{source.Name}' still has autonomous members after remapping.");

        foreach (DbKeep keep in DOLDB<DbKeep>.SelectObjects(DB.Column("ClaimedGuildName").IsEqualTo(source.Name)))
        {
            keep.ClaimedGuildName = MappedKeepOwner(keep.ClaimedGuildName, source.Name, target.Name);
            if (!GameServer.Database.SaveObject(keep))
                throw new InvalidOperationException($"Could not transfer keep {keep.KeepID} from {source.Name} to {target.Name}.");
            AbstractGameKeep live = GameServer.KeepManager.GetKeepByID(keep.KeepID);
            if (live != null)
            {
                live.Guild = target;
                live.DBKeep.ClaimedGuildName = target.Name;
                foreach (GameKeepGuard guard in live.Guards.Values)
                    guard.ChangeGuild();
            }
        }

        ReconcileAllianceReferences(source, target);

        if (!GuildMgr.DeleteGuild(source.Name))
            throw new InvalidOperationException($"Could not remove empty generated guild '{source.Name}'.");
        mapping.State = "Completed";
        mapping.UpdatedUtc = DateTime.UtcNow.ToString("O");
        if (!GameServer.Database.SaveObject(mapping))
            throw new InvalidOperationException($"Could not complete persisted generated guild mapping for '{source.Name}'.");
    }

    private static void ReconcileAllianceReferences(Guild source, Guild target)
    {
        Alliance sourceAlliance = source.alliance;
        if (sourceAlliance != null)
        {
            if (target.alliance == null)
                sourceAlliance.AddGuild(target);

            if (sourceAlliance.DbAlliance?.LeaderGuildID == source.GuildID)
            {
                Guild replacement = target.alliance == sourceAlliance
                    ? target
                    : sourceAlliance.Guilds.FirstOrDefault(guild => guild != source);
                if (replacement != null)
                {
                    sourceAlliance.DbAlliance.LeaderGuildID = replacement.GuildID;
                    sourceAlliance.DbAlliance.DBguildleader = DOLDB<DbGuild>
                        .SelectObject(DB.Column("GuildID").IsEqualTo(replacement.GuildID));
                    if (!GameServer.Database.SaveObject(sourceAlliance.DbAlliance))
                        throw new InvalidOperationException($"Could not transfer alliance leadership from {source.Name} to {replacement.Name}.");
                }
            }

            sourceAlliance.RemoveGuild(source);
            return;
        }

        // Repair database-only references as well; a partially loaded alliance
        // must not retain a deleted leader or member ID across the next restart.
        DbGuild sourceRow = DOLDB<DbGuild>.SelectObject(DB.Column("GuildID").IsEqualTo(source.GuildID));
        DbGuild targetRow = DOLDB<DbGuild>.SelectObject(DB.Column("GuildID").IsEqualTo(target.GuildID));
        if (sourceRow != null && targetRow != null && string.IsNullOrWhiteSpace(targetRow.AllianceID) &&
            !string.IsNullOrWhiteSpace(sourceRow.AllianceID))
        {
            targetRow.AllianceID = sourceRow.AllianceID;
            if (!GameServer.Database.SaveObject(targetRow))
                throw new InvalidOperationException($"Could not transfer alliance membership from {source.Name} to {target.Name}.");
        }
        foreach (DbGuildAlliance alliance in DOLDB<DbGuildAlliance>
                     .SelectObjects(DB.Column("LeaderGuildID").IsEqualTo(source.GuildID)))
        {
            alliance.LeaderGuildID = target.GuildID;
            alliance.DBguildleader = targetRow;
            if (!GameServer.Database.SaveObject(alliance))
                throw new InvalidOperationException($"Could not transfer alliance leadership from {source.Name} to {target.Name}.");
        }
    }

    private static bool AreLevelsCompatible(int playerLevel, int botLevel) =>
        playerLevel >= 50 && botLevel >= 50 || Math.Abs(playerLevel - botLevel) <= 5;
}

[DataTable(TableName = "offline_crew_consolidation")]
public sealed class AutonomousCrewMappingRecord : DataObject
{
    [PrimaryKey] public string SourceGuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SourceGuildName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetGuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetGuildName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string State { get; set; } = "Planned";
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
}
