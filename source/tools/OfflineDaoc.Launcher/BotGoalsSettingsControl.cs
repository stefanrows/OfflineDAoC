using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

internal sealed class BotGoalsSettingsControl : UserControl
{
    private static readonly string[] PresetNames = ["Camlann 2003", "Peaceful", "Bloodbath", "Keep Wars", "Custom"];
    private static readonly string[] DangerNames = ["Mild", "Authentic", "Full Camlann"];
    private static readonly string[] ShapeNames = ["Fresh launch", "Established live server"];
    private static readonly string[] TypeNames = ["Leveler", "Casual", "Hybrid", "Hunter", "Roamer", "Keep warrior"];
    private static readonly string[] TypeToolTips =
    [
        "Weight for new autonomous bots assigned the Leveler behavior. Levelers focus on PvE leveling, usually hunt in groups, and only consider RvR from level 35, then rarely. A higher value makes this behavior more common; class and guild role also affect exact counts.",
        "Weight for new autonomous bots assigned the Casual behavior. Casuals favor PvE, take more town breaks, form fewer groups, and do not choose RvR on their own. A higher value makes this behavior more common; class and guild role also affect exact counts.",
        "Weight for new autonomous bots assigned the Hybrid behavior. Hybrids mix PvE leveling with RvR, considering RvR from level 20 and fighting more often during local evening hours. A higher value makes this behavior more common; class and guild role also affect exact counts.",
        "Weight for new autonomous bots assigned the Hunter behavior. Hunters favor RvR patrols and nearby, level-appropriate targets; the danger setting changes their patrol frequency and whether they may attack much lower-level players. A higher value makes this behavior more common; class and guild role also affect exact counts.",
        "Weight for new autonomous bots assigned the Roamer behavior. Roamers travel RvR routes and can form warband-sized groups from level 20. A higher value makes this behavior more common; class and guild role also affect exact counts.",
        "Weight for new autonomous bots assigned the Keep warrior behavior. At level 35 and above, a Keep warrior group leader can start a keep campaign with four or more group members. A higher value makes this behavior more common; class and guild role also affect exact counts.",
    ];
    private readonly string _path;
    private readonly Func<bool> _serverStopped;
    private readonly Func<int> _rosterCount;
    private readonly ToolTip _toolTips = new() { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 20000, ShowAlways = true };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _danger = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _worldShape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown _altHours = new() { Minimum = 0, Maximum = 720, Width = 90 };
    private readonly NumericUpDown _altCap = new() { Minimum = 0, Maximum = 100000, Width = 100 };
    private readonly TrackBar[] _sliders = new TrackBar[6];
    private readonly NumericUpDown[] _values = new NumericUpDown[6];
    private readonly Label _total = new() { AutoSize = true };
    private readonly Label _population = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly Button _save = new() { Text = "Save settings", AutoSize = true };
    private readonly Button _undo = new() { Text = "Undo edits", AutoSize = true };
    private readonly Button _defaults = new() { Text = "Restore defaults", AutoSize = true };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private BotGoalSettings _saved = BotGoalSettings.Defaults;
    private bool _loading, _loadFailed, _savedFile, _legacyMapping;
    private string _notice = string.Empty;
    public bool HasUnsavedChanges => _legacyMapping || _loadFailed || ReadValues() != _saved;

    public BotGoalsSettingsControl(string path, Func<bool> serverStopped)
        : this(path, serverStopped, () => 0) { }

