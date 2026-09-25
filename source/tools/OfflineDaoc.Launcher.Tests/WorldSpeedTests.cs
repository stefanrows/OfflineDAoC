#nullable enable
using System.Data.SQLite;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class WorldSpeedTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private static readonly BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void FreshStatusCarriesServerAcknowledgedSpeedAndClock()
    {
        string folder = Path.Combine(Path.GetTempPath(), "offline-world-speed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DateTime now = DateTime.UtcNow;
            string path = Path.Combine(folder, "world-speed.status.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SessionId = "session-1",
                UpdatedUtc = now,
                SimulatedUtc = now.AddHours(4),
                SelectedMultiplier = 3,
                EffectiveMultiplier = 1,
                AchievedMultiplier = 0.98,
                ConnectedClients = 1,
                TickP95Ms = 8.2,
                Error = (string?)null,
            }));

            object?[] arguments = { path, now, null };
            object? status = ProtocolType.GetMethod("ReadFreshStatus", HiddenStatic)!.Invoke(null, arguments);
            Assert.That(status, Is.Not.Null);
            Assert.That(Property(status!, "SelectedMultiplier"), Is.EqualTo(3));
            Assert.That(Property(status!, "EffectiveMultiplier"), Is.EqualTo(1));
            Assert.That(Property(status!, "ConnectedClients"), Is.EqualTo(1));
            Assert.That(Property(status!, "SessionId"), Is.EqualTo("session-1"));
            Assert.That(arguments[2], Is.Null);

            arguments = [path, now.AddSeconds(6), null];
            Assert.That(ProtocolType.GetMethod("ReadFreshStatus", HiddenStatic)!.Invoke(null, arguments), Is.Null,
                "A status older than the five-second heartbeat window is unavailable.");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void MissingOrInvalidSimulationClockNeverHidesAClockError()
    {
        using var database = new SQLiteConnection("Data Source=:memory:;Version=3;New=True;");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)";
        command.ExecuteNonQuery();

        object clock = ClockType.GetMethod("ReadCheckpoint", HiddenStatic)!.Invoke(null, [database])!;
        Assert.That(ClockType.GetMethod("AdvanceStoppedClock", HiddenStatic)!.Invoke(null, [clock, DateTime.UtcNow]),
            Is.TypeOf<DateTime>(), "A legacy save without a checkpoint uses real UTC.");

        command.CommandText = "INSERT INTO offline_local_options VALUES ('WorldSimulationClock','{broken')";
        command.ExecuteNonQuery();
        clock = ClockType.GetMethod("ReadCheckpoint", HiddenStatic)!.Invoke(null, [database])!;
        Assert.That(ClockType.GetMethod("AdvanceStoppedClock", HiddenStatic)!.Invoke(null, [clock, DateTime.UtcNow]), Is.Null,
            "A present but malformed checkpoint makes countdown time unavailable instead of falling back to wall UTC.");
        Assert.That(Property(clock, "Error"), Is.Not.Null);
    }

    [Test]
    public void StoppedClockAdvancesFromTheSavedSimulationAndWallPair()
    {
        using var database = new SQLiteConnection("Data Source=:memory:;Version=3;New=True;");
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "CREATE TABLE offline_local_options (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        DateTime wall = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
        DateTime simulation = new(2026, 9, 26, 13, 0, 0, DateTimeKind.Utc);
        command.CommandText = "INSERT INTO offline_local_options VALUES ('WorldSimulationClock',@value)";
        command.Parameters.AddWithValue("@value", JsonSerializer.Serialize(new { Version = 1, SimulationUtc = simulation, WallUtc = wall }));
        command.ExecuteNonQuery();

        object clock = ClockType.GetMethod("ReadCheckpoint", HiddenStatic)!.Invoke(null, [database])!;
        object? advanced = ClockType.GetMethod("AdvanceStoppedClock", HiddenStatic)!
            .Invoke(null, [clock, wall.AddHours(7)]);
        Assert.That((DateTime)advanced!, Is.EqualTo(simulation.AddHours(7)));
    }

    [Test]
    public void RequestWriteIsAtomicAndIncludesCurrentSessionFreshUtcAndChosenMultiplier()
    {
        string folder = Path.Combine(Path.GetTempPath(), "offline-world-speed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "world-speed.request.json");
            DateTime created = DateTime.UtcNow;
            ProtocolType.GetMethod("WriteRequest", HiddenStatic)!.Invoke(null,
                [path, "current-session", 2, created]);

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement request = json.RootElement;
            Assert.That(request.GetProperty("SessionId").GetString(), Is.EqualTo("current-session"));
            Assert.That(request.GetProperty("RequestId").GetString(), Is.Not.Empty);
            Assert.That(request.GetProperty("Multiplier").GetInt32(), Is.EqualTo(2));
            Assert.That(request.GetProperty("CreatedUtc").GetDateTime().ToUniversalTime(), Is.EqualTo(created));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void CountdownFormattingUsesOnlyTheSuppliedSimulationClock()
    {
        Type display = Launcher.GetType("OfflineDaoc.Launcher.TaskTimerDisplay")!;
        MethodInfo remaining = display.GetMethod("Remaining", HiddenStatic | BindingFlags.Public)!;
        DateTime deadline = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(remaining.Invoke(null, [deadline.ToString("O"), null]), Is.Null,
            "A stale live status cannot silently substitute wall UTC.");
        Assert.That(remaining.Invoke(null, [deadline.ToString("O"), deadline.AddMinutes(-4)]), Is.EqualTo(240_000L));

        MethodInfo auction = display.GetMethod("FormatAuctionExpiry", HiddenStatic | BindingFlags.Public)!;
        Assert.That(auction.Invoke(null, [deadline, null]), Is.EqualTo("Unavailable"));
        Assert.That(auction.Invoke(null, [deadline, deadline.AddMinutes(-10)]), Does.Contain("10m left"));
    }

    [Test]
    public void LauncherShowsWorldSpeedSeparatelyAndDisablesItWithoutLiveStatus()
    {
        Type formType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using var form = (Form)Activator.CreateInstance(formType)!;
        var tabs = Descendants(form).OfType<TabControl>().SelectMany(tab => tab.TabPages.Cast<TabPage>());
        Assert.That(tabs.Select(page => page.Text), Does.Contain("World Speed"));
        var selector = (ComboBox)formType.GetField("_worldSpeedMultiplier", HiddenInstance)!.GetValue(form)!;
        Assert.That(selector.Items.Cast<object>().Select(item => item.ToString()), Is.EqualTo(new[] { "1×", "2×", "3×" }));
        Assert.That(selector.Enabled, Is.False);
    }

    [Test]
    public void PendingChoiceDoesNotReplaceTheSpeedReportedByTheServer()
    {
        string folder = Path.Combine(Path.GetTempPath(), "offline-world-speed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            DateTime now = DateTime.UtcNow;
            string path = Path.Combine(folder, "status.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SessionId = "session-1", UpdatedUtc = now, SimulatedUtc = now,
                SelectedMultiplier = 1, EffectiveMultiplier = 1, AchievedMultiplier = 1d,
                ConnectedClients = 0, TickP95Ms = 8d, Error = (string?)null,
            }));
            object?[] arguments = [path, now, null];
            object status = ProtocolType.GetMethod("ReadFreshStatus", HiddenStatic)!.Invoke(null, arguments)!;

            Type formType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
            using var form = (Form)Activator.CreateInstance(formType)!;
            formType.GetField("_pendingWorldSpeedMultiplier", HiddenInstance)!.SetValue(form, 3);
            formType.GetMethod("UpdateWorldSpeedPresentation", HiddenInstance)!.Invoke(form, [true, status]);

            var statusLabel = (Label)formType.GetField("_worldSpeedStatusText", HiddenInstance)!.GetValue(form)!;
            var selector = (ComboBox)formType.GetField("_worldSpeedMultiplier", HiddenInstance)!.GetValue(form)!;
            Assert.That(statusLabel.Text, Does.Contain("Selected 1×"));
            Assert.That(statusLabel.Text, Does.Contain("requested 3×"));
            Assert.That(selector.SelectedItem?.ToString(), Is.EqualTo("3×"), "The selector communicates the pending request.");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static Type ProtocolType => Launcher.GetType("OfflineDaoc.Launcher.WorldSpeedProtocol")!;
    private static Type ClockType => Launcher.GetType("OfflineDaoc.Launcher.WorldSimulationClock")!;

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance);

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
