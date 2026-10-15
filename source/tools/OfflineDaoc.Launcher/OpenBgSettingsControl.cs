namespace OfflineDaoc.Launcher;

/// <summary>
/// Launcher tab "open BG": the open BG switch, its seven numbers and each realm's own crew (the same open_bg_* rows
/// and ranges as /bgsetup). Saving writes the server property rows and is possible only while the server is stopped;
/// while it runs the tab shows the live state from open-bg-status.json, or points to /bgsetup in game.
/// </summary>
internal sealed class OpenBgSettingsControl : UserControl
{
    private static readonly string[] NumberFields = ["groups8", "small", "solos", "max_bots", "cycle_min", "cycle_max", "band_min", "band_max"];
    private readonly string _database;
    private readonly string _statusPath;
    private readonly Func<bool> _serverStopped;
    private readonly ToolTip _toolTips = new() { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 20000, ShowAlways = true };
    private readonly CheckBox _enabled = new() { AutoSize = true, Text = "Open BG on" };
    private readonly CheckBox _drift = new() { AutoSize = true, Text = "Player drift" };
    private readonly Dictionary<string, NumericUpDown> _numbers = new();
    private readonly Dictionary<string, TextBox> _units = new();
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = "Mode" };
    private readonly ComboBox _peace = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, AccessibleName = "Landing peace" };
    private readonly TextBox _presets = new() { Width = 420, BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text, AccessibleName = "Group presets",
        PlaceholderText = "off" };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly Label _live = new() { AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(3, 12, 3, 3) };
    private readonly Button _save = DaocTheme.ToolButton("Save");
    private readonly Button _undo = DaocTheme.ToolButton("Undo edits");
    private readonly Button _defaults = DaocTheme.ToolButton("Restore defaults");
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private OpenBgSettings _saved = OpenBgSettings.Defaults;
    private bool _loading, _loadFailed, _wasStopped = true;
    private string _notice = string.Empty;

    // A failed read is shown in red but never blocks a server start.
    public bool HasUnsavedChanges => ReadValues() != _saved;

    public OpenBgSettingsControl(string database, string statusPath, Func<bool> serverStopped)
    {
        _database = database;
        _statusPath = statusPath;
        _serverStopped = serverStopped;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoScroll = true;
        DaocTheme.StyleCheckBox(_enabled);
        DaocTheme.StyleCheckBox(_drift);
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16),
        };
        body.Controls.Add(new Label { Text = "open BG — Thidranki", AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(3, 9, 3, 12),
            Text = "A permanently open battleground for levels 1-50, reached through the porters' [open BG] line. Its own bot crew per realm comes from the world bots inside the crew level band (default 40-50) and is relieved in part every 20-30 minutes. Bots keep their levels, gear and realm points." });

        body.Controls.Add(SwitchRow(_enabled, OpenBgSettings.Field("enabled")));
        foreach (string name in NumberFields)
        {
            OpenBgField field = OpenBgSettings.Field(name);
            var number = new NumericUpDown { Minimum = field.Min, Maximum = field.Max, Width = 80, AccessibleName = field.Label };
            DaocTheme.StyleNumeric(number);
            _numbers[name] = number;
            number.ValueChanged += (_, _) => Edited();
            body.Controls.Add(NumberRow(number, field));
            if (name != "solos") continue;
            // Each realm's own crew right below the three per-realm values it overrides.
            foreach (OpenBgUnitsField units in OpenBgSettings.UnitFields)
            {
                var text = new TextBox { Width = 80, BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text, AccessibleName = units.Label,
                    PlaceholderText = "global" };
                _units[units.Name] = text;
                text.TextChanged += (_, _) => Edited();
                body.Controls.Add(UnitsRow(text, units));
            }
        }
        body.Controls.Add(SwitchRow(_drift, OpenBgSettings.Field("drift")));
        // Mode, landing peace and presets: "serverpvp"/"perrealm" and "auto"/"all"/"realm" shown as words, stored as the rows hold them.
        _mode.Items.AddRange(["Server PvP", "Per realm"]);
        _peace.Items.AddRange(["Auto (by mode)", "All realms", "Own realm only"]);
        foreach (ComboBox box in new[] { _mode, _peace })
        {
            DaocTheme.StyleInput(box);
            box.SelectedIndexChanged += (_, _) => Edited();
        }
        _presets.TextChanged += (_, _) => Edited();
        body.Controls.Add(TextRow(_mode, OpenBgSettings.TextFields[0]));
        body.Controls.Add(TextRow(_peace, OpenBgSettings.TextFields[1]));
        body.Controls.Add(TextRow(_presets, OpenBgSettings.TextFields[2]));
        _enabled.CheckedChanged += (_, _) => Edited();
        _drift.CheckedChanged += (_, _) => Edited();

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 13, 0, 9) };
        buttons.Controls.AddRange([_save, _undo, _defaults]);
        body.Controls.Add(buttons);
        body.Controls.Add(_status);
        body.Controls.Add(_live);
        Controls.Add(body);

        _save.Click += (_, _) => SaveSettings();
        _undo.Click += (_, _) => LoadSettings();
        _defaults.Click += (_, _) =>
        {
            SetValues(OpenBgSettings.Defaults);
            _notice = "Defaults loaded. Click Save to write them.";
            UpdateState();
        };
        LoadSettings();
        // The server-process check is not free; poll only while the tab is shown.
        _poll.Tick += (_, _) => { if (Visible) UpdateState(); };
        _poll.Start();
    }

    private Control SwitchRow(CheckBox box, OpenBgField field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        box.Margin = new Padding(3, 3, 3, 0);
        _toolTips.SetToolTip(box, field.Help);
        row.Controls.Add(box);
        row.Controls.Add(HelpLabel(field));
        return row;
    }

    private Control NumberRow(NumericUpDown number, OpenBgField field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        var label = new Label { Text = field.Label, Width = 190, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        _toolTips.SetToolTip(label, field.Help);
        _toolTips.SetToolTip(number, field.Help);
        row.Controls.Add(label);
        row.Controls.Add(number);
        var help = HelpLabel(field);
        help.MaximumSize = new Size(560, 0);
        help.Padding = new Padding(0, 6, 0, 0);
        row.Controls.Add(help);
        return row;
    }

    private Control UnitsRow(TextBox text, OpenBgUnitsField field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        var label = new Label { Text = field.Label, Width = 190, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        _toolTips.SetToolTip(label, field.Help);
        _toolTips.SetToolTip(text, field.Help);
        row.Controls.Add(label);
        row.Controls.Add(text);
        row.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = DaocTheme.Parchment, Text = field.Help,
            Margin = new Padding(3, 0, 3, 2), Padding = new Padding(0, 6, 0, 0),
        });
        return row;
    }

    private Control TextRow(Control input, OpenBgTextField field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 2) };
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var label = new Label { Text = field.Label, Width = 190, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        _toolTips.SetToolTip(label, field.Help);
        _toolTips.SetToolTip(input, field.Help);
        line.Controls.Add(label);
        line.Controls.Add(input);
        row.Controls.Add(line);
        row.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = DaocTheme.Parchment, Text = field.Help, Margin = new Padding(3, 0, 3, 2),
        });
        return row;
    }

    private static readonly string[] ModeValues = ["serverpvp", "perrealm"];
    private static readonly string[] PeaceValues = ["", "all", "realm"];

    // The presets as typed: a readable list in its stored form, anything else as it stands (it blocks Save).
    private string PresetsText() =>
        OpenBgSettings.TryNormalizePresets(_presets.Text, out string normalized) ? normalized : _presets.Text;

    private string? InvalidPresets() => OpenBgSettings.TryNormalizePresets(_presets.Text, out _) ? null :
        "Group presets: use name:count entries such as std8:2,caster8:1, or leave it empty.";

    // The realm crews as typed: a readable text in its stored form, anything else as it stands (it blocks Save).
    private string UnitsText(string name) =>
        OpenBgSettings.TryNormalizeUnits(_units[name].Text, out string normalized) ? normalized : _units[name].Text;

    private string? InvalidUnits() => OpenBgSettings.UnitFields
        .Where(field => !OpenBgSettings.TryNormalizeUnits(_units[field.Name].Text, out _))
        .Select(field => $"{field.Label}: use 8-man/small/solos such as 1/2/4, or leave it empty for the global values.")
        .FirstOrDefault();

    private static Label HelpLabel(OpenBgField field) => new()
    {
        AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = DaocTheme.Parchment,
        Text = field.Help, Margin = new Padding(3, 0, 3, 2),
    };

    private void Edited()
    {
        if (_loading) return;
        _notice = string.Empty;
        UpdateState();
    }

    private OpenBgSettings ReadValues() => new(
        _enabled.Checked,
        (int)_numbers["groups8"].Value,
        (int)_numbers["small"].Value,
        (int)_numbers["solos"].Value,
        (int)_numbers["max_bots"].Value,
        (int)_numbers["cycle_min"].Value,
        (int)_numbers["cycle_max"].Value,
        _drift.Checked)
    {
        BandMin = (int)_numbers["band_min"].Value,
        BandMax = (int)_numbers["band_max"].Value,
        AlbionUnits = UnitsText("albion"),
        MidgardUnits = UnitsText("midgard"),
        HiberniaUnits = UnitsText("hibernia"),
        Mode = ModeValues[Math.Max(0, _mode.SelectedIndex)],
        LandingPeace = PeaceValues[Math.Max(0, _peace.SelectedIndex)],
        Presets = PresetsText(),
    };

    private void SetValues(OpenBgSettings settings)
    {
        OpenBgSettings value = settings.Clamped();
        _loading = true;
        _enabled.Checked = value.Enabled;
        _drift.Checked = value.Drift;
        _numbers["groups8"].Value = value.Groups8;
        _numbers["small"].Value = value.Small;
        _numbers["solos"].Value = value.Solos;
        _numbers["max_bots"].Value = value.MaxBots;
        _numbers["cycle_min"].Value = value.CycleMin;
        _numbers["cycle_max"].Value = value.CycleMax;
        _numbers["band_min"].Value = value.BandMin;
        _numbers["band_max"].Value = value.BandMax;
        foreach (OpenBgUnitsField field in OpenBgSettings.UnitFields)
            _units[field.Name].Text = value.UnitsOf(field.Name);
        _mode.SelectedIndex = Math.Max(0, Array.IndexOf(ModeValues, value.Mode));
        _peace.SelectedIndex = Math.Max(0, Array.IndexOf(PeaceValues, value.LandingPeace));
        _presets.Text = value.Presets;
        _loading = false;
    }

    private void LoadSettings()
    {
        try
        {
            _saved = OpenBgSettings.Load(_database);
            _loadFailed = false;
            _notice = string.Empty;
        }
        catch (Exception ex)
        {
            _saved = OpenBgSettings.Defaults;
            _loadFailed = true;
            _notice = "Cannot read the saved open BG setup: " + ex.Message;
        }
        SetValues(_saved);
        UpdateState();
    }

    private void SaveSettings()
    {
        try
        {
            OpenBgSettings values = ReadValues();
            if (values.CycleMax < values.CycleMin)
                throw new InvalidOperationException("The longest cycle must not be shorter than the shortest cycle.");
            if (values.BandMax < values.BandMin)
                throw new InvalidOperationException("The highest crew level must not be below the lowest crew level.");
            if (InvalidUnits() is string invalid)
                throw new InvalidOperationException(invalid);
            if (InvalidPresets() is string invalidPresets)
                throw new InvalidOperationException(invalidPresets);
            OpenBgSettings.Save(_database, values, _serverStopped);
            _saved = values.Clamped();
            _loadFailed = false;
            SetValues(_saved);
            _notice = "Saved. The server reads the open BG setup at its next start.";
        }
        catch (Exception ex) { _notice = "Not saved: " + ex.Message; }
        UpdateState();
    }

    public void UpdateState()
    {
        bool stopped = _serverStopped();
        // /bgsetup may have changed the rows while the server ran: reread once it has stopped, unless edits are pending.
        if (stopped && !_wasStopped && !HasUnsavedChanges)
        {
            _wasStopped = true;
            LoadSettings();
            return;
        }
        _wasStopped = stopped;
        OpenBgSettings values = ReadValues();
        string? invalidUnits = InvalidUnits();
        string? invalidPresets = InvalidPresets();
        bool valid = values.CycleMax >= values.CycleMin && values.BandMax >= values.BandMin && invalidUnits == null && invalidPresets == null;
        _enabled.Enabled = _drift.Enabled = stopped;
        foreach (NumericUpDown number in _numbers.Values) number.Enabled = stopped;
        foreach (TextBox units in _units.Values) units.Enabled = stopped;
        _mode.Enabled = _peace.Enabled = _presets.Enabled = stopped;
        _save.Enabled = stopped && valid;
        _undo.Enabled = _defaults.Enabled = stopped;
        _status.Text = !stopped ? "The server is running: these fields are read-only. Change the setup live in game with /bgsetup (window) or /bgsetup <key> <value>." :
            _notice.Length > 0 ? _notice :
            invalidUnits != null ? invalidUnits :
            invalidPresets != null ? invalidPresets :
            !valid ? "The longest cycle must not be shorter than the shortest cycle." :
            HasUnsavedChanges ? "Unsaved changes — click Save before starting the server." :
            "Saved setup shown. The server reads it at its next start.";
        bool error = !valid || _loadFailed || _notice.StartsWith("Not saved", StringComparison.OrdinalIgnoreCase) ||
            _notice.StartsWith("Cannot read", StringComparison.OrdinalIgnoreCase);
        _status.ForeColor = error && stopped ? Color.Salmon : DaocTheme.GoldLight;
        _live.Visible = !stopped;
        if (!stopped)
            _live.Text = OpenBgLiveStatus.Read(_statusPath, DateTime.UtcNow) ?? OpenBgLiveStatus.Fallback;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Dispose();
            _toolTips.Dispose();
        }
        base.Dispose(disposing);
    }
}
