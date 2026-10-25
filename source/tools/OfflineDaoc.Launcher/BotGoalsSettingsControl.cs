using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

internal sealed class BotGoalsSettingsControl : UserControl
{
    private static readonly string[] PresetNames = ["Classic PvP", "Peaceful", "Bloodbath", "Keep Wars", "Custom"];
    private static readonly string[] DangerNames = ["Mild", "Authentic", "Aggressive"];
    private static readonly string[] ShapeNames = ["Fresh launch", "Established live server"];
    private static readonly string[] WorldModeNames = ["Normal", "Gothwaite PvP"];
    private const int GothwaiteWorldModeIndex = 1;
    private const string WorldModeToolTip = "Normal is the standard autonomous bot world. Gothwaite PvP moves every autonomous bot to the Isle of Glass around Caer Gothwaite for PvP only: no leveling, raids or sieges. The first switch saves a backup of the save database. Positions are restored when you switch back to Normal. Takes effect at the next server start.";
    private const string GothwaiteWorldModeHint = "All autonomous bots PvP around Caer Gothwaite. They do not level, raid or siege. Their positions are restored when you switch back to Normal. Takes effect at the next server start.";
    private const string OpenBgWorldModeHint = "Open BG runs with a reserved crew in this mode (level band: open BG tab).";
    private static readonly string[] TypeNames = ["Leveler", "Casual", "Hybrid", "Hunter", "Roamer", "Keep warrior"];
    private const string TypeMixHint = "\n\nHigher values give this type more weight for new bots. Existing saved types stay as they are until you apply the mix while the server is running. Keep all six sliders at 100% total.";
    private static readonly string[] TypeToolTips =
    [
        "Leveler: mainly PvE leveling, usually in groups. Rarely chooses PvP, and only from level 35. Increase this for more leveling parties." + TypeMixHint,
        "Casual: relaxed PvE, more town breaks and fewer groups. Does not choose PvP on its own, but can defend itself. Increase this for a quieter population." + TypeMixHint,
        "Hybrid: alternates PvE and PvP from level 20, with more PvP during local evening hours. Increase this for a mix of leveling and fighting." + TypeMixHint,
        "Hunter: seeks victims near level-appropriate leveling spots from level 10, alone or in crews of up to four. Increase this for more PvP encounters while leveling. Danger controls hunting tendency and attacks on much lower-level targets." + TypeMixHint,
        "Roamer: favors traveling PvP groups. Starts considering PvP at level 15 and favors eight-player parties from level 20. Increase this for more organized roaming group fights." + TypeMixHint,
        "Keep warrior: favors keep and siege warfare as levels rise. From level 35, a leader with at least four group members can start a keep campaign. Increase this for more keep-focused activity." + TypeMixHint,
    ];
    private readonly string _path;
    private readonly string _worldSpeedStatusPath;
    private readonly string _mixRequestPath;
    private readonly string _worldModePath;
    private readonly Func<bool> _serverStopped;
    private readonly Func<int> _rosterCount;
    private readonly Func<string>? _createWorldModeBackup;
    private readonly ToolTip _toolTips = new() { InitialDelay = 450, ReshowDelay = 100, AutoPopDelay = 20000, ShowAlways = true };
    private readonly ComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _danger = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _worldShape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _worldMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown _altHours = new() { Minimum = 0, Maximum = 720, Width = 90 };
    private readonly NumericUpDown _altCap = new() { Minimum = 0, Maximum = 100000, Width = 100 };
    private readonly TrackBar[] _sliders = new TrackBar[6];
    private readonly NumericUpDown[] _values = new NumericUpDown[6];
    private readonly Label _total = new() { AutoSize = true };
    private readonly Label _population = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly Label _worldModeHint = new() { AutoSize = true, MaximumSize = new Size(840, 0),
        Margin = new Padding(3, 0, 3, 6), Visible = false, Text = GothwaiteWorldModeHint };
    private readonly Label _openBgHint = new() { AutoSize = true, MaximumSize = new Size(840, 0),
        Margin = new Padding(3, 0, 3, 6), Visible = false, Text = OpenBgWorldModeHint };
    private readonly Button _save = DaocTheme.ToolButton("Save settings");
    private readonly Button _undo = DaocTheme.ToolButton("Undo edits");
    private readonly Button _defaults = DaocTheme.ToolButton("Restore defaults");
    private readonly Button _applyMix = DaocTheme.ToolButton("Apply mix to existing bots");
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private BotGoalSettings _saved = BotGoalSettings.Defaults;
    private bool _loading, _loadFailed, _savedFile, _legacyMapping;
    private bool _worldModeSaved, _worldModeLoadFailed;
    private string _notice = string.Empty;
    private string? _pendingMixRequestId;
    private DateTime _pendingMixRequestSinceUtc;
    private string _lastAppliedRequestId = string.Empty;
    /// <summary>
    /// True when the current save is an RvR world (marker Realm-1). Gothwaite PvP belongs to the PvP world only (owner
    /// decision 18): the switch is disabled and a save that turns it on is refused. MainForm reads the current save's
    /// marker; R5b passes the picked world. Evaluated at most every few seconds.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<bool>? IsRvrWorld { get; set; }
    private bool _rvrWorld;
    private DateTime _rvrWorldCheckedUtc = DateTime.MinValue;
    private bool RvrWorld
    {
        get
        {
            if (IsRvrWorld is null) return false;
            DateTime now = DateTime.UtcNow;
            if (now - _rvrWorldCheckedUtc > TimeSpan.FromSeconds(5))
            {
                try { _rvrWorld = IsRvrWorld(); }
                catch (Exception) { _rvrWorld = false; }
                _rvrWorldCheckedUtc = now;
            }
            return _rvrWorld;
        }
    }

