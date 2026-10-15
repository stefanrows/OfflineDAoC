using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;

// Shared look (theme helpers, grids, tabs, buttons, icon) and the layout rules that keep it on screen.
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class LauncherThemeTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private static readonly Type Theme = Launcher.GetType("OfflineDaoc.Launcher.DaocTheme")!;
    private static readonly Type Main = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
    private static readonly Type RuneButtonType = Launcher.GetType("OfflineDaoc.Launcher.RuneButton")!;
    private static readonly Type TabType = Launcher.GetType("OfflineDaoc.Launcher.DaocTabControl")!;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly string[] TabTitles =
    [
        "Active Population", "Active Groups", "Realm Events", "Realm Records", "XP Settings",
        "World Speed", "Server population", "open BG", "Realm Exchange",
    ];

    [TestCase("Albion", 225, 116, 105)]
    [TestCase("Midgard", 124, 161, 215)]
    [TestCase("Hibernia", 112, 178, 112)]
    [TestCase("Unknown", 255, 255, 255)]
    [TestCase(null, 255, 255, 255)]
    public void RealmNamesUseTheExistingListColours(string? realm, int red, int green, int blue)
    {
        var method = Theme.GetMethod("RealmColor", HiddenStatic, null, [typeof(string)], null)!;
        Assert.That(((Color)method.Invoke(null, [realm])!).ToArgb(), Is.EqualTo(Color.FromArgb(red, green, blue).ToArgb()));
    }

    [TestCase(1, 225, 116, 105)]
    [TestCase(2, 124, 161, 215)]
    [TestCase(3, 112, 178, 112)]
    [TestCase(0, 255, 255, 255)]
    [TestCase(4, 255, 255, 255)]
    public void RealmIdsUseTheSameColoursAsRealmNames(int realm, int red, int green, int blue)
    {
        var method = Theme.GetMethod("RealmColor", HiddenStatic, null, [typeof(int)], null)!;
        Assert.That(((Color)method.Invoke(null, [realm])!).ToArgb(), Is.EqualTo(Color.FromArgb(red, green, blue).ToArgb()));
    }

    [Test]
    public void StoneGridLooksTheSameAcrossTheLauncher()
    {
        using var grid = new DataGridView();
        StyleGrid(grid, "Stone");
        Assert.Multiple(() =>
        {
            Assert.That(grid.RowHeadersVisible, Is.False);
            Assert.That(grid.EnableHeadersVisualStyles, Is.False);
            Assert.That(grid.BackgroundColor, Is.EqualTo(DaocTheme("StoneDark")));
            Assert.That(grid.DefaultCellStyle.BackColor, Is.EqualTo(DaocTheme("Panel")).Or.EqualTo(Color.FromArgb(35, 31, 26)));
            Assert.That(grid.DefaultCellStyle.SelectionForeColor, Is.EqualTo(DaocTheme("GoldLight")));
            Assert.That(grid.AlternatingRowsDefaultCellStyle.BackColor, Is.EqualTo(Color.FromArgb(42, 37, 30)));
            Assert.That(grid.ColumnHeadersDefaultCellStyle.ForeColor, Is.EqualTo(DaocTheme("GoldLight")));
            Assert.That(grid.ColumnHeadersDefaultCellStyle.Font.Bold, Is.True);
            Assert.That(grid.CellBorderStyle, Is.EqualTo(DataGridViewCellBorderStyle.SingleHorizontal));
        });
    }

    [Test]
    public void ParchmentGridKeepsTheRealmExchangePalette()
    {
        using var grid = new DataGridView();
        StyleGrid(grid, "Parchment");
        Assert.Multiple(() =>
        {
            Assert.That(grid.BackgroundColor, Is.EqualTo(Color.FromArgb(205, 187, 144)));
            Assert.That(grid.DefaultCellStyle.BackColor, Is.EqualTo(Color.FromArgb(215, 199, 157)));
            Assert.That(grid.DefaultCellStyle.ForeColor, Is.EqualTo(DaocTheme("Ink")));
            Assert.That(grid.DefaultCellStyle.SelectionBackColor, Is.EqualTo(Color.FromArgb(117, 85, 45)));
            Assert.That(grid.DefaultCellStyle.SelectionForeColor, Is.EqualTo(Color.White));
            Assert.That(grid.RowHeadersVisible, Is.False);
        });
    }

    [TestCase(960)]
    [TestCase(1100)]
    public void AllNineTabsStayOnOneRowAtTheMinimumAndDefaultWidths(int windowWidth)
    {
        // The strip gets the window width minus borders (16), stone frame (28) and root padding (10).
        int stripWidth = windowWidth - 54;
        using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20_000, -20_000),
            ShowInTaskbar = false, ClientSize = new Size(stripWidth, 400) };
        var tabs = (TabControl)Activator.CreateInstance(TabType, true)!;
        tabs.Dock = DockStyle.Fill;
        foreach (string title in TabTitles)
            tabs.TabPages.Add(new TabPage(title));
        host.Controls.Add(tabs);
        ShowHost(host);
        Rectangle last = tabs.GetTabRect(tabs.TabCount - 1);
        Assert.That(last.Right, Is.LessThanOrEqualTo(tabs.Width - 2), $"last tab ends at {last.Right} of {tabs.Width}");
        Assert.That(tabs.Font.Size, Is.LessThanOrEqualTo(8.5f));
        for (int index = 0; index < tabs.TabCount; index++)
            Assert.That(tabs.GetTabRect(index).Width, Is.GreaterThan(TextRenderer.MeasureText(TabTitles[index], tabs.Font).Width),
                $"'{TabTitles[index]}' must fit its own tab");
    }

    [TestCase(960)]
    [TestCase(1100)]
    public void TheRealLauncherWindowKeepsAllNineTabsOnOneRow(int windowWidth)
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        ShowOffscreen(form, new Size(windowWidth, 700));
        TabControl tabs = Descendants(form).OfType<TabControl>().Single(tab => tab.TabPages.Count == 9);
        Rectangle last = tabs.GetTabRect(tabs.TabCount - 1);
        Assert.That(last.Right, Is.LessThanOrEqualTo(tabs.Width - 2), $"last tab ends at {last.Right} of {tabs.Width}");
        Assert.That(tabs.TabPages.Cast<TabPage>().Select(page => page.Text), Is.EqualTo(TabTitles));
    }

    [Test]
    public void WindowIconIsBuiltAtRuntimeFromTheRealmShield()
    {
        using var icon = (Icon?)Theme.Assembly.GetType("OfflineDaoc.Launcher.LauncherIcon")!
            .GetMethod("Create", HiddenStatic)!.Invoke(null, null);
        Assert.That(icon, Is.Not.Null);
        Assert.That(icon!.Width, Is.GreaterThanOrEqualTo(16));
        Assert.That(icon.Height, Is.GreaterThanOrEqualTo(16));
        using var bitmap = icon.ToBitmap();
        Assert.That(bitmap.Width, Is.GreaterThan(0));
    }

    [Test]
    public void MainWindowAndExchangeSalesWindowCarryTheIcon()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        Assert.That(form.Icon, Is.Not.Null, "The launcher window must show the realm shield.");
        using var sales = (Form)Activator.CreateInstance(Launcher.GetType("OfflineDaoc.Launcher.ExchangeSalesForm")!, "missing.db")!;
        Assert.That(sales.Icon, Is.Not.Null);
    }

    [Test]
    public void EveryButtonInTheMainWindowIsStyledAsAnActionButton()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var plainButtons = Descendants(form).OfType<Button>().Where(button => !RuneButtonType.IsInstanceOfType(button)).ToList();
        Assert.That(plainButtons.Select(button => button.Text), Is.Empty, "Plain buttons found in the launcher.");
    }

    [Test]
    public void EveryGridInTheMainWindowUsesTheSharedStyle()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var grids = Descendants(form).OfType<DataGridView>().ToList();
        Assert.That(grids, Is.Not.Empty);
        Assert.That(grids.Where(grid => grid.RowHeadersVisible || grid.EnableHeadersVisualStyles).Count(), Is.EqualTo(0));
        Assert.That(grids.Where(grid => grid.BackgroundColor == SystemColors.Window).Count(), Is.EqualTo(0),
            "A grid without launcher styling uses the white system background.");
    }

    [Test]
    public void LabelsDoNotSwallowAmpersands()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var labels = Descendants(form).OfType<Label>().Where(label => label.Text.Contains('&')).ToList();
        Assert.That(labels, Is.Not.Empty);
        Assert.That(labels.Where(label => label.UseMnemonic).Select(label => label.Text), Is.Empty);
    }

    [Test]
    public void PopulationGridFitsTheDefaultWindowWithoutHorizontalScrolling()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        ShowOffscreen(form, new Size(1100, 700));
        var grid = (DataGridView)Main.GetField("_grid", Hidden)!.GetValue(form)!;
        // A horizontal scroll would push the last column past the grid's client edge.
        Rectangle last = grid.GetColumnDisplayRectangle(grid.Columns.Count - 1, false);
        Assert.That(last.Right, Is.LessThanOrEqualTo(grid.ClientSize.Width),
            "Population columns must fit the default width without a horizontal scroll.");
        Assert.That(grid.Columns.Cast<DataGridViewColumn>().Select(column => column.DataPropertyName),
            Is.SupersetOf(new[] { "Name", "Realm", "RaceName", "Gender", "ClassName", "PlayerType", "GuildName", "GuildCharter", "Level", "RealmPointsDisplay", "ZoneName", "TaskRemaining", "Activity", "State" }));
        Assert.That(grid.Columns.Cast<DataGridViewColumn>().All(column => column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill), Is.True);
    }

    [Test]
    public void PopulationColumnMinimumsFitTheMinimumWindowWidth()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var grid = (DataGridView)Main.GetField("_grid", Hidden)!.GetValue(form)!;
        int minimums = grid.Columns.Cast<DataGridViewColumn>().Sum(column => column.MinimumWidth);
        Assert.That(minimums, Is.LessThanOrEqualTo(960 - 60), "Column minimums alone must not force a scroll at 960 px.");
    }

    [Test]
    public void PopulationContextMenuUsesTheDarkRendererAndReadableItems()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        var grid = (DataGridView)Main.GetField("_grid", Hidden)!.GetValue(form)!;
        var menu = grid.ContextMenuStrip!;
        Assert.That(menu.Renderer, Is.InstanceOf<ToolStripProfessionalRenderer>());
        Assert.That(menu.Items.OfType<ToolStripMenuItem>().Select(item => item.ForeColor), Is.All.EqualTo(DaocTheme("Text")));
        Assert.That(menu.BackColor, Is.EqualTo(DaocTheme("StoneDark")));
    }

    [Test]
    public void ToolButtonsAreSizedToTheirLabelsAndKeepTheRuneStyle()
    {
        var button = (Button)Theme.GetMethod("ToolButton", HiddenStatic, null, [typeof(string)], null)!.Invoke(null, ["Save settings"])!;
        Assert.That(button, Is.InstanceOf(RuneButtonType));
        Assert.That(button.AutoSize, Is.True);
        Assert.That(button.Text, Is.EqualTo("Save settings"));
        button.Dispose();
    }

    [Test]
    public void RvrSnapshotsOlderThanAKeepResetAreHiddenAndNewerOnesShown()
    {
        string folder = Path.Combine(Path.GetTempPath(), "launcher-theme-rvr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string database = Path.Combine(folder, "world.db");
            using (var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE offline_local_options(Key TEXT PRIMARY KEY, Value TEXT);" +
                    "INSERT INTO offline_local_options VALUES(@key, '2026-09-10T00:00:00.0000000Z');";
                command.Parameters.AddWithValue("@key", OfflineDaoc.Launcher.KeepRelicReset.ResetKey);
                command.ExecuteNonQuery();
            }
            var read = Main.GetMethod("ReadRvrWorldSnapshot", HiddenStatic)!;

            Assert.That(read.Invoke(null, [folder, database]), Is.Null, "A missing snapshot file yields nothing.");

            File.WriteAllText(Path.Combine(folder, "rvr-world.json"), """{"UpdatedUtc":"2026-09-04T15:00:00Z","Running":true,"Objectives":[]}""");
            Assert.That(read.Invoke(null, [folder, database]), Is.Null, "A snapshot from before the reset is hidden.");

            File.WriteAllText(Path.Combine(folder, "rvr-world.json"), """{"UpdatedUtc":"2026-09-12T15:00:00Z","Running":true,"Objectives":[]}""");
            Assert.That(read.Invoke(null, [folder, database]), Is.Not.Null, "A snapshot after the reset is shown.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void StoppedServerReadingLoadsTheSavedClockOnlyWhenAsked()
    {
        Assume.That(!(bool)Main.GetMethod("IsServerRunning", HiddenStatic)!.Invoke(null, null)!,
            "A live server on port 10300 makes the stopped-state reading inconclusive.");
        string folder = Path.Combine(Path.GetTempPath(), "launcher-theme-clock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var read = Main.GetMethod("ReadWorldSpeedReading", HiddenStatic)!;
            DateTime now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
            string status = Path.Combine(folder, "world-speed.status.json");
            string database = Path.Combine(folder, "missing.db");
            object skipped = read.Invoke(null, [now, false, false, status, database])!;
            Assert.That(SavedClockOf(skipped), Is.Null);
            object loaded = read.Invoke(null, [now, false, true, status, database])!;
            Assert.That(SavedClockOf(loaded), Is.Not.Null, "The saved clock is read when the server has just stopped or never loaded.");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void ApplyingAStoppedReadingKeepsTheWorldSpeedPresentationAtOneX()
    {
        using var form = (Form)Activator.CreateInstance(Main)!;
        ShowOffscreen(form, new Size(1100, 700));
        Type readingType = Main.GetNestedType("WorldSpeedReading", BindingFlags.NonPublic)!;
        Type clockType = Launcher.GetType("OfflineDaoc.Launcher.WorldSimulationClockRead")!;
        object clock = Activator.CreateInstance(clockType, [null, false, null])!;
        object reading = Activator.CreateInstance(readingType, [false, null, null, clock])!;
        Main.GetMethod("ApplyWorldSpeedReading", Hidden)!.Invoke(form, [reading, DateTime.UtcNow]);
        Assert.That(Main.GetField("_stoppedSimulationClockLoaded", Hidden)!.GetValue(form), Is.EqualTo(true));
        var status = (Label)Main.GetField("_worldSpeedStatusText", Hidden)!.GetValue(form)!;
        Assert.That(status.Text, Does.Contain("Server stopped"));
    }

    private static void StyleGrid(DataGridView grid, string tone)
    {
        var toneType = Launcher.GetType("OfflineDaoc.Launcher.DaocGridTone")!;
        object value = Enum.Parse(toneType, tone);
        Theme.GetMethod("StyleGrid", HiddenStatic)!.Invoke(null, [grid, value]);
    }

    private static Color DaocTheme(string name) =>
        (Color)Theme.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;

    private static void ShowOffscreen(Form form, Size size)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20_000, -20_000);
        form.Size = size;
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
    }

    private static void ShowHost(Form host)
    {
        host.Show();
        Application.DoEvents();
        host.PerformLayout();
    }

    private static object? SavedClockOf(object reading) =>
        reading.GetType().GetProperty("SavedClock")!.GetValue(reading);

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
