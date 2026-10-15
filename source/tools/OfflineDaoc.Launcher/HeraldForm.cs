using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Camlann Herald: a dark, gold-ruled ranking and statistics window over the save.
/// Modeless and read-only. The save is read once when the window opens and again on
/// REFRESH. Nothing polls, so the server and the bots do no extra work.
/// </summary>
internal sealed class HeraldForm : Form
{
    private readonly string _database;
    private readonly Label _stand = new()
    {
        AutoSize = true,
        ForeColor = DaocTheme.Parchment,
        Font = HeraldPalette.Body,
        Margin = new Padding(1, 4, 0, 0),
    };
    private readonly Label _status = new()
    {
        AutoSize = true,
        ForeColor = DaocTheme.Muted,
        Font = HeraldPalette.Small,
        Margin = new Padding(2, 6, 0, 0),
        Text = "Reading the save…",
    };
    private readonly RuneButton _refresh = new()
    {
        Text = "REFRESH",
        Accent = DaocTheme.Gold,
        Width = 118,
        Height = 31,
        Margin = new Padding(0, 8, 0, 0),
    };
    private readonly PillSelector _sections = new("Rankings", "Guilds", "Keeps & Relics", "Titles", "Server statistics");
    private readonly RankingsView _rankings = new();
    private readonly GuildsView _guilds = new();
    private readonly KeepsRelicsView _keepsRelics = new();
    private readonly TitlesView _titles = new();
    private readonly StatisticsView _statistics = new();
    private bool _loading;

    internal HeraldForm(string database)
    {
        _database = database;
        Text = "Camlann Herald";
        Size = new Size(1100, 700);
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(9) };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSectionRow(), 0, 1);
        root.Controls.Add(BuildHost(), 0, 2);
        root.Controls.Add(_status, 0, 3);
        frame.Controls.Add(root);
        Controls.Add(frame);

        _sections.Changed += (_, _) => ShowSection(_sections.SelectedIndex);
        _refresh.Click += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => await RefreshAsync();
        ShowSection(0);
    }

    /// <summary>Opens the Herald once and focuses the existing window on later clicks.</summary>
    internal static HeraldForm ShowOrFocus(Form owner, string database, HeraldForm? existing)
    {
        if (existing is { IsDisposed: false })
        {
            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return existing;
        }

        var herald = new HeraldForm(database);
        herald.Show(owner);
        return herald;
    }

    private HeraldView[] Views => [_rankings, _guilds, _keepsRelics, _titles, _statistics];

    private Control BuildHeader()
    {
        var banner = new FantasyBanner { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 8, 12, 8),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titles.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "CAMLANN HERALD",
            Font = HeraldPalette.Heading,
            ForeColor = DaocTheme.GoldLight,
            Margin = new Padding(0),
        }, 0, 0);
        titles.Controls.Add(_stand, 0, 1);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_refresh, 1, 0);
        banner.Controls.Add(header);
        return banner;
    }

    private Control BuildSectionRow()
    {
        _sections.BackColor = DaocTheme.StoneDark;
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = DaocTheme.StoneDark,
            Padding = new Padding(6, 6, 6, 6),
        };
        row.Controls.Add(_sections);
        return row;
    }

    private Control BuildHost()
    {
        var host = new InsetPanel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = DaocTheme.Panel };
        foreach (HeraldView view in Views)
            host.Controls.Add(view);
        return host;
    }

    private void ShowSection(int index)
    {
        HeraldView[] views = Views;
        for (int i = 0; i < views.Length; i++)
            views[i].Visible = i == index;
    }

    private async Task RefreshAsync()
    {
        if (_loading || IsDisposed)
            return;
        _loading = true;
        _refresh.Enabled = false;
        _status.Text = "Reading the save…";
        try
        {
            string database = _database;
            HeraldSnapshot snapshot = await Task.Run(() => HeraldData.Load(database));
            if (IsDisposed)
                return;
            ApplySnapshot(snapshot);
        }
        catch (Exception error)
        {
            if (!IsDisposed)
                _status.Text = "Could not read the save: " + error.Message;
        }
        finally
        {
            _loading = false;
            if (!IsDisposed)
                _refresh.Enabled = true;
        }
    }

    private void ApplySnapshot(HeraldSnapshot snapshot)
    {
        var visible = snapshot.Characters.Where(character => !character.IsHidden).ToList();
        int hidden = snapshot.Characters.Count - visible.Count;
        string stamp = snapshot.GeneratedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        _stand.Text = snapshot.DatabaseFound
            ? $"Stand {stamp} · {visible.Count:N0} characters ({visible.Count(c => c.Kind == HeraldKind.Player):N0} players, "
              + $"{visible.Count(c => c.Kind == HeraldKind.Gamebot):N0} gamebots, {visible.Count(c => c.Kind == HeraldKind.Companion):N0} companions)"
              + (hidden > 0 ? $" · {hidden:N0} hidden by /statsanon or /anonymous" : string.Empty)
            : $"Stand {stamp} · no save database found in the install's data folder";
        foreach (HeraldView view in Views)
            view.Render(snapshot);
        _status.Text = StatusText(snapshot);
    }

    private static string StatusText(HeraldSnapshot snapshot)
    {
        if (!snapshot.DatabaseFound)
            return "Start the install once so the save exists, then press REFRESH.";

        HeraldCoverage coverage = snapshot.Coverage;
        var notes = new List<string> { "Read-only · REFRESH re-reads the save" };
        if (coverage.Gamebots && !coverage.GamebotKillCounters)
            notes.Add("gamebot kill counters are not in this save yet (shown as —)");
        if (coverage.Companions && !coverage.CompanionKillCounters)
            notes.Add("companion kill counters are not in this save yet (shown as —)");
        if (!coverage.Players)
            notes.Add("no DOLCharacters table, so no players");
        if (!coverage.Accounts)
            notes.Add("no Account table, so GM tags are off");
        if (!coverage.Guilds)
            notes.Add("no Guild table");
        if (!coverage.Keeps)
            notes.Add("no Keep table");
        if (!coverage.Relics)
            notes.Add("no Relic table");
        return string.Join(" · ", notes);
    }

    internal static string Share(long part, long total) =>
        total <= 0 ? "0%" : (part * 100.0 / total).ToString("0.0", CultureInfo.CurrentCulture) + "%";
}

