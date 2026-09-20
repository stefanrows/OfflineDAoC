using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Owns the durable identity of autonomous crews. A crew is a real guild so
/// the existing PvP alliance rules can treat mixed-realm members as allies.
/// </summary>
public static class AutonomousCrewManager
{
    public const string CrewNamePrefix = "Camlann Crew ";
    public const int MaximumCrewSize = 8;

    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousCrewManager));
    private static readonly string[] CrewNames =
    {
        "Ashen Concord", "Blackwood Pact", "Duskbound", "Emberwake", "Frostfall",
        "Gravewind", "Ironroot", "Moonlit Fang", "Ravenshade", "Stormwake",
    };

    public static bool IsCrewGuild(Guild guild) =>
        guild != null && guild != Guild.DummyGuild &&
        guild.Name?.StartsWith(CrewNamePrefix, StringComparison.Ordinal) == true;

    public static bool AreInSameCrew(GameBot first, GameBot second) =>
        first?.Guild != null && second?.Guild != null &&
        IsCrewGuild(first.Guild) && IsCrewGuild(second.Guild) &&
        string.Equals(first.Guild.GuildID, second.Guild.GuildID, StringComparison.Ordinal);

    public static string NameForOrdinal(int ordinal)
    {
        ordinal = Math.Max(0, ordinal);
        string baseName = CrewNamePrefix + CrewNames[ordinal % CrewNames.Length];
        int suffix = ordinal / CrewNames.Length;
        return suffix == 0 ? baseName : $"{baseName} {suffix + 1}";
    }

    /// <summary>
    /// Repairs missing crew assignments and creates enough real guild rows for
    /// the current autonomous roster. Existing player-guild assignments survive.
    /// </summary>
    public static int Reconcile()
    {
        try
        {
            OfflineWorldBotRecord[] roster = DOLDB<OfflineWorldBotRecord>
                .SelectObjects(DB.Column("IsRetired").IsEqualTo(false))
                .OrderBy(record => record.BotId)
                .ToArray();
            if (roster.Length == 0)
                return 0;

            List<Guild> crewGuilds = GuildMgr.GetGuilds().Where(IsCrewGuild).ToList();
            var counts = crewGuilds.ToDictionary(guild => guild.GuildID, _ => 0, StringComparer.Ordinal);
            var realmCounts = crewGuilds.ToDictionary(
                guild => guild.GuildID,
                _ => new Dictionary<eRealm, int>(),
                StringComparer.Ordinal);
            var unassigned = new List<OfflineWorldBotRecord>();

            foreach (OfflineWorldBotRecord record in roster)
            {
                Guild guild = GuildMgr.GetGuildByGuildID(record.GuildId);
                if (guild == null)
                {
                    unassigned.Add(record);
                    continue;
                }

                if (!IsCrewGuild(guild))
                    continue;

                counts[guild.GuildID] = counts.GetValueOrDefault(guild.GuildID) + 1;
                Dictionary<eRealm, int> byRealm = realmCounts[guild.GuildID];
                eRealm realm = (eRealm)record.Realm;
                byRealm[realm] = byRealm.GetValueOrDefault(realm) + 1;
            }

            int assignedCrewCount = counts.Values.Sum();
            int desiredCrewCount = Math.Max(1, (int)Math.Ceiling((assignedCrewCount + unassigned.Count) / (double)MaximumCrewSize));
            for (int ordinal = 0; crewGuilds.Count < desiredCrewCount; ordinal++)
            {
                string name = NameForOrdinal(ordinal);
                if (GuildMgr.GetGuildByName(name) != null)
                    continue;

                Guild guild = GuildMgr.CreateGuild(eRealm.None, name);
                if (guild == null)
                    break;

                crewGuilds.Add(guild);
                counts[guild.GuildID] = 0;
                realmCounts[guild.GuildID] = new Dictionary<eRealm, int>();
            }

            var changed = new List<DataObject>();
            foreach (OfflineWorldBotRecord record in unassigned)
            {
                Guild guild = crewGuilds
                    .Where(candidate => counts[candidate.GuildID] < MaximumCrewSize)
                    .OrderBy(candidate => realmCounts[candidate.GuildID].GetValueOrDefault((eRealm)record.Realm))
                    .ThenBy(candidate => counts[candidate.GuildID])
                    .ThenBy(candidate => candidate.GuildID, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (guild == null)
                    break;

                record.GuildId = guild.GuildID;
                record.GuildRank = 9;
                record.Dirty = true;
                changed.Add(record);
                counts[guild.GuildID]++;
                Dictionary<eRealm, int> byRealm = realmCounts[guild.GuildID];
                eRealm realm = (eRealm)record.Realm;
                byRealm[realm] = byRealm.GetValueOrDefault(realm) + 1;
            }

            if (changed.Count > 0)
            {
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    GameServer.Database.SaveObject(changed);
                Log.Info($"Assigned {changed.Count} autonomous characters to mixed-realm crews.");
            }

            return changed.Count;
        }
        catch (Exception exception)
        {
            Log.Error("Autonomous crew reconciliation failed", exception);
            return 0;
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

    private static bool AreLevelsCompatible(int playerLevel, int botLevel) =>
        playerLevel >= 50 && botLevel >= 50 || Math.Abs(playerLevel - botLevel) <= 5;
}
