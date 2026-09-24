using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;

[TestFixture]
public sealed class EstablishedGearTests
{
    [Test]
    public void LevelFiftyBotGetsItsCompletePreparedClassLoadout()
    {
        using var db = new SQLiteConnection("Data Source=:memory:;Version=3;New=True");
        db.Open();
        using (var schema = db.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE offline_level50_loadouts (ClassId INTEGER, SlotPosition INTEGER, TemplateId TEXT);
                CREATE TABLE ItemTemplate (Id_nb TEXT, Object_Type INTEGER, Item_Type INTEGER,
                    Level INTEGER, LevelRequirement INTEGER, Model INTEGER, MaxCount INTEGER,
                    Realm INTEGER, Quality INTEGER, AllowedClasses TEXT, MaxCondition INTEGER, MaxDurability INTEGER);
                CREATE TABLE Inventory (Inventory_ID TEXT, OwnerID TEXT, ITemplate_Id TEXT,
                    SlotPosition INTEGER, Count INTEGER, Condition INTEGER, Durability INTEGER, LastTimeRowUpdated TEXT);
                """;
            schema.ExecuteNonQuery();
        }
        int[] slots = [10, 21, 22, 23, 25, 27, 28, 24, 26, 29, 32, 33, 34, 35, 36];
        using var transaction = db.BeginTransaction();
        foreach (int slot in slots)
        {
            int type = slot == 10 ? 3 : slot is 21 or 22 or 23 or 25 or 27 or 28 ? 35 : 41;
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO offline_level50_loadouts VALUES (1,@slot,@top);
                INSERT INTO ItemTemplate VALUES (@top,@type,@slot,50,50,1,1,1,100,'1',100,100);
                INSERT INTO ItemTemplate VALUES (@mid,@type,@slot,20,20,1,1,1,100,'1',100,100);
                INSERT INTO ItemTemplate VALUES (@wrong,@type,@slot,24,24,1,1,1,100,'2',100,100);
                """;
            insert.Parameters.AddWithValue("@slot", slot);
            insert.Parameters.AddWithValue("@type", type);
            insert.Parameters.AddWithValue("@top", $"top-{slot}");
            insert.Parameters.AddWithValue("@mid", $"mid-{slot}");
            insert.Parameters.AddWithValue("@wrong", $"wrong-{slot}");
            insert.ExecuteNonQuery();
        }
        Assembly launcher = Assembly.Load("OfflineDAoC");
        Type identityType = launcher.GetType("OfflineDaoc.Configuration.BotCharacterGenerator+Identity")!;
        object identity = Activator.CreateInstance(identityType,
            ["Testbot", 1, 1, 1, "Paladin", 1, "Briton"])!;
        Type main = launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        main.GetMethod("EquipGeneratedBot", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [db, transaction, 7L, identity, DateTime.UtcNow.ToString("O")]);
        transaction.Commit();
        using var check = db.CreateCommand();
        check.CommandText = "SELECT COUNT(*), MIN(ITemplate_Id), MAX(ITemplate_Id) FROM Inventory";
        using var reader = check.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(15));
        Assert.That(reader.GetString(1), Does.StartWith("top-"));
        Assert.That(reader.GetString(2), Does.StartWith("top-"));
    }
}