    public bool HasUnsavedChanges => _legacyMapping || _loadFailed || _worldModeLoadFailed || ReadValues() != _saved || WorldModeSelected != _worldModeSaved;
    private bool WorldModeSelected => _worldMode.SelectedIndex == GothwaiteWorldModeIndex;

    public BotGoalsSettingsControl(string path, string worldSpeedStatusPath, string mixRequestPath,
        Func<bool> serverStopped, Func<int> rosterCount)
        : this(path, worldSpeedStatusPath, mixRequestPath, serverStopped, rosterCount, null) { }

    public BotGoalsSettingsControl(string path, string worldSpeedStatusPath, string mixRequestPath,
        Func<bool> serverStopped, Func<int> rosterCount, Func<string>? createWorldModeBackup)
        : this(path, worldSpeedStatusPath, mixRequestPath, serverStopped, rosterCount, createWorldModeBackup, null) { }

    public BotGoalsSettingsControl(string path, string worldSpeedStatusPath, string mixRequestPath,
        Func<bool> serverStopped, Func<int> rosterCount, Func<string>? createWorldModeBackup, string? worldModePath)
    {
        _path = path;
        _worldSpeedStatusPath = worldSpeedStatusPath;
        _mixRequestPath = mixRequestPath;
        // The Gothwaite switch is per save (gothwaite-pvp-camlann.json beside the Camlann save, docs/RVR_WORLDS.md).
        _worldModePath = worldModePath ?? Path.Combine(Path.GetDirectoryName(path)!, GothwaitePvpSettings.FileName);
        _serverStopped = serverStopped;
        _rosterCount = rosterCount;
        _createWorldModeBackup = createWorldModeBackup;
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        AutoScroll = true;
        foreach (ComboBox choice in new[] { _worldMode, _preset, _danger, _worldShape })
            DaocTheme.StyleInput(choice);
        foreach (NumericUpDown number in new[] { _altHours, _altCap })
            DaocTheme.StyleNumeric(number);
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16),
        };
        body.Controls.Add(new Label { Text = "Server population", AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold), ForeColor = DaocTheme.GoldLight });
        body.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0), Margin = new Padding(3, 9, 3, 12),
            Text = "Choose the mix for new autonomous guilds and bots. Existing bot types stay saved until you apply the mix while the server is running. Levels, items, money, and other character progress are preserved." });

        _worldMode.Items.AddRange(WorldModeNames);
        _preset.Items.AddRange(PresetNames);
        _danger.Items.AddRange(DangerNames);
        _worldShape.Items.AddRange(ShapeNames);
        body.Controls.Add(ChoiceRow("World mode", _worldMode, WorldModeToolTip));
        body.Controls.Add(_worldModeHint);
        body.Controls.Add(_openBgHint);
        body.Controls.Add(ChoiceRow("Preset", _preset,
            "Quick starting mixes: Classic PvP is balanced; Peaceful favors leveling and mild danger; Bloodbath favors Hunters and Roamers with Aggressive danger; Keep Wars favors Keep warriors and Roamers. Selecting a preset replaces the six percentages and danger. Choose Custom to edit them. Saving a preset changes future type assignments; use Apply mix to existing bots for a live rebalance."));
        var mixHeading = new Label { Text = "Player-type mix — total must equal 100%", AutoSize = true,
            Margin = new Padding(3, 13, 3, 4), ForeColor = DaocTheme.GoldLight };
        _toolTips.SetToolTip(mixHeading,
            "These percentages weight new type assignments and set the target mix for the entire saved autonomous roster when you apply it live. The server changes the minimum number of types needed; active bots wait for a safe task boundary. They must total 100%.");
        body.Controls.Add(mixHeading);
        for (int index = 0; index < TypeNames.Length; index++)
        {
            int captured = index;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
            var typeLabel = new Label { Text = TypeNames[index], Width = 130, Height = 35, TextAlign = ContentAlignment.MiddleLeft };
            var slider = new DaocTrackBar { Minimum = 0, Maximum = 100, Width = 390,
                Height = 42, AccessibleName = TypeNames[index] + " percentage" };
            var value = new NumericUpDown { Minimum = 0, Maximum = 100, Width = 64,
                AccessibleName = TypeNames[index] + " exact percentage" };
            DaocTheme.StyleNumeric(value);
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
            "Changes hunting tendency for existing and new Hunters after saving and restarting the server. Mild reduces hunting and disables attacks on much lower-level (grey-con) targets. Authentic uses normal hunting and excludes targets over 20 levels lower. Aggressive increases hunting and grey-target attacks, including rare attacks on targets over 20 levels lower. It does not convert other bot types into Hunters. Choose Custom to edit."));
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
        buttons.Controls.AddRange([_save, _undo, _defaults, _applyMix]);
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
        _worldMode.SelectedIndexChanged += (_, _) => { if (!_loading) { _notice = string.Empty; UpdateState(); } };
        _danger.SelectedIndexChanged += (_, _) => { if (!_loading) { SetCustom(); UpdateState(); } };
        _worldShape.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateState(); };
        _altHours.ValueChanged += (_, _) => { if (!_loading) UpdateState(); };
        _altCap.ValueChanged += (_, _) => { if (!_loading) UpdateState(); };
        _save.Click += (_, _) => SaveSettings();
        _applyMix.Click += (_, _) => ApplyMixToExistingBots();
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
        string worldModeNotice = LoadWorldMode();
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
        if (worldModeNotice.Length > 0)
            _notice = _notice.Length > 0 ? _notice + " " + worldModeNotice : worldModeNotice;
        UpdateState();
    }

    /// <summary>Reads the world mode switch. An unreadable file shows Normal and blocks start until a Save repairs it.</summary>
    private string LoadWorldMode()
    {
        try
        {
            GothwaitePvpSettings settings = GothwaitePvpSettings.Load(_worldModePath);
            _worldModeSaved = settings.Enabled;
            _worldModeLoadFailed = false;
            SetWorldMode(settings.Enabled);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _worldModeSaved = false;
            _worldModeLoadFailed = true;
            SetWorldMode(false);
            return "Cannot read saved world mode settings: " + ex.Message + " Review and Save to repair the file.";
        }
    }

    private void SetWorldMode(bool enabled)
    {
        bool wasLoading = _loading;
        _loading = true;
        _worldMode.SelectedIndex = enabled ? GothwaiteWorldModeIndex : 0;
        _loading = wasLoading;
    }

    private void SaveSettings()
    {
        try
        {
            BotGoalSettings values = ReadValues();
            values.Validate();
            bool worldModeOn = WorldModeSelected;
            if (worldModeOn && RvrWorld)
                throw new InvalidOperationException("Gothwaite PvP belongs to the PvP world (Camlann); the Realm vs Realm world never runs it.");
            bool writeWorldMode = worldModeOn || File.Exists(_worldModePath);
            string? backup = null;
            if (worldModeOn && !_worldModeSaved)
            {
                // The first switch into Gothwaite PvP backs up the save before anything is written (docs/GOTHWAITE_PVP_MODE.md 6.7).
                if (!_serverStopped()) throw new InvalidOperationException("Stop the server before changing the world mode.");
                backup = CreateWorldModeBackup();
            }
            values.Save(_path, _serverStopped);
            _saved = values;
            _savedFile = true;
            _loadFailed = _legacyMapping = false;
            _notice = "Saved. Population settings apply on the next server start.";
            if (writeWorldMode)
            {
                try
                {
                    SaveWorldMode(worldModeOn);
                    _notice = "Saved. Population and world mode settings apply on the next server start." +
                        (backup is null ? string.Empty : " Save backup: " + backup);
                }
                catch (Exception ex) { _notice = "World mode not saved: " + ex.Message + " Population settings were saved."; }
            }
        }
        catch (Exception ex) { _notice = "Not saved: " + ex.Message; }
        UpdateState();
    }

    /// <summary>Writes the mode switch and keeps every other field of an existing settings file.</summary>
    private void SaveWorldMode(bool enabled)
    {
        GothwaitePvpSettings basis;
        try { basis = GothwaitePvpSettings.Load(_worldModePath); }
        catch (Exception) { basis = GothwaitePvpSettings.Defaults; } // unreadable: its error was shown, and this save repairs the file
        (basis with { Enabled = enabled }).Save(_worldModePath, _serverStopped);
        _worldModeSaved = enabled;
        _worldModeLoadFailed = false;
    }

    /// <summary>
    /// Save-database backup for the first switch into Gothwaite PvP (MainForm passes
    /// <see cref="SaveDatabaseBackup.Create"/>). Returns the backup folder and throws on failure, so nothing is written.
    /// </summary>
    private string CreateWorldModeBackup() =>
        _createWorldModeBackup?.Invoke() ??
        throw new InvalidOperationException("Gothwaite PvP needs a save backup first, and no backup routine is configured. Nothing was changed.");

    private void ApplyMixToExistingBots()
    {
        try
        {
            if (_serverStopped())
                throw new InvalidOperationException("Start the server before applying the mix to existing bots.");
            BotGoalSettings values = ReadValues();
            values.Validate();
            WorldSpeedStatus? live = WorldSpeedProtocol.ReadFreshStatus(
                _worldSpeedStatusPath, DateTime.UtcNow, out string? unavailable);
            if (live == null)
                throw new InvalidOperationException(unavailable ?? "Fresh live server status is required.");

            string statusPath = Path.Combine(Path.GetDirectoryName(_mixRequestPath)!, PopulationTypeMixProtocol.StatusFileName);
            PopulationTypeMixStatus? previous = PopulationTypeMixProtocol.ReadStatus(statusPath);
            if (previous?.State.Equals("pending", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("A population mix is still being applied. Wait for it to finish before sending another.");

            PopulationTypeMixRequest request = PopulationTypeMixProtocol.WriteRequest(
                _mixRequestPath, live.SessionId, values.Mix, DateTime.UtcNow);
            _pendingMixRequestId = request.RequestId;
            _pendingMixRequestSinceUtc = DateTime.UtcNow;
            _notice = "Mix request sent. Waiting for the server to accept it.";
        }
        catch (Exception exception)
        {
            _pendingMixRequestId = null;
            _notice = "Mix not applied: " + exception.Message;
        }
        UpdateState();
    }

    public void UpdateState()
    {
        bool stopped = _serverStopped();
        DateTime nowUtc = DateTime.UtcNow;
        WorldSpeedStatus? liveStatus = stopped ? null : WorldSpeedProtocol.ReadFreshStatus(
            _worldSpeedStatusPath, nowUtc, out _);
        string statusPath = Path.Combine(Path.GetDirectoryName(_mixRequestPath)!, PopulationTypeMixProtocol.StatusFileName);
        PopulationTypeMixStatus? mixStatus = PopulationTypeMixProtocol.ReadStatus(statusPath);
        bool matchingRequest = _pendingMixRequestId != null && mixStatus?.RequestId == _pendingMixRequestId;
        if (_pendingMixRequestId != null && matchingRequest && mixStatus != null)
        {
            if (mixStatus.State.Equals("applied", StringComparison.OrdinalIgnoreCase))
            {
                _saved = _saved with { Preset = PopulationPreset.Custom, Mix = mixStatus.Mix };
                _savedFile = true;
                _loadFailed = _legacyMapping = false;
                _pendingMixRequestId = null;
                _lastAppliedRequestId = mixStatus.RequestId;
                SetValues(_saved);
                _notice = "Applied to the entire saved autonomous roster. Character progress was preserved.";
            }
            else if (mixStatus.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                _pendingMixRequestId = null;
                _notice = "Mix apply failed: " + mixStatus.Error;
            }
        }
        else if (_pendingMixRequestId != null && nowUtc - _pendingMixRequestSinceUtc > TimeSpan.FromSeconds(15))
        {
            _pendingMixRequestId = null;
            _notice = "The server did not acknowledge the mix request. Review status and try again.";
        }
        bool mixPending = mixStatus?.State.Equals("pending", StringComparison.OrdinalIgnoreCase) == true;
        bool mixOperationPending = mixPending || _pendingMixRequestId != null;
        if (_pendingMixRequestId == null && mixStatus?.RequestId != _lastAppliedRequestId &&
            mixStatus?.State.Equals("applied", StringComparison.OrdinalIgnoreCase) == true &&
            liveStatus?.SessionId == mixStatus.SessionId)
        {
            _saved = _saved with { Preset = PopulationPreset.Custom, Mix = mixStatus.Mix };
            _savedFile = true;
            _loadFailed = _legacyMapping = false;
            _lastAppliedRequestId = mixStatus.RequestId;
            SetValues(_saved);
            _notice = "Applied to the entire saved autonomous roster. Character progress was preserved.";
        }
        int total = ReadValues().Mix.Total;
        bool valid = total == 100;
        _total.Text = valid ? "Total: 100% — ready" : $"Total: {total}% — adjust to 100%";
        _total.ForeColor = valid ? Color.LightGreen : Color.Salmon;
        foreach (Control control in _sliders.Cast<Control>().Concat(_values))
            control.Enabled = !mixOperationPending && (stopped || liveStatus != null);
        _preset.Enabled = _danger.Enabled = _worldShape.Enabled = stopped && !mixOperationPending;
        // An RvR save never offers Gothwaite: a stale switch shows Normal, and Save writes it off.
        if (RvrWorld && WorldModeSelected)
            SetWorldMode(false);
        _worldMode.Enabled = stopped && !mixOperationPending && !RvrWorld;
        _worldModeHint.Visible = _openBgHint.Visible = WorldModeSelected;
        _altHours.Enabled = _altCap.Enabled = stopped && !mixOperationPending;
        _save.Enabled = stopped && valid && !mixOperationPending;
        _undo.Enabled = _defaults.Enabled = stopped && !mixOperationPending;
        _applyMix.Enabled = !stopped && liveStatus != null && valid && !mixPending && _pendingMixRequestId == null;
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
        string liveMixSummary = string.Empty;
        if (mixStatus != null)
        {
            string[] names = ["Leveler", "Casual", "Hybrid", "Hunter", "Roamer", "Keep warrior"];
            string counts = string.Join(" · ", names.Select((name, index) =>
                $"{name} {mixStatus.CurrentCounts.ElementAtOrDefault(index)}/{mixStatus.TargetCounts.ElementAtOrDefault(index)}"));
            liveMixSummary = mixStatus.State.ToLowerInvariant() switch
            {
                "pending" => $"{(stopped ? "Pending; this mix will resume when the server starts" : "Applying to the full saved autonomous roster")} ({mixStatus.RosterCount:N0} bots). {counts}. Active bots waiting at safe task boundaries: {mixStatus.ActivePending:N0}. " +
                    (string.IsNullOrWhiteSpace(mixStatus.Error) ? string.Empty : mixStatus.Error),
                "applied" => $"Last mix applied to the full saved autonomous roster ({mixStatus.RosterCount:N0} bots). {counts}.",
                "failed" => "Last mix apply failed: " + mixStatus.Error,
                _ => string.Empty,
            };
        }
        _status.Text = mixPending && liveMixSummary.Length > 0 ? liveMixSummary :
            !stopped && liveStatus == null ? "Waiting for fresh live server status before applying a mix." :
            _notice.Length > 0 ? _notice :
            liveMixSummary.Length > 0 ? liveMixSummary :
            !stopped ? "Adjust the six percentages, then apply the mix to the saved autonomous roster." :
            !valid ? "Not saved. Adjust the player-type mix to exactly 100%." :
            HasUnsavedChanges ? "Unsaved changes — click Save settings before starting the server." :
            _savedFile ? "Saved settings ready. Start the server when ready." :
            "Default population settings shown. Save to make this preset explicit.";
        bool statusError = _notice.StartsWith("Not saved", StringComparison.OrdinalIgnoreCase) ||
                           _notice.StartsWith("Mix not applied", StringComparison.OrdinalIgnoreCase) ||
                           _notice.StartsWith("Mix apply failed", StringComparison.OrdinalIgnoreCase) ||
                           _notice.StartsWith("The server did not acknowledge", StringComparison.OrdinalIgnoreCase) ||
                           _notice.StartsWith("Cannot read", StringComparison.OrdinalIgnoreCase) ||
                           _notice.StartsWith("World mode not saved", StringComparison.OrdinalIgnoreCase) ||
                           mixPending && !string.IsNullOrWhiteSpace(mixStatus?.Error);
        _status.ForeColor = valid && !_loadFailed && !_worldModeLoadFailed && !statusError ? DaocTheme.GoldLight : Color.Salmon;
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
