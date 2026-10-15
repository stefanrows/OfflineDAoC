using System.Data.SQLite;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class HeraldPresentationTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [Test]
    public void MainFormHeraldButtonOpensOneReusableWindow()
    {
        Type mainType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using var main = (Form)Activator.CreateInstance(mainType)!;
        main.ShowInTaskbar = false;
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-20_000, -20_000);
        main.Show();
        var button = (Button)mainType.GetField("_heraldButton", Hidden)!.GetValue(main)!;
        Assert.That(button.Text, Is.EqualTo("HERALD"));

        button.PerformClick();
        var first = (Form)mainType.GetField("_herald", Hidden)!.GetValue(main)!;
        Assert.That(first, Is.Not.Null);
        Assert.That(first.Text, Is.EqualTo("Camlann Herald"));

        button.PerformClick();
        Assert.That(mainType.GetField("_herald", Hidden)!.GetValue(main), Is.SameAs(first),
            "A second click must focus the open window, not open another.");

        first.Close();
        Assert.That(first.IsDisposed, Is.True);
        button.PerformClick();
        var reopened = (Form)mainType.GetField("_herald", Hidden)!.GetValue(main)!;
        Assert.That(reopened, Is.Not.SameAs(first));
        reopened.Close();
    }

    [Test]
    public void HeraldButtonSitsInsideTheHeaderBannerBesideTheOtherActions()
    {
        Type mainType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using var main = (Form)Activator.CreateInstance(mainType)!;
        main.ShowInTaskbar = false;
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-20_000, -20_000);
        main.Show();
        Application.DoEvents();
        main.PerformLayout();

        var herald = (Button)mainType.GetField("_heraldButton", Hidden)!.GetValue(main)!;
        var play = (Button)mainType.GetField("_playButton", Hidden)!.GetValue(main)!;
        Control banner = Descendants(main).Single(control => control.GetType().FullName == "OfflineDaoc.Launcher.FantasyBanner");
        Rectangle bannerBounds = banner.RectangleToScreen(banner.ClientRectangle);
        Rectangle heraldBounds = herald.RectangleToScreen(herald.ClientRectangle);
        Assert.Multiple(() =>
        {
            Assert.That(herald.Visible, Is.True);
            Assert.That(heraldBounds.Top, Is.GreaterThanOrEqualTo(bannerBounds.Top));
            Assert.That(heraldBounds.Bottom, Is.LessThanOrEqualTo(bannerBounds.Bottom - 4),
                "The HERALD button must stay fully inside the header banner.");
            Assert.That(play.RectangleToScreen(play.ClientRectangle).Bottom, Is.LessThanOrEqualTo(bannerBounds.Bottom - 8));
        });
        using var preview = new Bitmap(main.Width, main.Height);
        main.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "launcher-herald-button.png");
        preview.Save(path);
        TestContext.AddTestAttachment(path);
    }

    [Test]
    public async Task HeraldWindowRendersEverySectionFromASaveAndSavesPreviews()
    {
        string directory = Path.Combine(Path.GetTempPath(), "daoc-herald-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = HeraldDataTests.BuildSaveAt(directory, counters: true);
        try
        {
            Type heraldType = Launcher.GetType("OfflineDaoc.Launcher.HeraldForm")!;
            using var form = (Form)Activator.CreateInstance(heraldType, Hidden, null, new object[] { path }, null)!;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20_000, -20_000);
            form.ClientSize = new Size(1100, 700);
            form.Show();
            await WaitForLoad(form, heraldType);

            Assert.That(form.Text, Is.EqualTo("Camlann Herald"));
            Label stand = Field<Label>(form, "_stand");
            Assert.That(stand.Text, Does.Contain("Stand "));
            Assert.That(stand.Text, Does.Contain(7.ToString("N0", CultureInfo.CurrentCulture) + " characters"));
            Assert.That(stand.Text, Does.Contain("3").And.Contain("players"));
            Assert.That(stand.Text, Does.Contain("2 hidden by /statsanon or /anonymous"));

            object rankings = Field<object>(form, "_rankings");
            var rankingGrid = (DataGridView)rankings.GetType().GetField("_grid", Hidden)!.GetValue(rankings)!;
            Assert.That(rankingGrid.Rows.Count, Is.EqualTo(7), "Visible characters: 3 players, 2 gamebots, 2 companions.");
            Assert.That(rankingGrid.Rows[0].Cells[1].Value, Is.EqualTo("Charlie"), "Ranked by realm points, highest first.");

            var sections = Field<Control>(form, "_sections");
            PropertyInfo selected = sections.GetType().GetProperty("SelectedIndex", Hidden)!;
            string[] names = ["rankings", "guilds", "keeps-relics", "titles", "statistics"];
            for (int index = 0; index < names.Length; index++)
            {
                selected.SetValue(sections, index);
                Application.DoEvents();
                form.PerformLayout();
                AssertInsideWindow(form);
                foreach (DataGridView grid in Descendants(form).OfType<DataGridView>().Where(grid => grid.Visible))
                {
                    Assert.That(grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible), Is.LessThanOrEqualTo(grid.ClientSize.Width),
                        $"The {names[index]} grid columns must fit without a horizontal scroll bar.");
                }
                using var preview = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                string image = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"launcher-herald-{names[index]}.png");
                preview.Save(image);
                TestContext.AddTestAttachment(image);
            }

            form.ClientSize = new Size(960, 620);
            for (int index = 0; index < names.Length; index++)
            {
                selected.SetValue(sections, index);
                Application.DoEvents();
                form.PerformLayout();
                AssertInsideWindow(form);
            }
            form.ClientSize = new Size(1100, 700);

            object guilds = Field<object>(form, "_guilds");
            var guildGrid = (DataGridView)guilds.GetType().GetField("_grid", Hidden)!.GetValue(guilds)!;
            Assert.That(guildGrid.Rows.Count, Is.EqualTo(2));

            object keeps = Field<object>(form, "_keepsRelics");
            Assert.That(((DataGridView)keeps.GetType().GetField("_keeps", Hidden)!.GetValue(keeps)!).Rows.Count, Is.EqualTo(3));
            Assert.That(((DataGridView)keeps.GetType().GetField("_relics", Hidden)!.GetValue(keeps)!).Rows.Count, Is.EqualTo(2));

            object titles = Field<object>(form, "_titles");
            var titleBox = (ComboBox)titles.GetType().GetField("_title", Hidden)!.GetValue(titles)!;
            titleBox.SelectedIndex = 3;
            Application.DoEvents();
            var holders = (DataGridView)titles.GetType().GetField("_grid", Hidden)!.GetValue(titles)!;
            Assert.That(holders.Rows.Count, Is.EqualTo(1), "Bravo holds Bane of Midgard (2,000+ Midgard player kills).");
            Assert.That(holders.Rows[0].Cells[0].Value, Is.EqualTo("Bravo"));

            object statistics = Field<object>(form, "_statistics");
            var cards = (Label[])statistics.GetType().GetField("_cards", Hidden)!.GetValue(statistics)!;
            Assert.That(cards[0].Text, Is.EqualTo(7.ToString("N0", CultureInfo.CurrentCulture)));
            Assert.That(cards[2].Text, Is.EqualTo(3.ToString("N0", CultureInfo.CurrentCulture)));
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task HeraldWithoutAnInstallShowsAHelpfulEmptyState()
    {
        Type heraldType = Launcher.GetType("OfflineDaoc.Launcher.HeraldForm")!;
        string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, "no-such-install", "opendaoc.sqlite3.db");
        using var form = (Form)Activator.CreateInstance(heraldType, Hidden, null, new object[] { missing }, null)!;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20_000, -20_000);
        form.Show();
        await WaitForLoad(form, heraldType);

        Assert.That(Field<Label>(form, "_stand").Text, Does.Contain("no save database found"));
        Assert.That(File.Exists(missing), Is.False, "Opening the Herald must not create a save.");
    }

    [Test]
    public void ProfileShowsTheRealmBreakdownAndTheCompanionOwnerWithoutOpeningAModal()
    {
        string directory = Path.Combine(Path.GetTempPath(), "daoc-herald-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            HeraldSnapshot snapshot = HeraldData.Load(HeraldDataTests.BuildSaveAt(directory, counters: false));
            HeraldCharacter companion = snapshot.Characters.Single(c => c.Name == "Comp One");
            Type dialogType = Launcher.GetType("OfflineDaoc.Launcher.HeraldProfileDialog")!;
            using var dialog = (Form)Activator.CreateInstance(dialogType, Hidden, null, new object[] { companion, snapshot }, null)!;
            dialog.ShowInTaskbar = false;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-20_000, -20_000);
            dialog.Show();
            Application.DoEvents();

            var grid = Descendants(dialog).OfType<DataGridView>().Single();
            Assert.That(grid.Rows.Count, Is.EqualTo(4), "Albion, Midgard, Hibernia and the total.");
            Assert.That(grid.Rows[0].Cells[0].Value, Is.EqualTo("Albion"));
            Assert.That(grid.Rows[0].Cells[1].Value, Is.Null, "A companion without recorded counters shows a dash, not zero.");
            List<string> labels = Descendants(dialog).OfType<Label>().Select(label => label.Text).ToList();
            Assert.That(labels.Any(text => text.Contains("Owner: Alpha", StringComparison.Ordinal)), Is.True,
                "The owner is the character the companion belongs to.");
            Assert.That(labels.Any(text => text.Contains("does not record kill counters for companions", StringComparison.Ordinal)), Is.True);
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static async Task WaitForLoad(Form form, Type heraldType)
    {
        FieldInfo status = heraldType.GetField("_status", Hidden)!;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            Application.DoEvents();
            var label = (Label)status.GetValue(form)!;
            if (!label.Text.StartsWith("Reading", StringComparison.Ordinal))
                return;
            await Task.Delay(20);
        }

        Assert.Fail("The Herald did not finish reading the save.");
    }

    /// <summary>Every visible control must sit inside the window; scrolling areas may extend past it.</summary>
    private static void AssertInsideWindow(Form form)
    {
        Rectangle client = new(Point.Empty, form.ClientSize);
        foreach (Control control in Descendants(form))
        {
            if (!control.Visible || control is Panel || InsideScrollingArea(control))
                continue;
            Rectangle inForm = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.That(inForm.Right, Is.LessThanOrEqualTo(client.Right + 1), $"{control.GetType().Name} '{control.Name}' runs past the right edge.");
            Assert.That(inForm.Bottom, Is.LessThanOrEqualTo(client.Bottom + 1), $"{control.GetType().Name} '{control.Name}' runs past the bottom edge.");
        }
    }

    private static bool InsideScrollingArea(Control control)
    {
        for (Control parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollableControl { AutoScroll: true })
                return true;
        }

        return false;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Hidden)!.GetValue(target)!;

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
