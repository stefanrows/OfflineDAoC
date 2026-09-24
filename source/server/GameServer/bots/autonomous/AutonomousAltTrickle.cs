using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.GameEvents;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>Creates at most one new level-one guild alt per configured interval.</summary>
public static class AutonomousAltTrickle
{
    public static bool IsDue(BotGoalSettings settings, int rosterCount, DateTime nowUtc,
        DateTime nextUtc) => settings.AltJoinIntervalHours > 0 && settings.AltRosterCap > 0 &&
        rosterCount > 0 && rosterCount < settings.AltRosterCap && nowUtc >= nextUtc;

    public static OfflineWorldBotRecord Create(IReadOnlyList<OfflineWorldBotRecord> roster, BotGoalSettings settings)
    {
        if (roster == null || roster.Count >= settings.AltRosterCap)
            throw new InvalidOperationException("The autonomous roster is at its alt cap.");

        var guild = roster.Where(record => !record.IsRetired && !string.IsNullOrWhiteSpace(record.GuildId))
            .GroupBy(record => record.GuildId, StringComparer.Ordinal)
            .Where(group => AutonomousCrewManager.IsCrewGuild(GuildMgr.GetGuildByGuildID(group.Key)))
            .OrderBy(group => group.Count(record => record.Level < 10))
            .ThenBy(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new InvalidOperationException("No managed guild is available for a new alt.");
        OfflineWorldBotRecord exemplar = guild.ElementAt(Random.Shared.Next(guild.Count()));
        int realm = exemplar.Realm;
        var reserved = DOLDB<OfflineWorldBotRecord>.SelectAllObjects()
            .Select(record => record.Name)
            .Concat(DOLDB<DbCoreCharacter>.SelectAllObjects().Select(record => record.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        BotCharacterGenerator.Identity identity = BotCharacterGenerator.Generate(realm, reserved);
        StartupLocation start = StartupLocations.ChooseUniformAutonomousLocation(
            StartupLocations.GetClassicSiLocationsForAutonomous((eRealm)realm, identity.RaceId, identity.ClassId))
            ?? throw new InvalidOperationException("No valid starting location exists for the new guild alt.");
        string now = DateTime.UtcNow.ToString("O");
        var record = new OfflineWorldBotRecord
        {
            Name = identity.Name, Realm = realm, ClassId = identity.ClassId, ClassName = identity.ClassName,
            RaceId = identity.RaceId, RaceName = identity.RaceName, Gender = identity.Gender,
            Level = 1, Experience = 0, RealmPoints = 0, GuildId = guild.Key,
            GuildRank = 9, RegionId = start.Region, X = start.XPos, Y = start.YPos, Z = start.ZPos,
            BindRegionId = start.Region, BindX = start.XPos, BindY = start.YPos, BindZ = start.ZPos,
            IsAlive = true, IsOnline = false, Health = 1, LastUpdateUtc = now,
            Activity = "New level-1 guild alt queued for login",
            SerializedAbilities = "generated-level|1",
            CurrentGoal = "Find a reachable level-appropriate XP camp",
            ObjectiveProgress = "Joined an existing managed guild as a new alt",
        };
        if (!GameServer.Database.AddObject(record))
            throw new InvalidOperationException("Could not save the new guild alt.");
        AutonomousGuildCharterRecord charter = DOLDB<AutonomousGuildCharterRecord>
            .SelectObject(DB.Column("GuildId").IsEqualTo(guild.Key));
        AutonomousGuildCharter kind = charter != null && Enum.TryParse(charter.Charter, out AutonomousGuildCharter parsed)
            ? parsed : AutonomousGuildCharter.Leveling;
        if (AutonomousBotIdentity.Ensure(record, kind, settings.Mix) && !GameServer.Database.SaveObject(record))
            throw new InvalidOperationException("The new guild alt was created but its identity could not be saved.");
        return record;
    }
}