/// <summary>Shared look for the Herald sections.</summary>
internal abstract class HeraldView : UserControl
{
    protected HeraldView()
    {
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        Font = HeraldPalette.Body;
    }

    internal abstract void Render(HeraldSnapshot snapshot);

    protected static Label Caption(string text) => new()
    {
        AutoSize = true,
        Text = text.ToUpperInvariant(),
        ForeColor = DaocTheme.Muted,
        Font = HeraldPalette.Caption,
        Margin = new Padding(0, 9, 8, 0),
    };

    protected static Label Note() => new()
    {
        AutoSize = true,
        ForeColor = DaocTheme.Muted,
        Font = HeraldPalette.Small,
        Margin = new Padding(2, 6, 0, 0),
    };

    protected static FlowLayoutPanel Strip(params Control[] items)
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = DaocTheme.Panel,
            Padding = new Padding(0, 0, 0, 4),
        };
        strip.Controls.AddRange(items);
        return strip;
    }

    protected static ComboBox Dropdown(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = width,
        Margin = new Padding(0, 4, 12, 0),
        BackColor = Color.FromArgb(59, 53, 43),
        ForeColor = DaocTheme.Text,
        FlatStyle = FlatStyle.Flat,
    };

    protected static void SetRealmColour(DataGridViewCellFormattingEventArgs e, string property, string value)
    {
        if (property == "Realm" || property == "Owner" || property == "CurrentRealm")
        {
            e.CellStyle.ForeColor = HeraldPalette.RealmNamed(value);
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        }
    }
}

internal sealed class RankingsView : HeraldView
{
    private static readonly string[] MetricLabels = ["realm points", "kills", "deaths", "death blows", "solo kills"];
    private readonly PillSelector _type = new("All", "Players", "Gamebots", "Companions");
    private readonly PillSelector _realm = new("All realms", "Albion", "Midgard", "Hibernia");
    private readonly PillSelector _metric = new("Realm points", "Kills", "Deaths", "Death blows", "Solo kills");
    private readonly PillSelector _count = new("100", "250", "500", "All");
    private readonly ComboBox _class = Dropdown(150);
    private readonly TextBox _name = new()
    {
        Width = 170,
        Margin = new Padding(0, 4, 0, 0),
        PlaceholderText = "Name contains…",
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(59, 53, 43),
        ForeColor = DaocTheme.Text,
    };
    private readonly HeraldGridView _grid = new();
    private readonly Label _note = Note();
    private HeraldSnapshot? _snapshot;
    private bool _binding;

