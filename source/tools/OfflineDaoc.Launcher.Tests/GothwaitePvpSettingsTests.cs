using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Configuration;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class GothwaitePvpSettingsTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Explanation = "All autonomous bots PvP around Caer Gothwaite. They do not level, raid or siege. Their positions are restored when you switch back to Normal. Takes effect at the next server start.";
    private string _folder;
    private Control _panel;
    private bool _stopped;
    private string SettingsPath => Path.Combine(_folder, GothwaitePvpSettings.FileName);
    private T Field<T>(string name) => (T)_panel.GetType().GetField(name, Hidden)!.GetValue(_panel)!;
    private void Call(string name) => _panel.GetType().GetMethod(name, Hidden | BindingFlags.Public)!.Invoke(_panel, null);
    private bool HasUnsavedChanges => (bool)_panel.GetType().GetProperty("HasUnsavedChanges")!.GetValue(_panel)!;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "gothwaite-settings-" + Guid.NewGuid().ToString("N"));
        _stopped = true;
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.BotGoalsSettingsControl")!;
        _panel = (Control)Activator.CreateInstance(type, Path.Combine(_folder, BotGoalSettings.FileName),
            Path.Combine(_folder, "world-speed.status.json"), Path.Combine(_folder, "population-mix.request.json"),
            (Func<bool>)(() => _stopped), (Func<int>)(() => 0))!;
    }

    [TearDown] public void Cleanup()
    {
        _panel.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Test] public void LauncherCompilesTheSharedSettingsClass()
    {
        Assert.That(typeof(GothwaitePvpSettings).Assembly.GetName().Name, Is.EqualTo("OfflineDAoC"));
        Assert.That(GothwaitePvpSettings.FileName, Is.EqualTo("gothwaite-pvp.json"));
        Assert.That(GothwaitePvpSettings.Defaults.Enabled, Is.False);
    }

    [Test] public void MissingFileLoadsNormalWithoutCreatingAFile()
    {
        Assert.That(GothwaitePvpSettings.Load(SettingsPath).Enabled, Is.False);
        Assert.That(File.Exists(SettingsPath), Is.False);
    }

    [Test] public void SavingEnabledRoundTripsAndKeepsOtherFields()
    {
        Directory.CreateDirectory(_folder);
        GothwaitePvpSettings tuned = GothwaitePvpSettings.Defaults with
        {
            SafeRadius = 5000, LevelBand = 7, RestPercent = 60, GroupShares = new(10, 20, 30, 40),
        };
        tuned.Save(SettingsPath, () => true);
        GothwaitePvpSettings loaded = GothwaitePvpSettings.Load(SettingsPath);
        Assert.That(loaded.Enabled, Is.False);

        (loaded with { Enabled = true }).Save(SettingsPath, () => true);

        GothwaitePvpSettings reread = GothwaitePvpSettings.Load(SettingsPath);
        Assert.That(reread.Enabled, Is.True);
        Assert.That(reread.SafeRadius, Is.EqualTo(5000));
        Assert.That(reread.LevelBand, Is.EqualTo(7));
        Assert.That(reread.RestPercent, Is.EqualTo(60));
        Assert.That(reread.GroupShares, Is.EqualTo(new GothwaitePvpGroupShares(10, 20, 30, 40)));
    }

    [Test] public void SaveRefusesWhileTheServerRunsAndLeavesNoFile()
    {
        Directory.CreateDirectory(_folder);
        Assert.That(() => (GothwaitePvpSettings.Defaults with { Enabled = true }).Save(SettingsPath, () => false),
            Throws.InvalidOperationException.With.Message.Contains("Stop the server"));
        Assert.That(File.Exists(SettingsPath), Is.False);
    }

    [Test] public void ServerStartingDuringTheWriteAbortsAndRemovesTheTemporaryFile()
    {
        Directory.CreateDirectory(_folder);
        int checks = 0;
        Assert.That(() => (GothwaitePvpSettings.Defaults with { Enabled = true }).Save(SettingsPath, () => ++checks == 1),
            Throws.InvalidOperationException.With.Message.Contains("server started"));
        Assert.That(File.Exists(SettingsPath), Is.False);
        Assert.That(Directory.GetFiles(_folder, "*.tmp"), Is.Empty);
    }

    [Test] public void WorldModeOffersNormalAndGothwaiteAndStartsOnNormal()
    {
        ComboBox mode = Field<ComboBox>("_worldMode");
        Assert.That(mode.Items.Cast<object>().Select(item => item.ToString()).ToArray(), Is.EqualTo(new[] { "Normal", "Gothwaite PvP" }));
        Assert.That(mode.SelectedIndex, Is.EqualTo(0));
        Assert.That(Field<Label>("_worldModeHint").Visible, Is.False);
        Assert.That(HasUnsavedChanges, Is.False);
    }

    [Test] public void EnabledFileSelectsGothwaiteAndShowsTheExplanation()
    {
        Directory.CreateDirectory(_folder);
        (GothwaitePvpSettings.Defaults with { Enabled = true }).Save(SettingsPath, () => true);
        Call("LoadSettings");
        Assert.That(Field<ComboBox>("_worldMode").SelectedIndex, Is.EqualTo(1));
        Assert.That(Field<Label>("_worldModeHint").Visible, Is.True);
        Assert.That(Field<Label>("_worldModeHint").Text, Is.EqualTo(Explanation));
        Assert.That(HasUnsavedChanges, Is.False);
    }

    [Test] public void SwitchingOnWithoutABackupRoutineIsRefusedAndWritesNothing()
    {
        Field<ComboBox>("_worldMode").SelectedIndex = 1;
        Field<NumericUpDown>("_altHours").Value = 48;
        Call("SaveSettings");
        Assert.That(Field<Label>("_status").Text, Does.StartWith("Not saved: ").And.Contains("backup"));
        Assert.That(File.Exists(SettingsPath), Is.False);
        Assert.That(File.Exists(Path.Combine(_folder, BotGoalSettings.FileName)), Is.False);
        Assert.That(HasUnsavedChanges, Is.True);
    }

    [Test] public void SwitchingOnWhileTheServerRunsAsksToStopFirst()
    {
        _stopped = false;
        Field<ComboBox>("_worldMode").SelectedIndex = 1;
        Call("SaveSettings");
        Assert.That(Field<string>("_notice"), Does.StartWith("Not saved: ").And.Contains("Stop the server"));
        Assert.That(File.Exists(SettingsPath), Is.False);
    }

    [Test] public void SavingNormalWithoutAFileWritesNoWorldModeFile()
    {
        Call("SaveSettings");
        Assert.That(File.Exists(Path.Combine(_folder, BotGoalSettings.FileName)), Is.True);
        Assert.That(File.Exists(SettingsPath), Is.False);
        Assert.That(Field<Label>("_status").Text, Is.EqualTo("Saved. Population settings apply on the next server start."));
    }

    [Test] public void SwitchingBackToNormalKeepsOtherFieldsAndNeedsNoBackup()
    {
        Directory.CreateDirectory(_folder);
        (GothwaitePvpSettings.Defaults with { Enabled = true, SafeRadius = 5000 }).Save(SettingsPath, () => true);
        Call("LoadSettings");
        Field<ComboBox>("_worldMode").SelectedIndex = 0;
        Assert.That(HasUnsavedChanges, Is.True);
        Call("SaveSettings");
        GothwaitePvpSettings reread = GothwaitePvpSettings.Load(SettingsPath);
        Assert.That(reread.Enabled, Is.False);
        Assert.That(reread.SafeRadius, Is.EqualTo(5000));
        Assert.That(Field<Label>("_status").Text, Does.StartWith("Saved."));
        Assert.That(HasUnsavedChanges, Is.False);
    }

    [Test] public void UnreadableFileShowsNormalBlocksStartUntilSaveRepairsIt()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ not json");
        Call("LoadSettings");
        Assert.That(Field<ComboBox>("_worldMode").SelectedIndex, Is.EqualTo(0));
        Assert.That(Field<Label>("_status").Text, Does.StartWith("Cannot read saved world mode settings"));
        Assert.That(HasUnsavedChanges, Is.True);
        Call("SaveSettings");
        Assert.That(GothwaitePvpSettings.Load(SettingsPath).Enabled, Is.False);
        Assert.That(HasUnsavedChanges, Is.False);
    }

    [Test] public void UndoRevertsAnUnsavedWorldModeChange()
    {
        Field<ComboBox>("_worldMode").SelectedIndex = 1;
        Assert.That(HasUnsavedChanges, Is.True);
        Call("LoadSettings");
        Assert.That(Field<ComboBox>("_worldMode").SelectedIndex, Is.EqualTo(0));
        Assert.That(HasUnsavedChanges, Is.False);
    }

    [Test] public void WorldModeIsEditableOnlyWhileTheServerIsStopped()
    {
        ComboBox mode = Field<ComboBox>("_worldMode");
        _stopped = false;
        Call("UpdateState");
        Assert.That(mode.Enabled, Is.False);
        _stopped = true;
        Call("UpdateState");
        Assert.That(mode.Enabled, Is.True);
    }

    [Test] public void WorldModeRowSitsAboveThePresetAndShowsTheExplanation()
    {
        Field<ComboBox>("_worldMode").SelectedIndex = 1;
        using var host = new Form { ClientSize = new Size(960, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000), ShowInTaskbar = false };
        host.Controls.Add(_panel);
        host.Show();
        Application.DoEvents();
        Control worldModeRow = Field<ComboBox>("_worldMode").Parent!;
        Control presetRow = Field<ComboBox>("_preset").Parent!;
        Label hint = Field<Label>("_worldModeHint");
        Assert.That(worldModeRow.Top, Is.LessThan(hint.Top));
        Assert.That(hint.Top, Is.LessThan(presetRow.Top));
        Assert.That(hint.Visible, Is.True);
        using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
        _panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.ClientSize));
        bitmap.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory, "gothwaite-world-mode-preview.png"));
        host.Controls.Remove(_panel);
    }

    [Test] public void SwitchingOnRunsTheBackupFirstThenWritesTheSwitch()
    {
        _panel.Dispose();
        int backups = 0;
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.BotGoalsSettingsControl")!;
        _panel = (Control)Activator.CreateInstance(type, Path.Combine(_folder, BotGoalSettings.FileName),
            Path.Combine(_folder, "world-speed.status.json"), Path.Combine(_folder, "population-mix.request.json"),
            (Func<bool>)(() => _stopped), (Func<int>)(() => 0),
            (Func<string>)(() => { backups++; Assert.That(File.Exists(SettingsPath), Is.False); return "backup-folder"; }))!;
        Field<ComboBox>("_worldMode").SelectedIndex = 1;
        Call("SaveSettings");
        Assert.That(backups, Is.EqualTo(1));
        Assert.That(GothwaitePvpSettings.Load(SettingsPath).Enabled, Is.True);
        Assert.That(Field<string>("_notice"), Does.Contain("backup-folder"));
        Assert.That(HasUnsavedChanges, Is.False);

        Call("SaveSettings");
        Assert.That(backups, Is.EqualTo(1), "only the switch from Normal into the mode backs up");
    }

    [Test] public void SaveDatabaseBackupCopiesTheStoppedDatabase()
    {
        Directory.CreateDirectory(_folder);
        string database = Path.Combine(_folder, "opendaoc.sqlite3.db");
        File.WriteAllText(database, "save");
        File.WriteAllText(database + "-wal", string.Empty);
        var backup = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.SaveDatabaseBackup")!.GetMethod("Create")!;
        string folder = (string)backup.Invoke(null, new object[] { database, Path.Combine(_folder, "backups"), "gothwaite-pvp" })!;
        Assert.That(Path.GetFileName(folder), Does.StartWith("gothwaite-pvp-"));
        Assert.That(File.ReadAllText(Path.Combine(folder, "opendaoc.sqlite3.db")), Is.EqualTo("save"));
    }

    [Test] public void SaveDatabaseBackupRefusesAnUncheckpointedWal()
    {
        Directory.CreateDirectory(_folder);
        string database = Path.Combine(_folder, "opendaoc.sqlite3.db");
        File.WriteAllText(database, "save");
        File.WriteAllText(database + "-wal", "pending pages");
        var backup = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.SaveDatabaseBackup")!.GetMethod("Create")!;
        Assert.That(() => backup.Invoke(null, new object[] { database, Path.Combine(_folder, "backups"), "gothwaite-pvp" }),
            Throws.TargetInvocationException.With.InnerException.TypeOf<InvalidOperationException>());
        Assert.That(Directory.Exists(Path.Combine(_folder, "backups")), Is.False);
    }
}
