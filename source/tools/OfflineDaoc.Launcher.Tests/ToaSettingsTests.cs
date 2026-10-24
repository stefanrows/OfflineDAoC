using System.Data.SQLite;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class ToaSettingsTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private static readonly BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _folder = null!, _database = null!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "toa-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "world.db");
        // The server's own shape: Key primary key, ServerProperty_ID unique and NULL for server rows (one legacy '').
        Sql("CREATE TABLE ServerProperty(Category TEXT NOT NULL, `Key` VARCHAR(255) NOT NULL PRIMARY KEY, Description TEXT NOT NULL, " +
            "DefaultValue TEXT NOT NULL, Value TEXT NOT NULL, LastTimeRowUpdated DATETIME NOT NULL, ServerProperty_ID VARCHAR(255)); " +
            "CREATE UNIQUE INDEX U_ServerProperty_ServerProperty_ID ON ServerProperty(ServerProperty_ID); " +
            "INSERT INTO ServerProperty VALUES('pve','atlantis_teleport_plvl','server text','1','1','2026-01-01',NULL);" +
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

    private string Value(string key) => Sql($"SELECT Value FROM ServerProperty WHERE `Key`='{key}'") as string;

    [Test] public void FieldsAreTheSixServerProperties()
    {
        Assert.That(ToaSettings.Fields.Select(field => $"{field.Name}:{field.RowKey}:{field.Category}"), Is.EqualTo(new[]
        {
            "atlantis:atlantis_teleport_plvl:pve", "loot:toa_loot_in_atlantis:atlas_rog", "cap_chance:rog_toa_item_chance:atlas_rog",
            "ml_xp:ml_xp_per_level:progression", "artifact_xp:artifact_xp_rate:pve", "data_packs:world_data_packs_enabled:world",
        }));
        Assert.That(ToaSettings.Defaults, Is.EqualTo(new ToaSettings(1, true, 25, 10, 350, true)));
    }

    [Test] public void MissingRowsAndUnreadableValuesGiveTheCodeDefaults()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToaSettings.FromRows([]), Is.EqualTo(ToaSettings.Defaults));
            Assert.That(ToaSettings.FromRows(null), Is.EqualTo(ToaSettings.Defaults));
            Assert.That(ToaSettings.FromRows([("toa_loot_in_atlantis", "yes"), ("ml_xp_per_level", "ten"), ("artifact_xp_rate", "")]),
                Is.EqualTo(ToaSettings.Defaults), "shown as the code default; the server's parse fails and it keeps the field's current value (mostly 0/false)");
            Assert.That(ToaSettings.FromRows([("TOA_LOOT_IN_ATLANTIS", " False "), ("Artifact_XP_Rate", "35")]),
                Is.EqualTo(ToaSettings.Defaults with { LootInAtlantis = false, ArtifactXpRate = 35 }), "keys match without case, like the server");
        });
    }

    [Test] public void SavedValuesAreReadAndClampedForDisplay()
    {
        ToaSettings read = ToaSettings.FromRows([
            ("atlantis_teleport_plvl", "4"), ("toa_loot_in_atlantis", "false"), ("rog_toa_item_chance", "140"),
            ("ml_xp_per_level", "-3"), ("artifact_xp_rate", "20000"), ("world_data_packs_enabled", "False")]);
        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(new ToaSettings(4, false, 100, 0, 10000, false)));
            Assert.That(read.AtlantisOpen, Is.False);
            Assert.That(ToaSettings.FromRows([("atlantis_teleport_plvl", "2")]).AtlantisOpen, Is.False, "GM only is not open to players");
            Assert.That(ToaSettings.FromRows([("atlantis_teleport_plvl", "2")]).AtlantisPlvl, Is.EqualTo(2));
            Assert.That(ToaSettings.FromRows([("atlantis_teleport_plvl", "0")]).AtlantisOpen, Is.True, "0 lets every account in too");
            Assert.That(ToaSettings.FromRows([("atlantis_teleport_plvl", "9")]).AtlantisPlvl, Is.EqualTo(4));
        });
    }

    [Test] public void RowsCarryTheServersValueFormat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToaSettings.SwitchRow("atlantis", true).Value, Is.EqualTo("1"));
            Assert.That(ToaSettings.SwitchRow("atlantis", false).Value, Is.EqualTo("4"));
            Assert.That(ToaSettings.SwitchRow("atlantis", false).DefaultValue, Is.EqualTo("1"));
            Assert.That(ToaSettings.SwitchRow("loot", true), Is.EqualTo(new ServerPropertyRow("toa_loot_in_atlantis",
                ToaSettings.Field("loot").Description, "True", "True")));
            Assert.That(ToaSettings.SwitchRow("data_packs", false).Value, Is.EqualTo("False"));
            Assert.That(ToaSettings.NumberRow("cap_chance", 101).Value, Is.EqualTo("100"));
            Assert.That(ToaSettings.NumberRow("ml_xp", -1).Value, Is.EqualTo("0"));
            Assert.That(ToaSettings.NumberRow("artifact_xp", 35), Is.EqualTo(new ServerPropertyRow("artifact_xp_rate",
                ToaSettings.Field("artifact_xp").Description, "350", "35")));
            Assert.Throws<ArgumentOutOfRangeException>(() => ToaSettings.SwitchRow("ml_xp", true));
            Assert.Throws<ArgumentOutOfRangeException>(() => ToaSettings.NumberRow("loot", 1));
        });
    }

    [Test] public void SaveAndLoadRoundTripOneRowAtATime()
    {
        Assert.That(ToaSettings.Load(_database), Is.EqualTo(ToaSettings.Defaults), "only the Atlantis row exists, value 1");
        ToaSettings.Save(_database, "atlantis", ToaSettings.SwitchRow("atlantis", false), () => true);
        ToaSettings.Save(_database, "loot", ToaSettings.SwitchRow("loot", false), () => true);
        ToaSettings.Save(_database, "cap_chance", ToaSettings.NumberRow("cap_chance", 40), () => true);
        ToaSettings.Save(_database, "ml_xp", ToaSettings.NumberRow("ml_xp", 0), () => true);
        ToaSettings.Save(_database, "artifact_xp", ToaSettings.NumberRow("artifact_xp", 35), () => true);
        ToaSettings.Save(_database, "data_packs", ToaSettings.SwitchRow("data_packs", false), () => true);
        Assert.Multiple(() =>
        {
            Assert.That(ToaSettings.Load(_database), Is.EqualTo(new ToaSettings(4, false, 40, 0, 35, false)));
            Assert.That(Value("atlantis_teleport_plvl"), Is.EqualTo("4"));
            Assert.That(Sql("SELECT Category || '|' || Description || '|' || DefaultValue FROM ServerProperty WHERE `Key`='atlantis_teleport_plvl'"),
                Is.EqualTo("pve|server text|1"), "an existing row keeps its category, description and default");
            Assert.That(Sql("SELECT Category || '|' || DefaultValue FROM ServerProperty WHERE `Key`='ml_xp_per_level'"),
                Is.EqualTo("progression|10"), "a new row gets the server's category and default");
            Assert.That(Sql("SELECT Category FROM ServerProperty WHERE `Key`='world_data_packs_enabled'"), Is.EqualTo("world"));
            Assert.That(Sql("SELECT count(*) FROM ServerProperty WHERE ServerProperty_ID IS NOT NULL"), Is.EqualTo(0L),
                "new rows leave the unique id NULL like the server");
            Assert.That(Value("xp_rate"), Is.EqualTo("5"), "other rows are not touched");
        });
        ToaSettings.Save(_database, "atlantis", ToaSettings.SwitchRow("atlantis", true), () => true);
        Assert.That(ToaSettings.Load(_database), Is.EqualTo(new ToaSettings(1, false, 40, 0, 35, false)), "only the changed row moves");
    }

    [Test] public void SaveIsRefusedWhileTheServerRuns()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ToaSettings.Save(_database, "ml_xp", ToaSettings.NumberRow("ml_xp", 50), () => false));
        int calls = 0;
        // Stopped at the first check, running again right before the commit: rolled back.
        Assert.Throws<InvalidOperationException>(() =>
            ToaSettings.Save(_database, "ml_xp", ToaSettings.NumberRow("ml_xp", 50), () => ++calls == 1));
        Assert.That(Value("ml_xp_per_level"), Is.Null);
    }

    [Test] public void LoadOfAMissingDatabaseOrTableGivesDefaults()
    {
        Assert.That(ToaSettings.Load(Path.Combine(_folder, "missing.db")), Is.EqualTo(ToaSettings.Defaults));
        Sql("DROP TABLE ServerProperty");
        Assert.That(ToaSettings.Load(_database), Is.EqualTo(ToaSettings.Defaults));
    }

    [Test] public void CodeDefaultsMatchTheServerDeclarations()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "source", "server", "GameServer", "serverproperty", "ServerProperties.cs")))
            root = Path.GetDirectoryName(root);
        if (root == null) Assert.Ignore("Server source not found above the test folder.");
        string server = File.ReadAllText(Path.Combine(root!, "source", "server", "GameServer", "serverproperty", "ServerProperties.cs")) +
            File.ReadAllText(Path.Combine(root!, "source", "server", "GameServer", "gameutils", "MasterLevelProgression.cs"));
        foreach (ToaField field in ToaSettings.Fields)
        {
            Match declaration = Regex.Match(server,
                $@"\[ServerProperty\(\s*""(?<category>[^""]+)"",\s*""{field.RowKey}"",(?:[^\]]|\n)*?,\s*(?<default>true|false|-?\d+)\)\]");
            Assert.That(declaration.Success, Is.True, field.RowKey);
            Assert.That(declaration.Groups["category"].Value, Is.EqualTo(field.Category), field.RowKey);
            string expected = field.IsSwitch && field.Name != "atlantis" ? (field.Default == 1 ? "true" : "false") : field.Default.ToString();
            Assert.That(declaration.Groups["default"].Value, Is.EqualTo(expected), field.RowKey);
        }
    }

    [Test] public void LauncherShowsTheBlockBelowTheRatesAndWritesThroughTheForm()
    {
        Type mainFormType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using var form = (Form)Activator.CreateInstance(mainFormType)!;
        // Layout first (before the test database is set): the TRIALS OF ATLANTIS block sits below the rate block.
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20_000, -20_000);
        form.Show();
        TabControl tabs = Descendants(form).OfType<TabControl>().First(tab => tab.TabPages.Cast<TabPage>().Any(page => page.Text == "XP Settings"));
        TabPage xp = tabs.TabPages.Cast<TabPage>().First(page => page.Text == "XP Settings");
        tabs.SelectedTab = xp;
        Application.DoEvents();
        Control Block(string title) => xp.Controls.Cast<Control>()
            .Single(panel => Descendants(panel).OfType<Label>().Any(label => label.Text == title));
        Control rates = Block("EXPERIENCE & REALM POINT RATES"), toa = Block("TRIALS OF ATLANTIS");
        Assert.That(toa.Top, Is.GreaterThan(rates.Bottom));
        Assert.That(Descendants(toa).OfType<CheckBox>().Select(box => box.Text),
            Is.EqualTo(new[] { "Atlantis open to players", "ToA loot in Atlantis", "World data packs enabled" }));
        using var preview = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
        preview.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory, "launcher-toa-settings-preview.png"));
        xp.AutoScrollPosition = new Point(0, 10_000); // scrolled to the end
        Application.DoEvents();
        Assert.That(toa.Bottom, Is.LessThanOrEqualTo(xp.ClientSize.Height + 1), "the tab scrolls down to the end of the block");
        using var scrolled = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(scrolled, new Rectangle(Point.Empty, scrolled.Size));
        scrolled.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory, "launcher-toa-settings-scrolled-preview.png"));

        mainFormType.GetField("_database", HiddenInstance)!.SetValue(form, _database);
        mainFormType.GetField("_persistXpRateServerRunningOverride", HiddenInstance)!.SetValue(form, (bool?)false);
        var atlantis = (CheckBox)mainFormType.GetField("_toaAtlantisOpen", HiddenInstance)!.GetValue(form)!;
        var numbers = (Dictionary<string, NumericUpDown>)mainFormType.GetField("_toaNumbers", HiddenInstance)!.GetValue(form)!;
        var status = (Label)mainFormType.GetField("_toaStatus", HiddenInstance)!.GetValue(form)!;
        Assert.That(numbers.Keys, Is.EquivalentTo(new[] { "cap_chance", "ml_xp", "artifact_xp" }));
        Assert.That((numbers["artifact_xp"].Minimum, numbers["artifact_xp"].Maximum), Is.EqualTo((0m, 10000m)));
        Assert.That((numbers["ml_xp"].Minimum, numbers["ml_xp"].Maximum), Is.EqualTo((0m, 100m)));

        // Write through the form's persist path (the server guard is stubbed as stopped).
        MethodInfo persist = mainFormType.GetMethod("PersistToaSettings", HiddenInstance)!;
        persist.Invoke(form, new object[] { new (string, ServerPropertyRow)[]
        {
            ("atlantis", ToaSettings.SwitchRow("atlantis", false)), ("artifact_xp", ToaSettings.NumberRow("artifact_xp", 35)),
        } });
        Assert.That(ToaSettings.Load(_database), Is.EqualTo(ToaSettings.Defaults with { AtlantisPlvl = 4, ArtifactXpRate = 35 }));

        // A snapshot puts the saved values into the controls without saving anything back.
        Type snapshotType = mainFormType.GetNestedType("DashboardSnapshot", BindingFlags.NonPublic)!;
        object Snapshot(string state, ToaSettings toa)
        {
            object snapshot = snapshotType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(constructor => constructor.GetParameters().Length == 13)
                .Invoke(new object[] { state, null!, null!, 0, 0d, 0d, 1d, 1d, 1d, 1d, false, null!, null! });
            snapshotType.GetProperty("Toa")!.SetValue(snapshot, toa);
            return snapshot;
        }
        MethodInfo update = mainFormType.GetMethod("UpdateXpRateControls", HiddenInstance)!;
        update.Invoke(form, new[] { Snapshot("Stopped", ToaSettings.Load(_database)) });
        Assert.Multiple(() =>
        {
            Assert.That(atlantis.Checked, Is.False);
            Assert.That(atlantis.Enabled, Is.True);
            Assert.That(numbers["artifact_xp"].Value, Is.EqualTo(35m));
            Assert.That(numbers["artifact_xp"].Enabled, Is.True);
            Assert.That(status.Text, Does.Contain("next server startup"));
        });
        var pending = (HashSet<string>)mainFormType.GetField("_toaPendingNumbers", HiddenInstance)!.GetValue(form)!;
        Assert.That(pending, Is.Empty, "loading values marks nothing for saving");

        update.Invoke(form, new[] { Snapshot("Running", ToaSettings.Defaults with { AtlantisPlvl = 2 }) });
        var notes = (Dictionary<string, Label>)mainFormType.GetField("_toaNotes", HiddenInstance)!.GetValue(form)!;
        Assert.Multiple(() =>
        {
            Assert.That(atlantis.Enabled || numbers.Values.Any(number => number.Enabled), Is.False, "locked while the server runs");
            Assert.That(status.Text, Does.Contain("LOCKED"));
            Assert.That(notes["atlantis"].Text, Does.Contain("GMs"));
        });

    }

    [Test] public void ClosingTheLauncherSavesNumbersStillWaitingForTheirDelayedSave()
    {
        Type mainFormType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        var form = (Form)Activator.CreateInstance(mainFormType)!;
        try
        {
            // FormClosing needs a shown window; the test database is set after the first (default-path) refresh.
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20_000, -20_000);
            form.Show();
            Application.DoEvents();
            mainFormType.GetField("_database", HiddenInstance)!.SetValue(form, _database);
            mainFormType.GetField("_persistXpRateServerRunningOverride", HiddenInstance)!.SetValue(form, (bool?)false);
            var numbers = (Dictionary<string, NumericUpDown>)mainFormType.GetField("_toaNumbers", HiddenInstance)!.GetValue(form)!;
            var pending = (HashSet<string>)mainFormType.GetField("_toaPendingNumbers", HiddenInstance)!.GetValue(form)!;

            numbers["artifact_xp"].Value = 35;   // committed, waiting for the delayed save
            // Typing goes into the inner edit box; NumericUpDown commits it only on validation or a Value read.
            numbers["ml_xp"].Controls.OfType<TextBox>().Single().Text = "77";
            Assert.That(pending, Is.EquivalentTo(new[] { "artifact_xp" }));
            Assert.That(Value("artifact_xp_rate"), Is.Null, "nothing is written before the delay");

            form.Close();
            Assert.Multiple(() =>
            {
                Assert.That(ToaSettings.Load(_database), Is.EqualTo(ToaSettings.Defaults with { ArtifactXpRate = 35, MlXpPerLevel = 77 }));
                Assert.That(pending, Is.Empty);
                Assert.That(Value("atlantis_teleport_plvl"), Is.EqualTo("1"), "rows without an edit stay as they were");
            });
        }
        finally
        {
            form.Dispose();
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
}