    internal RankingsView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Strip(Caption("Type"), _type, Caption("Realm"), _realm, Caption("Class"), _class), 0, 0);
        layout.Controls.Add(Strip(Caption("Metric"), _metric, Caption("Show"), _count, Caption("Name"), _name), 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_note, 0, 3);
        Controls.Add(layout);

        _class.Items.Add("All classes");
        _class.SelectedIndex = 0;
        AddColumns();
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            string property = _grid.Columns[e.ColumnIndex].DataPropertyName;
            string value = e.Value?.ToString() ?? string.Empty;
            SetRealmColour(e, property, value);
            if (property == "Type")
            {
                e.CellStyle.ForeColor = HeraldPalette.TypeTag(value);
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                if (value == "GM")
                    e.CellStyle.Font = HeraldPalette.BodyBold;
            }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _snapshot is null || _grid.Rows[e.RowIndex].DataBoundItem is not HeraldRankRow row)
                return;
            using var profile = new HeraldProfileDialog(row.Character, _snapshot);
            profile.ShowDialog(FindForm());
        };

        _type.Changed += (_, _) => Apply();
        _realm.Changed += (_, _) => Apply();
        _metric.Changed += (_, _) => Apply();
        _count.Changed += (_, _) => Apply();
        _class.SelectedIndexChanged += (_, _) => Apply();
        _name.TextChanged += (_, _) => Apply();
    }

    internal override void Render(HeraldSnapshot snapshot)
    {
        _snapshot = snapshot;
        _binding = true;
        string previous = _class.SelectedItem?.ToString() ?? "All classes";
        _class.Items.Clear();
        _class.Items.Add("All classes");
        foreach (string name in HeraldData.ClassNames(snapshot))
            _class.Items.Add(name);
        int index = _class.Items.IndexOf(previous);
        _class.SelectedIndex = index < 0 ? 0 : index;
        _binding = false;
        Apply();
    }

    private void AddColumns()
    {
        AddColumn("#", "Position", 40, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Name", "Name", 110);
        AddColumn("Type", "Type", 88);
        AddColumn("Realm", "Realm", 68);
        AddColumn("Class", "ClassName", 90);
        AddColumn("Lvl", "Level", 40, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Guild", "Guild", 120, fill: true);
        AddColumn("Realm points", "RealmPoints", 112, DataGridViewContentAlignment.MiddleRight, "N0");
        AddColumn("Rank", "Rank", 72);
        AddColumn("Kills", "Kills", 66, DataGridViewContentAlignment.MiddleRight, "N0");
        AddColumn("Deaths", "Deaths", 66, DataGridViewContentAlignment.MiddleRight, "N0");
        AddColumn("Death blows", "DeathBlows", 96, DataGridViewContentAlignment.MiddleRight, "N0");
        AddColumn("Solo", "Solo", 62, DataGridViewContentAlignment.MiddleRight, "N0");
    }

    private void AddColumn(string header, string property, int width,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft, string? format = null, bool fill = false) =>
        _grid.AddColumn(header, property, width, alignment, format, fill);

    private void Apply()
    {
        if (_binding || _snapshot is null)
            return;
        HeraldRankQuery query = CurrentQuery();
        IReadOnlyList<HeraldRankRow> rows = HeraldData.Rankings(_snapshot, query);
        int total = query.Limit == 0 ? rows.Count : HeraldData.Rankings(_snapshot, query with { Limit = 0 }).Count;
        _grid.DataSource = rows.ToList();
        _note.Text = $"Showing {rows.Count:N0} of {total:N0} matching characters · sorted by {MetricLabels[_metric.SelectedIndex]} · double-click for details";
    }

    private HeraldRankQuery CurrentQuery()
    {
        HeraldKindFilter kind = _type.SelectedIndex switch
        {
            1 => HeraldKindFilter.Players,
            2 => HeraldKindFilter.Gamebots,
            3 => HeraldKindFilter.Companions,
            _ => HeraldKindFilter.All,
        };
        int realm = _realm.SelectedIndex switch
        {
            1 => HeraldData.Albion,
            2 => HeraldData.Midgard,
            3 => HeraldData.Hibernia,
            _ => 0,
        };
        HeraldMetric metric = _metric.SelectedIndex switch
        {
            1 => HeraldMetric.Kills,
            2 => HeraldMetric.Deaths,
            3 => HeraldMetric.DeathBlows,
            4 => HeraldMetric.SoloKills,
            _ => HeraldMetric.RealmPoints,
        };
        int limit = _count.SelectedText == "All" ? 0 : int.Parse(_count.SelectedText, CultureInfo.InvariantCulture);
        string className = _class.SelectedIndex > 0 ? _class.SelectedItem?.ToString() ?? string.Empty : string.Empty;
        return new HeraldRankQuery(kind, realm, metric, limit, className, _name.Text);
    }
}

internal sealed class GuildsView : HeraldView
{
    private readonly HeraldGridView _grid = new();
    private readonly Label _note = Note();

    internal GuildsView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_grid, 0, 0);
        layout.Controls.Add(_note, 0, 1);
        Controls.Add(layout);

        _grid.AddColumn("Guild", "Name", 170, fill: true);
        _grid.AddColumn("Realm", "Realm", 84);
        _grid.AddColumn("Members", "Members", 76, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Players", "Players", 68, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Gamebots", "Gamebots", 92, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Companions", "Companions", 108, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Member RP", "MemberRealmPoints", 112, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Guild RP", "GuildRealmPoints", 104, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Keeps claimed", "KeepsClaimed", 110, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                SetRealmColour(e, _grid.Columns[e.ColumnIndex].DataPropertyName, e.Value?.ToString() ?? string.Empty);
        };
    }

    internal override void Render(HeraldSnapshot snapshot)
    {
        IReadOnlyList<HeraldGuildRow> rows = HeraldData.GuildRows(snapshot);
        _grid.DataSource = rows.ToList();
        _note.Text = snapshot.Coverage.Guilds
            ? $"{rows.Count:N0} guilds with members or claimed keeps · sorted by member realm points · members counted by guild across players, gamebots and companions"
            : "This save has no Guild table, so guild totals are unavailable.";
    }
}

