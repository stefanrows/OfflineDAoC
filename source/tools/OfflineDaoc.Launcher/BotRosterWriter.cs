using System.Data.SQLite;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Writes generated autonomous world bots into a save (the launcher's ADD CREW buttons and the fresh-world roster of
/// docs/RVR_WORLDS.md). Each bot gets a realm/race/class-valid starting location from StartupLocation (level 20 and up
/// start in the capital, level 50 bots also get the class loadout), and queues for the server's staggered login.
/// Runs inside the caller's transaction.
/// </summary>
internal static class BotRosterWriter
{
    /// <summary>The names already taken by bots and characters of the save (case-insensitive).</summary>
    public static HashSet<string> ReservedNames(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var names = connection.CreateCommand();
        names.Transaction = transaction;
        names.CommandText = "SELECT Name FROM offline_world_bots UNION SELECT Name FROM DOLCharacters";
        using var reader = names.ExecuteReader();
        while (reader.Read()) reserved.Add(reader.GetString(0));
        return reserved;
    }

    /// <summary>Adds one bot of <paramref name="realm"/> per entry of <paramref name="levels"/>; <paramref name="reserved"/> gains the new names.</summary>
    public static IReadOnlyList<BotCharacterGenerator.Identity> Add(SQLiteConnection connection, SQLiteTransaction transaction,
        int realm, IReadOnlyList<int> levels, ISet<string> reserved)
    {
        var identities = new List<BotCharacterGenerator.Identity>(levels.Count);
        for (int index = 0; index < levels.Count; index++)
        {
            int newLevel = levels[index];
            BotCharacterGenerator.Identity identity = BotCharacterGenerator.Generate(realm, reserved);
            BotStartingLocation start = newLevel >= 20
                ? CapitalBotStartingLocation(identity.Realm)
                : ChooseBotStartingLocation(connection, transaction, identity.Realm, identity.RaceId, identity.ClassId);
            identities.Add(identity);
            string now = DateTime.UtcNow.ToString("O");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO offline_world_bots
                    (Name, Realm, ClassId, ClassName, RaceId, RaceName, Gender, Level, Experience, RealmPoints,
                     ZoneName, Activity, IsOnline, IsAlive, LastUpdateUtc, MoneyCopper, InventoryRevision,
                     ZoneId, X, Y, Z, RegionId, Health, Mana, Endurance, BindRegionId, BindX, BindY, BindZ,
                     CurrentGoal, ObjectiveProgress, LastMeaningfulProgressUtc, IsRetired, SerializedAbilities)
                VALUES
                    (@name, @realm, @classId, @className, @raceId, @raceName, @gender, @level, @xp, @rp,
                     @zoneName, 'Queued at randomized starting location', 0, 1, @now, @money, 0,
                     @zoneId, @x, @y, @z, @region, 1, 0, 0, @region, @x, @y, @z,
                     'Awaiting staggered login queue', @placement, @now, 0, @abilities)
                """;
            insert.Parameters.AddWithValue("@name", identity.Name);
            insert.Parameters.AddWithValue("@level", newLevel);
            insert.Parameters.AddWithValue("@xp", OfflineDaoc.Configuration.AutonomousPopulationShape.ExperienceForLevel(newLevel));
            insert.Parameters.AddWithValue("@rp", newLevel == 50 ? OfflineDaoc.Configuration.AutonomousPopulationShape.RealmPointsForNewLevelFifty() : 0L);
            insert.Parameters.AddWithValue("@money", newLevel == 50 ? 100000000L : newLevel >= 10 ? 1000L * newLevel * newLevel : 0L);
            insert.Parameters.AddWithValue("@abilities", $"generated-level|{newLevel}");
            insert.Parameters.AddWithValue("@realm", identity.Realm);
            insert.Parameters.AddWithValue("@classId", identity.ClassId);
            insert.Parameters.AddWithValue("@className", identity.ClassName);
            insert.Parameters.AddWithValue("@raceId", identity.RaceId);
            insert.Parameters.AddWithValue("@raceName", identity.RaceName);
            insert.Parameters.AddWithValue("@gender", identity.Gender);
            insert.Parameters.AddWithValue("@now", now);
            insert.Parameters.AddWithValue("@region", start.RegionId);
            insert.Parameters.AddWithValue("@x", start.X);
            insert.Parameters.AddWithValue("@y", start.Y);
            insert.Parameters.AddWithValue("@z", start.Z);
            insert.Parameters.AddWithValue("@zoneId", start.ZoneId);
            insert.Parameters.AddWithValue("@zoneName", newLevel >= 20 ? start.ZoneName : start.RegionId is 51 or 151 or 181
                ? "Shrouded Isles starting area"
                : "Classic starting area");
            insert.Parameters.AddWithValue("@placement",
                $"Launcher assigned a realm/race/class-valid random start in region {start.RegionId} before server startup");
            insert.ExecuteNonQuery();
            if (newLevel == 50)
            {
                long botId = connection.LastInsertRowId;
                EquipGeneratedBot(connection, transaction, botId, identity, now);
            }
        }
        return identities;
    }

    private static void EquipGeneratedBot(SQLiteConnection connection, SQLiteTransaction transaction,
        long botId, BotCharacterGenerator.Identity identity, string now)
    {
        using var gear = connection.CreateCommand();
        gear.Transaction = transaction;
        gear.CommandText = """
            INSERT INTO Inventory (Inventory_ID,OwnerID,ITemplate_Id,SlotPosition,Count,Condition,Durability,LastTimeRowUpdated)
            SELECT lower(hex(randomblob(16))),@owner,l.TemplateId,l.SlotPosition,1,t.MaxCondition,t.MaxDurability,@now
            FROM offline_level50_loadouts l JOIN ItemTemplate t ON t.Id_nb=l.TemplateId WHERE l.ClassId=@class
            """;
        gear.Parameters.AddWithValue("@owner", $"offlinebot:{botId}");
        gear.Parameters.AddWithValue("@now", now);
        gear.Parameters.AddWithValue("@class", identity.ClassId);
        if (gear.ExecuteNonQuery() < 15)
            throw new InvalidOperationException($"The level-50 class template for {identity.ClassName} is missing or incomplete. No bots were added.");
    }

    private sealed record BotStartingLocation(int RegionId, int X, int Y, int Z, int ZoneId = 0, string ZoneName = "");

    // Match the server's established capital recovery anchors. Its normal
    // login validation grounds these positions on the current navmesh.
    private static BotStartingLocation CapitalBotStartingLocation(int realm) => realm switch
    {
        1 => new(10, 35990, 30298, 8000, 26, "City of Camelot"),
        2 => new(101, 32020, 28294, 8819, 120, "Jordheim"),
        3 => new(201, 33197, 31200, 8000, 209, "Tir na Nog"),
        _ => throw new ArgumentOutOfRangeException(nameof(realm)),
    };

    private static BotStartingLocation ChooseBotStartingLocation(
        SQLiteConnection connection,
        SQLiteTransaction transaction,
        int realm,
        int raceId,
        int classId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Region, XPos, YPos, ZPos
            FROM StartupLocation
            WHERE MinVersion <= 168
              AND (RealmID=0 OR RealmID=@realm)
              AND (RaceID=0 OR RaceID=@race)
              AND (ClassID=0 OR ClassID=@class)
              AND (XPos<>0 OR YPos<>0 OR ZPos<>0)
              AND ((@realm=1 AND Region IN (1,51))
                OR (@realm=2 AND Region IN (100,151))
                OR (@realm=3 AND Region IN (200,181)))
            GROUP BY Region, XPos, YPos, ZPos
            """;
        command.Parameters.AddWithValue("@realm", realm);
        command.Parameters.AddWithValue("@race", raceId);
        command.Parameters.AddWithValue("@class", classId);

        var starts = new List<BotStartingLocation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            starts.Add(new BotStartingLocation(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        if (starts.Count == 0)
        {
            throw new InvalidOperationException(
                $"No authoritative Classic/Shrouded Isles starting location exists for realm {realm}, race {raceId}, class {classId}. No capital fallback was used.");
        }

        return starts[Random.Shared.Next(starts.Count)];
    }

    /// <summary>Points the population switches (settings table and server properties) at the roster: on with any bot, target = roster size.</summary>
    public static void UpdatePopulationTarget(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        int count;
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM offline_world_bots WHERE IsRetired=0";
            count = Convert.ToInt32(countCommand.ExecuteScalar());
        }
        int target = count;
        using (var settings = connection.CreateCommand())
        {
            settings.Transaction = transaction;
            settings.CommandText = "UPDATE offline_population_settings SET Value=CASE Key WHEN 'PopulationEnabled' THEN @enabled WHEN 'ActiveTarget' THEN @target WHEN 'HardActiveCap' THEN '0' ELSE Value END WHERE Key IN ('PopulationEnabled','ActiveTarget','HardActiveCap')";
            settings.Parameters.AddWithValue("@enabled", count > 0 ? "true" : "false");
            settings.Parameters.AddWithValue("@target", target.ToString());
            settings.ExecuteNonQuery();
        }
        using (var serverProperties = connection.CreateCommand())
        {
            serverProperties.Transaction = transaction;
            serverProperties.CommandText = "UPDATE ServerProperty SET Value=CASE Key WHEN 'population_enabled' THEN @enabled WHEN 'active_target' THEN @target WHEN 'hard_active_cap' THEN '0' ELSE Value END WHERE Key IN ('population_enabled','active_target','hard_active_cap')";
            serverProperties.Parameters.AddWithValue("@enabled", count > 0 ? "true" : "false");
            serverProperties.Parameters.AddWithValue("@target", target.ToString());
            serverProperties.ExecuteNonQuery();
        }
    }
}
