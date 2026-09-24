using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Configuration;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class BotGoalsSettingsTests
{
    private string _folder;
    private Control _panel;
    private bool _stopped;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private T Field<T>(string name) => (T)_panel.GetType().GetField(name, Hidden)!.GetValue(_panel)!;
    private void Call(string name) => _panel.GetType().GetMethod(name, Hidden | BindingFlags.Public)!.Invoke(_panel, null);

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "population-ui-" + Guid.NewGuid().ToString("N"));
        _stopped = true;
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.BotGoalsSettingsControl")!;
        _panel = (Control)Activator.CreateInstance(type, Path.Combine(_folder, BotGoalSettings.FileName),
            (Func<bool>)(() => _stopped))!;
    }

    [TearDown] public void Cleanup()
    {
        _panel.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Test] public void MixTotalAndServerStateGateSaving()
    {
        NumericUpDown[] values = Field<NumericUpDown[]>("_values");
        TrackBar[] sliders = Field<TrackBar[]>("_sliders");
        values[0].Value = 20;
        Assert.That(Field<Button>("_save").Enabled, Is.False);
        values[3].Value = 20;
        Assert.That(Field<Button>("_save").Enabled, Is.True);
        Assert.That(Field<ComboBox>("_preset").SelectedIndex, Is.EqualTo((int)PopulationPreset.Custom));
        Assert.That(sliders[3].Value, Is.EqualTo(20));
        _stopped = false;
        Call("UpdateState");
        Assert.That(Field<Button>("_save").Enabled, Is.False);
        Assert.That(sliders.All(slider => !slider.Enabled), Is.True);
        Call("SaveSettings");
        Assert.That(File.Exists(Path.Combine(_folder, BotGoalSettings.FileName)), Is.False);
        _stopped = true;
        Call("SaveSettings");
        Assert.That(BotGoalSettings.Load(Path.Combine(_folder, BotGoalSettings.FileName)).Mix.Hunter, Is.EqualTo(20));
        Assert.That(Field<Label>("_status").Text, Does.StartWith("Saved."));
    }

    [Test] public void PresetAndWorldShapeSaveAndUndo()
    {
        Field<ComboBox>("_preset").SelectedIndex = (int)PopulationPreset.KeepWars;
        Field<ComboBox>("_worldShape").SelectedIndex = (int)PopulationWorldShape.Established;
        Field<NumericUpDown>("_altHours").Value = 48;
        Field<NumericUpDown>("_altCap").Value = 2500;
        Call("SaveSettings");
        BotGoalSettings saved = BotGoalSettings.Load(Path.Combine(_folder, BotGoalSettings.FileName));
        Assert.That(saved.WorldShape, Is.EqualTo(PopulationWorldShape.Established));
        Assert.That(saved.AltJoinIntervalHours, Is.EqualTo(48));
        Assert.That(saved.AltRosterCap, Is.EqualTo(2500));
        Field<NumericUpDown[]>("_values")[0].Value = 99;
        Call("LoadSettings");
        Assert.That(Field<NumericUpDown[]>("_values")[0].Value, Is.EqualTo(15));
        Assert.That(_panel.GetType().GetProperty("HasUnsavedChanges")!.GetValue(_panel), Is.False);
    }

    [Test] public void LegacyFileExplainsMappingBeforeRewriting()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, BotGoalSettings.FileName);
        string legacy = """
            {"Version":1,"Levels1To19":{"SoloPve":10,"GroupPve":90,"RvR":0},
             "Levels20To49":{"SoloPve":20,"GroupPve":70,"RvR":10},
             "Level50":{"SoloPve":20,"GroupPve":40,"RvR":40}}
            """;
        File.WriteAllText(path, legacy);
        Call("LoadSettings");
        Assert.That(Field<Label>("_status").Text, Does.Contain("mapped"));
        Assert.That(_panel.GetType().GetProperty("HasUnsavedChanges")!.GetValue(_panel), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo(legacy));
        Call("SaveSettings");
        Assert.That(BotGoalSettings.LoadDetailed(path).MigratedFromV1, Is.False);
    }

    [Test] public void MeasuredRecommendationAndPanelRenderWithoutLaunchingServer()
    {
        Assert.That(Field<Label>("_population").Text, Does.Contain("pending"));
        var benchmarks = new PopulationBenchmarks();
        int cores = Environment.ProcessorCount;
        long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        foreach (int tier in new[] { 500, 1000, 1500 })
            benchmarks.Record(new(tier, tier, cores, memory, 1024L * 1024 * tier,
                tier == 1500 ? 90 : 20, 100, DateTime.UtcNow));
        Directory.CreateDirectory(_folder);
        benchmarks.Save(Path.Combine(_folder, PopulationBenchmarks.FileName));
        Call("UpdateState");
        Assert.That(Field<Label>("_population").Text, Does.Contain(1000.ToString("N0")));
        using var host = new Form { ClientSize = new Size(960, 600), StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000), ShowInTaskbar = false };
        host.Controls.Add(_panel);
        host.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
        _panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.ClientSize));
        bitmap.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory, "server-population-preview.png"));
        Assert.That(Field<Button>("_save").Bounds.Width, Is.GreaterThan(60));
        host.Controls.Remove(_panel);
    }
}