internal sealed class KeepsRelicsView : HeraldView
{
    private readonly Label _keepsHeading = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = HeraldPalette.BodyBold, Margin = new Padding(0, 4, 0, 4) };
    private readonly Label _relicsHeading = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = HeraldPalette.BodyBold, Margin = new Padding(0, 4, 0, 4) };
    private readonly HeraldGridView _keeps = new();
    private readonly HeraldGridView _relics = new();
    private readonly Label _note = Note();

    internal KeepsRelicsView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_keepsHeading, 0, 0);
        layout.Controls.Add(_relicsHeading, 1, 0);
        layout.Controls.Add(_keeps, 0, 1);
        layout.Controls.Add(_relics, 1, 1);
        layout.Controls.Add(_note, 0, 2);
        layout.SetColumnSpan(_note, 2);
        Controls.Add(layout);

        _keeps.AddColumn("Keep", "Name", 130, fill: true);
        _keeps.AddColumn("Owner", "Owner", 84);
        _keeps.AddColumn("Originally", "OriginalRealm", 88);
        _keeps.AddColumn("Lvl", "Level", 40, DataGridViewContentAlignment.MiddleRight);
        _keeps.AddColumn("Claimed by", "ClaimedBy", 120, fill: true);
        _relics.AddColumn("Relic", "RelicId", 52, DataGridViewContentAlignment.MiddleRight);
        _relics.AddColumn("Type", "Type", 72);
        _relics.AddColumn("Originally", "OriginalRealm", 88);
        _relics.AddColumn("Now held", "CurrentRealm", 80);
        _relics.AddColumn("Keep", "Keep", 70, fill: true);
        _relics.AddColumn("Last captured", "LastCaptured", 124);
        _keeps.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                SetRealmColour(e, _keeps.Columns[e.ColumnIndex].DataPropertyName, e.Value?.ToString() ?? string.Empty);
        };
        _relics.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                SetRealmColour(e, _relics.Columns[e.ColumnIndex].DataPropertyName, e.Value?.ToString() ?? string.Empty);
        };
    }

    internal override void Render(HeraldSnapshot snapshot)
    {
        IReadOnlyList<HeraldKeepRow> keeps = HeraldData.KeepRows(snapshot);
        IReadOnlyList<HeraldRelicRow> relics = HeraldData.RelicRows(snapshot);
        _keeps.DataSource = keeps.ToList();
        _relics.DataSource = relics.ToList();
        _keepsHeading.Text = $"KEEPS · {keeps.Count:N0}";
        _relicsHeading.Text = $"RELICS · {relics.Count:N0}";
        int owned = keeps.Count(keep => keep.OwnerRealm is HeraldData.Albion or HeraldData.Midgard or HeraldData.Hibernia);
        _note.Text = $"{owned:N0} keeps are held by a realm. Relic times are the world clock's local time, which can run faster than real time at world speed.";
    }
}