    public BotGoalsSettingsControl(string path, Func<bool> serverStopped, Func<int> rosterCount)
    {
        _path = path;
        _serverStopped = serverStopped;
        _rosterCount = rosterCount;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoScroll = true;
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16),
        };
        body.Controls.Add(new Label { Text = "Server population", AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(3, 9, 3, 12),
            Text = "Choose the mix for new autonomous guilds and bots. Existing bot types, levels, items, and money stay saved. Changes apply on the next server start." });

        _preset.Items.AddRange(PresetNames);
        _danger.Items.AddRange(DangerNames);
        _worldShape.Items.AddRange(ShapeNames);
        body.Controls.Add(ChoiceRow("Preset", _preset,
            "Quick starting mixes: Camlann 2003 is balanced; Peaceful favors leveling and mild danger; Bloodbath favors Hunters and Roamers with Full Camlann danger; Keep Wars favors Keep warriors and Roamers. Selecting a preset replaces the six percentages and danger. Choose Custom to edit them. Saved bot types are retained."));
        var mixHeading = new Label { Text = "Player-type mix — total must equal 100%", AutoSize = true,
            Margin = new Padding(3, 13, 3, 4), ForeColor = DaocTheme.GoldLight };
        _toolTips.SetToolTip(mixHeading,
            "These percentages weight type assignments for autonomous bots that receive a new type. They must total 100%. Class and guild role also influence individual assignments, so exact counts can vary. Saved bot types are retained.");
        body.Controls.Add(mixHeading);
        for (int index = 0; index < TypeNames.Length; index++)
        {
            int captured = index;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
            var typeLabel = new Label { Text = TypeNames[index], Width = 130, Height = 35, TextAlign = ContentAlignment.MiddleLeft };
            var slider = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 390,
                Height = 42, AccessibleName = TypeNames[index] + " percentage" };
            var value = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 64,
                BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text,
                AccessibleName = TypeNames[index] + " exact percentage" };
            var percentLabel = new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            _toolTips.SetToolTip(typeLabel, TypeToolTips[index]);
            _toolTips.SetToolTip(slider, TypeToolTips[index]);
            _toolTips.SetToolTip(value, TypeToolTips[index]);
            _toolTips.SetToolTip(percentLabel, TypeToolTips[index]);
            _sliders[index] = slider;
            _values[index] = value;
            slider.ValueChanged += (_, _) => TypeValueChanged(captured, slider.Value, fromSlider: true);
            value.ValueChanged += (_, _) => TypeValueChanged(captured, (int)value.Value, fromSlider: false);
            row.Controls.Add(typeLabel);
            row.Controls.Add(slider);
            row.Controls.Add(value);
            row.Controls.Add(percentLabel);
            body.Controls.Add(row);
        }
        body.Controls.Add(_total);
        body.Controls.Add(ChoiceRow("Danger in leveling zones", _danger,
            "Controls autonomous Hunters' RvR patrol frequency and chance to attack much lower-level (grey-con) players. Mild cuts patrols to about one third and disables grey-target attacks. Authentic uses normal patrol frequency and excludes targets over 20 levels lower. Full Camlann increases patrols and grey-target chances, including rare attacks on targets over 20 levels lower. Presets set this value; choose Custom to change it."));
        body.Controls.Add(ChoiceRow("World shape", _worldShape,
            "Once saved, this is used when you add a crew through the launcher. Fresh launch creates every added bot at level 1; Established live server gives the new crew a spread of levels from 1 to 50. Existing bots keep their levels and progress, and Add Lv.50 remains a separate option."));
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(3, 8, 3, 3),
            Text = "Add crew creates level-1 bots for Fresh launch, or a spread from levels 1–50 for Established. Add Lv.50 remains an explicit option. Existing bots keep their progress." });
        body.Controls.Add(ChoiceRow("New alt every (hours; 0 = off)", _altHours));
        body.Controls.Add(ChoiceRow("Total roster cap for alts", _altCap));
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0),
            Text = "New level-1 alts join existing managed guilds while the server runs. The cap counts the full roster. The server reads these settings at startup." });
        body.Controls.Add(_population);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 13, 0, 9) };
        buttons.Controls.AddRange([_save, _undo, _defaults]);
        body.Controls.Add(buttons);
        body.Controls.Add(_status);
        Controls.Add(body);

        _preset.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _preset.SelectedIndex < 0) return;
            if (_preset.SelectedIndex != (int)PopulationPreset.Custom)
            {
                PopulationWorldShape shape = (PopulationWorldShape)Math.Max(0, _worldShape.SelectedIndex);
                SetValues(BotGoalSettings.ForPreset((PopulationPreset)_preset.SelectedIndex) with
                {
                    WorldShape = shape,
                    AltJoinIntervalHours = (int)_altHours.Value,
                    AltRosterCap = (int)_altCap.Value,
                });
            }
            _notice = string.Empty;
            UpdateState();
        };
        _danger.SelectedIndexChanged += (_, _) => { if (!_loading) { SetCustom(); UpdateState(); } };
        _worldShape.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateState(); };
        _altHours.ValueChanged += (_, _) => { if (!_loading) UpdateState(); };
        _altCap.ValueChanged += (_, _) => { if (!_loading) UpdateState(); };
        _save.Click += (_, _) => SaveSettings();
        _undo.Click += (_, _) => LoadSettings();
        _defaults.Click += (_, _) =>
        {
            SetValues(BotGoalSettings.Defaults);
            _notice = "Default preset loaded. Click Save settings to apply.";
            UpdateState();
        };
        LoadSettings();
        _poll.Tick += (_, _) => UpdateState();
        _poll.Start();
    }

    private Control ChoiceRow(string name, Control combo, string toolTip = "")
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        var label = new Label { Text = name, Width = 210, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        if (toolTip.Length > 0)
        {
            _toolTips.SetToolTip(label, toolTip);
            _toolTips.SetToolTip(combo, toolTip);
        }
        row.Controls.Add(label);
        row.Controls.Add(combo);
        return row;
    }

    private void TypeValueChanged(int index, int value, bool fromSlider)
    {
        if (_loading) return;
        _loading = true;
        if (fromSlider) _values[index].Value = value;
        else _sliders[index].Value = value;
        SetCustom();
        _loading = false;
        _notice = string.Empty;
        UpdateState();
    }

    private void SetCustom() => _preset.SelectedIndex = (int)PopulationPreset.Custom;

    private BotGoalSettings ReadValues() => new()
    {
        Preset = (PopulationPreset)Math.Max(0, _preset.SelectedIndex),
        Mix = new((int)_values[0].Value, (int)_values[1].Value, (int)_values[2].Value,
            (int)_values[3].Value, (int)_values[4].Value, (int)_values[5].Value),
        Danger = (PopulationDanger)Math.Max(0, _danger.SelectedIndex),
        WorldShape = (PopulationWorldShape)Math.Max(0, _worldShape.SelectedIndex),
        AltJoinIntervalHours = (int)_altHours.Value,
        AltRosterCap = (int)_altCap.Value,
    };

    private void SetValues(BotGoalSettings settings)
    {
        _loading = true;
        _preset.SelectedIndex = (int)settings.Preset;
        _danger.SelectedIndex = (int)settings.Danger;
        _worldShape.SelectedIndex = (int)settings.WorldShape;
        _altHours.Value = settings.AltJoinIntervalHours;
        _altCap.Value = settings.AltRosterCap;
        int[] mix = settings.Mix.Values;
        for (int index = 0; index < mix.Length; index++)
        {
            _sliders[index].Value = mix[index];
            _values[index].Value = mix[index];
        }
        _loading = false;
    }

    private void LoadSettings()
    {
        try
        {
            BotGoalLoadResult loaded = BotGoalSettings.LoadDetailed(_path);
            _saved = loaded.Settings;
            _savedFile = File.Exists(_path);
            _legacyMapping = loaded.MigratedFromV1;
            _loadFailed = false;
            _notice = _legacyMapping
                ? $"Version-1 goal percentages were mapped to {PresetNames[(int)loaded.MappedPreset]}. Review the mix, then Save to write version 2."
                : string.Empty;
            SetValues(_saved);
        }
        catch (Exception ex)
        {
            _loadFailed = true;
            _legacyMapping = false;
            SetValues(BotGoalSettings.Defaults);
            _notice = "Cannot read saved population settings: " + ex.Message + " Review and Save to repair the file.";
        }
        UpdateState();
    }

    private void SaveSettings()
    {
        try
        {
            BotGoalSettings values = ReadValues();
            values.Save(_path, _serverStopped);
            _saved = values;
            _savedFile = true;
            _loadFailed = _legacyMapping = false;
            _notice = "Saved. Population settings apply on the next server start.";
        }
        catch (Exception ex) { _notice = "Not saved: " + ex.Message; }
        UpdateState();
    }

    public void UpdateState()
    {
        bool stopped = _serverStopped();
        int total = ReadValues().Mix.Total;
        bool valid = total == 100;
        _total.Text = valid ? "Total: 100% — ready" : $"Total: {total}% — adjust to 100%";
        _total.ForeColor = valid ? Color.LightGreen : Color.Salmon;
        foreach (Control control in _sliders.Cast<Control>().Concat(_values))
            control.Enabled = stopped;
        _preset.Enabled = _danger.Enabled = _worldShape.Enabled = stopped;
        _altHours.Enabled = _altCap.Enabled = stopped;
        _save.Enabled = stopped && valid;
        _undo.Enabled = _defaults.Enabled = stopped;
        string recommendation;
        try
        {
            PopulationBenchmarks benchmarks = PopulationBenchmarks.Load(Path.Combine(
                Path.GetDirectoryName(_path)!, PopulationBenchmarks.FileName));
            int? suggested = benchmarks.Recommended(Environment.ProcessorCount,
                GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
            recommendation = suggested is > 0
                ? $"Measured total-roster recommendation for this PC: up to {suggested:N0} bots. You can choose another roster size."
                : suggested == 0
                    ? "The measured tiers exceeded the CPU or memory budget; use a roster below 500 bots."
                    : "Measured recommendation pending: run stable 500, 1,000, and 1,500-bot samples. Server logs record each sample after five minutes at that size.";
        }
        catch (Exception ex) { recommendation = "Could not read population measurements: " + ex.Message; }
        _population.Text = $"Generated roster: {_rosterCount():N0} bots. {recommendation}";
        _status.Text = !stopped ? "Locked: stop the server to change population settings." :
            _notice.Length > 0 ? _notice : !valid ? "Not saved. Adjust the player-type mix to exactly 100%." :
            HasUnsavedChanges ? "Unsaved changes — click Save settings before starting the server." :
            _savedFile ? "Saved settings ready. Start the server when ready." :
            "Default population settings shown. Save to make this preset explicit.";
        _status.ForeColor = stopped && valid && !_loadFailed ? DaocTheme.GoldLight : Color.Salmon;
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
