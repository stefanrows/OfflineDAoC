using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class OpenBgSettingsTests
{
    private string _folder = null!, _database = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "open-bg-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "world.db");
        Sql("CREATE TABLE ServerProperty(Category TEXT, `Key` TEXT NOT NULL, Description TEXT, DefaultValue TEXT, Value TEXT, " +
            "LastTimeRowUpdated TEXT, ServerProperty_ID TEXT); CREATE UNIQUE INDEX ServerProperty_Key ON ServerProperty(`Key`); " +
            "CREATE UNIQUE INDEX ServerProperty_ID ON ServerProperty(ServerProperty_ID); " +
            "INSERT INTO ServerProperty VALUES('pvp','open_bg_solos','server text','4','9','2026-01-01',NULL);" +
            "INSERT INTO ServerProperty VALUES('rates','xp_rate','xp','1','5','2026-01-01','');");
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private object Sql(string text)
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = text;
        return command.ExecuteScalar();
    }

    [Test] public void ToRowsWritesEveryKey()
    {
        var rows = OpenBgSettings.ToRows(new OpenBgSettings(true, 2, 3, 5, 60, 10, 30, false));
        Assert.That(rows, Is.EqualTo(new[]
        {
            ("open_bg_enabled", "True"), ("open_bg_groups8", "2"), ("open_bg_groups_small", "3"), ("open_bg_solos", "5"),
            ("open_bg_max_bots", "60"), ("open_bg_cycle_min_minutes", "10"), ("open_bg_cycle_max_minutes", "30"),
            ("open_bg_player_drift", "False"),
        }));
    }

    [Test] public void FromRowsReadsDefaultsWhenMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBgSettings.FromRows([]), Is.EqualTo(OpenBgSettings.Defaults));
            Assert.That(OpenBgSettings.FromRows(null), Is.EqualTo(OpenBgSettings.Defaults));
            Assert.That(OpenBgSettings.Defaults, Is.EqualTo(new OpenBgSettings(false, 1, 2, 4, 72, 15, 45, true)), "the server's defaults");
            // A row that is present wins; an unreadable one falls back to the default.
            Assert.That(OpenBgSettings.FromRows([("OPEN_BG_ENABLED", "true"), ("open_bg_solos", "7"), ("open_bg_groups8", "lots")]),
                Is.EqualTo(OpenBgSettings.Defaults with { Enabled = true, Solos = 7 }));
            Assert.That(OpenBgSettings.FromRows(OpenBgSettings.ToRows(new OpenBgSettings(true, 4, 6, 12, 120, 5, 120, false))),
                Is.EqualTo(new OpenBgSettings(true, 4, 6, 12, 120, 5, 120, false)), "round trip");
        });
    }

    [Test] public void RangesClamp()
    {
        var wild = new OpenBgSettings(true, 9, -1, 40, 500, 1, 3, true);
        Assert.Multiple(() =>
        {
            Assert.That(wild.Clamped(), Is.EqualTo(new OpenBgSettings(true, 4, 0, 12, 120, 5, 5, true)), "cycle max never below the min");
            Assert.That(OpenBgSettings.ToRows(wild).Select(row => row.value),
                Is.EqualTo(new[] { "True", "4", "0", "12", "120", "5", "5", "True" }));
            Assert.That(OpenBgSettings.FromRows([("open_bg_max_bots", "2"), ("open_bg_cycle_min_minutes", "200"), ("open_bg_cycle_max_minutes", "10")]),
                Is.EqualTo(OpenBgSettings.Defaults with { MaxBots = 8, CycleMin = 120, CycleMax = 120 }));
            foreach (OpenBgField field in OpenBgSettings.Fields)
                Assert.That(field.Default, Is.InRange(field.Min, field.Max), field.Name);
        });
    }

    [Test] public void FieldsMatchTheServerKeysAndRanges()
    {
        Assert.That(OpenBgSettings.Fields.Select(field => $"{field.Name}:{field.RowKey}:{field.Min}-{field.Max}"), Is.EqualTo(new[]
        {
            "enabled:open_bg_enabled:0-1", "groups8:open_bg_groups8:0-4", "small:open_bg_groups_small:0-6", "solos:open_bg_solos:0-12",
            "max_bots:open_bg_max_bots:8-120", "cycle_min:open_bg_cycle_min_minutes:5-120", "cycle_max:open_bg_cycle_max_minutes:5-120",
            "drift:open_bg_player_drift:0-1",
        }));
        Assert.That(OpenBgSettings.Fields.All(field => field.Help.Length > 0 && field.Description.Length > 0));
    }

    [Test] public void SaveWritesTheRowsWithNullIdsAndKeepsExistingDescriptions()
    {
        Assert.That(OpenBgSettings.Load(_database), Is.EqualTo(OpenBgSettings.Defaults with { Solos = 9 }));
        var settings = new OpenBgSettings(true, 2, 1, 3, 48, 20, 40, false);
        OpenBgSettings.Save(_database, settings, () => true);
        Assert.Multiple(() =>
        {
            Assert.That(OpenBgSettings.Load(_database), Is.EqualTo(settings));
            Assert.That(Sql("SELECT count(*) FROM ServerProperty WHERE `Key` LIKE 'open_bg_%'"), Is.EqualTo(8L));
            Assert.That(Sql("SELECT count(*) FROM ServerProperty WHERE ServerProperty_ID IS NOT NULL"), Is.EqualTo(0L), "new rows and the legacy '' id stay NULL");
            Assert.That(Sql("SELECT Description FROM ServerProperty WHERE `Key`='open_bg_solos'"), Is.EqualTo("server text"));
            Assert.That(Sql("SELECT Category || '|' || DefaultValue || '|' || Value FROM ServerProperty WHERE `Key`='open_bg_enabled'"), Is.EqualTo("pvp|False|True"));
            Assert.That(Sql("SELECT Value FROM ServerProperty WHERE `Key`='xp_rate'"), Is.EqualTo("5"), "other rows untouched");
        });
    }

    [Test] public void SaveRefusesWhileTheServerRuns()
    {
        Assert.Throws<InvalidOperationException>(() => OpenBgSettings.Save(_database, OpenBgSettings.Defaults with { Enabled = true }, () => false));
        Assert.That(Sql("SELECT count(*) FROM ServerProperty WHERE `Key` LIKE 'open_bg_%'"), Is.EqualTo(1L));
        // Started between the first check and the commit: rolled back.
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => OpenBgSettings.Save(_database, OpenBgSettings.Defaults, () => ++calls == 1));
        Assert.That(Sql("SELECT count(*) FROM ServerProperty WHERE `Key` LIKE 'open_bg_%'"), Is.EqualTo(1L));
    }

    [Test] public void LoadOfAMissingDatabaseGivesTheDefaults()
    {
        Assert.That(OpenBgSettings.Load(Path.Combine(_folder, "missing.db")), Is.EqualTo(OpenBgSettings.Defaults));
    }

    private const string LiveJson = """
        {"UpdatedUtc":"2026-10-11T03:00:00Z","Running":true,"CampaignEnabled":true,"OpenEnabled":true,"Ready":true,
         "NotReadyReason":null,"Phase":"n/a (stage 2)","Attacker":null,"PhaseSecondsLeft":0,"KeepOwner":"Thidranki Faste: Midgard",
         "AchievedSpeed":19.6,"SelectedSpeed":20,"Cap":72,"Players":[{"Realm":"Albion","Name":"Ked","Level":50}],
         "Bots":[{"Realm":"Albion","State":"inside"},{"Realm":"Albion","State":"travelling"},{"Realm":"Hibernia","State":"inside"}]}
        """;

    [Test] public void LiveStatusSummarisesAFreshSnapshot()
    {
        DateTime now = new(2026, 10, 11, 3, 0, 10, DateTimeKind.Utc);
        string text = OpenBgLiveStatus.Describe(LiveJson, now);
        Assert.That(text, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Live (10 s ago): open BG on and ready. Phase: n/a (stage 2)."));
            Assert.That(text, Does.Contain("Keep 11: Thidranki Faste: Midgard."));
            Assert.That(text, Does.Contain("Bots inside: Albion 1, Midgard 0, Hibernia 1 (travelling 1, leaving 0), cap 72."));
            Assert.That(text, Does.Contain("Players inside: 1. World speed 19.6x."));
        });
    }

    [Test] public void LiveStatusIgnoresStaleStoppedOrBrokenSnapshots()
    {
        DateTime now = new(2026, 10, 11, 3, 0, 10, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(OpenBgLiveStatus.Describe(LiveJson, now.AddMinutes(5)), Is.Null, "stale");
            Assert.That(OpenBgLiveStatus.Describe(LiveJson.Replace("\"Running\":true", "\"Running\":false"), now), Is.Null, "stopped");
            Assert.That(OpenBgLiveStatus.Describe("{not json", now), Is.Null);
            Assert.That(OpenBgLiveStatus.Read(Path.Combine(_folder, "missing.json"), now), Is.Null);
        });
    }

    [Test] public void TabEnablesSaveOnlyWhileTheServerIsStopped()
    {
        bool stopped = true;
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.OpenBgSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, Path.Combine(_folder, OpenBgLiveStatus.FileName),
            (Func<bool>)(() => stopped))!;
        Button save = (Button)type.GetField("_save", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        Label live = (Label)type.GetField("_live", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        void Update() => type.GetMethod("UpdateState")!.Invoke(panel, null);
        Assert.That(save.Enabled, Is.True);
        stopped = false;
        Update();
        Assert.Multiple(() =>
        {
            Assert.That(save.Enabled, Is.False);
            Assert.That(live.Text, Is.EqualTo(OpenBgLiveStatus.Fallback), "no snapshot yet");
        });
        stopped = true;
        Update();
        Assert.That(save.Enabled, Is.True);
    }
}