internal sealed class TitlesView : HeraldView
{
    private readonly ComboBox _title = Dropdown(360);
    private readonly PillSelector _kinds = new("Players", "All characters");
    private readonly PillSelector _realm = new("All realms", "Albion", "Midgard", "Hibernia");
    private readonly Label _summary = new() { AutoSize = true, ForeColor = DaocTheme.GoldLight, Font = HeraldPalette.BodyBold, Margin = new Padding(0, 6, 0, 4) };
    private readonly HeraldGridView _grid = new();
    private readonly DataGridViewColumn _valueColumn;
    private readonly Label _note = Note();
    private HeraldSnapshot? _snapshot;

    internal TitlesView()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Strip(Caption("Title"), _title, Caption("Holders"), _kinds, Caption("Realm"), _realm), 0, 0);
        layout.Controls.Add(_summary, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_note, 0, 3);
        Controls.Add(layout);

        foreach (HeraldTitle title in HeraldData.Titles)
            _title.Items.Add($"{title.Name} · {title.Threshold}");
        _title.SelectedIndex = 0;
        _grid.AddColumn("Name", "Name", 140, fill: true);
        _grid.AddColumn("Type", "Type", 78);
        _grid.AddColumn("Realm", "Realm", 80);
        _grid.AddColumn("Class", "ClassName", 96);
        _grid.AddColumn("Lvl", "Level", 42, DataGridViewContentAlignment.MiddleRight);
        _grid.AddColumn("Guild", "Guild", 130, fill: true);
        _valueColumn = _grid.AddColumn("Value", "Value", 110, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Realm points", "RealmPoints", 112, DataGridViewContentAlignment.MiddleRight, "N0");
        _grid.AddColumn("Rank", "Rank", 74);
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            string property = _grid.Columns[e.ColumnIndex].DataPropertyName;
            string value = e.Value?.ToString() ?? string.Empty;
            SetRealmColour(e, property, value);
            if (property == "Type")
            {
                e.CellStyle.ForeColor = HeraldPalette.TypeTag(value);
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            }
        };
        _title.SelectedIndexChanged += (_, _) => Apply();
        _kinds.Changed += (_, _) => Apply();
        _realm.Changed += (_, _) => Apply();
    }

    internal override void Render(HeraldSnapshot snapshot)
    {
        _snapshot = snapshot;
        Apply();
    }

    private void Apply()
    {
        if (_snapshot is null || _title.SelectedIndex < 0)
            return;
        HeraldTitle title = HeraldData.Titles[_title.SelectedIndex];
        HeraldKindFilter kinds = _kinds.SelectedIndex == 0 ? HeraldKindFilter.Players : HeraldKindFilter.All;
        int realm = _realm.SelectedIndex switch
        {
            1 => HeraldData.Albion,
            2 => HeraldData.Midgard,
            3 => HeraldData.Hibernia,
            _ => 0,
        };
        IReadOnlyList<HeraldTitleHolder> holders = HeraldData.TitleHolders(_snapshot, title, kinds, realm);
        _valueColumn.HeaderText = ValueHeader(title.Metric);
        _grid.DataSource = holders.ToList();
        _summary.Text = $"{title.Name} · {title.Group} {title.Threshold} · {holders.Count:N0} holder{(holders.Count == 1 ? string.Empty : "s")}";
        _note.Text = kinds == HeraldKindFilter.Players
            ? "Titles are granted to real players in game. Switch HOLDERS to show gamebots and companions too. Counters a save does not record never qualify."
            : "Gamebots and companions can hold titles here for comparison only. Counters a save does not record never qualify.";
    }

    private static string ValueHeader(HeraldTitleMetric metric) => metric switch
    {
        HeraldTitleMetric.AlbionKills => "Albion kills",
        HeraldTitleMetric.MidgardKills => "Midgard kills",
        HeraldTitleMetric.HiberniaKills => "Hibernia kills",
        HeraldTitleMetric.AllPlayerKills => "All kills",
        HeraldTitleMetric.DeathBlows => "Death blows",
        HeraldTitleMetric.SoloKills => "Solo kills",
        HeraldTitleMetric.CapturedKeeps => "Keeps captured",
        _ => "Towers captured",
    };
}

