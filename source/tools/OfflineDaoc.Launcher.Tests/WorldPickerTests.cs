using System.Data.SQLite;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OfflineDaoc.Configuration;
using OfflineDaoc.Launcher;

/// <summary>The launcher's world picker (package R5b, docs/RVR_WORLDS.md "World picker") on a temporary installation.</summary>
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class WorldPickerTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Main = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
    private static readonly FieldInfo ProbeField = Main.GetField("ServerRunningProbeForTests", BindingFlags.Static | BindingFlags.NonPublic)!;

    private string _install = null!;
    private string _runtime = null!;
    private WorldFiles _rvr = null!;
    private WorldFiles _camlann = null!;
    private string ConfigPath => Path.Combine(_runtime, "server", "config", "serverconfig.xml");

    [SetUp]
    public void Setup()
    {
        _install = Path.Combine(Path.GetTempPath(), "world-picker-test-" + Guid.NewGuid().ToString("N"), "playable");
        _runtime = Path.Combine(_install, "runtime");
        Directory.CreateDirectory(Path.Combine(_runtime, "data"));
        Directory.CreateDirectory(Path.Combine(_runtime, "server", "config"));
        _rvr = WorldFiles.For(_runtime, WorldSaveKind.Realm);
        _camlann = WorldFiles.For(_runtime, WorldSaveKind.Camlann);
        // A window picks its world by the running server first; these tests run beside a live server too.
        ProbeField.SetValue(null, (Func<bool>)(() => false));
    }

    [TearDown]
    public void Cleanup()
    {
        ProbeField.SetValue(null, null);
        SQLiteConnection.ClearAllPools();
        string root = Path.GetDirectoryName(_install)!;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    // ---- remembered choice -------------------------------------------------------------------------------------

    [Test]
    public void TheChoiceIsRememberedPerInstallation()
    {
        string store = Path.Combine(_install, "user", "world-choice.json");
        string other = Path.Combine(_install, "other-install", "runtime");
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.Null, "nothing picked yet");

        WorldChoiceStore.Save(store, _runtime, WorldSaveKind.Camlann);
        WorldChoiceStore.Save(store, other, WorldSaveKind.Realm);
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.EqualTo(WorldSaveKind.Camlann));
        Assert.That(WorldChoiceStore.Load(store, other), Is.EqualTo(WorldSaveKind.Realm));
        Assert.That(WorldChoiceStore.Load(store, _runtime.ToUpperInvariant() + Path.DirectorySeparatorChar), Is.EqualTo(WorldSaveKind.Camlann),
            "the key is the folder, case-insensitive and without a trailing separator");

        WorldChoiceStore.Save(store, _runtime, WorldSaveKind.Realm);
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.EqualTo(WorldSaveKind.Realm));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(store)!), Has.Length.EqualTo(1), "no temporary file is left");
        Assert.That(WorldChoiceStore.DefaultPath, Does.EndWith(Path.Combine("OfflineDAoC", "world-choice.json")));
    }

    [Test]
    public void AnUnreadableOrUnknownChoiceCountsAsNotPicked()
    {
        string store = Path.Combine(_install, "world-choice.json");
        File.WriteAllText(store, "{ not json");
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.Null);
        File.WriteAllText(store, "{\"Installs\":{\"" + WorldChoiceStore.InstallKey(_runtime).Replace("\\", "\\\\") + "\":\"Gothwaite\"}}");
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.Null);
        WorldChoiceStore.Save(store, _runtime, WorldSaveKind.Camlann);
        Assert.That(WorldChoiceStore.Load(store, _runtime), Is.EqualTo(WorldSaveKind.Camlann), "an unreadable file is replaced on the next pick");
    }

    // ---- first-run default and save states ---------------------------------------------------------------------

    [Test]
    public void FirstRunShowsRvrUnlessOnlyACamlannWorldExists()
    {
        Assert.That(WorldPicker.DefaultWorld(_runtime), Is.EqualTo(WorldSaveKind.Realm), "neither world exists");

        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        Assert.That(WorldPicker.DefaultWorld(_runtime), Is.EqualTo(WorldSaveKind.Camlann), "only the Camlann world exists");

        CreateSave(_rvr.Database, WorldSaveLayout.RealmMarker);
        Assert.That(WorldPicker.DefaultWorld(_runtime), Is.EqualTo(WorldSaveKind.Realm), "the RvR world exists");
    }

    [Test]
    public void TodaysInstallWithItsCamlannSaveInTheOldFileShowsPvp()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        Assert.That(WorldPicker.IsLegacyCamlannLayout(_runtime), Is.True);
        Assert.That(WorldPicker.DefaultWorld(_runtime), Is.EqualTo(WorldSaveKind.Camlann));

        File.Delete(_rvr.Database);
        CreateSave(_rvr.Database, "Normal");
        Assert.That(WorldPicker.IsLegacyCamlannLayout(_runtime), Is.False);
        Assert.That(WorldPicker.DefaultWorld(_runtime), Is.EqualTo(WorldSaveKind.Realm), "an old Normal save is no world yet");
    }

    [Test]
    public void EachSaveStateIsRecognised()
    {
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Realm).State, Is.EqualTo(WorldSaveState.Missing));
        CreateSave(_rvr.Database, WorldSaveLayout.RealmMarker);
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Realm).State, Is.EqualTo(WorldSaveState.Ready));
        Assert.That(WorldPicker.InspectDatabase(WorldSaveKind.Camlann, _rvr.Database).State, Is.EqualTo(WorldSaveState.OtherWorld));

        CreateSave(_camlann.Database, "Normal");
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Camlann).State, Is.EqualTo(WorldSaveState.NotCreated));
        File.Delete(_camlann.Database);
        CreateSave(_camlann.Database, null);
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Camlann).State, Is.EqualTo(WorldSaveState.NotCreated), "no marker at all");
        File.Delete(_camlann.Database);
        CreateSave(_camlann.Database, "Camlann-2");
        WorldSaveStatus unsupported = WorldPicker.Inspect(_runtime, WorldSaveKind.Camlann);
        Assert.That(unsupported.State, Is.EqualTo(WorldSaveState.Unsupported));
        Assert.That(unsupported.CanCreate, Is.False);
        Assert.That(MainFormFooter(unsupported, true), Does.Contain("Camlann-2"));
    }

    [Test]
    public void ARunningServerKeepsTheLauncherOnTheSaveItWasStartedWith()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        WorldSelection running = WorldPicker.Resolve(_runtime, WorldSaveKind.Realm, WorldSaveLayout.RealmDatabaseFileName);
        Assert.That(running.World, Is.EqualTo(WorldSaveKind.Camlann), "the marker of the running save names the world");
        Assert.That(running.Database, Is.EqualTo(_rvr.Database));

        WorldSelection stopped = WorldPicker.Resolve(_runtime, WorldSaveKind.Realm, null);
        Assert.That(stopped, Is.EqualTo(new WorldSelection(WorldSaveKind.Realm, _rvr.Database)), "stopped: the remembered choice wins");
        Assert.That(WorldPicker.Resolve(_runtime, null, null).World, Is.EqualTo(WorldSaveKind.Camlann), "stopped, nothing picked: the default");
        Assert.That(WorldPicker.Resolve(_runtime, WorldSaveKind.Camlann, null).Database, Is.EqualTo(_camlann.Database),
            "a stopped launcher always works on the world's own file");
    }

    // ---- one-time move of today's Camlann save -----------------------------------------------------------------

    [Test]
    public void TodaysCamlannSaveMovesToItsOwnFilesWithNothingLost()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        File.WriteAllBytes(_rvr.Database + "-wal", []);
        File.WriteAllText(_rvr.EventRecords, "records");
        File.WriteAllText(_rvr.EventRecords + "-shm", "shm");
        File.WriteAllText(_rvr.GothwaiteSwitch, "{\"Enabled\":true}");
        File.WriteAllText(_rvr.BotWorld, "bots");
        File.WriteAllText(Path.Combine(_runtime, "server", "bot-goals.json"), "shared");
        string saveHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_rvr.Database)));

        IReadOnlyList<string> moved = WorldPicker.RelocateLegacyCamlann(_runtime, () => true);

        // Reading the marker can make SQLite add a -shm beside the save; it moves with it like every sidecar.
        Assert.That(moved, Is.SupersetOf(new[]
        {
            _camlann.Database, _camlann.Database + "-wal", _camlann.EventRecords, _camlann.EventRecords + "-shm",
            _camlann.GothwaiteSwitch, _camlann.BotWorld,
        }));
        Assert.That(moved.All(path => Path.GetFileName(path).Contains("-camlann", StringComparison.Ordinal)), Is.True);
        Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_camlann.Database))), Is.EqualTo(saveHash));
        Assert.That(File.ReadAllText(_camlann.GothwaiteSwitch), Is.EqualTo("{\"Enabled\":true}"));
        Assert.That(WorldReset.ReplacedFiles(_rvr).Any(File.Exists), Is.False, "no RvR file is left behind");
        Assert.That(File.ReadAllText(Path.Combine(_runtime, "server", "bot-goals.json")), Is.EqualTo("shared"), "shared files stay");
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Camlann).State, Is.EqualTo(WorldSaveState.Ready));
        Assert.That(WorldPicker.Inspect(_runtime, WorldSaveKind.Realm).State, Is.EqualTo(WorldSaveState.Missing));
        Assert.That(WorldPicker.IsLegacyCamlannLayout(_runtime), Is.False);
        Assert.That(WorldReset.DefaultSeed(_runtime, WorldSaveKind.Realm), Is.EqualTo(_camlann.Database), "the RvR world is then built from it");
    }

    [Test]
    public void TheMoveIsRefusedWhileTheServerRunsOrWhenTargetsExist()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        File.WriteAllText(_rvr.RvrWorld, "old");
        Assert.That(() => WorldPicker.RelocateLegacyCamlann(_runtime, () => false), Throws.InvalidOperationException);
        Assert.That(File.Exists(_rvr.Database), Is.True);

        File.WriteAllText(_camlann.RvrWorld, "stale");
        Assert.That(() => WorldPicker.RelocateLegacyCamlann(_runtime, () => true),
            Throws.InvalidOperationException.With.Message.Contains(Path.GetFileName(_camlann.RvrWorld)));
        Assert.That(File.Exists(_rvr.Database) && File.ReadAllText(_rvr.RvrWorld) == "old", Is.True, "nothing moved");
        Assert.That(File.ReadAllText(_camlann.RvrWorld), Is.EqualTo("stale"));
    }

    [Test]
    public void AFailedMovePutsEveryFileBack()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        File.WriteAllText(_rvr.OpenBgStatus, "bg");
        void Move(string from, string to)
        {
            if (from == _rvr.OpenBgStatus) throw new IOException("simulated lock");
            File.Move(from, to);
        }

        Assert.That(() => WorldPicker.RelocateLegacyCamlann(_runtime, () => true, Move),
            Throws.TypeOf<IOException>().With.Message.Contains("Every file was put back"));
        Assert.That(File.Exists(_rvr.Database) && File.Exists(_rvr.OpenBgStatus), Is.True);
        Assert.That(File.Exists(_camlann.Database), Is.False);
    }

    // ---- server configuration before START SERVER --------------------------------------------------------------

    [Test]
    public void StartPointsTheServerConfigurationAtThePickedWorld()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.RealmMarker);
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        WriteConfig("Normal", @"Data Source=..\data\opendaoc.sqlite3.db;Version=3;Pooling=False;");

        Assert.That(PrepareWorldForStart(WorldSaveKind.Camlann, _camlann.Database), Is.Null);
        Assert.That(CamlannServerConfig.ReadDatabaseFileName(ConfigPath), Is.EqualTo(WorldSaveLayout.CamlannDatabaseFileName));
        Assert.That(File.ReadAllText(ConfigPath), Does.Contain("<GameType>PvP</GameType>").And.Contain(@"Data Source=..\data\opendaoc-camlann.sqlite3.db;Version=3;Pooling=False;"));

        Assert.That(PrepareWorldForStart(WorldSaveKind.Realm, _rvr.Database), Is.Null);
        Assert.That(CamlannServerConfig.ReadDatabaseFileName(ConfigPath), Is.EqualTo(WorldSaveLayout.RealmDatabaseFileName));
    }

    [Test]
    public void StartRefusesAWorldWithoutAStartableSaveOrAWindowOnAnotherSave()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        WriteConfig("PvP", @"Data Source=..\data\opendaoc.sqlite3.db");
        string before = File.ReadAllText(ConfigPath);

        Assert.That(PrepareWorldForStart(WorldSaveKind.Camlann, _rvr.Database), Does.Contain("reopens"),
            "a window that followed a running server on the old file reopens first");
        Assert.That(PrepareWorldForStart(WorldSaveKind.Realm, _rvr.Database), Does.Contain("CREATE WORLD"),
            "the RvR file holds a Camlann world: no start");
        Assert.That(PrepareWorldForStart(WorldSaveKind.Camlann, _camlann.Database), Does.Contain("CREATE WORLD"), "missing save: no start");
        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(before), "the configuration is untouched");
    }

    [Test]
    public void PickingAWorldWritesTheConfigurationOnlyForAReadySave()
    {
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        WriteConfig("PvP", @"Data Source=..\data\opendaoc-camlann.sqlite3.db");
        MethodInfo point = Main.GetMethod("PointServerConfigAtWorld", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.That(point.Invoke(null, [_runtime, ConfigPath, WorldSaveKind.Realm]), Is.Null);
        Assert.That(CamlannServerConfig.ReadDatabaseFileName(ConfigPath), Is.EqualTo(WorldSaveLayout.CamlannDatabaseFileName),
            "a missing RvR save is never written into the configuration (a hand-started server would create an empty save)");

        CreateSave(_rvr.Database, WorldSaveLayout.RealmMarker);
        Assert.That(point.Invoke(null, [_runtime, ConfigPath, WorldSaveKind.Realm]), Is.Null);
        Assert.That(CamlannServerConfig.ReadDatabaseFileName(ConfigPath), Is.EqualTo(WorldSaveLayout.RealmDatabaseFileName));
    }

    [Test]
    public void StartServerPreparesTheWorldRightBeforeCoreServerStarts()
    {
        string source = File.ReadAllText(Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "OfflineDaoc.Launcher", "MainForm.cs")));
        string start = Regex.Match(source, @"private void StartServer\(\)(.*?)private void OnServerExited", RegexOptions.Singleline).Groups[1].Value;
        int prepare = start.IndexOf("PrepareSelectedWorldForStart()", StringComparison.Ordinal);
        int launch = start.IndexOf("_serverProcess.Start()", StringComparison.Ordinal);
        Assert.That(prepare, Is.GreaterThan(0));
        Assert.That(launch, Is.GreaterThan(prepare), "serverconfig.xml names the picked world before every start");
        Assert.That(source, Does.Not.Contain("EnsureCamlannWorldAsync"), "the one-time in-place conversion is replaced by CREATE WORLD");
    }

    // ---- create / reset world ----------------------------------------------------------------------------------

    [Test]
    public void TheConfirmationNamesTheWorldAndTheBackupFolder()
    {
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        string backups = WorldFiles.DefaultBackupRoot(_runtime);

        string create = WorldPicker.CreationPrompt(WorldPicker.Inspect(_runtime, WorldSaveKind.Realm), _camlann.Database, backups);
        Assert.That(create, Does.StartWith("Create a fresh RvR world?"));
        Assert.That(create, Does.Contain("copied from opendaoc-camlann.sqlite3.db; that save is only read"));
        Assert.That(create, Does.Contain("The PvP world (Camlann) is not changed"));
        Assert.That(create, Does.Contain("No save is replaced"));

        string reset = WorldPicker.CreationPrompt(WorldPicker.Inspect(_runtime, WorldSaveKind.Camlann), _camlann.Database, backups);
        Assert.That(reset, Does.StartWith("Reset the PvP world (Camlann)?"));
        Assert.That(reset, Does.Contain(Path.Combine(backups, "<date-time>-camlann-world-reset")));
        Assert.That(reset, Does.Contain("Nothing is deleted"));
        Assert.That(reset, Does.Contain("The RvR world is not changed"));
    }

    [Test]
    public void AMissingWorldTurnsStartServerIntoCreateWorld()
    {
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        using var form = (Form)Activator.CreateInstance(Main)!;
        Main.GetField("_root", Hidden)!.SetValue(form, _runtime);
        Main.GetField("_database", Hidden)!.SetValue(form, _rvr.Database);
        Main.GetField("_selectedWorldStatus", Hidden)!.SetValue(form, WorldPicker.Inspect(_runtime, WorldSaveKind.Realm));
        Assert.That(Main.GetField("_world", Hidden)!.GetValue(form), Is.EqualTo(WorldSaveKind.Realm), "the test runtime has no world: RvR");

        Main.GetMethod("ShowWorldCreationState", Hidden)!.Invoke(form, null);

        var start = (Button)Main.GetField("_startButton", Hidden)!.GetValue(form)!;
        var footer = (Label)Main.GetField("_footer", Hidden)!.GetValue(form)!;
        var play = (Button)Main.GetField("_playButton", Hidden)!.GetValue(form)!;
        Assert.That(start.Text, Is.EqualTo("CREATE WORLD…"));
        Assert.That((bool)Main.GetProperty("StartCreatesWorld", Hidden)!.GetValue(form)!, Is.True, "a click creates the world instead of starting");
        Assert.That(footer.Text, Does.Contain("The RvR world has no save yet").And.Contain("PvP world (Camlann) is kept"));
        Assert.That(play.Enabled, Is.False);
        if (!ServerListening())
            Assert.That(start.Enabled, Is.True, "stopped and a seed exists: CREATE WORLD is clickable");
    }

    [Test]
    public void WithoutAnySaveCreateWorldStaysOff()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        Main.GetField("_root", Hidden)!.SetValue(form, _runtime);
        Main.GetField("_database", Hidden)!.SetValue(form, _rvr.Database);
        Main.GetField("_selectedWorldStatus", Hidden)!.SetValue(form, WorldPicker.Inspect(_runtime, WorldSaveKind.Realm));
        Main.GetMethod("ShowWorldCreationState", Hidden)!.Invoke(form, null);
        Assert.That(((Button)Main.GetField("_startButton", Hidden)!.GetValue(form)!).Enabled, Is.False);
        Assert.That(((Label)Main.GetField("_footer", Hidden)!.GetValue(form)!).Text, Does.Contain("install the server data"));
    }

    [Test]
    public void FootersExplainEveryCreatableState()
    {
        Assert.That(MainFormFooter(new WorldSaveStatus(WorldSaveKind.Realm, _rvr.Database, WorldSaveState.NotCreated, "Normal"), true),
            Does.Contain("builds a fresh RvR world from it"));
        Assert.That(MainFormFooter(new WorldSaveStatus(WorldSaveKind.Realm, _rvr.Database, WorldSaveState.OtherWorld, "Camlann-1"), true),
            Does.Contain("The RvR world file holds the PvP world (Camlann)"));
    }

    // ---- the window per world ----------------------------------------------------------------------------------

    [Test]
    public void TheWindowShowsTheWorldInTheDropdownTitleAndStatusLine()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var picker = (ComboBox)Main.GetField("_worldPicker", Hidden)!.GetValue(form)!;
        var label = (Label)Main.GetField("_worldStatusLabel", Hidden)!.GetValue(form)!;
        Assert.That(picker.Items.Cast<WorldSaveKind>().Select(WorldPicker.PickerText), Is.EqualTo(new[] { "RvR (Realm vs Realm)", "PvP (Camlann)" }));
        Assert.That(picker.SelectedItem, Is.EqualTo(WorldSaveKind.Realm));
        Assert.That(picker.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
        Assert.That(picker.Enabled, Is.False, "off until the window has checked the server is stopped");
        Assert.That(label.Text, Is.EqualTo("WORLD: RvR"));
        Assert.That(form.Text, Is.EqualTo("Offline DAoC — RvR world"));

        using var pvp = OpenFor(WorldSaveKind.Camlann);
        Assert.That(((Label)Main.GetField("_worldStatusLabel", Hidden)!.GetValue(pvp)!).Text, Is.EqualTo("WORLD: PvP (Camlann)"));
        Assert.That(pvp.Text, Is.EqualTo("Offline DAoC — PvP world (Camlann)"));
        Assert.That(((ComboBox)Main.GetField("_worldPicker", Hidden)!.GetValue(pvp)!).SelectedItem, Is.EqualTo(WorldSaveKind.Camlann));
    }

    [Test]
    public void EveryLauncherReadFollowsThePickedWorldsSave()
    {
        foreach (WorldSaveKind world in WorldPicker.Worlds)
        {
            using var form = OpenFor(world);
            string root = (string)Main.GetField("_root", Hidden)!.GetValue(form)!;
            WorldFiles expected = WorldFiles.For(root, world);
            Assert.That(Main.GetField("_database", Hidden)!.GetValue(form), Is.EqualTo(expected.Database));
            var files = (WorldFiles)Main.GetField("_worldFiles", Hidden)!.GetValue(form)!;
            Assert.That(files.GothwaiteSwitch, Is.EqualTo(expected.GothwaiteSwitch));
            Assert.That(files.EventRecords, Is.EqualTo(expected.EventRecords));
            var openBg = Main.GetField("_openBgSettings", Hidden)!.GetValue(form)!;
            Assert.That(openBg.GetType().GetField("_database", Hidden)!.GetValue(openBg), Is.EqualTo(expected.Database), "open BG tab");
            Assert.That(openBg.GetType().GetField("_statusPath", Hidden)!.GetValue(openBg), Is.EqualTo(expected.OpenBgStatus));
            var population = Main.GetField("_botGoalsSettings", Hidden)!.GetValue(form)!;
            Assert.That(population.GetType().GetField("_worldModePath", Hidden)!.GetValue(population), Is.EqualTo(expected.GothwaiteSwitch), "population tab");
            var isRvr = (Func<bool>)population.GetType().GetProperty("IsRvrWorld")!.GetValue(population)!;
            Assert.That(isRvr(), Is.EqualTo(world == WorldSaveKind.Realm), "Gothwaite follows the picked world");
        }
    }

    [Test]
    public void GothwaiteControlsAreShownOnlyInThePvpWorld()
    {
        foreach (WorldSaveKind world in WorldPicker.Worlds)
        {
            using var form = OpenFor(world);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20_000, -20_000);
            var population = (Control)Main.GetField("_botGoalsSettings", Hidden)!.GetValue(form)!;
            TabControl tabs = Descendants(form).OfType<TabControl>().First(tab => tab.TabPages.Cast<TabPage>().Any(page => page.Text == "Server population"));
            form.CreateControl();
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(page => page.Text == "Server population");
            form.Show();
            // No UpdateState call: the row must be right from the first paint, not after the two-second poll.
            var row = (Control)population.GetType().GetField("_worldModeRow", Hidden)!.GetValue(population)!;
            var mode = (ComboBox)population.GetType().GetField("_worldMode", Hidden)!.GetValue(population)!;
            Assert.That(row.Visible, Is.EqualTo(world == WorldSaveKind.Camlann), $"{world}: the World mode row");
            if (world == WorldSaveKind.Realm)
                Assert.That(mode.Enabled, Is.False);
            form.Hide();
        }
    }

    [Test]
    public void ReopeningKeepsTheWindowPlaceAndTab()
    {
        using var form = OpenFor(WorldSaveKind.Camlann, new Rectangle(40, 50, 1000, 650), tab: 4);
        Assert.That(form.Bounds, Is.EqualTo(new Rectangle(40, 50, 1000, 650)));
        Assert.That(form.StartPosition, Is.EqualTo(FormStartPosition.Manual));
        var tabs = (TabControl)Main.GetField("_mainTabs", Hidden)!.GetValue(form)!;
        Assert.That(tabs.SelectedIndex, Is.EqualTo(4));
        Assert.That(Main.GetProperty("Reopen", Hidden)!.GetValue(form), Is.Null, "a fresh window does not reopen by itself");
    }

    // ---- review fixes: guards before CREATE / RESET, writers, seed, move order ---------------------------------

    [Test]
    public void CreateAndResetWaitWhileTodaysCamlannSaveStillSitsInTheRvrFile()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        Assert.That(LayoutRefusal(WorldSaveKind.Realm, _rvr.Database), Does.Contain("still sits in the RvR save file"));
        Assert.That(LayoutRefusal(WorldSaveKind.Camlann, _camlann.Database), Does.Contain("still sits in the RvR save file"));

        using var form = (Form)Activator.CreateInstance(Main)!;
        Main.GetField("_root", Hidden)!.SetValue(form, _runtime);
        Main.GetField("_database", Hidden)!.SetValue(form, _rvr.Database);
        Main.GetField("_selectedWorldStatus", Hidden)!.SetValue(form, WorldPicker.Inspect(_runtime, WorldSaveKind.Realm));
        Main.GetMethod("ShowWorldCreationState", Hidden)!.Invoke(form, null);

        var start = (Button)Main.GetField("_startButton", Hidden)!.GetValue(form)!;
        var reset = (Button)Main.GetField("_resetWorldButton", Hidden)!.GetValue(form)!;
        Assert.That(start.Text, Is.EqualTo("START SERVER"), "no CREATE WORLD over the PvP world in the RvR file");
        Assert.That(start.Enabled || reset.Enabled, Is.False);
        Assert.That(((Label)Main.GetField("_footer", Hidden)!.GetValue(form)!).Text, Does.Contain("moves that world to its own save first"));
        Assert.That(File.Exists(_rvr.Database), Is.True);
    }

    [Test]
    public void CreateAndResetWaitWhileTheWindowShowsTheRunningServersSave()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.RealmMarker);
        Assert.That(LayoutRefusal(WorldSaveKind.Camlann, _rvr.Database), Does.Contain("reopens on the PvP world (Camlann) first"));
        Assert.That(LayoutRefusal(WorldSaveKind.Realm, _rvr.Database), Is.Null);
        Assert.That(LayoutRefusal(WorldSaveKind.Camlann, _camlann.Database), Is.Null, "a missing own file may be created");
    }

    [Test]
    public void AFailedMoveLeavesTheWindowNeedingAttentionWithoutCreateWorld()
    {
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        using var form = (Form)Activator.CreateInstance(Main)!;
        Main.GetField("_root", Hidden)!.SetValue(form, _runtime);
        Main.GetField("_worldAttention", Hidden)!.SetValue(form, "NEEDS ATTENTION: test");
        Main.GetField("_selectedWorldStatus", Hidden)!.SetValue(form, WorldPicker.Inspect(_runtime, WorldSaveKind.Realm));
        Main.GetMethod("ShowWorldCreationState", Hidden)!.Invoke(form, null);

        var start = (Button)Main.GetField("_startButton", Hidden)!.GetValue(form)!;
        Assert.That(start.Text, Is.EqualTo("START SERVER"));
        Assert.That(start.Enabled, Is.False);
        Assert.That((bool)Main.GetProperty("StartCreatesWorld", Hidden)!.GetValue(form)!, Is.True, "a click still goes to the guarded create path");
        Assert.That(((Label)Main.GetField("_footer", Hidden)!.GetValue(form)!).Text, Is.EqualTo("NEEDS ATTENTION: test"));
        Assert.That(((Label)Main.GetField("_worldStatusLabel", Hidden)!.GetValue(form)!).Text, Does.EndWith("NEEDS ATTENTION"));
        string panel = File.ReadAllText(Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "OfflineDaoc.Launcher", "WorldPickerPanel.cs")));
        string relocation = Regex.Match(panel, @"catch \(Exception exception\) when \(exception is InvalidOperationException or IOException or UnauthorizedAccessException or System\.Data\.SQLite\.SQLiteException\)(.*?)finally", RegexOptions.Singleline).Groups[1].Value;
        Assert.That(relocation, Does.Contain("_worldAttention =").And.Contain("return false;"), "a failed move never falls through to CREATE WORLD");
    }

    [Test]
    public void TheOtherWorldPromptSaysThatWorldMovesToTheBackups()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        string prompt = WorldPicker.CreationPrompt(WorldPicker.Inspect(_runtime, WorldSaveKind.Realm), _rvr.Database, WorldFiles.DefaultBackupRoot(_runtime));
        Assert.That(prompt, Does.Contain("This file holds a PvP world (Camlann): that world moves to the backups with the file."));
        Assert.That(prompt, Does.Contain("own save, opendaoc-camlann.sqlite3.db, is not changed"));
        Assert.That(prompt, Does.Not.Contain("The PvP world (Camlann) is not changed."));
    }

    [Test]
    public void TheCreateWorldStateLocksEverySettingWriterAndGmCreatesNoFile()
    {
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        using var form = (Form)Activator.CreateInstance(Main)!;
        Main.GetField("_root", Hidden)!.SetValue(form, _runtime);
        Main.GetField("_database", Hidden)!.SetValue(form, _rvr.Database);
        Main.GetField("_selectedWorldStatus", Hidden)!.SetValue(form, WorldPicker.Inspect(_runtime, WorldSaveKind.Realm));
        Main.GetMethod("ShowWorldCreationState", Hidden)!.Invoke(form, null);

        foreach (string name in new[] { "_makeMeGm", "_playerXpRate", "_botXpRate", "_playerRpRate", "_botRpRate", "_realmRankNameplates", "_botRealmRankNameplates", "_toaAtlantisOpen", "_toaLoot", "_toaDataPacks" })
            Assert.That(((Control)Main.GetField(name, Hidden)!.GetValue(form)!).Enabled, Is.False, name);
        Assert.That(((Control)Main.GetField("_openBgSettings", Hidden)!.GetValue(form)!).Enabled, Is.False, "open BG tab");
        Assert.That(((Control)Main.GetField("_botGoalsSettings", Hidden)!.GetValue(form)!).Enabled, Is.False, "population tab");
        var generate = (System.Collections.IEnumerable)Main.GetField("_realmGenerateButtons", Hidden)!.GetValue(form)!;
        Assert.That(generate.Cast<Button>().Any(button => button.Enabled), Is.False, "ADD CREW");

        // Even a direct GM write (as the checkbox handler does) must not create the missing save.
        Main.GetField("_persistXpRateServerRunningOverride", Hidden)!.SetValue(form, (bool?)false);
        MethodInfo persist = Main.GetMethods(Hidden).Single(method => method.Name == "PersistGmSetting" && method.GetParameters().Length == 1);
        Assert.That(() => persist.Invoke(form, [true]), Throws.InnerException.TypeOf<InvalidOperationException>());
        Assert.That(File.Exists(_rvr.Database), Is.False);
    }

    [Test]
    public void AFileWithoutWorldDataIsNeverASeed()
    {
        using (var connection = new SQLiteConnection($"Data Source={_rvr.Database};Version=3;Pooling=False;"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Account(Name TEXT PRIMARY KEY)";
            command.ExecuteNonQuery();
        }
        File.WriteAllBytes(_camlann.Database, []);
        Assert.That(WorldReset.IsSeedable(_rvr.Database), Is.False);
        Assert.That(WorldReset.DefaultSeed(_runtime, WorldSaveKind.Realm), Is.Null, "an empty or schema-only file is skipped");

        File.Delete(_camlann.Database);
        CreateSave(_camlann.Database, WorldSaveLayout.CamlannMarker);
        Assert.That(WorldReset.DefaultSeed(_runtime, WorldSaveKind.Realm), Is.EqualTo(_camlann.Database), "the other world's real save seeds instead");
    }

    [Test]
    public void TheMoveRenamesTheSidecarsBeforeTheSave()
    {
        CreateSave(_rvr.Database, WorldSaveLayout.CamlannMarker);
        File.WriteAllBytes(_rvr.Database + "-wal", []);
        File.WriteAllBytes(_rvr.Database + "-journal", []);
        File.WriteAllText(_rvr.EventRecords, "records");
        var order = new List<string>();
        WorldPicker.RelocateLegacyCamlann(_runtime, () => true, (from, to) => { order.Add(Path.GetFileName(from)); File.Move(from, to); });

        int save = order.IndexOf(WorldSaveLayout.RealmDatabaseFileName);
        Assert.That(order.IndexOf(WorldSaveLayout.RealmDatabaseFileName + "-wal"), Is.LessThan(save));
        Assert.That(order.IndexOf(WorldSaveLayout.RealmDatabaseFileName + "-journal"), Is.LessThan(save));
        Assert.That(order.IndexOf(Path.GetFileName(_rvr.EventRecords)), Is.GreaterThan(save));
    }

    private string? LayoutRefusal(WorldSaveKind world, string database) =>
        (string?)Main.GetMethod("WorldLayoutRefusal", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [_runtime, world, database]);

    // ---- helpers -----------------------------------------------------------------------------------------------

    private string? PrepareWorldForStart(WorldSaveKind world, string database) =>
        (string?)Main.GetMethod("PrepareWorldForStart", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [_runtime, ConfigPath, world, database]);

    private static string MainFormFooter(WorldSaveStatus status, bool hasSeed) =>
        (string)Main.GetMethod("WorldCreationFooter", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [status, hasSeed])!;

    private static Form OpenFor(WorldSaveKind world, Rectangle? bounds = null, int tab = 0)
    {
        Type reopenType = Launcher.GetType("OfflineDaoc.Launcher.LauncherReopen")!;
        object reopen = Activator.CreateInstance(reopenType, world, bounds ?? Rectangle.Empty, FormWindowState.Normal, tab)!;
        return (Form)Main.GetConstructor(Hidden, [reopenType])!.Invoke([reopen]);
    }

    private static bool ServerListening() =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == 10300);

    private void WriteConfig(string gameType, string connection) =>
        File.WriteAllText(ConfigPath, $"<?xml version=\"1.0\"?>\r\n<ServerConfig>\r\n  <GameType>{gameType}</GameType>\r\n  <DBType>SQLITE</DBType>\r\n  <DBConnectionString>{connection}</DBConnectionString>\r\n</ServerConfig>\r\n");

    private static void CreateSave(string database, string? marker)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;");
        connection.Open();
        using var command = connection.CreateCommand();
        // ServerProperty marks a save with world data, the only kind WorldReset takes as a seed.
        command.CommandText = "CREATE TABLE ServerProperty(`Key` TEXT PRIMARY KEY, Value TEXT); " + (marker is null
            ? "CREATE TABLE Account(Name TEXT PRIMARY KEY)"
            : "CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO offline_local_options VALUES ('WorldModel', @marker)");
        command.Parameters.AddWithValue("@marker", marker ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}
