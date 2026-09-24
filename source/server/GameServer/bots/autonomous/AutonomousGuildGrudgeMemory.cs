using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Pure timing and eligibility rules for short-lived guild grudges.</summary>
public static class AutonomousGuildGrudgePolicy
{
    public static readonly TimeSpan GrudgeDuration = TimeSpan.FromHours(3);

    public static DateTime Expiry(DateTime createdUtc) => createdUtc.ToUniversalTime() + GrudgeDuration;

    public static bool IsActive(DateTime expiresUtc, DateTime nowUtc) =>
        expiresUtc > nowUtc;
}

/// <summary>
/// Keeps recent killers in a guild-scoped, additive save table. Memories are
/// cached after the first read so target scans never query the database.
/// </summary>
public static class AutonomousGuildGrudgeMemory
{
    private const int MaximumTargetsPerGuild = 16;
    private const long PruneIntervalMilliseconds = 60_000;
    private const string CharacterTarget = "Character";
    private const string WorldBotTarget = "WorldBot";
    private static readonly object LoadSync = new();
    private static readonly object WriteSync = new();
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousGuildGrudgeMemory));
    private static readonly ConcurrentDictionary<GrudgeKey, MemoryEntry> Memories = new();
    private static bool _loaded;
    private static long _nextPruneTick;

    private readonly record struct GrudgeKey(string GuildId, string TargetKind, string TargetKey);
    private sealed record MemoryEntry(AutonomousGuildGrudgeRecord Record, DateTime ExpiresUtc,
        DateTime LastSeenUtc, DateTime LastAnnouncedUtc);

    /// <summary>
    /// Records an enemy player-shaped killer after an autonomous world bot dies.
    /// Player companions are remembered as their human owner, never as a
    /// temporary or persistent companion identity.
    /// </summary>
    public static bool RememberKiller(GameBot victim, GameObject killer, DateTime nowUtc,
        out string targetName, out string location, out bool shouldAnnounce)
    {
        targetName = string.Empty;
        location = string.Empty;
        shouldAnnounce = false;
        if (victim?.IsAutonomousWorldBot != true || victim.IsTemporaryGroupHelper ||
            victim.PersistentRecord == null || victim.Guild == null || victim.Guild == Guild.DummyGuild ||
            killer is not GameLiving killerLiving || PvpCombatant.AreAllied(victim, killerLiving) ||
            PvpCombatant.IsSafeArea(victim))
            return false;

        GameLiving identity = PvpCombatant.Resolve(killerLiving);
        if (identity is GameBot { IsAutonomousWorldBot: false } companion)
            identity = companion.Owner ?? companion.PlayerGroupLeader;
        if (!TryGetTargetKey(identity, out string targetKind, out string targetKey, out targetName))
            return false;

        location = identity.CurrentZone?.Description ?? "the frontier";
        EnsureLoaded();
        if (!_loaded)
            return false;

        nowUtc = nowUtc.ToUniversalTime();
        string guildId = victim.Guild.GuildID;
        GrudgeKey key = new(guildId, targetKind, targetKey);
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
        lock (WriteSync)
        {
            MemoryEntry existing = Memories.GetValueOrDefault(key);
            AutonomousGuildGrudgeRecord record = existing?.Record ?? new AutonomousGuildGrudgeRecord
            {
                GuildId = guildId,
                TargetKind = targetKind,
                TargetKey = targetKey,
            };
            bool announce = existing == null || nowUtc - existing.LastAnnouncedUtc >= TimeSpan.FromMinutes(2);
            record.TargetName = targetName;
            record.LastKnownRegionId = identity.CurrentRegionID;
            record.LastKnownZone = location;
            record.LastSeenUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture);
            record.ExpiresUtc = AutonomousGuildGrudgePolicy.Expiry(nowUtc).ToString("O", CultureInfo.InvariantCulture);
            if (announce)
                record.LastAnnouncedUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture);
            record.Dirty = true;

            bool saved = record.IsPersisted
                ? GameServer.Database.SaveObject(record)
                : GameServer.Database.AddObject(record);
            if (!saved)
            {
                Log.Warn($"AUTONOMOUS_GUILD_GRUDGE_SAVE_FAILED guild={guildId} targetKind={targetKind}");
                return false;
            }

            Memories[key] = new(record, AutonomousGuildGrudgePolicy.Expiry(nowUtc), nowUtc,
                announce ? nowUtc : existing.LastAnnouncedUtc);
            shouldAnnounce = announce;
            TrimGuild(guildId);
        }

        PruneExpired(nowUtc);
        return true;
    }

    public static bool IsActiveTarget(GameBot hunter, GameLiving candidate, DateTime nowUtc)
    {
        if (hunter?.IsAutonomousWorldBot != true || hunter.IsTemporaryGroupHelper || hunter.Guild == null ||
            candidate == null || !TryGetTargetKey(PvpCombatant.Resolve(candidate), out string kind, out string targetKey, out _))
            return false;

        EnsureLoaded();
        if (!_loaded)
            return false;
        PruneIfDue(nowUtc);

        GrudgeKey key = new(hunter.Guild.GuildID, kind, targetKey);
        if (!Memories.TryGetValue(key, out MemoryEntry entry) ||
            !AutonomousGuildGrudgePolicy.IsActive(entry.ExpiresUtc, nowUtc.ToUniversalTime()))
            return false;
        return IsWorthTarget(candidate, nowUtc) && candidate.IsAlive &&
               !PvpCombatant.IsSafeArea(candidate) &&
               GameServer.ServerRules.IsAllowedToAttack(hunter, candidate, true) &&
               !PvpCombatant.AreAllied(hunter, candidate);
    }

    /// <summary>Returns online, reachable KOS targets for an existing RvR crew.</summary>
    public static GameLiving[] GetReachableTargets(GameBot hunter, ISet<ushort> reachableRegions, DateTime nowUtc)
    {
        if (hunter?.IsAutonomousWorldBot != true || hunter.IsTemporaryGroupHelper ||
            hunter.Guild == null || reachableRegions == null || reachableRegions.Count == 0)
            return [];

        EnsureLoaded();
        if (!_loaded)
            return [];
        PruneIfDue(nowUtc);

        MemoryEntry[] entries = Memories
            .Where(pair => pair.Key.GuildId == hunter.Guild.GuildID &&
                AutonomousGuildGrudgePolicy.IsActive(pair.Value.ExpiresUtc, nowUtc.ToUniversalTime()))
            .OrderByDescending(pair => pair.Value.LastSeenUtc)
            .Select(pair => pair.Value)
            .Take(MaximumTargetsPerGuild)
            .ToArray();
        if (entries.Length == 0)
            return [];

        Dictionary<string, GamePlayer> players = GameLoop.GetListForTick<GamePlayer>()
            .Where(player => player?.ObjectState == GameObject.eObjectState.Active &&
                !string.IsNullOrWhiteSpace(player.ObjectId))
            .GroupBy(player => player.ObjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        Dictionary<string, GameBot> bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.DatabaseID > 0)
            .ToDictionary(bot => bot.DatabaseID.ToString(CultureInfo.InvariantCulture), bot => bot, StringComparer.Ordinal);

        return entries.Select(entry => ResolveTarget(entry.Record, players, bots))
            .Where(target => target != null && reachableRegions.Contains(target.CurrentRegionID))
            .Where(target => target != null && target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
                !target.IsStealthed && !PvpCombatant.IsSafeArea(target) &&
                GameServer.ServerRules.IsAllowedToAttack(hunter, target, true) &&
                !PvpCombatant.AreAllied(hunter, target) && IsWorthTarget(target, nowUtc))
            .Distinct()
            .ToArray();
    }

    public static string StableTargetId(GameLiving target)
    {
        if (!TryGetTargetKey(PvpCombatant.Resolve(target), out string kind, out string key, out _))
            return string.Empty;
        return $"guild-kos-{kind}-{key}";
    }

    public static bool IsWorthTarget(GameLiving target, DateTime nowUtc)
    {
        long worthSeconds = Math.Max(0, ServerProperties.Properties.RP_WORTH_SECONDS);
        if (target is GamePlayer player)
            return player.DeathTime <= 0 || player.DeathTime + worthSeconds <= player.PlayedTime;
        if (target is GameBot bot)
        {
            long lastDeathTick = bot.TempProperties.GetProperty<long>(
                AutonomousBotRealmPointRewards.LastRealmPointDeathTickProperty, -1);
            long worthMilliseconds = worthSeconds * 1000L;
            return lastDeathTick < 0 || GameLoop.GameLoopTime - lastDeathTick >= worthMilliseconds;
        }
        return false;
    }

    private static GameLiving ResolveTarget(AutonomousGuildGrudgeRecord record,
        IReadOnlyDictionary<string, GamePlayer> players, IReadOnlyDictionary<string, GameBot> bots)
    {
        if (record.TargetKind == CharacterTarget)
            return players.GetValueOrDefault(record.TargetKey);
        if (record.TargetKind == WorldBotTarget)
            return bots.GetValueOrDefault(record.TargetKey);
        return null;
    }

    private static bool TryGetTargetKey(GameLiving identity, out string kind, out string key, out string name)
    {
        kind = string.Empty;
        key = string.Empty;
        name = string.Empty;
        switch (identity)
        {
            case GamePlayer player when !string.IsNullOrWhiteSpace(player.ObjectId):
                kind = CharacterTarget;
                key = player.ObjectId;
                name = player.Name;
                return true;
            case GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } bot when bot.DatabaseID > 0:
                kind = WorldBotTarget;
                key = bot.DatabaseID.ToString(CultureInfo.InvariantCulture);
                name = bot.Name;
                return true;
            default:
                return false;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;
        lock (LoadSync)
        {
            if (_loaded)
                return;
            if (GameServer.Database == null)
                return;
            try
            {
                DateTime now = DateTime.UtcNow;
                IList<AutonomousGuildGrudgeRecord> records;
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    records = DOLDB<AutonomousGuildGrudgeRecord>.SelectAllObjects();
                List<AutonomousGuildGrudgeRecord> expired = new();
                foreach (AutonomousGuildGrudgeRecord record in records)
                {
                    if (!DateTime.TryParse(record.ExpiresUtc, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime expires) ||
                        !AutonomousGuildGrudgePolicy.IsActive(expires, now))
                    {
                        expired.Add(record);
                        continue;
                    }
                    DateTime.TryParse(record.LastSeenUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime lastSeen);
                    DateTime.TryParse(record.LastAnnouncedUtc, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime lastAnnounced);
                    Memories[new(record.GuildId, record.TargetKind, record.TargetKey)] =
                        new(record, expires, lastSeen, lastAnnounced);
                }
                _loaded = true;
                if (expired.Count > 0)
                {
                    try
                    {
                        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                            foreach (AutonomousGuildGrudgeRecord record in expired)
                                GameServer.Database.DeleteObject(record);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("AUTONOMOUS_GUILD_GRUDGE_EXPIRED_PRUNE_FAILED", exception);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("AUTONOMOUS_GUILD_GRUDGE_LOAD_FAILED", exception);
            }
        }
    }

    private static void PruneIfDue(DateTime nowUtc)
    {
        long nowTick = GameLoop.GameLoopTime;
        long next = System.Threading.Interlocked.Read(ref _nextPruneTick);
        if (nowTick < next || System.Threading.Interlocked.CompareExchange(
                ref _nextPruneTick, nowTick + PruneIntervalMilliseconds, next) != next)
            return;
        PruneExpired(nowUtc.ToUniversalTime());
    }

    private static void PruneExpired(DateTime nowUtc)
    {
        KeyValuePair<GrudgeKey, MemoryEntry>[] expired = Memories
            .Where(pair => !AutonomousGuildGrudgePolicy.IsActive(pair.Value.ExpiresUtc, nowUtc))
            .ToArray();
        if (expired.Length == 0 || GameServer.Database == null)
            return;
        lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
        lock (WriteSync)
        {
            foreach ((GrudgeKey key, MemoryEntry entry) in expired)
            {
                if (Memories.TryGetValue(key, out MemoryEntry current) && ReferenceEquals(current, entry))
                {
                    GameServer.Database.DeleteObject(entry.Record);
                    Memories.TryRemove(key, out _);
                }
            }
        }
    }

    private static void TrimGuild(string guildId)
    {
        KeyValuePair<GrudgeKey, MemoryEntry>[] excess = Memories
            .Where(pair => pair.Key.GuildId == guildId)
            .OrderByDescending(pair => pair.Value.LastSeenUtc)
            .Skip(MaximumTargetsPerGuild)
            .ToArray();
        foreach ((GrudgeKey key, MemoryEntry entry) in excess)
        {
            GameServer.Database.DeleteObject(entry.Record);
            Memories.TryRemove(key, out _);
        }
    }
}

[DataTable(TableName = "offline_guild_grudges")]
public sealed class AutonomousGuildGrudgeRecord : DataObject
{
    [PrimaryKey(AutoIncrement = true)] public long GrudgeId { get; set; }
    [DataElement(AllowDbNull = false)] public string GuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetKind { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string TargetName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int LastKnownRegionId { get; set; }
    [DataElement(AllowDbNull = false)] public string LastKnownZone { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastSeenUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ExpiresUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastAnnouncedUtc { get; set; } = string.Empty;
}