internal sealed class StatisticsView : HeraldView
{
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DaocTheme.Panel };
    private readonly TableLayoutPanel _content = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
        RowCount = 4,
        BackColor = DaocTheme.Panel,
    };
    private readonly Label[] _cards = [FigureLabel(), FigureLabel(), FigureLabel(), FigureLabel(), FigureLabel()];
    private readonly BarList _characterBars = new() { Dock = DockStyle.Top };
    private readonly BarList _realmPointBars = new() { Dock = DockStyle.Top };
    private readonly BarList _keepBars = new() { Dock = DockStyle.Top };
    private readonly DonutChart[] _donuts = [new DonutChart { Dock = DockStyle.Top }, new DonutChart { Dock = DockStyle.Top }, new DonutChart { Dock = DockStyle.Top }];
    private readonly Label[] _donutHeadings = [Caption("Albion"), Caption("Midgard"), Caption("Hibernia")];
    private readonly BarList _levelBars = new() { Dock = DockStyle.Top };
    private readonly Label[] _level50 = [FigureLabel(), FigureLabel(), FigureLabel()];
    private readonly Label[] _level50Heads = [Caption("Albion"), Caption("Midgard"), Caption("Hibernia")];

    internal StatisticsView()
    {
        Controls.Add(_scroll);
        HeraldPalette.UseDarkScrollbars(_scroll);
        _scroll.Controls.Add(_content);
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++)
            _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _content.Controls.Add(BuildCards(), 0, 0);
        _content.Controls.Add(Section("Realm balance", BuildRealmBalance()), 0, 1);
        _content.Controls.Add(Section("Class distribution by realm", BuildDonuts()), 0, 2);
        _content.Controls.Add(Section("Level distribution", BuildLevels()), 0, 3);
    }

    private static Label FigureLabel() => new()
    {
        AutoSize = true,
        Font = HeraldPalette.Figure,
        ForeColor = DaocTheme.GoldLight,
        Margin = new Padding(0, 2, 0, 0),
        Text = "—",
    };

    internal override void Render(HeraldSnapshot snapshot)
    {
        HeraldStatistics stats = HeraldData.Statistics(snapshot);
        _cards[0].Text = stats.Visible.ToString("N0", CultureInfo.CurrentCulture);
        _cards[1].Text = stats.RealmPoints.ToString("N0", CultureInfo.CurrentCulture);
        _cards[2].Text = stats.Players.ToString("N0", CultureInfo.CurrentCulture);
        _cards[3].Text = stats.Gamebots.ToString("N0", CultureInfo.CurrentCulture);
        _cards[4].Text = stats.Companions.ToString("N0", CultureInfo.CurrentCulture);

        long characters = stats.Realms.Sum(realm => (long)realm.Characters);
        long points = stats.Realms.Sum(realm => realm.RealmPoints);
        long keeps = stats.Realms.Sum(realm => (long)realm.KeepsOwned);
        _characterBars.SetItems(stats.Realms.Select(realm => new BarItem(realm.Name, realm.Characters,
            $"{realm.Characters:N0} · {HeraldForm.Share(realm.Characters, characters)}", HeraldPalette.Realm(realm.Realm))).ToList());
        _realmPointBars.SetItems(stats.Realms.Select(realm => new BarItem(realm.Name, realm.RealmPoints,
            $"{realm.RealmPoints:N0} · {HeraldForm.Share(realm.RealmPoints, points)}", HeraldPalette.Realm(realm.Realm))).ToList());
        _keepBars.SetItems(stats.Realms.Select(realm => new BarItem(realm.Name, realm.KeepsOwned,
            $"{realm.KeepsOwned:N0} · {HeraldForm.Share(realm.KeepsOwned, keeps)}", HeraldPalette.Realm(realm.Realm))).ToList());

        for (int i = 0; i < _donuts.Length && i < stats.ClassesByRealm.Count; i++)
        {
            HeraldRealmClasses realm = stats.ClassesByRealm[i];
            _donutHeadings[i].Text = $"{realm.Name.ToUpperInvariant()} · {realm.Total:N0} characters";
            _donuts[i].SetData(realm.Classes.Select(count => new DonutSlice(count.ClassName, count.Count)).ToList(),
                HeraldPalette.Realm(realm.Realm), realm.Total.ToString("N0", CultureInfo.CurrentCulture), "characters");
        }

        _levelBars.SetItems(stats.Levels.Select(band => new BarItem(band.Label, band.Count,
            $"{band.Count:N0} · {HeraldForm.Share(band.Count, stats.Visible)}", DaocTheme.Gold)).ToList());
        for (int i = 0; i < _level50.Length && i < stats.Realms.Count; i++)
            _level50[i].Text = stats.Realms[i].Level50.ToString("N0", CultureInfo.CurrentCulture);
    }

    private Control BuildCards()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = 1, BackColor = DaocTheme.Panel };
        for (int column = 0; column < 5; column++)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        string[] captions = ["Characters", "Realm points", "Players", "Gamebots", "Companions"];
        for (int column = 0; column < captions.Length; column++)
            row.Controls.Add(Card(captions[column], _cards[column]), column, 0);
        return row;
    }

    private Control BuildRealmBalance()
    {
        var row = Columns(3);
        row.Controls.Add(Labelled("Characters", _characterBars), 0, 0);
        row.Controls.Add(Labelled("Realm points", _realmPointBars), 1, 0);
        row.Controls.Add(Labelled("Keeps owned", _keepBars), 2, 0);
        return row;
    }

    private Control BuildDonuts()
    {
        var row = Columns(3);
        for (int i = 0; i < _donuts.Length; i++)
        {
            var stack = Stack();
            stack.Controls.Add(_donutHeadings[i], 0, 0);
            stack.Controls.Add(_donuts[i], 0, 1);
            row.Controls.Add(stack, i, 0);
        }
        return row;
    }

    private Control BuildLevels()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, BackColor = DaocTheme.Panel };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(Labelled("Characters by level", _levelBars), 0, 0);

        string[] realmNames = ["Albion", "Midgard", "Hibernia"];
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = realmNames.Length + 1,
            BackColor = DaocTheme.Panel,
            Margin = new Padding(0, 0, 10, 0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.Controls.Add(Caption("Level 50 by realm"), 0, 0);
        for (int i = 0; i < realmNames.Length; i++)
        {
            stack.Controls.Add(Card(realmNames[i], _level50[i]), 0, i + 1);
        }
        for (int i = 0; i <= realmNames.Length; i++)
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.Controls.Add(stack, 1, 0);
        return row;
    }

    private static Control Section(string title, Control body)
    {
        var stack = Stack();
        stack.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title.ToUpperInvariant(),
            ForeColor = DaocTheme.GoldLight,
            Font = HeraldPalette.BodyBold,
            Margin = new Padding(0, 8, 0, 2),
        }, 0, 0);
        stack.Controls.Add(body, 0, 1);
        return stack;
    }

    private static Control Labelled(string caption, Control body)
    {
        var stack = Stack();
        stack.Controls.Add(Caption(caption), 0, 0);
        stack.Controls.Add(body, 0, 1);
        return stack;
    }

    private static InsetPanel Card(string caption, Label value)
    {
        var card = new InsetPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(10, 6, 8, 6),
            BackColor = DaocTheme.Panel,
            AutoSize = true,
        };
        var stack = Stack();
        stack.Dock = DockStyle.Fill;
        stack.Controls.Add(Caption(caption), 0, 0);
        stack.Controls.Add(value, 0, 1);
        card.Controls.Add(stack);
        return card;
    }

    private static TableLayoutPanel Stack()
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = DaocTheme.Panel,
            Margin = new Padding(0, 0, 10, 0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return stack;
    }

    private static TableLayoutPanel Columns(int count)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = count, RowCount = 1, BackColor = DaocTheme.Panel };
        for (int column = 0; column < count; column++)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return row;
    }
}

internal sealed record HeraldProfileLine(string Realm, long? Kills, long? DeathBlows, long? Solo);

/// <summary>Double-click detail for one character: realm breakdown and owner or counters.</summary>
internal sealed class HeraldProfileDialog : Form
{
    internal HeraldProfileDialog(HeraldCharacter character, HeraldSnapshot snapshot)
    {
        Text = "Herald · " + character.Name;
        Size = new Size(540, 470);
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var grid = new HeraldGridView();
        grid.AddColumn("Realm", "Realm", 110);
        grid.AddColumn("Player kills", "Kills", 120, DataGridViewContentAlignment.MiddleRight, "N0");
        grid.AddColumn("Death blows", "DeathBlows", 120, DataGridViewContentAlignment.MiddleRight, "N0");
        grid.AddColumn("Solo kills", "Solo", 120, DataGridViewContentAlignment.MiddleRight, "N0");
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
                e.CellStyle.ForeColor = HeraldPalette.RealmNamed(e.Value?.ToString() ?? string.Empty);
        };
        grid.DataSource = new List<HeraldProfileLine>
        {
            new("Albion", character.Combat.KillsAlbion, character.Combat.DeathBlowsAlbion, character.Combat.SoloAlbion),
            new("Midgard", character.Combat.KillsMidgard, character.Combat.DeathBlowsMidgard, character.Combat.SoloMidgard),
            new("Hibernia", character.Combat.KillsHibernia, character.Combat.DeathBlowsHibernia, character.Combat.SoloHibernia),
            new("Total", character.Combat.Kills, character.Combat.DeathBlows, character.Combat.Solo),
        };

        string kind = HeraldData.TypeTag(character);
        string owner = character.Kind != HeraldKind.Companion ? string.Empty
            : character.OwnerMissing ? "Owner: no owner (orphaned companion)"
            : "Owner: " + (character.OwnerName.Length > 0 ? character.OwnerName : "—");
        var facts = new List<string>
        {
            $"Guild: {(character.GuildName.Length > 0 ? character.GuildName : "—")}",
            $"Realm points: {character.RealmPoints.ToString("N0", CultureInfo.CurrentCulture)} · {character.RealmRank}",
            $"Deaths (PvP): {(character.Combat.Deaths?.ToString("N0", CultureInfo.CurrentCulture) ?? "not recorded")}",
        };
        if (character.Kind == HeraldKind.Player)
        {
            facts.Add($"Keeps captured: {(character.CapturedKeeps?.ToString("N0", CultureInfo.CurrentCulture) ?? "—")} · "
                + $"Towers captured: {(character.CapturedTowers?.ToString("N0", CultureInfo.CurrentCulture) ?? "—")}");
        }
        if (owner.Length > 0)
            facts.Add(owner);
        if (character.IsGameMaster)
            facts.Add("Account is a GM (PrivLevel above 1).");

        string note = character.Kind == HeraldKind.Player || character.Combat.Kills is not null
            ? string.Empty
            : $"This save does not record kill counters for {(character.Kind == HeraldKind.Gamebot ? "gamebots" : "companions")} yet, so the cells show —.";

        var name = new Label
        {
            AutoSize = true,
            Text = character.Name,
            ForeColor = DaocTheme.GoldLight,
            Font = HeraldPalette.Heading,
            Margin = new Padding(0),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = $"{kind} · {HeraldData.RealmName(character.Realm)} {character.ClassName} · Level {character.Level}",
            ForeColor = HeraldPalette.Realm(character.Realm),
            Font = HeraldPalette.BodyBold,
            Margin = new Padding(0, 2, 0, 6),
        };
        var details = new Label
        {
            AutoSize = true,
            Text = string.Join(Environment.NewLine, facts),
            ForeColor = DaocTheme.Parchment,
            Font = HeraldPalette.Body,
            Margin = new Padding(0, 0, 0, 6),
        };
        var noteLabel = new Label
        {
            AutoSize = true,
            Text = note,
            ForeColor = DaocTheme.Muted,
            Font = HeraldPalette.Small,
            Margin = new Padding(0, 6, 0, 0),
        };
        var close = new RuneButton { Text = "CLOSE", Width = 110, Height = 31, Accent = DaocTheme.Gold, Margin = new Padding(0, 8, 0, 0) };
        close.Click += (_, _) => Close();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(name, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(details, 0, 2);
        layout.Controls.Add(grid, 0, 3);
        layout.Controls.Add(noteLabel, 0, 4);
        layout.Controls.Add(close, 0, 5);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }
}
