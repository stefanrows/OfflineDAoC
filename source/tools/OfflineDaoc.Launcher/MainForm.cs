using System.Data.SQLite;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

internal sealed partial class MainForm : Form
{
    internal const string DisplayVersion = "0.336.0";
    internal const int AutoRefreshMilliseconds = 5 * 60 * 1000;
    internal const int RvrSnapshotRefreshMilliseconds = 30 * 1000;
    internal const int LiveBotSnapshotMaxAgeMilliseconds = 20_000;
    internal const int LiveBotSnapshotWaitMilliseconds = 3_000;
    internal const int ServerReadinessPollMilliseconds = 500;
    private readonly string _root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly string _database;
    private readonly string _serverDirectory;
    // The save's per-world side files (docs/RVR_WORLDS.md); R5b points _database at the chosen world.
    private readonly WorldFiles _worldFiles;
    private readonly string _worldSpeedRequestPath;
    private readonly string _worldSpeedStatusPath;
    private readonly string _serverExecutable;
    private readonly string _clientDirectory;
    private readonly string _clientConnector;
    private readonly string _logsDirectory;
    // Test-only seam: when set, PersistXpRate uses this instead of probing the
    // real server port/process, so its unit test does not depend on whether an
    // actual game server happens to be running on the machine. Production code
    // never assigns this field, so live behavior is unchanged.
    private bool? _persistXpRateServerRunningOverride;
    private readonly BindingSource _botSource = new();
    private readonly List<BotRow> _bots = [];
    private readonly BindingSource _auctionSource = new();
    private readonly List<AuctionRow> _auctions = [];
    private readonly List<GroupRow> _groups = [];
    private readonly Label _serverState = CardValue();
    private readonly Label _onlineValue = CardValue();
    private readonly Label _albionValue = CardValue();
    private readonly Label _midgardValue = CardValue();
    private readonly Label _hiberniaValue = CardValue();
    private readonly Label _performanceValue = CardValue();
    private readonly Label _footer = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Muted,
        BackColor = DaocTheme.StoneDark,
        Font = new Font("Georgia", 8f),
        Padding = new Padding(8, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Label _autoRefreshCountdownCaption = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Muted,
        BackColor = DaocTheme.StoneDark,
        Font = new Font("Georgia", 8f),
        Text = "Auto-refresh in",
        TextAlign = ContentAlignment.MiddleRight,
    };
    private readonly Label _autoRefreshCountdownValue = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        BackColor = DaocTheme.StoneDark,
        Font = new Font("Georgia", 8f, FontStyle.Bold),
        Text = "5:00",
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly ComboBox _realmFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly TextBox _search = new() { PlaceholderText = "Search name, type, guild, zone or activity", Width = 200 };
    private readonly CheckBox _onlineOnly = new()
    {
        Text = "ONLINE ONLY",
        AutoSize = false,
        Size = new Size(118, 31),
        Margin = new Padding(8, 0, 0, 0),
        Padding = new Padding(4, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        ForeColor = DaocTheme.GoldLight,
        BackColor = DaocTheme.Panel,
        Font = new Font("Georgia", 8.25f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Cursor = Cursors.Hand,
        AccessibleName = "Show online bots only",
    };
    private readonly CheckBox _showCompanions = new()
    {
        Text = "COMPANIONS",
        AutoSize = false,
        Size = new Size(118, 31),
        Margin = new Padding(8, 0, 0, 0),
        Padding = new Padding(4, 0, 0, 0),
        FlatStyle = FlatStyle.Flat,
        ForeColor = DaocTheme.GoldLight,
        BackColor = DaocTheme.Panel,
        Font = new Font("Georgia", 8.25f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        Cursor = Cursors.Hand,
        AccessibleName = "Show companions",
    };
    private readonly TextBox _auctionSearch = new() { PlaceholderText = "Search item, seller or state", Width = 300 };
    private readonly DaocTabControl _auctionRealmTabs = new() { Width = 280, Height = 29, StripColor = Color.FromArgb(48, 38, 26) };
    private readonly TextBox _groupSearch = new() { PlaceholderText = "Search bot, class, zone or task", Width = 340 };
    private readonly ComboBox _groupRealm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ComboBox _playerXpRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly ComboBox _botXpRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly ComboBox _playerRpRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly ComboBox _botRpRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly ComboBox _worldSpeedMultiplier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 108 };
    private readonly Label _worldSpeedStatusText = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Label _worldSpeedTelemetryText = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Label _worldSpeedErrorText = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Label _worldSpeedClockText = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly CheckBox _makeMeGm = new() { Text = "Make Me a GM", AutoSize = true, ForeColor = DaocTheme.Parchment, Font = new Font("Georgia", 9f, FontStyle.Bold) };
    private const string RealmRankNameplateKey = "show_realm_rank_nameplates";
    private const string RealmRankNameplateDescription = "Show realm rank (RR xLy) after the names of companions and players on nameplates. Presentation only.";
    private const string BotRealmRankNameplateKey = "show_bot_realm_rank_nameplates";
    private const string BotRealmRankNameplateDescription = "Show realm rank (RR xLy) after the names of autonomous world bots on nameplates. Presentation only.";
    private readonly CheckBox _realmRankNameplates = new()
    {
        Text = "Realm rank on companion and player nameplates",
        AutoSize = true,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 9f, FontStyle.Bold),
    };
    private readonly Label _realmRankNameplatesNote = XpRateDescription("Adds RR xLy after companion and player names. Applies at the next server startup.");
    private readonly CheckBox _botRealmRankNameplates = new()
    {
        Text = "Realm rank on autonomous bot nameplates",
        AutoSize = true,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 9f, FontStyle.Bold),
    };
    private readonly Label _botRealmRankNameplatesNote = XpRateDescription("On by default. Adds RR xLy after autonomous bot names. Applies at the next server startup.");
    // Trials of Atlantis block below the rates (ToaSettings): three switches and three numbers, one row each.
    private readonly CheckBox _toaAtlantisOpen = ToaCheckBox("atlantis");
    private readonly CheckBox _toaLoot = ToaCheckBox("loot");
    private readonly CheckBox _toaDataPacks = ToaCheckBox("data_packs");
    private readonly Dictionary<string, NumericUpDown> _toaNumbers = new();
    private readonly Dictionary<string, Label> _toaNotes = new();
    // A changed number is saved by the one-second display clock once it has rested 800 ms, so arrow clicks
    // and typing write one row, not one per step (no extra timer).
    private static readonly TimeSpan ToaNumberSaveDelay = TimeSpan.FromMilliseconds(800);
    private readonly HashSet<string> _toaPendingNumbers = [];
    private DateTime _toaLastNumberEditUtc;
    private string? _toaNotice;
    private readonly Label _toaStatus = new()
    {
        AutoSize = true,
        ForeColor = DaocTheme.Muted,
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        Text = "Trials of Atlantis settings are loaded from the saved server settings.",
    };
    private readonly Label _xpSettingsStatus = new()
    {
        AutoSize = true,
        ForeColor = DaocTheme.Muted,
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        Text = "Rates are loaded from the saved server settings.",
    };
    private readonly Label _groupSearchStatus = new() { AutoSize = true, ForeColor = DaocTheme.Muted, Margin = new Padding(10, 7, 0, 0) };
    private readonly BindingSource _groupSource = new();
    private readonly DataGridView _groupsGrid = new();
    private readonly DataGridView _groupMembersGrid = new();
    private readonly Label _groupDetailsTitle = new();
    private readonly Label _groupDetailsSummary = new();
    private readonly System.Windows.Forms.Timer _displayClock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _autoRefresh = new() { Interval = AutoRefreshMilliseconds };
    private readonly System.Windows.Forms.Timer _serverReadinessPoll = new() { Interval = ServerReadinessPollMilliseconds };
    private readonly ToolTip _helpTip = new()
    {
        AutomaticDelay = 250,
        AutoPopDelay = 12_000,
        ReshowDelay = 100,
        ShowAlways = true,
    };
    private readonly DataGridView _grid = new();
    private readonly DataGridView _auctionGrid = new();
    private readonly DataGridView _rvrObjectivesGrid = new();
    private readonly Label _rvrUpdated = new();
    private RvrWorldSnapshot? _rvrWorld;
    private bool _rvrServerRunning;
    private string _groupSortProperty = "Realm";
    private bool _groupSortAscending = true;
    private bool _updatingGroupGrid;
    private Button? _refreshButton;
    private Button? _refreshExchangeButton;
    private Button? _deleteBotButton;
    private Button? _deleteOrphansButton;
    private Button? _deleteAllBotsButton;
    private readonly List<Button> _realmGenerateButtons = [];
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _playButton;
    private readonly Button _heraldButton;
    private readonly Button _uiPresetButton;
    private Process? _serverProcess;
    private RollingServerLog? _serverLog;
    private bool _stoppingServer;
    private bool _worldReady;
    private bool _refreshing;
    private bool _livePollInFlight;
    private Icon? _windowIcon;
    private bool _refreshingExchange;
    private bool _loadingXpRates;
    private bool _savingXpRates;
    private bool _loadingWorldSpeed;
    private WorldSpeedStatus? _worldSpeedSnapshot;
    private string? _worldSpeedUnavailableReason;
    private string? _worldSpeedLocalError;
    private string? _worldSpeedSessionId;
    private int? _pendingWorldSpeedMultiplier;
    private DateTime _pendingWorldSpeedSinceUtc;
    private bool? _lastWorldSpeedServerRunning;
    private bool _stoppedSimulationClockLoaded;
    private WorldSimulationClockRead? _savedSimulationClock;
    private DateTime? _simulationUtc;
    private bool _generatingBot;
    private DateTime _nextAutoRefreshUtc;
    private DateTime _nextRvrSnapshotRefreshUtc;
    private string _dashboardFooterSummary = string.Empty;
    private string _botSortProperty = "Name";
    private bool _botSortAscending = true;
    private string _auctionSortProperty = "ItemName";
    private bool _auctionSortAscending = true;
    private ExchangeSalesForm? _salesLedger;
    private HeraldForm? _herald;

    public MainForm()
    {
        _database = Path.Combine(_root, "data", "opendaoc.sqlite3.db");
        _serverDirectory = Path.Combine(_root, "server");
        _worldFiles = WorldFiles.ForDatabase(_serverDirectory, _database);
        _worldSpeedRequestPath = Path.Combine(_serverDirectory, WorldSpeedProtocol.RequestFileName);
        _worldSpeedStatusPath = Path.Combine(_serverDirectory, WorldSpeedProtocol.StatusFileName);
        _serverExecutable = Path.Combine(_serverDirectory, "CoreServer.exe");
        string officialClientDirectory = Path.Combine(_root, "client-opendaoc", "app");
        _clientDirectory = File.Exists(Path.Combine(officialClientDirectory, "game.dll"))
            ? officialClientDirectory
            : Path.Combine(_root, "client");
        _clientConnector = Path.Combine(_clientDirectory, "connect.exe");
        _logsDirectory = Path.Combine(_root, "logs");

        Text = "Offline DAoC — PvP & Co-op";
        MinimumSize = new Size(960, 620);
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        _windowIcon = LauncherIcon.Create();
        if (_windowIcon is not null) Icon = _windowIcon;

        var stoneFrame = new StoneSurface { Dock = DockStyle.Fill };
        Controls.Add(stoneFrame);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(5),
            BackColor = Color.Transparent,
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        // Fixed to the window width: a wrapping filter bar must not widen the whole layout.
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stoneFrame.Controls.Add(rootLayout);

        _startButton = ActionButton("START SERVER", Color.FromArgb(110, 128, 70));
        _stopButton = ActionButton("STOP SERVER", Color.FromArgb(151, 67, 57));
        _playButton = ActionButton("ENTER REALM", Color.FromArgb(65, 94, 145));
        _startButton.Click += (_, _) => StartServer();
        _stopButton.Click += async (_, _) => await StopServerAsync();
        _playButton.Click += (_, _) => LaunchClient();
        _heraldButton = ActionButton("HERALD", DaocTheme.Gold);
        _heraldButton.Click += (_, _) => _herald = HeraldForm.ShowOrFocus(this, _database, _herald);
        _uiPresetButton = ActionButton("UI PRESET", DaocTheme.Iron);
        _uiPresetButton.Click += (_, _) =>
        {
            using var presets = new ClientUiPresetForm(_clientDirectory);
            presets.ShowDialog(this);
        };

        rootLayout.Controls.Add(BuildHeader(), 0, 0);
        rootLayout.Controls.Add(BuildCards(), 0, 1);
        rootLayout.Controls.Add(BuildTabs(), 0, 2);
        rootLayout.Controls.Add(BuildFooter(), 0, 3);

        _realmFilter.Items.AddRange(["All realms", "Albion", "Midgard", "Hibernia"]);
        _realmFilter.SelectedIndex = 0;
        _realmFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _search.TextChanged += (_, _) => ApplyFilter();
        _onlineOnly.CheckedChanged += (_, _) => ApplyFilter();
        _showCompanions.CheckedChanged += (_, _) => ApplyFilter();
        DaocTheme.StyleCheckBox(_makeMeGm);
        DaocTheme.StyleCheckBox(_realmRankNameplates);
        DaocTheme.StyleCheckBox(_botRealmRankNameplates);
        DaocTheme.StyleCheckBox(_toaAtlantisOpen);
        DaocTheme.StyleCheckBox(_toaLoot);
        DaocTheme.StyleCheckBox(_toaDataPacks);
        DaocTheme.StyleCheckBox(_eventActiveOnly);
        _auctionSearch.TextChanged += (_, _) => ApplyAuctionFilter();
        _auctionRealmTabs.SelectedIndexChanged += (_, _) => ApplyAuctionFilter();
        ConfigureXpRateSelector(_playerXpRate);
        ConfigureXpRateSelector(_botXpRate);
        ConfigureXpRateSelector(_playerRpRate);
        ConfigureXpRateSelector(_botRpRate);
        _playerXpRate.SelectedIndexChanged += async (_, _) => await SaveXpRateAsync("xp_rate", _playerXpRate);
        _botXpRate.SelectedIndexChanged += async (_, _) => await SaveXpRateAsync("bot_xp_rate", _botXpRate);
        _playerRpRate.SelectedIndexChanged += async (_, _) => await SaveXpRateAsync("rp_rate", _playerRpRate);
        _botRpRate.SelectedIndexChanged += async (_, _) => await SaveXpRateAsync("bot_rp_rate", _botRpRate);
        _worldSpeedMultiplier.SelectedIndexChanged += (_, _) => RequestWorldSpeedChange();
        _groupSearch.TextChanged += (_, _) => ApplyGroupSearch();
        _groupSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { _groupSearch.Clear(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { ApplyGroupSearch(); e.SuppressKeyPress = true; }
        };

        _displayClock.Tick += async (_, _) => await RefreshVisibleCountdownsAsync();
        _displayClock.Tick += async (_, _) => await SaveToaNumbersAsync();
        _autoRefresh.Tick += async (_, _) =>
        {
            ScheduleNextAutoRefresh();
            await RefreshDashboardAsync();
        };
        _serverReadinessPoll.Tick += async (_, _) => await RefreshWhenServerReadyAsync();
        Shown += async (_, _) =>
        {
            if (!File.Exists(_database))
            {
                _worldReady = false;
                _startButton.Enabled = false;
                _playButton.Enabled = false;
                _footer.Text = "The world database is missing; install the server data before starting.";
                return;
            }
            _worldReady = await EnsureCamlannWorldAsync();
            if (!_worldReady)
            {
                _startButton.Enabled = false;
                _playButton.Enabled = false;
                _footer.Text = "The one-time world reset must complete before the server can start.";
                return;
            }
            await RefreshDashboardAsync();
            await RefreshRealmExchangeAsync();
            _displayClock.Start();
            ScheduleNextAutoRefresh();
            UpdateAutoRefreshCountdown();
            _autoRefresh.Start();
        };
        FormClosing += (_, _) =>
        {
            FlushPendingToaNumbersOnClose();
            _serverReadinessPoll.Stop();
            _serverLog?.Dispose();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayClock.Dispose();
            _autoRefresh.Dispose();
            _serverReadinessPoll.Dispose();
            _helpTip.Dispose();
        }
        base.Dispose(disposing);
        // The window icon must outlive the window handle that uses it.
        if (disposing)
            _windowIcon?.Dispose();
    }

    private async Task RefreshVisibleCountdownsAsync()
    {
        // World speed, and the RVR snapshot every 30 s, are read off the UI thread.
        await PollLiveStateAsync();
        if (IsDisposed) return;
        UpdateAutoRefreshCountdown();
        InvalidateCountdownCells();
    }

    private void InvalidateCountdownCells()
    {
        // Repaint visible clock cells from the last snapshot. No resort or
        // card reconstruction occurs on this one-second display timer.
        if (_rvrObjectivesGrid.Visible)
            foreach (DataGridViewColumn column in _rvrObjectivesGrid.Columns)
                if (column.DataPropertyName == "PhaseRemainingMilliseconds")
                    _rvrObjectivesGrid.InvalidateColumn(column.Index);
        if (_grid.Visible)
            foreach (DataGridViewColumn column in _grid.Columns)
                if (column.DataPropertyName is "TaskRemaining" or "NameWithMeetUpTimer")
                    _grid.InvalidateColumn(column.Index);
        if (_auctionGrid.Visible)
            foreach (DataGridViewColumn column in _auctionGrid.Columns)
                if (column.DataPropertyName == "Expires")
                    _auctionGrid.InvalidateColumn(column.Index);
        if (_groupsGrid.Visible)
        {
            foreach (DataGridViewColumn column in _groupsGrid.Columns)
                if (column.DataPropertyName == "TimeRemaining")
                    _groupsGrid.InvalidateColumn(column.Index);
            if (SelectedGroupRow() is GroupRow selectedGroup)
                _groupDetailsSummary.Text = GroupDetailsSummary(selectedGroup);
        }
        if (_groupMembersGrid.Visible)
            foreach (DataGridViewColumn column in _groupMembersGrid.Columns)
                if (column.Name == "MemberTaskRemaining")
                    _groupMembersGrid.InvalidateColumn(column.Index);
    }

    // Single in-flight guard: a slow read is never stacked behind another one.
    private async Task PollLiveStateAsync()
    {
        if (_livePollInFlight || IsDisposed) return;
        _livePollInFlight = true;
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool readRvr = !_refreshing && nowUtc >= _nextRvrSnapshotRefreshUtc;
            if (readRvr)
                _nextRvrSnapshotRefreshUtc = nowUtc.AddMilliseconds(RvrSnapshotRefreshMilliseconds);
            var request = new LiveStateRequest(nowUtc, _serverProcess is { HasExited: false },
                !_stoppedSimulationClockLoaded || _lastWorldSpeedServerRunning == true, readRvr);
            LiveStateReading reading = await Task.Run(() => ReadLiveState(request));
            if (IsDisposed) return;
            ApplyWorldSpeedReading(reading.WorldSpeed, nowUtc);
            if (reading.RvrRead)
                ApplyRvrSnapshot(reading.Rvr);
        }
        finally
        {
            _livePollInFlight = false;
        }
    }

    private void ApplyRvrSnapshot(RvrWorldSnapshot? latest)
    {
        if (latest == null || _rvrWorld != null && latest.UpdatedUtc <= _rvrWorld.UpdatedUtc)
            return;
        _rvrWorld = latest;
        RenderActiveRvr();
    }

    // Used right after an event command is acknowledged; the snapshot is read off the UI thread.
    private async Task RefreshRvrSnapshotAsync()
    {
        string serverDirectory = _serverDirectory;
        string database = _database;
        _nextRvrSnapshotRefreshUtc = DateTime.UtcNow.AddMilliseconds(RvrSnapshotRefreshMilliseconds);
        RvrWorldSnapshot? latest = await Task.Run(() => ReadRvrWorldSnapshot(serverDirectory, database));
        if (!IsDisposed)
            ApplyRvrSnapshot(latest);
    }

    private void ScheduleNextAutoRefresh() =>
        _nextAutoRefreshUtc = DateTime.UtcNow.AddMilliseconds(AutoRefreshMilliseconds);

    private void UpdateAutoRefreshCountdown()
    {
        TimeSpan remaining = _nextAutoRefreshUtc == default
            ? TimeSpan.FromMilliseconds(AutoRefreshMilliseconds)
            : _nextAutoRefreshUtc - DateTime.UtcNow;
        string text = FormatAutoRefreshRemaining(remaining);
        if (!string.Equals(_autoRefreshCountdownValue.Text, text, StringComparison.Ordinal))
            _autoRefreshCountdownValue.Text = text;
    }

    internal static string FormatAutoRefreshRemaining(TimeSpan remaining)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private Control BuildHeader()
    {
        var banner = new FantasyBanner { Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 3) };
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent, Padding = new Padding(12, 7, 10, 6) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));

        // Flowing rows instead of fixed pixel positions: the lines keep their place at any
        // DPI and window width, and the shield spans all three lines.
        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 1, 0, 0),
        };
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 3; row++)
            titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var shield = new RealmShieldPicture { Size = new Size(43, 58), Margin = new Padding(3, 7, 9, 0) };
        titlePanel.Controls.Add(shield, 0, 0);
        titlePanel.SetRowSpan(shield, 3);
        // UseMnemonic is off so the ampersands in these labels are shown rather than swallowed.
        var title = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Text = "OFFLINE DAoC — PVP & CO-OP",
            Font = new Font("Georgia", 28f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
        };
        titlePanel.Controls.Add(title, 1, 0);
        // On narrow windows the 28 pt title would wrap and push the version line out of the
        // banner; step down to a smaller size that stays on one line.
        var titleLarge = title.Font;
        var titleSmall = new Font("Georgia", 21f, FontStyle.Bold);
        titlePanel.Resize += (_, _) =>
        {
            int available = titlePanel.ClientSize.Width - shield.Width - shield.Margin.Horizontal - title.Margin.Horizontal;
            var wanted = TextRenderer.MeasureText(title.Text, titleLarge).Width <= available ? titleLarge : titleSmall;
            if (!ReferenceEquals(title.Font, wanted))
                title.Font = wanted;
        };
        titlePanel.Disposed += (_, _) => { titleLarge.Dispose(); titleSmall.Dispose(); };
        titlePanel.Controls.Add(new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Text = "OLD FRONTIERS 1.65  •  FULL-PVP WORLD  •  BOTS & CO-OP",
            Font = new Font("Georgia", 9f),
            ForeColor = Color.FromArgb(196, 145, 70),
            Margin = new Padding(2, 0, 0, 0),
        }, 1, 1);
        titlePanel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"VERSION {DisplayVersion}",
            Font = new Font("Georgia", 12f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            Margin = new Padding(2, 0, 0, 0),
        }, 1, 2);
        header.Controls.Add(titlePanel, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(3, 5, 0, 0), BackColor = Color.Transparent };
        foreach (var button in new[] { _startButton, _stopButton, _playButton, _heraldButton, _uiPresetButton })
        {
            button.Width = 132;
            button.Height = 26;
            button.Margin = new Padding(2, 2, 2, 2);
        }
        actions.Controls.Add(_startButton);
        actions.Controls.Add(_stopButton);
        actions.Controls.Add(_playButton);
        actions.Controls.Add(_heraldButton);
        actions.Controls.Add(_uiPresetButton);
        header.Controls.Add(actions, 1, 0);
        banner.Controls.Add(header);
        return banner;
    }

    private Control BuildCards()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(0, 4, 0, 5), BackColor = Color.Transparent };
        // Realm cards need room for two generation actions; status cards do not.
        foreach (float width in new[] { 12f, 12f, 21.33f, 21.33f, 21.33f, 12f })
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cards.Controls.Add(Card("SERVER", _serverState, DaocTheme.Iron), 0, 0);
        _serverState.Font = new Font("Georgia", 12f, FontStyle.Bold);
        cards.Controls.Add(Card("ONLINE BOTS", _onlineValue, DaocTheme.Gold), 1, 0);
        cards.Controls.Add(Card("ALBION CREW", _albionValue, DaocTheme.Albion, RealmGenerateButton(1, "ALBION", DaocTheme.Albion)), 2, 0);
        cards.Controls.Add(Card("MIDGARD CREW", _midgardValue, DaocTheme.Midgard, RealmGenerateButton(2, "MIDGARD", DaocTheme.Midgard)), 3, 0);
        cards.Controls.Add(Card("HIBERNIA CREW", _hiberniaValue, DaocTheme.Hibernia, RealmGenerateButton(3, "HIBERNIA", DaocTheme.Hibernia)), 4, 0);
        Panel performanceCard = Card("BOT AI DELAY", _performanceValue, DaocTheme.Gold);
        const string performanceHelp = "How long bot AI updates take. 95 out of 100 updates finish within this time. Lower is better. ms means milliseconds; 1,000 ms equals one second.";
        SetToolTip(performanceCard, performanceHelp);
        cards.Controls.Add(performanceCard, 5, 0);
        return cards;
    }

    private void SetToolTip(Control root, string text)
    {
        _helpTip.SetToolTip(root, text);
        foreach (Control child in root.Controls)
            SetToolTip(child, text);
    }

    private Control RealmGenerateButton(int realm, string realmName, Color accent)
    {
        // Stacked and full width: side by side, "ADD LV.50 CREW" cannot fit a realm card at 960 px.
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        split.Controls.Add(RealmGenerateLevelButton(realm, realmName, accent, 1), 0, 0);
        split.Controls.Add(RealmGenerateLevelButton(realm, realmName, accent, 50), 0, 1);
        return split;
    }

    private Button RealmGenerateLevelButton(int realm, string realmName, Color accent, int level)
    {
        Button button = ActionButton(level == 1 ? "ADD CREW" : "ADD LV.50 CREW", accent);
        if (button is RuneButton rune) rune.ShowOrnaments = false;
        button.Margin = new Padding(0, 1, 0, 1);
        button.Font = DaocTheme.Fonts.Caption;
        button.Dock = DockStyle.Fill;
        _realmGenerateButtons.Add(button);
        button.Click += async (_, _) =>
        {
            if (_generatingBot)
                return;

            if (!BotGoalsServerStopped() || _botGoalsSettings?.HasUnsavedChanges == true)
            {
                MessageBox.Show(this, "Stop the server and save Server population settings before adding bots.",
                    "Population settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int batchSize = GetGenerationBatchSize();
            _generatingBot = true;
            SetRealmGenerationEnabled(false);
            _footer.Text = $"Adding {batchSize} {realmName[0] + realmName[1..].ToLowerInvariant()} bot(s) to the crew roster…";
            try
            {
                IReadOnlyList<BotCharacterGenerator.Identity> identities =
                    await Task.Run(() => GenerateBotCharacters(realm, batchSize, level));
                _realmFilter.SelectedItem = realmName[0] + realmName[1..].ToLowerInvariant();
                await RefreshDashboardAsync();
                BotCharacterGenerator.Identity last = identities[^1];
                SelectBot(last.Name);
                _footer.Text = batchSize == 1
                    ? $"Added {last.Name}, a {last.RaceName} {last.ClassName}, to the crew roster. Queued for staggered login."
                    : $"Added all {batchSize} {realmName[0] + realmName[1..].ToLowerInvariant()} bots to the crew roster. All are queued for staggered login.";
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Unable to generate bot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _generatingBot = false;
                SetRealmGenerationEnabled(true);
            }
        };
        return button;
    }

    private static int GetGenerationBatchSize()
    {
        if ((GetAsyncKeyState(VkLeftControl) & 0x8000) != 0)
            return 100;
        if ((GetAsyncKeyState(VkLeftShift) & 0x8000) != 0)
            return 10;
        return 1;
    }

    private void SetRealmGenerationEnabled(bool enabled)
    {
        foreach (Button button in _realmGenerateButtons)
            button.Enabled = enabled;
    }

    private Control BuildTabs()
    {
        var tabs = new DaocTabControl { Dock = DockStyle.Fill };
        var population = new TabPage("Active Population") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        var populationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        populationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        populationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        // Auto height: the filter bar wraps onto a second line on narrow windows.
        populationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        populationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        populationLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "PVP BOT POPULATION  •  autonomous crews and solo roamers  •  realms are identity, not teams",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.StoneDark,
            Padding = new Padding(9, 0, 0, 0),
            Font = new Font("Georgia", 8.25f, FontStyle.Bold),
        }, 0, 0);
        populationLayout.Controls.Add(BuildFilters(), 0, 1);
        populationLayout.Controls.Add(BuildGrid(), 0, 2);
        population.Controls.Add(populationLayout);

        var auction = new TabPage("Realm Exchange") { BackColor = Color.FromArgb(48, 38, 26), ForeColor = DaocTheme.Parchment };
        auction.Controls.Add(BuildAuctionPanel());
        var groups = new TabPage("Active Groups") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        groups.Controls.Add(BuildActiveGroupsPanel());
        // Scrolls on short windows instead of clipping the bottom settings.
        var xpSettings = new TabPage("XP Settings") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text, AutoScroll = true };
        // Dock=Top lays the last added control out first (on top): rates, a gap, then Trials of Atlantis.
        Control toaPanel = BuildToaSettingsPanel();
        var toaGap = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Color.Transparent };
        Control xpPanel = BuildXpSettingsPanel();
        xpSettings.Controls.Add(toaPanel);
        xpSettings.Controls.Add(toaGap);
        xpSettings.Controls.Add(xpPanel);
        xpSettings.AutoScrollMinSize = new Size(0, xpPanel.Height + toaGap.Height + toaPanel.Height);
        HeraldPalette.UseDarkScrollbars(xpSettings);
        var worldSpeed = new TabPage("World Speed") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        worldSpeed.Controls.Add(BuildWorldSpeedPanel());
        tabs.TabPages.Add(population);
        tabs.TabPages.Add(groups);
        // The Realm Events tab carries the one-click "Reset all keeps & relics"
        // button (server must be stopped); it clears guild claims and relic
        // mounts without touching characters.
        var events = new TabPage("Realm Events") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        events.Controls.Add(BuildRealmEventsPanel());
        tabs.TabPages.Add(events);
        var records = new TabPage("Realm Records") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        var eventRecords = new RealmEventRecordsControl(_worldFiles.EventRecords);
        records.Controls.Add(eventRecords);
        records.Enter += async (_, _) => await eventRecords.RefreshAsync();
        tabs.TabPages.Add(records);
        tabs.TabPages.Add(xpSettings);
        tabs.TabPages.Add(worldSpeed);
        var botGoals = new TabPage("Server population") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        _botGoalsSettings = new BotGoalsSettingsControl(
            Path.Combine(_serverDirectory, OfflineDaoc.Configuration.BotGoalSettings.FileName),
            _worldSpeedStatusPath,
            Path.Combine(_serverDirectory, PopulationTypeMixProtocol.RequestFileName),
            BotGoalsServerStopped, () => _bots.Count(bot => bot.BotId.HasValue && !bot.DeletionQueued),
            () => SaveDatabaseBackup.Create(_database, Path.Combine(Path.GetDirectoryName(_database)!, "gothwaite-pvp-backups"), "gothwaite-pvp"),
            _worldFiles.GothwaiteSwitch)
        {
            // Owner decision 18: no Gothwaite PvP in the RvR world. Until R5b's picker, the current save's marker decides.
            IsRvrWorld = () => string.Equals(CamlannWorldReset.ReadWorldModel(_database), WorldSaveLayout.RealmMarker, StringComparison.Ordinal),
        };
        botGoals.Controls.Add(_botGoalsSettings);
        tabs.TabPages.Add(botGoals);
        var openBg = new TabPage("open BG") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text };
        _openBgSettings = new OpenBgSettingsControl(_database,
            _worldFiles.OpenBgStatus, BotGoalsServerStopped);
        openBg.Controls.Add(_openBgSettings);
        tabs.TabPages.Add(openBg);
        tabs.TabPages.Add(auction);
        return tabs;
    }

    private Control BuildXpSettingsPanel()
    {
        var surface = new InsetPanel
        {
            Dock = DockStyle.Top,
            Height = 332,
            Margin = new Padding(18),
            Padding = new Padding(16, 12, 16, 12),
            Accent = DaocTheme.Gold,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 9,
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        titlePanel.Controls.Add(new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Text = "EXPERIENCE & REALM POINT RATES",
            Font = new Font("Georgia", 13f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            Location = new Point(0, 6),
        });
        layout.Controls.Add(titlePanel, 0, 0);
        layout.SetColumnSpan(titlePanel, 3);
        layout.Controls.Add(XpRateLabel("YOUR PLAYER XP"), 0, 1);
        layout.Controls.Add(_playerXpRate, 1, 1);
        layout.Controls.Add(XpRateDescription("Applies to real player characters only."), 2, 1);
        layout.Controls.Add(XpRateLabel("AUTONOMOUS BOT XP"), 0, 2);
        layout.Controls.Add(_botXpRate, 1, 2);
        layout.Controls.Add(XpRateDescription("Applies to autonomous world bots only; your companions follow your player XP rate."), 2, 2);
        layout.Controls.Add(XpRateLabel("YOUR PLAYER RP"), 0, 3);
        layout.Controls.Add(_playerRpRate, 1, 3);
        layout.Controls.Add(XpRateDescription("Applies to real player characters; your companions follow this rate."), 2, 3);
        layout.Controls.Add(XpRateLabel("AUTONOMOUS BOT RP"), 0, 4);
        layout.Controls.Add(_botRpRate, 1, 4);
        layout.Controls.Add(XpRateDescription("Applies to autonomous world bots only."), 2, 4);
        _xpSettingsStatus.Dock = DockStyle.Fill;
        _xpSettingsStatus.TextAlign = ContentAlignment.MiddleLeft;
        _xpSettingsStatus.Font = new Font("Georgia", 9f, FontStyle.Bold | FontStyle.Italic);
        layout.Controls.Add(_makeMeGm, 0, 5);
        layout.SetColumnSpan(_makeMeGm, 2);
        layout.Controls.Add(XpRateDescription("Applies to your account at the next server startup."), 2, 5);
        _makeMeGm.CheckedChanged += async (_, _) => await SaveGmSettingAsync();
        layout.Controls.Add(_realmRankNameplates, 0, 6);
        layout.SetColumnSpan(_realmRankNameplates, 2);
        layout.Controls.Add(_realmRankNameplatesNote, 2, 6);
        _realmRankNameplates.CheckedChanged += async (_, _) =>
            await SaveNameplateSettingAsync(_realmRankNameplates, RealmRankNameplateKey, RealmRankNameplateDescription, "false");
        layout.Controls.Add(_botRealmRankNameplates, 0, 7);
        layout.SetColumnSpan(_botRealmRankNameplates, 2);
        layout.Controls.Add(_botRealmRankNameplatesNote, 2, 7);
        _botRealmRankNameplates.CheckedChanged += async (_, _) =>
            await SaveNameplateSettingAsync(_botRealmRankNameplates, BotRealmRankNameplateKey, BotRealmRankNameplateDescription, "true");
        layout.Controls.Add(_xpSettingsStatus, 0, 8);
        layout.SetColumnSpan(_xpSettingsStatus, 3);
        surface.Controls.Add(layout);
        return surface;
    }

    private static CheckBox ToaCheckBox(string name) => new()
    {
        Text = ToaSettings.Field(name).Label,
        AutoSize = true,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 9f, FontStyle.Bold),
        AccessibleName = ToaSettings.Field(name).Label,
        // The code default until the first snapshot has read the save.
        Checked = ToaSettings.Defaults.SwitchOf(name),
    };

    private Control BuildToaSettingsPanel()
    {
        var surface = new InsetPanel
        {
            Dock = DockStyle.Top,
            Height = 306,
            Margin = new Padding(18),
            Padding = new Padding(16, 12, 16, 12),
            Accent = DaocTheme.Gold,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        foreach (ToaField field in ToaSettings.Fields)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, field.IsSwitch ? 32 : 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        titlePanel.Controls.Add(new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Text = "TRIALS OF ATLANTIS",
            Font = new Font("Georgia", 13f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            Location = new Point(0, 6),
        });
        layout.Controls.Add(titlePanel, 0, 0);
        layout.SetColumnSpan(titlePanel, 3);
        int row = 1;
        foreach (ToaField field in ToaSettings.Fields)
        {
            Label note = XpRateDescription(field.Note);
            note.UseMnemonic = false;
            _toaNotes[field.Name] = note;
            string help = field.Help + " Applies at the next server startup.";
            _helpTip.SetToolTip(note, help);
            if (field.IsSwitch)
            {
                CheckBox box = ToaSwitch(field.Name);
                _helpTip.SetToolTip(box, help);
                layout.Controls.Add(box, 0, row);
                layout.SetColumnSpan(box, 2);
                string name = field.Name;
                box.CheckedChanged += async (_, _) => await SaveToaSwitchAsync(name, box);
            }
            else
            {
                Label label = XpRateLabel(field.Label);
                var number = new NumericUpDown
                {
                    Minimum = field.Min,
                    Maximum = field.Max,
                    Value = field.Default,
                    Width = 108,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 7, 0, 6),
                    AccessibleName = field.Label,
                };
                DaocTheme.StyleNumeric(number);
                _toaNumbers[field.Name] = number;
                _helpTip.SetToolTip(label, help);
                _helpTip.SetToolTip(number, help);
                layout.Controls.Add(label, 0, row);
                layout.Controls.Add(number, 1, row);
                string name = field.Name;
                number.ValueChanged += (_, _) => ToaNumberEdited(name);
            }
            layout.Controls.Add(note, 2, row);
            row++;
        }
        _toaStatus.Dock = DockStyle.Fill;
        _toaStatus.TextAlign = ContentAlignment.MiddleLeft;
        _toaStatus.Font = new Font("Georgia", 9f, FontStyle.Bold | FontStyle.Italic);
        layout.Controls.Add(_toaStatus, 0, row);
        layout.SetColumnSpan(_toaStatus, 3);
        surface.Controls.Add(layout);
        return surface;
    }

    private CheckBox ToaSwitch(string name) => name switch
    {
        "atlantis" => _toaAtlantisOpen,
        "loot" => _toaLoot,
        "data_packs" => _toaDataPacks,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a ToA switch field."),
    };

    private Control BuildWorldSpeedPanel()
    {
        var surface = new InsetPanel
        {
            Dock = DockStyle.Top,
            Height = 314,
            Margin = new Padding(18),
            Padding = new Padding(20),
            Accent = DaocTheme.Gold,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "WORLD SPEED CONTROL",
            Font = new Font("Georgia", 13f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 3);
        layout.Controls.Add(XpRateLabel("WORLD SPEED"), 0, 1);
        StyleInput(_worldSpeedMultiplier);
        _worldSpeedMultiplier.Anchor = AnchorStyles.Left;
        _worldSpeedMultiplier.Margin = new Padding(0, 10, 0, 9);
        _loadingWorldSpeed = true;
        try
        {
            _worldSpeedMultiplier.Items.AddRange(new object[]
            {
                new WorldSpeedOption(1, "1×"),
                new WorldSpeedOption(2, "2×"),
                new WorldSpeedOption(3, "3×"),
                new WorldSpeedOption(5, "5×"),
                new WorldSpeedOption(10, "10×"),
                new WorldSpeedOption(20, "20×"),
            });
            _worldSpeedMultiplier.SelectedIndex = 0;
        }
        finally
        {
            _loadingWorldSpeed = false;
        }
        _worldSpeedMultiplier.Enabled = false;
        layout.Controls.Add(_worldSpeedMultiplier, 1, 1);
        layout.Controls.Add(XpRateDescription("Applies only while no game client is connected."), 2, 1);

        _worldSpeedStatusText.Text = "Waiting for a live server session.";
        _worldSpeedStatusText.TextAlign = ContentAlignment.MiddleLeft;
        _worldSpeedStatusText.ForeColor = DaocTheme.Parchment;
        _worldSpeedStatusText.Font = new Font("Georgia", 9f, FontStyle.Bold);
        layout.Controls.Add(_worldSpeedStatusText, 0, 2);
        layout.SetColumnSpan(_worldSpeedStatusText, 3);

        _worldSpeedTelemetryText.Text = "Tick P95 and the speed-specific tick budget appear with live status.";
        _worldSpeedTelemetryText.TextAlign = ContentAlignment.MiddleLeft;
        _worldSpeedTelemetryText.ForeColor = DaocTheme.Muted;
        _worldSpeedTelemetryText.Font = new Font("Georgia", 8.5f);
        layout.Controls.Add(_worldSpeedTelemetryText, 0, 3);
        layout.SetColumnSpan(_worldSpeedTelemetryText, 3);

        _worldSpeedClockText.Text = "Simulated time unavailable until the server publishes its clock.";
        _worldSpeedClockText.TextAlign = ContentAlignment.MiddleLeft;
        _worldSpeedClockText.ForeColor = DaocTheme.Muted;
        _worldSpeedClockText.Font = new Font("Georgia", 8.5f);
        layout.Controls.Add(_worldSpeedClockText, 0, 4);
        layout.SetColumnSpan(_worldSpeedClockText, 3);

        var description = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Text = "2× aims to complete two simulated hours per real hour. Connected clients hold the world at 1×; the selected speed resumes five seconds after the last client leaves. XP and loot per event stay the same. Higher speeds depend on available CPU time.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8.5f),
            Padding = new Padding(0, 5, 0, 0),
        };
        surface.Controls.Add(layout);
        surface.Controls.Add(description);

        _worldSpeedErrorText.Dock = DockStyle.Top;
        _worldSpeedErrorText.Height = 30;
        _worldSpeedErrorText.TextAlign = ContentAlignment.MiddleLeft;
        _worldSpeedErrorText.ForeColor = DaocTheme.Danger;
        _worldSpeedErrorText.Font = new Font("Georgia", 8.5f, FontStyle.Bold);
        _worldSpeedErrorText.Visible = false;
        surface.Controls.Add(_worldSpeedErrorText);
        return surface;
    }

    private void RequestWorldSpeedChange()
    {
        if (_loadingWorldSpeed || _worldSpeedMultiplier.SelectedItem is not WorldSpeedOption option)
            return;

        DateTime nowUtc = DateTime.UtcNow;
        WorldSpeedStatus? status = WorldSpeedProtocol.ReadFreshStatus(
            _worldSpeedStatusPath, nowUtc, out string? unavailableReason);
        if (status is null || !(IsServerRunning() || _serverProcess is { HasExited: false }))
        {
            _worldSpeedLocalError = unavailableReason ?? "A live server status is required to change world speed.";
            RefreshWorldSpeedState();
            return;
        }

        if (option.Multiplier == status.SelectedMultiplier && _pendingWorldSpeedMultiplier is null)
            return;

        try
        {
            WorldSpeedProtocol.WriteRequest(_worldSpeedRequestPath, status.SessionId, option.Multiplier, nowUtc);
            _worldSpeedSnapshot = status;
            _worldSpeedSessionId = status.SessionId;
            _pendingWorldSpeedMultiplier = option.Multiplier;
            _pendingWorldSpeedSinceUtc = nowUtc;
            _worldSpeedLocalError = null;
            RefreshWorldSpeedState();
        }
        catch (Exception exception)
        {
            _pendingWorldSpeedMultiplier = null;
            _worldSpeedLocalError = $"Unable to write the world speed request: {exception.Message}";
            SetWorldSpeedSelection(status.SelectedMultiplier);
            UpdateWorldSpeedPresentation(true, status);
        }
    }

    // Synchronous path for user actions and dashboard refreshes. The one-second timer uses
    // PollLiveStateAsync, which performs the same reads on a worker thread.
    private void RefreshWorldSpeedState()
    {
        DateTime nowUtc = DateTime.UtcNow;
        WorldSpeedReading reading = ReadWorldSpeedReading(nowUtc, _serverProcess is { HasExited: false },
            !_stoppedSimulationClockLoaded || _lastWorldSpeedServerRunning == true, _worldSpeedStatusPath, _database);
        ApplyWorldSpeedReading(reading, nowUtc);
    }

    private void ApplyWorldSpeedReading(WorldSpeedReading reading, DateTime nowUtc)
    {
        bool running = reading.Running;
        bool justStopped = _lastWorldSpeedServerRunning == true && !running;
        _lastWorldSpeedServerRunning = running;

        if (running)
        {
            _stoppedSimulationClockLoaded = false;
            _worldSpeedSnapshot = reading.Status;
            _worldSpeedUnavailableReason = reading.UnavailableReason;
            if (_worldSpeedSnapshot is WorldSpeedStatus status)
            {
                if (_worldSpeedSessionId != status.SessionId)
                {
                    _worldSpeedSessionId = status.SessionId;
                    _pendingWorldSpeedMultiplier = null;
                    _worldSpeedLocalError = null;
                }

                if (_pendingWorldSpeedMultiplier == status.SelectedMultiplier)
                {
                    _pendingWorldSpeedMultiplier = null;
                    _worldSpeedLocalError = null;
                }
                else if (_pendingWorldSpeedMultiplier.HasValue &&
                    nowUtc - _pendingWorldSpeedSinceUtc > WorldSpeedProtocol.StatusMaxAge)
                {
                    _pendingWorldSpeedMultiplier = null;
                    _worldSpeedLocalError = "The server did not acknowledge the speed request. Select a speed to retry.";
                }

                _simulationUtc = WorldSpeedProtocol.AdvanceLiveClock(status, nowUtc);
            }
            else
            {
                _simulationUtc = null;
                if (_pendingWorldSpeedMultiplier.HasValue &&
                    nowUtc - _pendingWorldSpeedSinceUtc > WorldSpeedProtocol.StatusMaxAge)
                {
                    _pendingWorldSpeedMultiplier = null;
                    _worldSpeedLocalError = "The server did not acknowledge the speed request. Select a speed to retry.";
                }
            }

            UpdateWorldSpeedPresentation(true, _worldSpeedSnapshot);
            return;
        }

        _worldSpeedSnapshot = null;
        _pendingWorldSpeedMultiplier = null;
        if (reading.SavedClock is not null)
        {
            _savedSimulationClock = reading.SavedClock;
            _stoppedSimulationClockLoaded = true;
        }
        else if (!_stoppedSimulationClockLoaded || justStopped)
        {
            // Reached only when the server stopped after the worker read was planned.
            _savedSimulationClock = ReadSavedSimulationClock(_database);
            _stoppedSimulationClockLoaded = true;
        }
        _simulationUtc = WorldSimulationClock.AdvanceStoppedClock(_savedSimulationClock, nowUtc);
        UpdateWorldSpeedPresentation(false, null);
    }

    // Worker-thread safe: reads only the status file, the saved clock and the local TCP table.
    private LiveStateReading ReadLiveState(LiveStateRequest request) => new(
        ReadWorldSpeedReading(request.NowUtc, request.ProcessAlive, request.ReadSavedClock, _worldSpeedStatusPath, _database),
        request.ReadRvr ? ReadRvrWorldSnapshot(_serverDirectory, _database) : null,
        request.ReadRvr);

    private static WorldSpeedReading ReadWorldSpeedReading(DateTime nowUtc, bool processAlive, bool readSavedClock,
        string statusPath, string database)
    {
        // The process check is free; the TCP table scan is not, so it only runs when needed.
        bool running = processAlive || IsServerRunning();
        if (running)
        {
            WorldSpeedStatus? status = WorldSpeedProtocol.ReadFreshStatus(statusPath, nowUtc, out string? unavailableReason);
            return new WorldSpeedReading(true, status, unavailableReason, null);
        }
        return new WorldSpeedReading(false, null, null, readSavedClock ? ReadSavedSimulationClock(database) : null);
    }

    private static WorldSimulationClockRead ReadSavedSimulationClock(string database)
    {
        if (!File.Exists(database))
            return new WorldSimulationClockRead(null, false, null);
        try
        {
            using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            return WorldSimulationClock.ReadCheckpoint(connection);
        }
        catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
        {
            return new WorldSimulationClockRead(null, false, $"Unable to read the saved simulation clock: {exception.Message}");
        }
    }

    private void UpdateWorldSpeedPresentation(bool running, WorldSpeedStatus? status)
    {
        if (!running)
        {
            _worldSpeedMultiplier.Enabled = false;
            SetWorldSpeedSelection(1);
            _worldSpeedStatusText.Text = "Server stopped. Each new session starts with 1× selected.";
            _worldSpeedTelemetryText.Text = "Choose 1×, 2×, 3×, 5×, 10×, or 20× after live server status appears.";
            _worldSpeedClockText.Text = _simulationUtc is DateTime stoppedNow
                ? $"Simulated UTC: {stoppedNow:yyyy-MM-dd HH:mm:ss} (server stopped; downtime advances at 1×)"
                : "Simulated time unavailable because the saved clock could not be read.";
            ShowWorldSpeedError(_worldSpeedLocalError ?? _savedSimulationClock?.Error);
            return;
        }

        if (status is null)
        {
            _worldSpeedMultiplier.Enabled = false;
            SetWorldSpeedSelection(null);
            _worldSpeedStatusText.Text = "World speed unavailable — waiting for fresh live server status.";
            _worldSpeedTelemetryText.Text = _worldSpeedUnavailableReason ?? "Status becomes available when the server reports its session.";
            _worldSpeedClockText.Text = "Simulated time unavailable while live status is stale.";
            ShowWorldSpeedError(_worldSpeedLocalError);
            return;
        }

        _worldSpeedMultiplier.Enabled = true;
        SetWorldSpeedSelection(_pendingWorldSpeedMultiplier ?? status.SelectedMultiplier);
        string clientSummary = status.ConnectedClients == 0
            ? "no clients"
            : $"{status.ConnectedClients} client{(status.ConnectedClients == 1 ? string.Empty : "s")}";
        string pendingSummary = _pendingWorldSpeedMultiplier is int requested && requested != status.SelectedMultiplier
            ? $" · requested {requested}×, waiting for server acknowledgement"
            : string.Empty;
        _worldSpeedStatusText.Text = $"Selected {status.SelectedMultiplier}×  ·  Effective {status.EffectiveMultiplier}× ({clientSummary})  ·  Achieved {status.AchievedMultiplier:0.00}×{pendingSummary}";
        double budgetMs = 1000d / (30d * status.EffectiveMultiplier);
        _worldSpeedTelemetryText.Text = $"Tick P95 {status.TickP95Ms:0.0} ms  ·  {budgetMs:0.0} ms budget at effective {status.EffectiveMultiplier}× (selected target {status.SelectedMultiplier}×)";
        _worldSpeedClockText.Text = _simulationUtc is DateTime liveNow
            ? $"Simulated UTC: {liveNow:yyyy-MM-dd HH:mm:ss}  ·  clients connected: {status.ConnectedClients}"
            : "Simulated time unavailable because live status is stale.";
        string? error = FirstNonEmpty(_worldSpeedLocalError, status.Error);
        ShowWorldSpeedError(error);
    }

    private void SetWorldSpeedSelection(int? multiplier)
    {
        _loadingWorldSpeed = true;
        try
        {
            _worldSpeedMultiplier.SelectedIndex = multiplier is int value
                ? Enumerable.Range(0, _worldSpeedMultiplier.Items.Count)
                    .FirstOrDefault(index => _worldSpeedMultiplier.Items[index] is WorldSpeedOption option && option.Multiplier == value, -1)
                : -1;
        }
        finally
        {
            _loadingWorldSpeed = false;
        }
    }

    private void ShowWorldSpeedError(string? error)
    {
        _worldSpeedErrorText.Visible = !string.IsNullOrWhiteSpace(error);
        _worldSpeedErrorText.Text = error ?? string.Empty;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static Label XpRateLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 9f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Label XpRateDescription(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 9.25f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static void ConfigureXpRateSelector(ComboBox selector)
    {
        StyleInput(selector);
        selector.Anchor = AnchorStyles.Left;
        selector.Margin = new Padding(0, 10, 0, 9);
        selector.Items.AddRange(new object[]
        {
            new XpRateOption(1, "1×  Original"),
            new XpRateOption(2, "2×"),
            new XpRateOption(3, "3×"),
            new XpRateOption(5, "5×"),
            new XpRateOption(10, "10×"),
            new XpRateOption(15, "15×"),
            new XpRateOption(20, "20×"),
            new XpRateOption(25, "25×"),
            new XpRateOption(50, "50×"),
            new XpRateOption(100, "100×"),
        });
    }

    private Control BuildActiveGroupsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(7),
            BackColor = DaocTheme.Panel,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "⚔  ACTIVE GROUPS  •  Filter or sort the list; select a row for roster and task details",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 9.5f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.StoneDark,
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        }, 0, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(3) };
        StyleInput(_groupSearch);
        StyleInput(_groupRealm);
        _groupRealm.Items.AddRange(["All realms", "Albion", "Midgard", "Hibernia"]);
        _groupRealm.SelectedIndex = 0;
        filters.Controls.Add(_groupSearch);
        filters.Controls.Add(_groupRealm);
        var clearSearch = ActionButton("CLEAR", DaocTheme.Panel);
        clearSearch.Width = 80;
        clearSearch.Click += (_, _) =>
        {
            _groupSearch.Clear();
            _groupRealm.SelectedIndex = 0;
        };
        filters.Controls.Add(clearSearch);
        filters.Controls.Add(_groupSearchStatus);
        layout.Controls.Add(filters, 0, 1);

        ConfigureGroupGrid(_groupsGrid);
        _groupsGrid.DataSource = _groupSource;
        _groupsGrid.Columns.Add(TextColumn("Realm", "Realm", 82));
        _groupsGrid.Columns.Add(TextColumn("Phase", "Phase", 105));
        _groupsGrid.Columns.Add(TextColumn("Leader", "LeaderName", 120));
        _groupsGrid.Columns.Add(TextColumn("Members", "MemberNames", 240));
        _groupsGrid.Columns.Add(TextColumn("Meet-up / area", "Location", 145));
        _groupsGrid.Columns.Add(TextColumn("Time left", "TimeRemaining", 115));
        _groupsGrid.Columns.Add(TextColumn("Shared goal", "SharedGoal", 220, DataGridViewAutoSizeColumnMode.Fill));
        UseFillColumns(_groupsGrid);
        foreach (DataGridViewColumn column in _groupsGrid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _groupsGrid.ColumnHeaderMouseClick += (_, eventArgs) =>
        {
            string property = _groupsGrid.Columns[eventArgs.ColumnIndex].DataPropertyName;
            _groupSortAscending = property == _groupSortProperty ? !_groupSortAscending : true;
            _groupSortProperty = property;
            ApplyGroupSearch(SelectedGroupRow()?.GroupId);
        };
        _groupsGrid.SelectionChanged += (_, _) =>
        {
            if (!_updatingGroupGrid)
                UpdateSelectedGroupDetails();
        };
        _groupsGrid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
                _groupsGrid.Rows[eventArgs.RowIndex].DataBoundItem is not GroupRow group)
                return;
            if (_groupsGrid.Columns[eventArgs.ColumnIndex].DataPropertyName == "Realm")
                eventArgs.CellStyle.ForeColor = DaocTheme.RealmColor(group.Realm);
        };
        _groupSearch.TextChanged += (_, _) => ApplyGroupSearch();
        _groupRealm.SelectedIndexChanged += (_, _) => ApplyGroupSearch();
        _groupSource.DataSource = new List<GroupRow>();

        ConfigureGroupGrid(_groupMembersGrid);
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Name", "MemberName", 145));
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Role", "MemberRole", 155));
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Level", "MemberLevel", 58));
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Class", "MemberClass", 110));
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Zone", "MemberZone", 145));
        _groupMembersGrid.Columns.Add(GroupMemberColumn("Time left", "MemberTaskRemaining", 135));
        _groupMembersGrid.Columns[^1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _groupMembersGrid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 ||
                _groupMembersGrid.Rows[eventArgs.RowIndex].Tag is not BotRow member ||
                _groupMembersGrid.Columns[eventArgs.ColumnIndex].Name != "MemberTaskRemaining")
                return;
            eventArgs.Value = member.TaskRemaining;
            eventArgs.FormattingApplied = true;
        };

        _groupDetailsTitle.Dock = DockStyle.Fill;
        _groupDetailsTitle.TextAlign = ContentAlignment.MiddleLeft;
        _groupDetailsTitle.Padding = new Padding(8, 0, 0, 0);
        _groupDetailsTitle.Font = new Font("Georgia", 9f, FontStyle.Bold);
        _groupDetailsTitle.ForeColor = DaocTheme.GoldLight;
        _groupDetailsTitle.BackColor = DaocTheme.StoneDark;
        _groupDetailsSummary.Dock = DockStyle.Fill;
        _groupDetailsSummary.TextAlign = ContentAlignment.MiddleLeft;
        _groupDetailsSummary.Padding = new Padding(8, 4, 8, 4);
        _groupDetailsSummary.Font = new Font("Georgia", 8.25f);
        _groupDetailsSummary.ForeColor = DaocTheme.Parchment;
        _groupDetailsSummary.BackColor = DaocTheme.Panel;

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 5, 0, 0),
            BackColor = DaocTheme.Panel,
        };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(_groupDetailsTitle, 0, 0);
        details.Controls.Add(_groupDetailsSummary, 0, 1);
        details.Controls.Add(_groupMembersGrid, 0, 2);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = DaocTheme.Panel,
        };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        content.Controls.Add(_groupsGrid, 0, 0);
        content.Controls.Add(details, 0, 1);
        layout.Controls.Add(content, 0, 2);
        return layout;
    }

    private static void ConfigureGroupGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        DaocTheme.StyleGrid(grid);
    }

    private static DataGridViewTextBoxColumn GroupMemberColumn(string header, string name, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private void ConfigureRvrObjectiveGrid()
    {
        if (_rvrObjectivesGrid.Columns.Count > 0)
            return;

        _rvrObjectivesGrid.Dock = DockStyle.Fill;
        _rvrObjectivesGrid.ReadOnly = true;
        _rvrObjectivesGrid.AllowUserToAddRows = false;
        _rvrObjectivesGrid.AllowUserToDeleteRows = false;
        _rvrObjectivesGrid.RowHeadersVisible = false;
        _rvrObjectivesGrid.AutoGenerateColumns = false;
        // Styled directly: these grids used to copy styles from a grid that was never built.
        DaocTheme.StyleGrid(_rvrObjectivesGrid);
        _rvrObjectivesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _rvrObjectivesGrid.Columns.Add(TextColumn("Type", "Marker", 110));
        _rvrObjectivesGrid.Columns.Add(TextColumn("Keep / relic", "Name", 165));
        _rvrObjectivesGrid.Columns.Add(TextColumn("Held by", "Owner", 85));
        _rvrObjectivesGrid.Columns.Add(TextColumn("Battle status", "State", 180));
        _rvrObjectivesGrid.Columns[^1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _rvrObjectivesGrid.Columns.Add(TextColumn("Carrier", "Carrier", 110));
        _rvrObjectivesGrid.Columns.Add(TextColumn("Location", "Location", 110));
        _rvrObjectivesGrid.Columns.Add(TextColumn("Rally attendance / battle", "Forces", 260, DataGridViewAutoSizeColumnMode.Fill));
        _rvrObjectivesGrid.Columns[^1].MinimumWidth = 260;
        _rvrObjectivesGrid.Columns[^1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _rvrObjectivesGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _rvrObjectivesGrid.Rows[e.RowIndex].DataBoundItem is not RvrObjective row) return;
            ApplyRvrStatusStyle(e.CellStyle, row.State);
        };
    }

    private void RenderActiveGroups()
    {
        ApplyGroupSearch(SelectedGroupRow()?.GroupId);
    }

    private void ApplyGroupSearch(string? preferredGroupId = null)
    {
        // Filter only the last snapshot and bind compact rows. This keeps both
        // typing and refreshes independent of per-member WinForms control creation.
        preferredGroupId ??= SelectedGroupRow()?.GroupId;
        string query = _groupSearch.Text.Trim();
        string realm = _groupRealm.SelectedItem?.ToString() ?? "All realms";
        List<GroupRow> matches = SortGroups(_groups.Where(group =>
            (realm == "All realms" || group.Realm.Equals(realm, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || GroupMatchesSearch(group, query)))).ToList();

        int exactMemberIndex = query.Length == 0 ? -1 : matches.FindIndex(group =>
            group.Members.Any(member => member.Name.Equals(query, StringComparison.OrdinalIgnoreCase)));
        int selectedIndex = exactMemberIndex >= 0
            ? exactMemberIndex
            : matches.FindIndex(group => group.GroupId.Equals(preferredGroupId, StringComparison.OrdinalIgnoreCase));

        _updatingGroupGrid = true;
        try
        {
            _groupSource.DataSource = matches;
            _groupSource.ResetBindings(false);
            _groupsGrid.ClearSelection();
            _groupsGrid.CurrentCell = null;
            if (matches.Count > 0)
            {
                if (selectedIndex < 0)
                    selectedIndex = 0;
                _groupsGrid.CurrentCell = _groupsGrid.Rows[selectedIndex].Cells[0];
                _groupsGrid.Rows[selectedIndex].Selected = true;
            }
        }
        finally
        {
            _updatingGroupGrid = false;
        }

        _groupSearchStatus.Text = matches.Count == _groups.Count && realm == "All realms"
            ? $"{matches.Count} groups"
            : $"{matches.Count} of {_groups.Count} groups";
        UpdateSelectedGroupDetails();
    }

    private static bool GroupMatchesSearch(GroupRow group, string query) =>
        group.Realm.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.Phase.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.GroupId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.LeaderName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.RendezvousName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.PullerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.SharedGoal.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.Status.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.TimeRemaining.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        group.Members.Any(member => member.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            member.ZoneName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            member.ClassName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            member.GroupRole.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            member.TaskRemaining.Contains(query, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<GroupRow> SortGroups(IEnumerable<GroupRow> groups)
    {
        bool ascending = _groupSortAscending;
        return (_groupSortProperty, ascending) switch
        {
            ("Realm", true) => groups.OrderBy(group => group.Realm, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("Realm", false) => groups.OrderByDescending(group => group.Realm, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("Phase", true) => groups.OrderBy(group => group.Phase, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("Phase", false) => groups.OrderByDescending(group => group.Phase, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("LeaderName", true) => groups.OrderBy(group => group.LeaderName, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("LeaderName", false) => groups.OrderByDescending(group => group.LeaderName, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("MemberNames", true) => groups.OrderBy(group => group.MemberNames, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("MemberNames", false) => groups.OrderByDescending(group => group.MemberNames, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("Location", true) => groups.OrderBy(group => group.Location, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("Location", false) => groups.OrderByDescending(group => group.Location, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("TimeRemaining", true) => groups.OrderBy(group => group.RemainingMilliseconds ?? long.MaxValue).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("TimeRemaining", false) => groups.OrderByDescending(group => group.RemainingMilliseconds ?? -1).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("SharedGoal", true) => groups.OrderBy(group => group.SharedGoal, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            ("SharedGoal", false) => groups.OrderByDescending(group => group.SharedGoal, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
            _ => groups.OrderBy(group => group.Realm, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.GroupId, StringComparer.OrdinalIgnoreCase),
        };
    }

    private GroupRow? SelectedGroupRow() => _groupsGrid.CurrentRow?.DataBoundItem as GroupRow;

    private void UpdateSelectedGroupDetails()
    {
        GroupRow? group = SelectedGroupRow();
        _groupMembersGrid.Rows.Clear();
        if (group is null)
        {
            _groupDetailsTitle.Text = _groups.Count == 0 ? "No active playerbot groups in the current snapshot" : "No groups match this filter";
            _groupDetailsSummary.Text = _groups.Count == 0
                ? "Groups appear here after matchmaking and disappear after disbanding."
                : "Change the search or realm filter to see groups.";
            return;
        }

        int minimumLevel = group.Members.Count == 0 ? 0 : group.Members.Min(member => member.Level);
        int maximumLevel = group.Members.Count == 0 ? 0 : group.Members.Max(member => member.Level);
        string levels = minimumLevel == maximumLevel ? minimumLevel.ToString() : $"{minimumLevel}–{maximumLevel}";
        _groupDetailsTitle.Text = $"{group.Realm.ToUpperInvariant()}  •  {group.Members.Count} PLAYERBOTS  •  LEVELS {levels}  •  {group.Phase.ToUpperInvariant()}";
        _groupDetailsSummary.Text = GroupDetailsSummary(group);

        foreach (BotRow member in group.Members.OrderBy(member => member.Level).ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase))
        {
            int rowIndex = _groupMembersGrid.Rows.Add(member.Name, GroupMemberRole(group, member), member.Level,
                member.ClassName, member.ZoneName, member.TaskRemaining);
            _groupMembersGrid.Rows[rowIndex].Tag = member;
        }
    }

    private static string GroupMemberRole(GroupRow group, BotRow member)
    {
        var roles = new List<string>();
        if (!string.IsNullOrWhiteSpace(member.GroupRole))
            roles.Add(member.GroupRole.ToUpperInvariant());
        if (member.Name.Equals(group.LeaderName, StringComparison.OrdinalIgnoreCase))
            roles.Add("LEADER");
        if (member.Name.Equals(group.PullerName, StringComparison.OrdinalIgnoreCase))
            roles.Add("PULLER");
        return roles.Count == 0 ? "—" : string.Join(" · ", roles);
    }

    private static string GroupDetailsSummary(GroupRow group) =>
        $"LEADER: {group.LeaderName}    TOWN: {group.RendezvousName}    PULLER: {group.PullerName}    {group.TimeRemaining}\n{group.SharedGoal}\n{group.Status}";

    private Control BuildFilters()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(5, 7, 0, 4), BackColor = DaocTheme.Panel };
        StyleInput(_realmFilter);
        StyleInput(_search);
        panel.Controls.Add(_realmFilter);
        panel.Controls.Add(_search);
        _refreshButton = ActionButton("REFRESH", Color.FromArgb(126, 104, 67));
        _refreshButton.Width = 100;
        _refreshButton.Click += async (_, _) => await RefreshDashboardAsync();
        panel.Controls.Add(_refreshButton);
        _deleteBotButton = ActionButton("DELETE CHARACTER…", Color.FromArgb(151, 67, 57));
        _deleteBotButton.Width = 174;
        _deleteBotButton.Enabled = false;
        _deleteBotButton.Click += async (_, _) => await DeleteSelectedBotAsync();
        panel.Controls.Add(_deleteBotButton);
        _deleteOrphansButton = ActionButton("DELETE ORPHANED COMPANIONS…", Color.FromArgb(151, 67, 57));
        _deleteOrphansButton.Width = 236;
        _deleteOrphansButton.Enabled = false;
        _deleteOrphansButton.Click += async (_, _) => await DeleteOrphanedCompanionsAsync();
        _helpTip.SetToolTip(_deleteOrphansButton, "Permanently delete every companion whose owning character no longer exists, with their items. Server must be stopped.");
        panel.Controls.Add(_deleteOrphansButton);
        panel.Controls.Add(_onlineOnly);
        _helpTip.SetToolTip(_onlineOnly, "Show only online bots in the current realm and search results. Uncheck to show all bots.");
        panel.Controls.Add(_showCompanions);
        _helpTip.SetToolTip(_showCompanions, "Include companion bots in the list. Unchecked shows only autonomous gamebots.");
        return panel;
    }

    private Control BuildFooter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DaocTheme.StoneDark,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(_footer, 0, 0);
        panel.Controls.Add(_autoRefreshCountdownCaption, 1, 0);
        panel.Controls.Add(_autoRefreshCountdownValue, 2, 0);
        _deleteAllBotsButton = ActionButton("DELETE ALL BOTS…", Color.FromArgb(151, 45, 40));
        _deleteAllBotsButton.Dock = DockStyle.Fill;
        _deleteAllBotsButton.Margin = new Padding(0);
        _deleteAllBotsButton.Enabled = false;
        _deleteAllBotsButton.Click += async (_, _) => await DeleteAllBotsAsync();
        panel.Controls.Add(_deleteAllBotsButton, 3, 0);
        return panel;
    }

    private Control BuildAuctionPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(7), BackColor = Color.FromArgb(48, 38, 26) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var exchangeHeader = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(69, 51, 31) };
        exchangeHeader.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "✦  THE REALM EXCHANGE  ✦    Camelot • Jordheim • Tir na Nog",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 9f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = Color.FromArgb(69, 51, 31),
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        });
        var salesButton = ActionButton("50 MOST RECENTLY SOLD", DaocTheme.Gold);
        salesButton.Dock = DockStyle.Right;
        salesButton.Width = 215;
        salesButton.Click += (_, _) =>
        {
            if (_salesLedger == null || _salesLedger.IsDisposed)
            {
                _salesLedger = new ExchangeSalesForm(_database);
                _salesLedger.Show(this);
            }
            else { _salesLedger.WindowState = FormWindowState.Normal; _salesLedger.Activate(); }
        };
        exchangeHeader.Controls.Add(salesButton);
        _refreshExchangeButton = ActionButton("REFRESH REALM EXCHANGE", Color.FromArgb(126, 104, 67));
        _refreshExchangeButton.Dock = DockStyle.Right;
        _refreshExchangeButton.Width = 215;
        _refreshExchangeButton.Click += async (_, _) => await RefreshRealmExchangeAsync();
        exchangeHeader.Controls.Add(_refreshExchangeButton);
        layout.Controls.Add(exchangeHeader, 0, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 3), BackColor = Color.FromArgb(48, 38, 26) };
        _auctionSearch.BackColor = Color.FromArgb(220, 204, 163);
        _auctionSearch.ForeColor = DaocTheme.Ink;
        _auctionSearch.BorderStyle = BorderStyle.FixedSingle;
        filters.Controls.Add(_auctionSearch);
        foreach (string realm in new[] { "Albion", "Midgard", "Hibernia" })
            _auctionRealmTabs.TabPages.Add(realm);
        _auctionRealmTabs.SelectedIndex = 0;
        filters.Controls.Add(_auctionRealmTabs);
        filters.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 6, 0, 0),
            Text = "Listings expire after 24h • startup/manual refresh only",
            ForeColor = Color.FromArgb(196, 174, 126),
            Font = new Font("Georgia", 8f, FontStyle.Italic),
        });
        layout.Controls.Add(filters, 0, 1);

        ConfigureAuctionGrid();
        layout.Controls.Add(_auctionGrid, 0, 2);
        return layout;
    }

    private void ConfigureAuctionGrid()
    {
        _auctionGrid.Dock = DockStyle.Fill;
        _auctionGrid.ReadOnly = true;
        _auctionGrid.AllowUserToAddRows = false;
        _auctionGrid.AllowUserToDeleteRows = false;
        _auctionGrid.AllowUserToResizeRows = false;
        _auctionGrid.AutoGenerateColumns = false;
        _auctionGrid.RowHeadersVisible = false;
        _auctionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        // Parchment, not stone: the exchange keeps its own look inside the dark launcher.
        DaocTheme.StyleGrid(_auctionGrid, DaocGridTone.Parchment);
        _auctionGrid.DataSource = _auctionSource;
        _auctionGrid.Columns.Add(TextColumn("Item", "ItemName", 230, DataGridViewAutoSizeColumnMode.Fill));
        _auctionGrid.Columns.Add(TextColumn("Seller", "Seller", 130));
        _auctionGrid.Columns.Add(TextColumn("Level", "ItemLevel", 60));
        _auctionGrid.Columns.Add(TextColumn("Qty", "Quantity", 55));
        _auctionGrid.Columns.Add(TextColumn("Sale type", "CurrentBid", 105));
        _auctionGrid.Columns.Add(TextColumn("Price", "Buyout", 120));
        _auctionGrid.Columns.Add(TextColumn("Expires / remaining", "Expires", 210));
        _auctionGrid.Columns.Add(TextColumn("Realm", "State", 85));
        foreach (DataGridViewColumn column in _auctionGrid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _auctionGrid.ColumnHeaderMouseClick += (_, eventArgs) => SortAuctionsByColumn(eventArgs.ColumnIndex);
    }

    private Control BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        DaocTheme.StyleGrid(_grid);
        // Long headers ("Realm Points") wrap onto two lines so the columns can narrow at 960 px.
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.DataSource = _botSource;
        _grid.Columns.Add(TextColumn("Name", "Name", 120));
        _grid.Columns.Add(TextColumn("Realm", "Realm", 88));
        _grid.Columns.Add(TextColumn("Race", "RaceName", 95));
        _grid.Columns.Add(TextColumn("Gender", "Gender", 60));
        _grid.Columns.Add(TextColumn("Class", "ClassName", 105));
        _grid.Columns.Add(TextColumn("Type", "PlayerType", 95));
        _grid.Columns.Add(TextColumn("Guild", "GuildName", 150));
        _grid.Columns.Add(TextColumn("Charter", "GuildCharter", 90));
        _grid.Columns.Add(TextColumn("Lvl", "Level", 45));
        _grid.Columns.Add(TextColumn("Realm Points", "RealmPointsDisplay", 100));
        _grid.Columns[^1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns.Add(TextColumn("Zone", "ZoneName", 110));
        _grid.Columns.Add(TextColumn("Task left", "TaskRemaining", 95));
        _grid.Columns.Add(TextColumn("Activity", "Activity", 210, DataGridViewAutoSizeColumnMode.Fill));
        _grid.Columns.Add(TextColumn("State", "State", 65));
        UseFillColumns(_grid);
        foreach (DataGridViewColumn column in _grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.ColumnHeaderMouseClick += (_, eventArgs) => SortBotsByColumn(eventArgs.ColumnIndex);
        _grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0 || _grid.Rows[eventArgs.RowIndex].DataBoundItem is not BotRow bot) return;
            if (_grid.Columns[eventArgs.ColumnIndex].DataPropertyName == "Realm")
                eventArgs.CellStyle.ForeColor = DaocTheme.RealmColor(bot.Realm);

            if (!bot.IsOnline)
            {
                eventArgs.CellStyle.BackColor = eventArgs.RowIndex % 2 == 0
                    ? Color.FromArgb(29, 28, 25)
                    : Color.FromArgb(33, 31, 27);
                eventArgs.CellStyle.ForeColor = _grid.Columns[eventArgs.ColumnIndex].DataPropertyName == "Realm"
                    ? Color.FromArgb(eventArgs.CellStyle.ForeColor.R / 2 + 45, eventArgs.CellStyle.ForeColor.G / 2 + 42, eventArgs.CellStyle.ForeColor.B / 2 + 36)
                    : Color.FromArgb(126, 120, 107);
                eventArgs.CellStyle.SelectionBackColor = Color.FromArgb(68, 61, 50);
                eventArgs.CellStyle.SelectionForeColor = Color.FromArgb(188, 178, 153);
            }
        };
        _grid.SelectionChanged += (_, _) => UpdateDeleteButton();
        _grid.CellMouseDown += (_, eventArgs) =>
        {
            if (eventArgs.Button != MouseButtons.Right || eventArgs.RowIndex < 0)
                return;

            _grid.ClearSelection();
            DataGridViewRow row = _grid.Rows[eventArgs.RowIndex];
            row.Selected = true;
            _grid.CurrentCell = row.Cells[eventArgs.ColumnIndex >= 0 ? eventArgs.ColumnIndex : 0];
        };
        var menu = new ContextMenuStrip();
        var teleportItem = new ToolStripMenuItem("Teleport to");
        teleportItem.Click += async (_, _) => await TeleportToSelectedBotAsync();
        menu.Items.Add(teleportItem);
        menu.Items.Add(new ToolStripSeparator());
        var deleteItem = new ToolStripMenuItem("Delete character forever…");
        deleteItem.Click += async (_, _) => await DeleteSelectedBotAsync();
        menu.Items.Add(deleteItem);
        menu.Opening += (_, args) =>
        {
            BotRow? bot = SelectedBot();
            bool canTeleport = bot is { BotId: not null, IsOnline: true, DeletionQueued: false } && IsServerRunning();
            bool canDelete = bot is { CanDelete: true } && (!bot.IsCompanion || !(IsServerRunning() || HasExactServerProcess()));
            teleportItem.Enabled = canTeleport;
            teleportItem.ToolTipText = canTeleport ? $"Teleport your logged-in character to {bot!.Name}." : "The server and selected playerbot must both be online.";
            deleteItem.Enabled = canDelete;
            args.Cancel = bot == null || !canTeleport && !canDelete;
        };
        DaocMenu.Style(menu);
        _grid.ContextMenuStrip = menu;
        return _grid;
    }

    // Fill mode keeps every column on screen at the default width. Each column keeps its
    // original width as its share of the free space. With wrapped headers a column may narrow to
    // its widest header word; otherwise it keeps its whole one-line header.
    private static void UseFillColumns(DataGridView grid, bool wrapHeaders = true)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            int headerWidth = wrapHeaders
                ? column.HeaderText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => TextRenderer.MeasureText(word, DaocTheme.Fonts.Bold, Size.Empty, TextFormatFlags.NoPadding).Width)
                    .DefaultIfEmpty(0).Max()
                : TextRenderer.MeasureText(column.HeaderText, DaocTheme.Fonts.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
            column.FillWeight = Math.Max(2f, column.Width);
            // Sortable headers reserve room for the sort glyph and padding (about 24 px).
            column.MinimumWidth = Math.Max(44, headerWidth + 24);
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
    }

    private async Task RefreshDashboardAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        Dictionary<Button, bool> buttonStates = CaptureAndDisableButtonsForSnapshot();
        bool buttonsRestored = false;
        if (_refreshButton != null)
        {
            _refreshButton.Text = "READING…";
        }
        _footer.Text = "Reading current playerbot status…";
        try
        {
            var snapshot = await Task.Run(ReadSnapshot);
            _savedSimulationClock = snapshot.SavedSimulationClock;
            if (snapshot.ServerState == "Stopped")
                _stoppedSimulationClockLoaded = true;
            RefreshWorldSpeedState();
            // Restore the pre-refresh baseline while still on the UI thread,
            // then apply the authoritative states from the completed snapshot.
            // No click can be processed between these operations.
            RestoreButtonsAfterSnapshot(buttonStates);
            buttonsRestored = true;
            _bots.Clear();
            _bots.AddRange(snapshot.Bots);
            _groups.Clear();
            _groups.AddRange(snapshot.Groups);
            _rvrWorld = snapshot.RvrWorld;
            _rvrServerRunning = snapshot.ServerState.Equals("Running", StringComparison.OrdinalIgnoreCase);
            bool snapshotStarting = snapshot.ServerState.Equals("Starting", StringComparison.OrdinalIgnoreCase);
            string displayedServerState = _stoppingServer
                ? "Stopping…"
                : snapshotStarting
                    ? "Starting…"
                    : snapshot.ServerState;
            _serverState.Text = displayedServerState.ToUpperInvariant();
            _serverState.ForeColor = _stoppingServer
                ? DaocTheme.Danger
                : snapshotStarting
                    ? DaocTheme.Gold
                : snapshot.ServerState.Equals("Running", StringComparison.OrdinalIgnoreCase)
                    ? DaocTheme.Success
                    : DaocTheme.Muted;
            _onlineValue.Text = snapshot.Bots.Count(bot => bot.IsOnline).ToString("N0");
            _albionValue.Text = RealmRosterValue(snapshot.Bots, "Albion");
            _midgardValue.Text = RealmRosterValue(snapshot.Bots, "Midgard");
            _hiberniaValue.Text = RealmRosterValue(snapshot.Bots, "Hibernia");
            _performanceValue.Text = $"{snapshot.TickP95Ms:0.0} ms";
            _startButton.Enabled = _worldReady && !_resettingKeepsRelics && !_savingXpRates && !_stoppingServer && snapshot.ServerState == "Stopped" && File.Exists(_serverExecutable);
            if (_resetKeepsRelics is not null)
                _resetKeepsRelics.Enabled = !_resettingKeepsRelics && !_savingXpRates && snapshot.ServerState == "Stopped" && BotGoalsServerStopped();
            _stopButton.Enabled = !_stoppingServer && snapshot.ServerState is "Running" or "Starting";
            _playButton.Enabled = !_stoppingServer && snapshot.ServerState == "Running" && File.Exists(_clientConnector);
            UpdateXpRateControls(snapshot);
            ApplyFilter();
            UpdateDeleteButton();
            RenderActiveGroups();
            RenderActiveRvr();
            _dashboardFooterSummary = $"Realm Exchange {_auctions.Count:N0} listings • Memory {snapshot.ServerMemoryMb:0} MB • Bot status updated {DateTime.Now:T}";
            _footer.Text = _dashboardFooterSummary;
            UpdateAutoRefreshCountdown();
        }
        catch (Exception exception)
        {
            _footer.Text = $"Dashboard refresh failed: {exception.Message}";
        }
        finally
        {
            if (!buttonsRestored)
                RestoreButtonsAfterSnapshot(buttonStates);
            _refreshing = false;
            if (_refreshButton != null)
                _refreshButton.Text = "REFRESH";
        }
    }

    private async Task RefreshRealmExchangeAsync()
    {
        if (_refreshingExchange)
            return;

        _refreshingExchange = true;
        if (_refreshExchangeButton != null)
        {
            _refreshExchangeButton.Enabled = false;
            _refreshExchangeButton.Text = "READING EXCHANGE…";
        }
        _footer.Text = "Reading the Realm Exchange inventory…";
        try
        {
            List<AuctionRow> auctions = await Task.Run(ReadRealmExchangeSnapshot);
            _auctions.Clear();
            _auctions.AddRange(auctions);
            ApplyAuctionFilter();
            _dashboardFooterSummary = $"Realm Exchange {_auctions.Count:N0} listings • Exchange updated {DateTime.Now:T}";
            _footer.Text = _dashboardFooterSummary;
        }
        catch (Exception exception)
        {
            _footer.Text = $"Realm Exchange refresh failed: {exception.Message}";
        }
        finally
        {
            _refreshingExchange = false;
            if (_refreshExchangeButton != null)
            {
                _refreshExchangeButton.Text = "REFRESH REALM EXCHANGE";
                _refreshExchangeButton.Enabled = true;
            }
        }
    }

    private List<AuctionRow> ReadRealmExchangeSnapshot()
    {
        var auctions = new List<AuctionRow>();
        if (!File.Exists(_database))
            return auctions;

        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        if (!TableExists(connection, "Inventory") || !TableExists(connection, "ItemTemplate") ||
            !TableExists(connection, "ItemUnique"))
            return auctions;

        using var command = connection.CreateCommand();
        string listedUtcColumn = ColumnExists(connection, "Inventory", "RealmExchangeListedUtc")
            ? "COALESCE(i.RealmExchangeListedUtc, '')" : "''";
        command.CommandText = $"""
            SELECT COALESCE(NULLIF(u.Name, ''), NULLIF(t.Name, ''), 'Unknown item') AS ItemName,
                   COALESCE(c.Name, b.Name, 'Unknown seller') AS Seller,
                   COALESCE(u.Level, t.Level, 0) AS ItemLevel,
                   i.Count,
                   i.SellPrice,
                   CASE i.OwnerLot WHEN 65001 THEN 'Albion' WHEN 65002 THEN 'Midgard' WHEN 65003 THEN 'Hibernia' ELSE 'Unknown' END AS RealmName,
                   i.OwnerID, {listedUtcColumn} AS ListedUtc
            FROM Inventory i
            LEFT JOIN ItemTemplate t ON t.Id_nb = i.ITemplate_Id
            LEFT JOIN ItemUnique u ON u.Id_nb = i.UTemplate_Id
            LEFT JOIN DOLCharacters c ON c.DOLCharacters_ID = i.OwnerID
            LEFT JOIN offline_world_bots b ON i.OwnerID = ('offlinebot:' || b.BotId)
            WHERE i.OwnerLot IN (65001, 65002, 65003)
              AND i.SellPrice > 0
            ORDER BY RealmName, ItemLevel DESC, ItemName
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long buyoutCopper = reader.GetInt64(4);
            string expiry = "Never (player listing)";
            DateTime? expiresUtc = null;
            if (reader.GetString(6).StartsWith("offlinebot:", StringComparison.OrdinalIgnoreCase))
            {
                expiry = "24h from first load";
                if (DateTime.TryParse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime listedUtc))
                    expiresUtc = listedUtc.ToUniversalTime().AddHours(24);
            }
            auctions.Add(new AuctionRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
                "Fixed price", FormatCopper(buyoutCopper), buyoutCopper, expiresUtc, expiry, reader.GetString(5))
            {
                SimulationUtcNow = () => _simulationUtc,
            });
        }
        return auctions;
    }

    private Dictionary<Button, bool> CaptureAndDisableButtonsForSnapshot()
    {
        var states = DescendantControls(this)
            .OfType<Button>()
            .ToDictionary(button => button, button => button.Enabled);
        foreach (Button button in states.Keys)
            button.Enabled = false;
        return states;
    }

    private static void RestoreButtonsAfterSnapshot(IReadOnlyDictionary<Button, bool> states)
    {
        foreach ((Button button, bool enabled) in states)
        {
            if (!button.IsDisposed)
                button.Enabled = enabled;
        }
    }

    private static IEnumerable<Control> DescendantControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in DescendantControls(child))
                yield return descendant;
        }
    }

    private void UpdateXpRateControls(DashboardSnapshot snapshot)
    {
        _loadingXpRates = true;
        try
        {
            SelectXpRate(_playerXpRate, snapshot.PlayerXpRate);
            SelectXpRate(_botXpRate, snapshot.BotXpRate);
            SelectXpRate(_playerRpRate, snapshot.PlayerRpRate);
            SelectXpRate(_botRpRate, snapshot.BotRpRate);
            bool editable = snapshot.ServerState == "Stopped" && !_savingXpRates;
            _playerXpRate.Enabled = editable;
            _botXpRate.Enabled = editable;
            _playerRpRate.Enabled = editable;
            _botRpRate.Enabled = editable;
            _makeMeGm.Checked = snapshot.MakeMeGm;
            _makeMeGm.Enabled = editable;
            _realmRankNameplates.Checked = snapshot.RealmRankNameplates;
            _realmRankNameplates.Enabled = editable;
            _botRealmRankNameplates.Checked = snapshot.BotRealmRankNameplates;
            _botRealmRankNameplates.Enabled = editable;
            UpdateToaControls(snapshot.Toa, editable);
            string nameplateHelp = editable
                ? "Shows the realm rank after companion and player names on nameplates. Applies at the next server startup."
                : "Stop the server before changing this setting.";
            string botNameplateHelp = editable
                ? "Shows the realm rank after autonomous world bot names on nameplates (on by default). Applies at the next server startup."
                : "Stop the server before changing this setting.";
            _helpTip.SetToolTip(_realmRankNameplates, nameplateHelp);
            _helpTip.SetToolTip(_realmRankNameplatesNote, nameplateHelp);
            _helpTip.SetToolTip(_botRealmRankNameplates, botNameplateHelp);
            _helpTip.SetToolTip(_botRealmRankNameplatesNote, botNameplateHelp);
            if (_savingXpRates)
            {
                _xpSettingsStatus.ForeColor = DaocTheme.Success;
                return;
            }
            _xpSettingsStatus.Text = editable
                ? "Server stopped — choose a rate to save it for the next startup."
                : "XP RATE LOCKED — stop the server before changing these settings.";
            _xpSettingsStatus.ForeColor = editable ? DaocTheme.Success : DaocTheme.Muted;
        }
        finally
        {
            _loadingXpRates = false;
        }
    }

    private static void SelectXpRate(ComboBox selector, double rate)
    {
        XpRateOption? match = selector.Items.Cast<XpRateOption>()
            .FirstOrDefault(option => Math.Abs(option.Multiplier - rate) < 0.001);
        if (match == null)
        {
            match = new XpRateOption(rate, $"{rate:0.###}×  Custom");
            selector.Items.Add(match);
        }
        selector.SelectedItem = match;
    }

    private async Task SaveXpRateAsync(string key, ComboBox selector)
    {
        if (_loadingXpRates || _savingXpRates || selector.SelectedItem is not XpRateOption option)
            return;
        if (IsServerRunning() || HasExactServerProcess())
        {
            MessageBox.Show(this, "Experience rates can only be changed while the server is fully stopped.",
                "Server is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshDashboardAsync();
            return;
        }

        _savingXpRates = true;
        ShowXpRateApplying(option.Label, key);
        try
        {
            await Task.Run(() => PersistXpRate(key, option.Multiplier));
            _xpSettingsStatus.Text = $"APPLIED {option.Label}. It will be used on the next server startup.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to save XP rate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _savingXpRates = false;
            await RefreshDashboardAsync();
        }
    }

    private async Task SaveGmSettingAsync()
    {
        if (_loadingXpRates || _savingXpRates) return;
        bool enabled = _makeMeGm.Checked;
        if (IsServerRunning() || HasExactServerProcess())
        {
            MessageBox.Show(this, "Stop the server before changing GM access.", "Server is running");
            await RefreshDashboardAsync();
            return;
        }
        _savingXpRates = true;
        _makeMeGm.Enabled = _realmRankNameplates.Enabled = _botRealmRankNameplates.Enabled = _playerXpRate.Enabled = _botXpRate.Enabled = _playerRpRate.Enabled = _botRpRate.Enabled = _startButton.Enabled = false;
        SetToaControlsEnabled(false);
        _xpSettingsStatus.Text = "APPLYING GM SETTING…";
        _xpSettingsStatus.ForeColor = DaocTheme.Success;
        try { await Task.Run(() => PersistGmSetting(enabled)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to save GM setting"); }
        finally { _savingXpRates = false; await RefreshDashboardAsync(); }
    }

    private void PersistGmSetting(bool enabled)
    {
        if (IsServerRunning() || HasExactServerProcess())
            throw new InvalidOperationException("The server must be fully stopped.");
        string account = ReadCredentials().Account;
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        PersistGmSetting(connection, account, enabled);
    }

    private static void PersistGmSetting(SQLiteConnection connection, string account, bool enabled)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO offline_local_options(Key,Value) VALUES ('MakeMeGM',@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@value", enabled ? "true" : "false");
        command.ExecuteNonQuery();
        command.CommandText = "UPDATE Account SET PrivLevel=@privilege WHERE Name=@account";
        command.Parameters.AddWithValue("@privilege", enabled ? 2 : 1);
        command.Parameters.AddWithValue("@account", account);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Your local account must exist before changing GM access. Enter the game once, then stop the server and retry.");
        transaction.Commit();
    }

    private void ShowXpRateApplying(string label, string key)
    {
        _playerXpRate.Enabled = false;
        _botXpRate.Enabled = false;
        _playerRpRate.Enabled = false;
        _botRpRate.Enabled = false;
        _makeMeGm.Enabled = false;
        _realmRankNameplates.Enabled = false;
        _botRealmRankNameplates.Enabled = false;
        SetToaControlsEnabled(false);
        _startButton.Enabled = false;
        _xpSettingsStatus.ForeColor = DaocTheme.Success;
        string target = key switch
        {
            "xp_rate" => "YOUR PLAYER XP",
            "bot_xp_rate" => "AUTONOMOUS BOT XP",
            "rp_rate" => "YOUR PLAYER RP",
            "bot_rp_rate" => "AUTONOMOUS BOT RP",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported rate key."),
        };
        _xpSettingsStatus.Text = $"APPLYING {label} TO {target}…";
    }

    private void PersistXpRate(string key, double multiplier)
    {
        if (key is not ("xp_rate" or "bot_xp_rate" or "rp_rate" or "bot_rp_rate") || multiplier is not (1 or 2 or 3 or 5 or 10 or 15 or 20 or 25 or 50 or 100))
            throw new InvalidOperationException("Unsupported experience-rate selection.");
        bool serverRunning = _persistXpRateServerRunningOverride ?? (IsServerRunning() || HasExactServerProcess());
        if (serverRunning)
            throw new InvalidOperationException("The server started before the rate could be saved. Stop it and try again.");
        if (!File.Exists(_database))
            throw new InvalidOperationException("The prepared world database is missing.");

        string stored = multiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string description = key switch
        {
            "xp_rate" => "Real player character experience rate selected in the Offline DAoC launcher.",
            "bot_xp_rate" => "Autonomous player-bot experience rate selected in the Offline DAoC launcher.",
            "rp_rate" => "Real player character realm point rate selected in the Offline DAoC launcher.",
            _ => "Autonomous player-bot realm point rate selected in the Offline DAoC launcher.",
        };
        ServerPropertyRows.Upsert(_database, "rates", [new ServerPropertyRow(key, description, "1", stored)]);
    }

    private async Task SaveNameplateSettingAsync(CheckBox checkBox, string key, string description, string defaultValue)
    {
        if (_loadingXpRates || _savingXpRates) return;
        bool enabled = checkBox.Checked;
        if (IsServerRunning() || HasExactServerProcess())
        {
            MessageBox.Show(this, "Stop the server before changing the nameplate setting.", "Server is running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshDashboardAsync();
            return;
        }

        _savingXpRates = true;
        _realmRankNameplates.Enabled = false;
        _botRealmRankNameplates.Enabled = false;
        SetToaControlsEnabled(false);
        _xpSettingsStatus.ForeColor = DaocTheme.Success;
        _xpSettingsStatus.Text = "APPLYING NAMEPLATE SETTING…";
        try
        {
            await Task.Run(() => PersistNameplateSetting(key, description, defaultValue, enabled));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to save nameplate setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _savingXpRates = false;
            await RefreshDashboardAsync();
        }
    }

    private void PersistNameplateSetting(string key, string description, string defaultValue, bool enabled)
    {
        bool serverRunning = _persistXpRateServerRunningOverride ?? (IsServerRunning() || HasExactServerProcess());
        if (serverRunning)
            throw new InvalidOperationException("The server started before the setting could be saved. Stop it and try again.");
        if (!File.Exists(_database))
            throw new InvalidOperationException("The prepared world database is missing.");
        ServerPropertyRows.Upsert(_database, "pvp",
            [new ServerPropertyRow(key, description, defaultValue, enabled ? "true" : "false")]);
    }

    private void SetToaControlsEnabled(bool enabled)
    {
        _toaAtlantisOpen.Enabled = _toaLoot.Enabled = _toaDataPacks.Enabled = enabled;
        foreach (NumericUpDown number in _toaNumbers.Values) number.Enabled = enabled;
    }

    // Called from UpdateXpRateControls with _loadingXpRates set, so setting the values saves nothing.
    // A number with an edit not yet saved keeps the typed value.
    private void UpdateToaControls(ToaSettings settings, bool editable)
    {
        _toaAtlantisOpen.Checked = settings.AtlantisOpen;
        _toaLoot.Checked = settings.LootInAtlantis;
        _toaDataPacks.Checked = settings.DataPacks;
        foreach ((string name, NumericUpDown number) in _toaNumbers)
            if (!_toaPendingNumbers.Contains(name))
                number.Value = settings.NumberOf(name);
        // Privilege level 2 or 3 (set with /serverproperty) is neither open nor closed: say so beside the switch.
        _toaNotes["atlantis"].Text = settings.AtlantisOpen || settings.AtlantisPlvl >= ToaSettings.AtlantisClosedPlvl
            ? ToaSettings.Field("atlantis").Note
            : $"Now only for {(settings.AtlantisPlvl == 2 ? "GMs" : "admins")} (privilege level {settings.AtlantisPlvl}). Tick to open it to every player.";
        SetToaControlsEnabled(editable && !_savingXpRates);
        if (_savingXpRates) return;
        if (!editable) _toaNotice = null;
        _toaStatus.Text = editable
            ? _toaNotice ?? "Server stopped — change a setting to save it for the next server startup."
            : "TRIALS OF ATLANTIS LOCKED — stop the server before changing these settings.";
        _toaStatus.ForeColor = editable ? DaocTheme.Success : DaocTheme.Muted;
    }

    private void ToaNumberEdited(string name)
    {
        if (_loadingXpRates) return;
        _toaPendingNumbers.Add(name);
        _toaLastNumberEditUtc = DateTime.UtcNow;
    }

    private async Task SaveToaSwitchAsync(string name, CheckBox box)
    {
        if (_loadingXpRates || _savingXpRates) return;
        bool on = box.Checked;
        await SaveToaRowsAsync([(name, ToaSettings.SwitchRow(name, on))], $"{ToaSettings.Field(name).Label}: {(on ? "on" : "off")}");
    }

    private async Task SaveToaNumbersAsync()
    {
        // Another setting being written, or an edit still in progress: the next tick tries again.
        if (_toaPendingNumbers.Count == 0 || _savingXpRates || IsDisposed ||
            DateTime.UtcNow - _toaLastNumberEditUtc < ToaNumberSaveDelay)
            return;
        (string name, ServerPropertyRow row)[] rows = TakePendingToaNumberRows();
        await SaveToaRowsAsync(rows, string.Join(", ", rows.Select(row => $"{ToaSettings.Field(row.name).Label} {row.row.Value}")));
    }

    // The rows of the numbers still waiting for their delayed save; the pending set is emptied.
    private (string name, ServerPropertyRow row)[] TakePendingToaNumberRows()
    {
        var rows = ToaSettings.Fields.Where(field => _toaPendingNumbers.Contains(field.Name))
            .Select(field => (field.Name, ToaSettings.NumberRow(field.Name, (int)_toaNumbers[field.Name].Value))).ToArray();
        _toaPendingNumbers.Clear();
        return rows;
    }

    /// <summary>
    /// On close: a number still being typed is committed, and numbers waiting for their delayed save are written now
    /// (synchronously) while the server is stopped. With the server running they are discarded with the usual notice.
    /// </summary>
    private void FlushPendingToaNumbersOnClose()
    {
        // Reading Value commits text still being typed (NumericUpDown validates its edit text), which marks it pending.
        foreach (NumericUpDown number in _toaNumbers.Values)
            _ = number.Value;
        if (_toaPendingNumbers.Count == 0) return;
        (string name, ServerPropertyRow row)[] rows = TakePendingToaNumberRows();
        if (_persistXpRateServerRunningOverride ?? (IsServerRunning() || HasExactServerProcess()))
        {
            MessageBox.Show(this, "Trials of Atlantis settings can only be changed while the server is fully stopped.",
                "Server is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            PersistToaSettings(rows);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to save Trials of Atlantis setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveToaRowsAsync(IReadOnlyList<(string name, ServerPropertyRow row)> rows, string summary)
    {
        if (IsServerRunning() || HasExactServerProcess())
        {
            MessageBox.Show(this, "Trials of Atlantis settings can only be changed while the server is fully stopped.",
                "Server is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshDashboardAsync();
            return;
        }

        _savingXpRates = true;
        SetToaControlsEnabled(false);
        _startButton.Enabled = false;
        _toaStatus.ForeColor = DaocTheme.Success;
        _toaStatus.Text = $"APPLYING {summary}…";
        try
        {
            await Task.Run(() => PersistToaSettings(rows));
            _toaNotice = $"APPLIED {summary}. It will be used on the next server startup.";
        }
        catch (Exception exception)
        {
            _toaNotice = null;
            MessageBox.Show(this, exception.Message, "Unable to save Trials of Atlantis setting", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _savingXpRates = false;
            await RefreshDashboardAsync();
        }
    }

    private void PersistToaSettings(IReadOnlyList<(string name, ServerPropertyRow row)> rows)
    {
        bool ServerStopped() => !(_persistXpRateServerRunningOverride ?? (IsServerRunning() || HasExactServerProcess()));
        if (!ServerStopped())
            throw new InvalidOperationException("The server started before the setting could be saved. Stop it and try again.");
        if (!File.Exists(_database))
            throw new InvalidOperationException("The prepared world database is missing.");
        foreach ((string name, ServerPropertyRow row) in rows)
            ToaSettings.Save(_database, name, row, ServerStopped);
    }

    private LiveBotSnapshot? RequestLiveBotSnapshot()
    {
        string requestId = Guid.NewGuid().ToString("N");
        string requestPath = Path.Combine(_serverDirectory, "bot-world.request");
        string temporaryPath = requestPath + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, requestId);
            File.Move(temporaryPath, requestPath, true);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // One fixed PID-scoped temporary name is overwritten on the
                // next refresh, so an interrupted cleanup cannot accumulate.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(LiveBotSnapshotWaitMilliseconds);
        do
        {
            LiveBotSnapshot? snapshot = TryReadLiveBotSnapshot();
            if (snapshot?.RequestId == requestId && IsLiveBotSnapshotFresh(snapshot, DateTime.UtcNow))
                return snapshot;
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);
        return null;
    }

    private LiveBotSnapshot? TryReadLiveBotSnapshot()
    {
        string path = _worldFiles.BotWorld;
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<LiveBotSnapshot>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsLiveBotSnapshotFresh(LiveBotSnapshot? snapshot, DateTime nowUtc)
    {
        if (snapshot?.Running != true || snapshot.Bots == null)
            return false;
        TimeSpan age = nowUtc - snapshot.UpdatedUtc.ToUniversalTime();
        return age >= TimeSpan.FromSeconds(-5) &&
               age <= TimeSpan.FromMilliseconds(LiveBotSnapshotMaxAgeMilliseconds);
    }

    private DashboardSnapshot ReadSnapshot()
    {
        if (!File.Exists(_database)) return new DashboardSnapshot("Not configured", [], [], 0, 0, 0, 1, 1, 1, 1);
        var bots = new List<BotRow>();
        var running = IsServerRunning();
        using var serverProcess = FindExactServerProcess();
        var serverProcessPresent = serverProcess is not null;
        LiveBotSnapshot? liveSnapshot = running ? RequestLiveBotSnapshot() : null;
        bool liveSnapshotIsAuthoritative = IsLiveBotSnapshotFresh(liveSnapshot, DateTime.UtcNow);
        Dictionary<long, LiveBotStatus> liveBots = liveSnapshotIsAuthoritative
            ? liveSnapshot!.Bots.ToDictionary(bot => bot.BotId)
            : [];
        var active = 0;
        var memoryMb = serverProcessPresent ? serverProcess!.WorkingSet64 / 1024d / 1024d : 0d;
        var tickP95Ms = 0d;

        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        double playerXpRate = ReadServerRate(connection, "xp_rate", 1);
        double botXpRate = ReadServerRate(connection, "bot_xp_rate", 1);
        double playerRpRate = ReadServerRate(connection, "rp_rate", 1);
        double botRpRate = ReadServerRate(connection, "bot_rp_rate", 1);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TickP95Ms FROM offline_runtime_status WHERE Id=1";
            using var reader = command.ExecuteReader();
            if (reader.Read())
                tickP95Ms = reader.GetDouble(0);
        }

        using (var command = connection.CreateCommand())
        {
            bool hasObjectiveColumns = ColumnExists(connection, "offline_world_bots", "ObjectiveKind") &&
                                       ColumnExists(connection, "offline_world_bots", "ObjectiveAssignmentId") &&
                                       ColumnExists(connection, "offline_world_bots", "ObjectiveAssignedUtc") &&
                                       ColumnExists(connection, "offline_world_bots", "ObjectivePhase");
            string objectiveColumns = hasObjectiveColumns
                ? "ObjectiveKind, ObjectiveAssignmentId, ObjectiveAssignedUtc, ObjectivePhase"
                : "'' AS ObjectiveKind, '' AS ObjectiveAssignmentId, '' AS ObjectiveAssignedUtc, '' AS ObjectivePhase";
            objectiveColumns += ColumnExists(connection, "offline_world_bots", "ObjectiveExpiresUtc")
                ? ", COALESCE(ObjectiveExpiresUtc, '')" : ", '' AS ObjectiveExpiresUtc";
            bool hasGuildId = ColumnExists(connection, "offline_world_bots", "GuildId");
            string playerType = ColumnExists(connection, "offline_world_bots", "PlayerType")
                ? "COALESCE(b.PlayerType, '')" : "'' AS PlayerType";
            string guildCharter = hasGuildId && TableExists(connection, "offline_managed_guild_charters")
                ? "COALESCE((SELECT c.Charter FROM offline_managed_guild_charters c WHERE c.GuildId=b.GuildId AND c.IsManaged=1), '')"
                : "'' AS GuildCharter";
            string guildName = hasGuildId && TableExists(connection, "Guild")
                ? "COALESCE((SELECT g.GuildName FROM Guild g WHERE g.GuildID=b.GuildId), '')"
                : "'' AS GuildName";
            command.CommandText = $"SELECT BotId, Name, Realm, RaceName, Gender, ClassName, Level, COALESCE(ZoneName, '—'), Activity, CurrentGoal, TargetName, TravelDestination, ObjectiveProgress, IsOnline, IsRetired, COALESCE(ItineraryJson, ''), {objectiveColumns}, {playerType}, {guildCharter}, {guildName}, COALESCE(b.RealmPoints, 0) FROM offline_world_bots b ORDER BY Realm, Level DESC, Name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                long botId = reader.GetInt64(0);
                bool hasLiveStatus = liveBots.TryGetValue(botId, out LiveBotStatus? live);
                bool isOnline = running && (liveSnapshotIsAuthoritative ? hasLiveStatus : reader.GetBoolean(13));
                bool deletionQueued = reader.GetBoolean(14);
                int level = hasLiveStatus ? live!.Level : reader.GetInt32(6);
                string zoneName = hasLiveStatus ? live!.ZoneName : reader.GetString(7);
                string rawActivity = hasLiveStatus ? live!.Activity : reader.GetString(8);
                string currentGoal = hasLiveStatus ? live!.CurrentGoal : reader.GetString(9);
                string targetName = hasLiveStatus ? live!.TargetName : reader.GetString(10);
                string destination = hasLiveStatus ? live!.TravelDestination : reader.GetString(11);
                string progress = hasLiveStatus ? live!.ObjectiveProgress : reader.GetString(12);
                string itinerary = hasLiveStatus ? live!.ItineraryJson : reader.GetString(15);
                string objectiveKind = hasLiveStatus ? live!.ObjectiveKind : reader.GetString(16);
                string assignmentId = hasLiveStatus ? live!.ObjectiveAssignmentId : reader.GetString(17);
                string assignedUtc = hasLiveStatus ? live!.ObjectiveAssignedUtc : reader.GetString(18);
                string objectivePhase = hasLiveStatus ? live!.ObjectivePhase : reader.GetString(19);
                string objectiveExpiresUtc = hasLiveStatus ? live!.ObjectiveExpiresUtc : reader.GetString(20);
                string activity = deletionQueued
                    ? running || serverProcessPresent
                        ? "Deletion requested — waiting for safe server removal"
                        : "Deletion pending — Delete is available while the server is stopped"
                    : FormatBotActivity(rawActivity, currentGoal, targetName, destination, progress, isOnline);
                GroupMetadata? group = ParseGroupMetadata(itinerary);
                bots.Add(new BotRow(botId, reader.GetString(1), RealmName(reader.GetInt32(2)), reader.GetString(3), GenderName(reader.GetInt32(4)), reader.GetString(5), level, zoneName, activity, isOnline, true, deletionQueued,
                    group?.GroupId ?? string.Empty, group?.Phase ?? string.Empty, group?.SharedGoal ?? string.Empty, group?.Status ?? string.Empty,
                    objectiveKind, assignmentId, assignedUtc, objectivePhase)
                {
                    ObjectiveExpiresUtc = objectiveExpiresUtc,
                    PlayerType = reader.GetString(21),
                    GuildCharter = reader.GetString(22).Equals("Rvr", StringComparison.OrdinalIgnoreCase)
                        ? "PvP" : reader.GetString(22),
                    GuildName = reader.GetString(23),
                    RealmPoints = hasLiveStatus ? live!.RealmPoints ?? reader.GetInt64(24) : reader.GetInt64(24),
                    HasGroupTaskClock = group?.HasTaskClock == true,
                    TaskTimerPaused = group?.TaskTimerPaused == true,
                    TaskRemainingMilliseconds = group?.TaskRemainingMilliseconds ?? 0,
                    GroupTaskExpiresUtc = group?.TaskExpiresUtc ?? string.Empty,
                    TravelDeadlineUtc = group?.TravelDeadlineUtc ?? string.Empty,
                    MeetUpDeadlineUtc = group?.MeetUpDeadlineUtc ?? string.Empty,
                    GroupLeaderName = group?.LeaderName ?? string.Empty,
                    GroupRendezvousName = group?.RendezvousName ?? string.Empty,
                    GroupPullerName = group?.PullerName ?? string.Empty
                    ,GroupRole = group?.MemberRole ?? string.Empty,
                    SimulationUtcNow = () => _simulationUtc,
                    ServerRunningAtSnapshot = running,
                });
            }
        }
        if (TableExists(connection, "bot_profiles"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name, ClassId, Level, IsActive FROM bot_profiles ORDER BY Name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var classId = reader.GetInt32(1);
                var classInfo = ClassInfo(classId);
                bots.Add(new BotRow(null, reader.GetString(0), classInfo.Realm, "—", "—", classInfo.Name, reader.GetInt32(2), "With owner", "Companion", running && reader.GetBoolean(3), false, false, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty));
            }
        }

        // Persistent player companions (the current `/companions` roster), with
        // their real saved Realm Points and owner name where those are available.
        // Additive columns/tables are optional so an older save still loads.
        if (TableExists(connection, "player_companions"))
        {
            bool hasRealmPoints = ColumnExists(connection, "player_companions", "RealmPoints");
            string realmPointsColumn = hasRealmPoints ? "COALESCE(pc.RealmPoints, 0)" : "0";
            bool hasCharacters = TableExists(connection, "DOLCharacters");
            string ownerNameColumn = hasCharacters
                ? "COALESCE((SELECT c.Name FROM DOLCharacters c WHERE c.DOLCharacters_ID = pc.OwnerCharacterId), '')"
                : "''";
            // NULL owner account means the owning character no longer exists (orphan).
            string ownerAccountColumn = hasCharacters
                ? "(SELECT c.AccountName FROM DOLCharacters c WHERE c.DOLCharacters_ID = pc.OwnerCharacterId)"
                : "NULL";
            string? localAccount = TryReadLocalAccount();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pc.Name, pc.Realm, pc.ClassId, pc.Level, pc.IsActive, {realmPointsColumn}, {ownerNameColumn}, pc.CompanionId, {ownerAccountColumn} FROM player_companions pc ORDER BY pc.Name";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var classInfo = ClassInfo(reader.GetInt32(2));
                string ownerName = reader.GetString(6);
                bool orphaned = hasCharacters && reader.IsDBNull(8);
                string zoneName = ownerName.Length > 0 ? $"With {ownerName}" : orphaned ? "No owner (orphan)" : "With owner";
                // Only companions of this installation's account, or orphans, may be deleted here;
                // a friend's companions stay protected.
                bool canDelete = orphaned || localAccount != null &&
                                 string.Equals(reader.GetString(8), localAccount, StringComparison.OrdinalIgnoreCase);
                bots.Add(new BotRow(null, reader.GetString(0), RealmName(reader.GetInt32(1)), "—", "—", classInfo.Name, reader.GetInt32(3), zoneName, "Companion", running && reader.GetBoolean(4), canDelete, false, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty)
                {
                    RealmPoints = reader.GetInt64(5),
                    CompanionId = reader.GetString(7),
                    IsOrphanCompanion = orphaned,
                });
            }
        }

        active = bots.Count(bot => bot.IsOnline);
        List<GroupRow> groups = bots
            .Where(bot => bot.IsOnline && !bot.DeletionQueued && bot.GroupId.Length > 0)
            .GroupBy(bot => bot.GroupId, StringComparer.Ordinal)
            .Select(group =>
            {
                List<BotRow> members = group.ToList();
                // Status writes are deliberately asynchronous. During the tiny
                // boundary after a leader leaves, prefer a row whose published
                // leader is actually present. Never advertise an absent bot as
                // the active leader while the server elects and republishes.
                BotRow first = members.First();
                BotRow? coherent = members.FirstOrDefault(member => members.Any(candidate =>
                    candidate.Name.Equals(member.GroupLeaderName, StringComparison.OrdinalIgnoreCase)));
                BotRow source = coherent ?? first;
                string leader = coherent != null ? source.GroupLeaderName : "Re-electing…";
                return new GroupRow(group.Key, source.Realm, source.GroupPhase, source.GroupGoal, source.GroupStatus,
                    leader, source.GroupRendezvousName, source.GroupPullerName, members);
            })
            // The server dissolves these immediately; avoid flashing stale
            // one-member metadata during the asynchronous snapshot boundary.
            .Where(group => group.Members.Count >= 2)
            .ToList();
        var serverState = running ? "Running" : serverProcessPresent ? "Starting" : "Stopped";
        bool makeMeGm = false;
        if (TableExists(connection, "offline_local_options"))
        {
            using var gm = connection.CreateCommand();
            gm.CommandText = "SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'";
            makeMeGm = string.Equals(gm.ExecuteScalar()?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        return new DashboardSnapshot(serverState, bots, groups, active, memoryMb, tickP95Ms, playerXpRate, botXpRate,
            playerRpRate, botRpRate, makeMeGm, ReadRvrWorldSnapshot(_serverDirectory, _database), WorldSimulationClock.ReadCheckpoint(connection))
        {
            RealmRankNameplates = ReadServerFlag(connection, RealmRankNameplateKey),
            BotRealmRankNameplates = ReadServerFlag(connection, BotRealmRankNameplateKey, fallback: true),
            Toa = ToaSettings.FromRows(ServerPropertyRows.Read(connection, ToaSettings.RowKeys)),
        };
    }

    private static double ReadServerRate(SQLiteConnection connection, string key, double fallback)
    {
        if (!TableExists(connection, "ServerProperty"))
            return fallback;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ServerProperty WHERE `Key`=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", key);
        object? value = command.ExecuteScalar();
        return value != null && double.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    // A missing row means the server has not stored the property yet, so it will start with its default (fallback).
    private static bool ReadServerFlag(SQLiteConnection connection, string key, bool fallback = false)
    {
        if (!TableExists(connection, "ServerProperty"))
            return fallback;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ServerProperty WHERE `Key`=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", key);
        object? value = command.ExecuteScalar();
        if (value == null || value is DBNull)
            return fallback;
        return string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static GroupMetadata? ParseGroupMetadata(string raw)
    {
        const string prefix = "offline-group-v1:";
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        try
        {
            GroupMetadata? metadata = JsonSerializer.Deserialize<GroupMetadata>(raw[prefix.Length..]);
            return string.IsNullOrWhiteSpace(metadata?.GroupId) ? null : metadata;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ApplyRvrStatusStyle(DataGridViewCellStyle style, string? status)
    {
        string state = status?.Trim() ?? string.Empty;
        Color color = state.Contains("rally", StringComparison.OrdinalIgnoreCase) ? DaocTheme.GoldLight
            : state.StartsWith("SIEGE", StringComparison.OrdinalIgnoreCase) || state.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase) || state.StartsWith("RAID", StringComparison.OrdinalIgnoreCase)
                ? DaocTheme.Danger
            : state.StartsWith("ESCORT", StringComparison.OrdinalIgnoreCase) || state.StartsWith("INTERCEPT", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(196, 160, 240)
            : DaocTheme.Text;
        style.ForeColor = color;
        // Selection must not replace the status color with the generic gold.
        style.SelectionForeColor = color;
        style.SelectionBackColor = Color.FromArgb(55, 49, 42);
    }

    private void RenderActiveRvr()
    {
        RenderRealmEvents();
        _rvrUpdated.Text = _rvrWorld == null ? "FRONTIER EVENTS — awaiting the server's first snapshot"
            : $"FRONTIER EVENTS  ·  {(_rvrServerRunning ? DateTime.UtcNow - _rvrWorld.UpdatedUtc > TimeSpan.FromMinutes(6) ? "Old snapshot" : "Snapshot" : "Server stopped — last known state")} {_rvrWorld.UpdatedUtc.ToLocalTime():g}  ·  Red: battle/drop  ·  Purple: relic carrier  ·  Gold: rally";
    }

    // Runs on a worker thread. The keep/relic reset time is read from the save on each call, so a
    // reset hides older snapshots without any UI-thread cache.
    private static RvrWorldSnapshot? ReadRvrWorldSnapshot(string serverDirectory, string database)
    {
        try
        {
            string path = WorldSaveLayout.SideFilePath(serverDirectory, WorldSaveLayout.RvrWorldFileName, database);
            if (!File.Exists(path) || new FileInfo(path).Length > 4_000_000) return null;
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<RvrWorldSnapshot>(File.ReadAllText(path));
            DateTime resetUtc = KeepRelicReset.ReadResetUtc(database);
            return snapshot?.UpdatedUtc.ToUniversalTime() < resetUtc ? null : snapshot;
        }
        catch (SQLiteException) { return null; }
        catch (IOException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private void ApplyFilter()
    {
        var realm = _realmFilter.SelectedItem?.ToString() ?? "All realms";
        var search = _search.Text.Trim();
        IEnumerable<BotRow> filtered = _bots.Where(bot =>
            (!_onlineOnly.Checked || bot.IsOnline) &&
            (_showCompanions.Checked || !(bot.IsCompanion || bot.Activity == "Companion")) &&
            (realm == "All realms" || bot.Realm == realm) &&
            (search.Length == 0 || $"{bot.Name} {bot.Realm} {bot.RaceName} {bot.Gender} {bot.ClassName} {bot.PlayerType} {bot.GuildName} {bot.GuildCharter} {bot.ZoneName} {bot.Activity}".Contains(search, StringComparison.OrdinalIgnoreCase)));
        _botSource.DataSource = SortBots(filtered).ToList();
        _botSource.ResetBindings(false);
        UpdateBotSortGlyph();
    }

    private void SortBotsByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _grid.Columns.Count)
            return;

        string property = _grid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
            return;

        if (_botSortProperty.Equals(property, StringComparison.Ordinal))
            _botSortAscending = !_botSortAscending;
        else
        {
            _botSortProperty = property;
            _botSortAscending = true;
        }

        ApplyFilter();
    }

    private IEnumerable<BotRow> SortBots(IEnumerable<BotRow> bots)
    {
        static IOrderedEnumerable<BotRow> TextAscending(IEnumerable<BotRow> rows, Func<BotRow, string> selector) =>
            rows.OrderBy(selector, StringComparer.OrdinalIgnoreCase).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase);
        static IOrderedEnumerable<BotRow> TextDescending(IEnumerable<BotRow> rows, Func<BotRow, string> selector) =>
            rows.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase);

        return (_botSortProperty, _botSortAscending) switch
        {
            ("TaskRemaining", true) => bots.OrderBy(bot => bot.RemainingMilliseconds ?? long.MaxValue).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("TaskRemaining", false) => bots.OrderByDescending(bot => bot.RemainingMilliseconds ?? -1).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("Level", true) => bots.OrderBy(bot => bot.Level).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("Level", false) => bots.OrderByDescending(bot => bot.Level).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("RealmPointsDisplay", true) => bots.OrderBy(bot => bot.RealmPoints ?? -1).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("RealmPointsDisplay", false) => bots.OrderByDescending(bot => bot.RealmPoints ?? -1).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("BotId", true) => bots.OrderBy(bot => bot.BotId).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("BotId", false) => bots.OrderByDescending(bot => bot.BotId).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase),
            ("State", true) => TextAscending(bots, bot => bot.State),
            ("State", false) => TextDescending(bots, bot => bot.State),
            ("Realm", true) => TextAscending(bots, bot => bot.Realm),
            ("Realm", false) => TextDescending(bots, bot => bot.Realm),
            ("RaceName", true) => TextAscending(bots, bot => bot.RaceName),
            ("RaceName", false) => TextDescending(bots, bot => bot.RaceName),
            ("Gender", true) => TextAscending(bots, bot => bot.Gender),
            ("Gender", false) => TextDescending(bots, bot => bot.Gender),
            ("ClassName", true) => TextAscending(bots, bot => bot.ClassName),
            ("ClassName", false) => TextDescending(bots, bot => bot.ClassName),
            ("ZoneName", true) => TextAscending(bots, bot => bot.ZoneName),
            ("ZoneName", false) => TextDescending(bots, bot => bot.ZoneName),
            ("Activity", true) => TextAscending(bots, bot => bot.Activity),
            ("Activity", false) => TextDescending(bots, bot => bot.Activity),
            (_, true) => TextAscending(bots, bot => bot.Name),
            _ => TextDescending(bots, bot => bot.Name),
        };
    }

    private void UpdateBotSortGlyph()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName.Equals(_botSortProperty, StringComparison.Ordinal)
                ? _botSortAscending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private void ApplyAuctionFilter()
    {
        var search = _auctionSearch.Text.Trim();
        string realm = _auctionRealmTabs.SelectedTab?.Text ?? "Albion";
        IEnumerable<AuctionRow> filtered = _auctions.Where(row => row.State.Equals(realm, StringComparison.OrdinalIgnoreCase) &&
            (search.Length == 0 || $"{row.ItemName} {row.Seller} {row.State}".Contains(search, StringComparison.OrdinalIgnoreCase)));
        _auctionSource.DataSource = SortAuctions(filtered).ToList();
        _auctionSource.ResetBindings(false);
        UpdateAuctionSortGlyph();
    }

    private void SortAuctionsByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _auctionGrid.Columns.Count)
            return;

        string property = _auctionGrid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
            return;

        if (_auctionSortProperty.Equals(property, StringComparison.Ordinal))
            _auctionSortAscending = !_auctionSortAscending;
        else
        {
            _auctionSortProperty = property;
            _auctionSortAscending = true;
        }

        ApplyAuctionFilter();
    }

    private IEnumerable<AuctionRow> SortAuctions(IEnumerable<AuctionRow> rows)
    {
        static IOrderedEnumerable<AuctionRow> TextAscending(IEnumerable<AuctionRow> source, Func<AuctionRow, string> selector) =>
            source.OrderBy(selector, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase);
        static IOrderedEnumerable<AuctionRow> TextDescending(IEnumerable<AuctionRow> source, Func<AuctionRow, string> selector) =>
            source.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase);

        return (_auctionSortProperty, _auctionSortAscending) switch
        {
            ("ItemLevel", true) => rows.OrderBy(row => row.ItemLevel).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("ItemLevel", false) => rows.OrderByDescending(row => row.ItemLevel).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("Quantity", true) => rows.OrderBy(row => row.Quantity).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("Quantity", false) => rows.OrderByDescending(row => row.Quantity).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("Buyout", true) => rows.OrderBy(row => row.BuyoutCopper).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("Buyout", false) => rows.OrderByDescending(row => row.BuyoutCopper).ThenBy(row => row.ItemName, StringComparer.OrdinalIgnoreCase),
            ("Seller", true) => TextAscending(rows, row => row.Seller),
            ("Seller", false) => TextDescending(rows, row => row.Seller),
            ("CurrentBid", true) => TextAscending(rows, row => row.CurrentBid),
            ("CurrentBid", false) => TextDescending(rows, row => row.CurrentBid),
            ("Expires", true) => TextAscending(rows, row => row.Expires),
            ("Expires", false) => TextDescending(rows, row => row.Expires),
            ("State", true) => TextAscending(rows, row => row.State),
            ("State", false) => TextDescending(rows, row => row.State),
            (_, true) => TextAscending(rows, row => row.ItemName),
            _ => TextDescending(rows, row => row.ItemName),
        };
    }

    private void UpdateAuctionSortGlyph()
    {
        foreach (DataGridViewColumn column in _auctionGrid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName.Equals(_auctionSortProperty, StringComparison.Ordinal)
                ? _auctionSortAscending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private IReadOnlyList<BotCharacterGenerator.Identity> GenerateBotCharacters(int realm, int count, int level = 1)
    {
        if (!File.Exists(_database))
            throw new InvalidOperationException("The prepared world database is missing.");
        if (count is not (1 or 10 or 100))
            throw new ArgumentOutOfRangeException(nameof(count));
        if (level is not (1 or 50)) throw new ArgumentOutOfRangeException(nameof(level));
        if (!BotGoalsServerStopped()) throw new InvalidOperationException("Stop the server before adding bots.");
        var settings = OfflineDaoc.Configuration.BotGoalSettings.Load(
            Path.Combine(_serverDirectory, OfflineDaoc.Configuration.BotGoalSettings.FileName));
        int[] levels = level == 50 ? Enumerable.Repeat(50, count).ToArray() :
            OfflineDaoc.Configuration.AutonomousPopulationShape.NewLevels(settings.WorldShape, count);

        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using (var busy = connection.CreateCommand())
        {
            busy.CommandText = "PRAGMA busy_timeout=10000; PRAGMA foreign_keys=OFF;";
            busy.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        IReadOnlyList<BotCharacterGenerator.Identity> identities =
            BotRosterWriter.Add(connection, transaction, realm, levels, BotRosterWriter.ReservedNames(connection, transaction));
        BotRosterWriter.UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return identities;
    }

    private async Task DeleteSelectedBotAsync()
    {
        BotRow? bot = SelectedBot();
        bool serverRunning = IsServerRunning() || HasExactServerProcess();
        if (bot?.CanDelete != true)
            return;

        if (bot.IsCompanion)
        {
            if (serverRunning)
            {
                MessageBox.Show(this,
                    "Companions can only be deleted from the launcher while the server is stopped. To delete one now, use the in-game Companion Manager, or stop the server first.",
                    "Server is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string ownerPart = bot.ZoneName.StartsWith("With ", StringComparison.Ordinal) && bot.ZoneName != "With owner"
                ? $" (with {bot.ZoneName["With ".Length..]})"
                : string.Empty;
            string companionWarning = $"Permanently delete your companion {bot.Name}, the level {bot.Level} {bot.ClassName}{ownerPart}?\n\n" +
                                      "This removes the companion and all of its equipment and backpack items. This cannot be undone.";
            if (MessageBox.Show(this, companionWarning, "Delete companion forever", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                DeleteCompanion(bot.CompanionId);
                await RefreshDashboardAsync();
                MessageBox.Show(this, $"{bot.Name} and all of its items were permanently deleted.", "Companion deleted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Unable to delete companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (bot.BotId is null || bot.DeletionQueued && serverRunning)
            return;

        string warning = $"Permanently delete {bot.Name}, the level {bot.Level} {bot.RaceName} {bot.ClassName}?\n\n" +
                         "This removes the character, equipment, backpack, bank contents, coins, and Realm Exchange listings. This cannot be undone.";
        if (MessageBox.Show(this, warning, "Delete bot character forever", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            bool queued = QueueOrDeleteBot(bot.BotId.Value);
            await RefreshDashboardAsync();
            MessageBox.Show(this,
                queued ? $"{bot.Name} was marked for deletion. The server will boot the live bot first, then permanently remove its character data." : $"{bot.Name} and all owned character data were permanently deleted.",
                queued ? "Deletion queued" : "Character deleted", MessageBoxButtons.OK,
                queued ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to delete character", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TeleportToSelectedBotAsync()
    {
        BotRow? bot = SelectedBot();
        if (bot is not { BotId: not null, IsOnline: true, DeletionQueued: false } || !IsServerRunning())
        {
            MessageBox.Show(this, "The server and selected playerbot must both be online.", "Teleport unavailable",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string account = ReadCredentials().Account;
            await Task.Run(() => QueueTeleportCommand(bot.BotId.Value, account));
            _footer.Text = $"Teleport requested — your logged-in character will move to {bot.Name}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to teleport", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void QueueTeleportCommand(long botId, string account)
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=5");
        connection.Open();
        InsertOrReplaceTeleportCommand(connection, botId, account, DateTime.UtcNow);
    }

    internal static void InsertOrReplaceTeleportCommand(SQLiteConnection connection, long botId, string account, DateTime requestedUtc)
    {
        if (connection == null || connection.State != System.Data.ConnectionState.Open)
            throw new InvalidOperationException("The local game database is not open.");
        if (string.IsNullOrWhiteSpace(account))
            throw new InvalidOperationException("The launcher account is not configured.");
        if (!TableExists(connection, "offline_world_bots") || !TableExists(connection, "offline_bot_commands") ||
            !ColumnExists(connection, "offline_bot_commands", "RequestedByAccount"))
            throw new InvalidOperationException("The updated server is still starting. Wait until Enter Realm is available, then try again.");

        using var transaction = connection.BeginTransaction();
        using (var target = connection.CreateCommand())
        {
            target.Transaction = transaction;
            target.CommandText = "SELECT COUNT(*) FROM offline_world_bots WHERE BotId=@id AND IsOnline=1 AND IsRetired=0";
            target.Parameters.AddWithValue("@id", botId);
            if (Convert.ToInt32(target.ExecuteScalar()) != 1)
                throw new InvalidOperationException("The selected playerbot is no longer online.");
        }

        string now = requestedUtc.ToString("O");
        int replaced;
        using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction;
            replace.CommandText = """
                UPDATE offline_bot_commands
                SET BotId=@id, RequestedUtc=@now, State='Pending', CompletedUtc=NULL, Error=NULL
                WHERE CommandId = (
                    SELECT CommandId FROM offline_bot_commands
                    WHERE CommandType='TeleportToBot' AND RequestedByAccount=@account AND State<>'Processing'
                    ORDER BY CASE WHEN State='Pending' THEN 0 ELSE 1 END, CommandId DESC
                    LIMIT 1)
                """;
            replace.Parameters.AddWithValue("@id", botId);
            replace.Parameters.AddWithValue("@now", now);
            replace.Parameters.AddWithValue("@account", account.Trim());
            replaced = replace.ExecuteNonQuery();
        }

        if (replaced == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO offline_bot_commands
                    (BotId, CommandType, RequestedUtc, State, RequestedByAccount)
                VALUES (@id, 'TeleportToBot', @now, 'Pending', @account)
                """;
            insert.Parameters.AddWithValue("@id", botId);
            insert.Parameters.AddWithValue("@now", now);
            insert.Parameters.AddWithValue("@account", account.Trim());
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private async Task DeleteAllBotsAsync()
    {
        bool serverRunning = IsServerRunning() || HasExactServerProcess();
        int count = _bots.Count(bot => bot.BotId.HasValue && (!bot.DeletionQueued || !serverRunning));
        if (count <= 0)
            return;

        string warning = $"Are you sure you want to permanently delete all {count:N0} bot characters?\n\n" +
                         "This removes every bot's character progress, equipment, backpack, bank contents, coins, and associated Realm Exchange records. " +
                         "Your player account and player characters are not affected. This cannot be undone.";
        if (MessageBox.Show(this, warning, "Delete every bot forever?", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            bool queued = QueueOrDeleteAllBots();
            await RefreshDashboardAsync();
            MessageBox.Show(this,
                queued
                    ? "All bot characters were marked for permanent deletion. The running server will remove each live world actor before deleting its saved data."
                    : "All bot characters and all of their owned data were permanently deleted.",
                queued ? "Delete-all queued" : "All bots deleted", MessageBoxButtons.OK,
                queued ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to delete all bots", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool QueueOrDeleteBot(long botId)
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        bool serverRunning = IsServerRunning() || HasExactServerProcess();
        if (serverRunning)
        {
            using var retire = connection.CreateCommand();
            retire.Transaction = transaction;
            retire.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Deletion requested', CurrentGoal='', LastUpdateUtc=@now WHERE BotId=@id";
            retire.Parameters.AddWithValue("@id", botId);
            retire.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            if (retire.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The selected character no longer exists.");

            using var queue = connection.CreateCommand();
            queue.Transaction = transaction;
            queue.CommandText = "INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State) VALUES (@id, 'Delete', @now, 'Pending')";
            queue.Parameters.AddWithValue("@id", botId);
            queue.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            queue.ExecuteNonQuery();
        }
        else
        {
            PurgeBotData(connection, transaction, botId);
        }

        BotRosterWriter.UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return serverRunning;
    }

    private bool QueueOrDeleteAllBots()
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        bool serverRunning = IsServerRunning() || HasExactServerProcess();
        string now = DateTime.UtcNow.ToString("O");
        if (serverRunning)
        {
            using (var queue = connection.CreateCommand())
            {
                queue.Transaction = transaction;
                queue.CommandText = """
                    INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State)
                    SELECT b.BotId, 'Delete', @now, 'Pending'
                    FROM offline_world_bots b
                    WHERE b.IsRetired=0
                      AND NOT EXISTS (
                          SELECT 1 FROM offline_bot_commands c
                          WHERE c.BotId=b.BotId AND c.CommandType='Delete' AND c.State IN ('Pending','Processing'))
                    """;
                queue.Parameters.AddWithValue("@now", now);
                queue.ExecuteNonQuery();
            }

            using var retire = connection.CreateCommand();
            retire.Transaction = transaction;
            retire.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Deletion requested', CurrentGoal='', LastUpdateUtc=@now WHERE IsRetired=0";
            retire.Parameters.AddWithValue("@now", now);
            retire.ExecuteNonQuery();
        }
        else
        {
            foreach (string sql in new[]
            {
                "DELETE FROM Inventory WHERE OwnerID LIKE 'offlinebot:%'",
                "DELETE FROM offline_auction_escrow WHERE BidderBotId IN (SELECT BotId FROM offline_world_bots)",
                "DELETE FROM offline_auction_ledger WHERE SellerBotId IN (SELECT BotId FROM offline_world_bots) OR BuyerBotId IN (SELECT BotId FROM offline_world_bots)",
                "DELETE FROM offline_auction_listings WHERE SellerBotId IN (SELECT BotId FROM offline_world_bots) OR HighBidderBotId IN (SELECT BotId FROM offline_world_bots)",
                "DELETE FROM offline_bot_commands WHERE BotId IN (SELECT BotId FROM offline_world_bots)",
                "DELETE FROM offline_world_bots",
            })
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        BotRosterWriter.UpdatePopulationTarget(connection, transaction);
        transaction.Commit();
        return serverRunning;
    }

    private async Task DeleteOrphanedCompanionsAsync()
    {
        if (IsServerRunning() || HasExactServerProcess())
        {
            MessageBox.Show(this, "Orphaned companions can only be deleted while the server is stopped.", "Server is running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int count = _bots.Count(bot => bot.IsOrphanCompanion);
        if (count == 0)
            return;

        string warning = $"Permanently delete all {count} orphaned companion(s)?\n\n" +
                         "These companions belong to characters that no longer exist, so nobody can invite them. " +
                         "This removes them and all of their equipment and backpack items. Companions of existing characters, " +
                         "including your friend's, are not touched. This cannot be undone.";
        if (MessageBox.Show(this, warning, "Delete orphaned companions forever", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            int deleted = DeleteOrphanedCompanions();
            await RefreshDashboardAsync();
            MessageBox.Show(this, $"{deleted} orphaned companion(s) and all of their items were permanently deleted.", "Orphaned companions deleted",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Unable to delete orphaned companions", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private int DeleteOrphanedCompanions()
    {
        // Orphan: the owning character row no longer exists. Re-evaluated inside the
        // transaction, so only companions orphaned at commit time are removed.
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (IsServerRunning() || HasExactServerProcess())
            throw new InvalidOperationException("Orphaned companions can only be deleted while the server is stopped.");
        if (!TableExists(connection, "player_companions"))
            return 0;
        if (!TableExists(connection, "DOLCharacters"))
            throw new InvalidOperationException("The character table is missing, so orphaned companions cannot be identified. Nothing was deleted.");

        const string orphanFilter = "NOT EXISTS (SELECT 1 FROM DOLCharacters c WHERE c.DOLCharacters_ID = pc.OwnerCharacterId)";
        using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = $"DELETE FROM Inventory WHERE OwnerID IN (SELECT 'playercompanion:' || pc.CompanionId FROM player_companions pc WHERE {orphanFilter})";
            items.ExecuteNonQuery();
        }

        int deleted;
        using (var records = connection.CreateCommand())
        {
            records.Transaction = transaction;
            records.CommandText = $"DELETE FROM player_companions WHERE CompanionId IN (SELECT pc.CompanionId FROM player_companions pc WHERE {orphanFilter})";
            deleted = records.ExecuteNonQuery();
        }

        transaction.Commit();
        return deleted;
    }

    private void DeleteCompanion(string companionId)
    {
        // Companions are live in memory while the server runs, so the database is
        // only touched when the server is stopped. Items and the roster record are
        // removed in one transaction, mirroring the in-game PlayerCompanionRoster delete.
        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (IsServerRunning() || HasExactServerProcess())
            throw new InvalidOperationException("Companions can only be deleted from the launcher while the server is stopped.");

        // Re-check ownership: only this installation's account or an orphan (owner character gone).
        string? localAccount = TryReadLocalAccount();
        using (var owner = connection.CreateCommand())
        {
            owner.Transaction = transaction;
            if (!TableExists(connection, "DOLCharacters"))
                throw new InvalidOperationException("The character table is missing, so companion ownership cannot be checked. Nothing was deleted.");
            owner.CommandText = "SELECT (SELECT c.AccountName FROM DOLCharacters c WHERE c.DOLCharacters_ID = pc.OwnerCharacterId) FROM player_companions pc WHERE pc.CompanionId=@id";
            owner.Parameters.AddWithValue("@id", companionId);
            using var ownerReader = owner.ExecuteReader();
            if (!ownerReader.Read())
                throw new InvalidOperationException("The selected companion no longer exists.");
            if (!ownerReader.IsDBNull(0) &&
                (localAccount == null || !string.Equals(ownerReader.GetString(0), localAccount, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This companion belongs to another account. Only your own companions and orphaned companions can be deleted here.");
        }

        using (var items = connection.CreateCommand())
        {
            items.Transaction = transaction;
            items.CommandText = "DELETE FROM Inventory WHERE OwnerID=@owner";
            items.Parameters.AddWithValue("@owner", $"playercompanion:{companionId}");
            items.ExecuteNonQuery();
        }

        using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = "DELETE FROM player_companions WHERE CompanionId=@id";
            record.Parameters.AddWithValue("@id", companionId);
            if (record.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("The selected companion no longer exists.");
        }

        transaction.Commit();
    }

    private static void PurgeBotData(SQLiteConnection connection, SQLiteTransaction transaction, long botId)
    {
        string owner = $"offlinebot:{botId}";
        foreach (string sql in new[]
        {
            "DELETE FROM Inventory WHERE OwnerID=@owner",
            "DELETE FROM offline_auction_escrow WHERE BidderBotId=@id",
            "DELETE FROM offline_auction_ledger WHERE SellerBotId=@id OR BuyerBotId=@id",
            "DELETE FROM offline_auction_listings WHERE SellerBotId=@id OR HighBidderBotId=@id",
            "DELETE FROM offline_bot_commands WHERE BotId=@id",
            "DELETE FROM offline_world_bots WHERE BotId=@id",
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", botId);
            command.Parameters.AddWithValue("@owner", owner);
            command.ExecuteNonQuery();
        }
    }

    private BotRow? SelectedBot() => _grid.CurrentRow?.DataBoundItem as BotRow;

    private void UpdateDeleteButton()
    {
        bool serverRunning = IsServerRunning() || HasExactServerProcess();
        if (_deleteBotButton != null)
            _deleteBotButton.Enabled = SelectedBot() is { CanDelete: true } selected &&
                                       (selected.IsCompanion ? !serverRunning : (!selected.DeletionQueued || !serverRunning));
        if (_deleteOrphansButton != null)
            _deleteOrphansButton.Enabled = !serverRunning && _bots.Any(bot => bot.IsOrphanCompanion);
        if (_deleteAllBotsButton != null)
            _deleteAllBotsButton.Enabled = _bots.Any(bot => bot.BotId.HasValue &&
                                                            (!bot.DeletionQueued || !serverRunning));
    }

    private void SelectBot(string name)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is BotRow bot && bot.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private BotGoalsSettingsControl? _botGoalsSettings;
    private OpenBgSettingsControl? _openBgSettings;

    private bool BotGoalsServerStopped()
    {
        if (_stoppingServer || _serverProcess is { HasExited: false } || IsServerRunning()) return false;
        using var process = FindExactServerProcess();
        return process is null;
    }

    private async Task<bool> EnsureCamlannWorldAsync()
    {
        try
        {
            CamlannServerConfig.EnsureWorld(Path.Combine(_serverDirectory, "config", "serverconfig.xml"), Path.GetFileName(_database));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot prepare server configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        string? worldModel;
        try
        {
            worldModel = await Task.Run(() => CamlannWorldReset.ReadWorldModel(_database));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot read world state", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        // Either world's marker (Realm-1 or Camlann-1, docs/RVR_WORLDS.md) is ready to start.
        if (WorldSaveLayout.FromMarker(worldModel) is not null)
            return true;
        if (!File.Exists(_database))
        {
            MessageBox.Show(this, "The world database is missing.", "World reset unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (worldModel is not null && !string.Equals(worldModel, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, $"This installation has an unsupported world marker: {worldModel}.", "World reset unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (!BotGoalsServerStopped())
        {
            MessageBox.Show(this, "Stop the server completely before creating the new world.", "Server must be stopped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        DialogResult confirmation = MessageBox.Show(this,
            "This installation needs a one-time world reset before the server can start.\n\n" +
            "The reset discards characters, inventories, coins, bot profiles, bot settings, guilds, keep claims, relic state, Realm Exchange listings and event history.\n" +
            "World definitions, item templates, spawns and navigation meshes are kept.\n\n" +
            "A complete database backup will be written beside the save before anything is changed. Continue?",
            "Create the new world", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
            return false;

        _startButton.Enabled = _playButton.Enabled = false;
        try
        {
            var credentials = ReadCredentials();
            var result = await Task.Run(() => CamlannWorldReset.Apply(
                _database,
                Path.Combine(Path.GetDirectoryName(_database)!, "camlann-world-reset-backups"),
                credentials.Account,
                _worldFiles.EventRecords,
                BotGoalsServerStopped));
            MessageBox.Show(this,
                $"World reset complete.\n\nCharacters: {result.Characters}\nBots: {result.Bots}\nGuilds: {result.Guilds}\nKeeps returned to neutral: {result.Keeps}\nRelics homed: {result.Relics}\nEvent records cleared: {result.EventRecords}\n\nBackup: {result.Backup}",
                "New world ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "World reset failed — no partial reset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void StartServer()
    {
        if (!_worldReady)
        {
            MessageBox.Show(this, "Complete the one-time world reset before starting the server.", "World reset required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // The owned process check closes the small window between Process.Start
        // and the TCP listener coming online.  Without it, a second click can
        // start another CoreServer while the first one is still loading the
        // world, and both processes then contend for the same SQLite database.
        if (_resettingKeepsRelics || _savingXpRates || _serverProcess is { HasExited: false } || IsServerRunning() || HasExactServerProcess()) return;
        if (_botGoalsSettings?.HasUnsavedChanges == true)
        {
            MessageBox.Show(this, "Review and save or undo your Server population edits before starting the server.", "Unsaved population settings");
            return;
        }
        if (_openBgSettings?.HasUnsavedChanges == true)
        {
            MessageBox.Show(this, "Review and save or undo your open BG edits before starting the server.", "Unsaved open BG settings");
            return;
        }
        if (_toaPendingNumbers.Count > 0)
        {
            // Saved by the display clock within about a second; starting first would leave the old value.
            MessageBox.Show(this, "A Trials of Atlantis setting is still being saved. Start the server again in a moment.", "Saving settings");
            return;
        }
        try { OfflineDaoc.Configuration.BotGoalSettings.Load(Path.Combine(_serverDirectory, OfflineDaoc.Configuration.BotGoalSettings.FileName)); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Correct Server population settings before starting:\n" + ex.Message, "Invalid population settings");
            return;
        }
        if (!File.Exists(_serverExecutable))
        {
            MessageBox.Show(this, "The compiled server package is missing.", "Cannot start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var availableGb = AvailableMemoryBytes() / 1024d / 1024d / 1024d;
        if (availableGb < 4 && MessageBox.Show(this,
                $"Only {availableGb:F1} GB of memory is currently available. Close some programs before running a large bot population. Start anyway?",
                "Low available memory", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _startButton.Enabled = false;
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            _serverLog?.Dispose();
            _serverLog = new RollingServerLog(Path.Combine(_logsDirectory, "server-console.log"));
            var startInfo = new ProcessStartInfo(_serverExecutable)
            {
                WorkingDirectory = _serverDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _serverProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _serverProcess.OutputDataReceived += (_, args) => { if (args.Data is not null) _serverLog?.WriteLine(args.Data); };
            _serverProcess.ErrorDataReceived += (_, args) => { if (args.Data is not null) _serverLog?.WriteLine("ERROR: " + args.Data); };
            _serverProcess.Exited += OnServerExited;
            _serverProcess.Start();
            ShowStartingState();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            // Runtime status is dashboard metadata, not a prerequisite for the
            // server.  During startup the server may briefly own SQLite schema
            // and world-load write locks, so a failed status update must never
            // crash the launcher and close CoreServer's redirected stdin.
            TryUpdateRuntimeState("Starting", _serverProcess.Id);
            BeginServerReadinessPolling();
        }
        catch (Exception exception)
        {
            _serverReadinessPoll.Stop();
            _serverProcess?.Dispose();
            _serverProcess = null;
            _startButton.Enabled = true;
            MessageBox.Show(this, exception.Message, "Unable to start server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _ = RefreshDashboardAsync();
        }
    }

    private void OnServerExited(object? sender, EventArgs args)
    {
        // Process events arrive on a worker thread, including after closing
        // the launcher has destroyed its window handle.
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(async () =>
            {
                if (IsDisposed || Disposing || !ReferenceEquals(sender, _serverProcess))
                    return;
                _serverReadinessPoll.Stop();
                _stoppingServer = false;
                await RefreshDashboardAsync();
            });
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
        {
            // Closing can destroy the handle between the check and BeginInvoke.
        }
    }

    private void ShowStartingState()
    {
        _serverState.Text = "STARTING…";
        _serverState.ForeColor = DaocTheme.Gold;
        _startButton.Enabled = false;
        _stopButton.Enabled = false;
        _playButton.Enabled = false;
        _footer.Text = "Server is starting — Enter Realm will unlock automatically when it is ready…";
    }

    private async Task StopServerAsync()
    {
        _serverReadinessPoll.Stop();
        Process? externalProcess = null;
        try
        {
            bool ownsRunningProcess = _serverProcess is { HasExited: false };
            if (!ownsRunningProcess)
                externalProcess = FindExactServerProcess();

            if (!ownsRunningProcess && externalProcess is not null &&
                MessageBox.Show(this, "Gracefully stopping is unavailable because this launcher did not start the process. Force-close this exact local CoreServer process?", "Stop server", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            if (!ownsRunningProcess && externalProcess is null)
            {
                _stoppingServer = false;
                TryUpdateRuntimeState("Stopped", null);
                return;
            }

            ShowStoppingState();
            TryUpdateRuntimeState("Stopping", ownsRunningProcess ? _serverProcess!.Id : externalProcess!.Id);

            if (ownsRunningProcess)
            {
                _serverProcess!.StandardInput.WriteLine("exit");
                _serverProcess.StandardInput.Flush();
                if (!await WaitForExitWithinAsync(_serverProcess, 10_000))
                {
                    MessageBox.Show(this, "The server is still shutting down. It has not been force-closed.", "Shutdown pending", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            else
            {
                externalProcess!.Kill(true);
                if (!await WaitForExitWithinAsync(externalProcess, 5_000))
                {
                    MessageBox.Show(this, "The server process has not exited yet.", "Shutdown pending", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            _stoppingServer = false;
            TryUpdateRuntimeState("Stopped", null);
        }
        catch (Exception exception)
        {
            _stoppingServer = false;
            MessageBox.Show(this, exception.Message, "Unable to stop server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            externalProcess?.Dispose();
            await RefreshDashboardAsync();
        }
    }

    private void ShowStoppingState()
    {
        _stoppingServer = true;
        _serverState.Text = "STOPPING…";
        _serverState.ForeColor = DaocTheme.Danger;
        _startButton.Enabled = false;
        _stopButton.Enabled = false;
        _playButton.Enabled = false;
        _footer.Text = "Server is shutting down safely…";
    }

    private static async Task<bool> WaitForExitWithinAsync(Process process, int timeoutMilliseconds)
    {
        if (process.HasExited)
            return true;
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private void LaunchClient()
    {
        if (!IsServerRunning())
        {
            MessageBox.Show(this, "Start the local server first.", "Server stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!File.Exists(_clientConnector) || !File.Exists(Path.Combine(_clientDirectory, "game.dll")))
        {
            MessageBox.Show(this, "The isolated client or connection utility is missing.", "Client not ready", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var credentials = ReadCredentials();
        ClientDisplayPreferences.EnsureIsolatedLaunchProfile(_clientDirectory);
        ClientSessionDiagnostics.Prepare(_logsDirectory);
        Process? client = Process.Start(new ProcessStartInfo(_clientConnector)
        {
            WorkingDirectory = _clientDirectory,
            UseShellExecute = false,
            ArgumentList = { "game.dll", "127.0.0.1", credentials.Account, credentials.Password },
        });
        if (client != null)
            ClientSessionDiagnostics.Start(client, _clientDirectory, _logsDirectory);
    }

    private (string Account, string Password) ReadCredentials()
    {
        return PortableCredentials.ReadOrCreate(_root);
    }

    private string? TryReadLocalAccount()
    {
        try
        {
            return File.Exists(Path.Combine(_root, "account.txt")) ? ReadCredentials().Account : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsServerRunning()
    {
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == 10300);
    }

    private void BeginServerReadinessPolling()
    {
        _serverReadinessPoll.Stop();
        _serverReadinessPoll.Start();
    }

    private async Task RefreshWhenServerReadyAsync()
    {
        bool processExited = _serverProcess is { HasExited: true };
        bool serverReady = IsServerRunning();
        if (!serverReady && !processExited)
            return;

        // Do not stop the probe while another refresh owns the snapshot gate.
        // Its next lightweight tick will perform the one readiness refresh.
        if (_refreshing)
            return;

        await RefreshDashboardAsync();
        // A short SQLite startup lock may make the first snapshot fail. Keep
        // probing until the successful refresh actually enables Enter Realm.
        if (processExited || _playButton.Enabled || !File.Exists(_clientConnector))
            _serverReadinessPoll.Stop();
    }

    private bool HasExactServerProcess()
    {
        using var process = FindExactServerProcess();
        return process is not null;
    }

    private Process? FindExactServerProcess()
    {
        Process? match = null;
        foreach (var process in Process.GetProcessesByName("CoreServer"))
        {
            try
            {
                if (match is null && string.Equals(process.MainModule?.FileName, _serverExecutable, StringComparison.OrdinalIgnoreCase))
                {
                    match = process;
                    continue;
                }
            }
            catch
            {
                // The process may have exited or its image path may be inaccessible.
            }

            // GetProcessesByName creates a Process wrapper for every result.
            // Retain only the first match for callers that need its metadata;
            // presence-only callers use HasExactServerProcess and dispose it too.
            process.Dispose();
        }
        return match;
    }

    private bool TryUpdateRuntimeState(string state, int? pid)
    {
        try
        {
            using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=1");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE offline_runtime_status SET ServerState=@state, ServerPid=@pid, LastHeartbeatUtc=@now WHERE Id=1";
            command.Parameters.AddWithValue("@state", state);
            command.Parameters.AddWithValue("@pid", pid is null ? DBNull.Value : pid.Value);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
            return true;
        }
        catch (SQLiteException exception) when (exception.ResultCode is SQLiteErrorCode.Busy or SQLiteErrorCode.Locked)
        {
            _footer.Text = "Server state is changing; dashboard status will catch up on refresh.";
            return false;
        }
        catch (Exception exception)
        {
            _footer.Text = $"Server state metadata could not be updated: {exception.Message}";
            return false;
        }
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SQLiteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table.Replace("]", "]]", StringComparison.Ordinal)}])";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (string Realm, string Name) ClassInfo(int id) => id switch
    {
        1 => ("Albion", "Paladin"), 2 => ("Albion", "Armsman"), 3 => ("Albion", "Scout"), 4 => ("Albion", "Minstrel"),
        5 => ("Albion", "Theurgist"), 6 => ("Albion", "Cleric"), 7 => ("Albion", "Wizard"), 8 => ("Albion", "Sorcerer"),
        9 => ("Albion", "Infiltrator"), 10 => ("Albion", "Friar"), 11 => ("Albion", "Mercenary"), 12 => ("Albion", "Necromancer"),
        13 => ("Albion", "Cabalist"), 19 => ("Albion", "Reaver"),
        21 => ("Midgard", "Thane"), 22 => ("Midgard", "Warrior"), 23 => ("Midgard", "Shadowblade"), 24 => ("Midgard", "Skald"),
        25 => ("Midgard", "Hunter"), 26 => ("Midgard", "Healer"), 27 => ("Midgard", "Spiritmaster"), 28 => ("Midgard", "Shaman"),
        29 => ("Midgard", "Runemaster"), 30 => ("Midgard", "Bonedancer"), 31 => ("Midgard", "Berserker"), 32 => ("Midgard", "Savage"),
        40 => ("Hibernia", "Eldritch"), 41 => ("Hibernia", "Enchanter"), 42 => ("Hibernia", "Mentalist"), 43 => ("Hibernia", "Blademaster"),
        44 => ("Hibernia", "Hero"), 45 => ("Hibernia", "Champion"), 46 => ("Hibernia", "Warden"), 47 => ("Hibernia", "Druid"),
        48 => ("Hibernia", "Bard"), 49 => ("Hibernia", "Nightshade"), 50 => ("Hibernia", "Ranger"), 55 => ("Hibernia", "Animist"),
        56 => ("Hibernia", "Valewalker"),
        _ => ("Unknown", $"Class {id}"),
    };

    private static string RealmName(int value) => value switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "Unknown" };
    private static string GenderName(int value) => value switch { 1 => "Male", 2 => "Female", _ => "—" };

    private static string FormatBotActivity(string activity, string goal, string target, string destination, string progress, bool isOnline)
    {
        activity = activity?.Trim() ?? string.Empty;
        goal = goal?.Trim() ?? string.Empty;
        target = target?.Trim() ?? string.Empty;
        destination = destination?.Trim() ?? string.Empty;
        progress = progress?.Trim() ?? string.Empty;

        var details = new List<string>(3);
        if (goal.Length > 0) details.Add(goal);
        if (destination.Length > 0 && !goal.Contains(destination, StringComparison.OrdinalIgnoreCase)) details.Add(destination);
        if (target.Length > 0 && !goal.Contains(target, StringComparison.OrdinalIgnoreCase)) details.Add($"Target: {target}");
        if (progress.Length > 0) details.Add(progress);
        string live = details.Count > 0 ? string.Join(" — ", details) : activity.Length > 0 ? activity : "Choosing next activity";
        if (isOnline && activity.Length > 0 && !live.StartsWith(activity, StringComparison.OrdinalIgnoreCase))
            live = $"{activity}: {live}";
        live = live.Replace("RvR", "PvP", StringComparison.OrdinalIgnoreCase);
        return isOnline ? live : $"Parked — resumes {live}";
    }

    internal static string FormatCopper(long copper)
    {
        long platinum = copper / 1_000_000;
        long gold = copper % 1_000_000 / 10_000;
        long silver = copper % 10_000 / 100;
        long remainder = copper % 100;
        var parts = new List<string>(4);
        if (platinum > 0) parts.Add($"{platinum}p");
        if (gold > 0) parts.Add($"{gold}g");
        if (silver > 0) parts.Add($"{silver}s");
        if (remainder > 0 || parts.Count == 0) parts.Add($"{remainder}c");
        return string.Join(" ", parts);
    }

    private static string RealmRosterValue(IReadOnlyCollection<BotRow> bots, string realm)
    {
        int total = bots.Count(bot => bot.Realm == realm && !bot.DeletionQueued);
        int online = bots.Count(bot => bot.Realm == realm && bot.IsOnline && !bot.DeletionQueued);
        return $"{total:N0} ROSTER\n{online:N0} ONLINE";
    }

    private static string FormatExpiry(string value)
    {
        if (!DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires)) return value;
        var remaining = expires.ToUniversalTime() - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return "Expired";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{Math.Max(0, remaining.Minutes)}m";
    }

    private static Button ActionButton(string text, Color color) => new RuneButton
    {
        Text = text,
        Accent = color,
    };

    private static Panel Card(string caption, Label value, Color accent, Control? action = null)
    {
        var panel = new InsetPanel { Dock = DockStyle.Fill, BackColor = DaocTheme.Panel, Margin = new Padding(3), Accent = accent };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = action == null ? 2 : 3,
            ColumnCount = 1,
            Padding = new Padding(9, 3, 6, 4),
            BackColor = Color.Transparent,
        };
        // Constrain nested layouts to the card instead of their preferred width.
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (action != null)
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8f, FontStyle.Bold),
        };
        value.Dock = DockStyle.Fill;
        value.Padding = new Padding(0);
        value.Margin = new Padding(3, 0, 0, 0);
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Font = action == null
            ? new Font("Georgia", 15f, FontStyle.Bold)
            : new Font("Georgia", 10f, FontStyle.Bold);
        content.Controls.Add(captionLabel, 0, 0);
        content.Controls.Add(value, 0, 1);
        if (action != null)
        {
            action.Margin = new Padding(0, 1, 0, 0);
            content.Controls.Add(action, 0, 2);
        }

        panel.Controls.Add(content);
        var accentBar = new Panel { Dock = DockStyle.Left, Width = 3, BackColor = accent };
        panel.Controls.Add(accentBar);
        accentBar.BringToFront();
        return panel;
    }

    private static Label CardValue() => new()
    {
        Text = "0",
        Dock = DockStyle.Fill,
        Padding = new Padding(11, 4, 0, 0),
        TextAlign = ContentAlignment.TopLeft,
        Font = new Font("Georgia", 14f, FontStyle.Bold),
        ForeColor = DaocTheme.GoldLight,
    };

    private static DataGridViewTextBoxColumn TextColumn(string header, string property, int width, DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None) => new()
    {
        HeaderText = header,
        DataPropertyName = property,
        Width = width,
        AutoSizeMode = mode,
        SortMode = DataGridViewColumnSortMode.Automatic,
    };

    private static void StyleInput(Control input) => DaocTheme.StyleInput(input);

    private void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private const int VkLeftShift = 0xA0;
    private const int VkLeftControl = 0xA2;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static ulong AvailableMemoryBytes()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.AvailablePhysical : 0;
    }

    private sealed record BotRow(long? BotId, string Name, string Realm, string RaceName, string Gender, string ClassName, int Level, string ZoneName, string Activity, bool IsOnline, bool CanDelete, bool DeletionQueued,
        string GroupId, string GroupPhase, string GroupGoal, string GroupStatus, string ObjectiveKind, string ObjectiveAssignmentId, string ObjectiveAssignedUtc, string ObjectivePhase)
    {
        public Func<DateTime?> SimulationUtcNow { get; init; } = () => null;
        public bool ServerRunningAtSnapshot { get; init; }
        public string PlayerType { get; init; } = string.Empty;
        public string GuildCharter { get; init; } = string.Empty;
        public string GuildName { get; init; } = string.Empty;
        public long? RealmPoints { get; init; }
        public string CompanionId { get; init; } = string.Empty;
        public bool IsCompanion => CompanionId.Length > 0;
        public bool IsOrphanCompanion { get; init; }
        public string RealmPointsDisplay => RealmPoints?.ToString("N0") ?? "—";
        public string ObjectiveExpiresUtc { get; init; } = string.Empty;
        public bool HasGroupTaskClock { get; init; }
        public bool TaskTimerPaused { get; init; }
        public long TaskRemainingMilliseconds { get; init; }
        public string GroupTaskExpiresUtc { get; init; } = string.Empty;
        public string TravelDeadlineUtc { get; init; } = string.Empty;
        public string MeetUpDeadlineUtc { get; init; } = string.Empty;
        public string GroupLeaderName { get; init; } = string.Empty;
        public string GroupRendezvousName { get; init; } = string.Empty;
        public string GroupPullerName { get; init; } = string.Empty;
        public string GroupRole { get; init; } = string.Empty;
        public long? AssemblyRemainingMilliseconds =>
            (IsOnline || !ServerRunningAtSnapshot && BotId.HasValue) &&
            GroupPhase is ("Leader staging" or "Meeting up")
                ? TaskTimerDisplay.Remaining(MeetUpDeadlineUtc, SimulationUtcNow()) : null;
        public long? RemainingMilliseconds => (!IsOnline && ServerRunningAtSnapshot) || !BotId.HasValue || DeletionQueued || GroupPhase == "Player-led" ? null :
            AssemblyRemainingMilliseconds ?? (HasGroupTaskClock && TaskTimerPaused
                ? TaskRemainingMilliseconds
                : TaskTimerDisplay.Remaining(HasGroupTaskClock ? GroupTaskExpiresUtc : ObjectiveExpiresUtc, SimulationUtcNow()));
        public string TaskRemaining => (!IsOnline && ServerRunningAtSnapshot) || !BotId.HasValue ? "Offline" : GroupPhase == "Player-led" ? "Player-led" :
            GroupPhase == "Leader staging" && AssemblyRemainingMilliseconds is long staging
                ? TaskTimerDisplay.Format(staging) + " (staging)" :
            GroupPhase == "Meeting up" && AssemblyRemainingMilliseconds is long meetup
                ? TaskTimerDisplay.Format(meetup) + " (meetup)" :
            ObjectiveAssignmentId.StartsWith("between-pve-services-", StringComparison.Ordinal)
                ? "Town " + (RemainingMilliseconds is long town ? TaskTimerDisplay.Format(town) : "Unavailable") :
            RemainingMilliseconds is long remaining
                ? TaskTimerDisplay.Format(remaining) + (HasGroupTaskClock && TaskTimerPaused ? " (paused)" : string.Empty)
                : HasUnavailableDeadline ? "Unavailable" : "—";
        private bool HasUnavailableDeadline => SimulationUtcNow() is null &&
            (MeetUpDeadlineUtc.Length > 0 || HasGroupTaskClock && GroupTaskExpiresUtc.Length > 0 || ObjectiveExpiresUtc.Length > 0);
        public string GroupTimerText => GroupPhase switch
        {
            "Leader staging" => "LEADER STAGING: " + AssemblyTimerText,
            "Meeting up" => "MEETUP LEFT: " + AssemblyTimerText,
            _ when TaskTimerPaused && TravelDeadlineUtc.Length > 0 =>
                "CAMP TRAVEL LEFT: " + (TaskTimerDisplay.Remaining(TravelDeadlineUtc, SimulationUtcNow()) is long travel
                    ? TaskTimerDisplay.Format(travel) : "Unavailable"),
            _ => "TASK LEFT: " + TaskRemaining,
        };
        private string AssemblyTimerText => AssemblyRemainingMilliseconds is long remaining
            ? TaskTimerDisplay.Format(remaining)
            : MeetUpDeadlineUtc.Length > 0 && SimulationUtcNow() is null ? "Unavailable" : "—";
        public string NameWithMeetUpTimer => IsOnline && GroupPhase == "Meeting up" &&
            TaskTimerDisplay.Remaining(MeetUpDeadlineUtc, SimulationUtcNow()) is long remaining
                ? $"{Name} ({TaskTimerDisplay.Format(remaining)})"
                : IsOnline && GroupPhase == "Meeting up" && MeetUpDeadlineUtc.Length > 0 && SimulationUtcNow() is null
                    ? $"{Name} (unavailable)" : Name;
        public string State => DeletionQueued ? "Deleting…" : IsOnline ? "Online" : "Offline";
        public string RvrFormation => GroupId.Length > 0 ? $"Warband {GroupId}" : "Solo roamer";
    }

    private sealed record AuctionRow(string ItemName, string Seller, int ItemLevel, int Quantity, string CurrentBid,
        string Buyout, long BuyoutCopper, DateTime? ExpiresUtc, string ExpiryFallback, string State)
    {
        public Func<DateTime?> SimulationUtcNow { get; init; } = () => null;
        public string Expires => ExpiresUtc is DateTime expires
            ? TaskTimerDisplay.FormatAuctionExpiry(expires, SimulationUtcNow())
            : ExpiryFallback;
    }
    private sealed record GroupRow(string GroupId, string Realm, string Phase, string SharedGoal, string Status,
        string LeaderName, string RendezvousName, string PullerName, List<BotRow> Members)
    {
        private BotRow? TimerOwner => Phase == "Meeting up"
            ? Members.Where(member => member.AssemblyRemainingMilliseconds.HasValue)
                .OrderByDescending(member => member.AssemblyRemainingMilliseconds)
                .FirstOrDefault()
            : Members.FirstOrDefault();
        public long? RemainingMilliseconds => TimerOwner?.RemainingMilliseconds;
        public string TimeRemaining => TimerOwner?.GroupTimerText ?? "—";
        public string MemberNames => string.Join(", ", Members.Select(member => member.Name));
        public string Location
        {
            get
            {
                string[] zones = Members.Select(member => member.ZoneName)
                    .Where(zone => !string.IsNullOrWhiteSpace(zone))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string area = zones.Length switch
                {
                    0 => string.Empty,
                    1 => zones[0],
                    2 => string.Join(" / ", zones),
                    _ => $"{zones[0]} (+{zones.Length - 1})",
                };
                if (string.IsNullOrWhiteSpace(RendezvousName))
                    return area;
                return string.IsNullOrWhiteSpace(area) ? RendezvousName : $"{RendezvousName} · {area}";
            }
        }
    }
    private sealed class GroupMetadata
    {
        public string GroupId { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string SharedGoal { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool HasTaskClock { get; set; }
        public bool TaskTimerPaused { get; set; }
        public long TaskRemainingMilliseconds { get; set; }
        public string TaskExpiresUtc { get; set; } = string.Empty;
        public string TravelDeadlineUtc { get; set; } = string.Empty;
        public string MeetUpDeadlineUtc { get; set; } = string.Empty;
        public string LeaderName { get; set; } = string.Empty;
        public string RendezvousName { get; set; } = string.Empty;
        public string PullerName { get; set; } = string.Empty;
        public string MemberRole { get; set; } = string.Empty;
    }
    private sealed record DashboardSnapshot(string ServerState, List<BotRow> Bots, List<GroupRow> Groups,
        int Active, double ServerMemoryMb, double TickP95Ms, double PlayerXpRate, double BotXpRate,
        double PlayerRpRate, double BotRpRate, bool MakeMeGm = false, RvrWorldSnapshot? RvrWorld = null, WorldSimulationClockRead? SavedSimulationClock = null)
    {
        // Set by the snapshot reader; not a constructor parameter, so the positional shape stays as it was.
        public bool RealmRankNameplates { get; init; }
        public bool BotRealmRankNameplates { get; init; } = true;
        public ToaSettings Toa { get; init; } = ToaSettings.Defaults;
    }

    private sealed record LiveBotStatus(long BotId, int Level, string ZoneName, string Activity,
        string CurrentGoal, string TargetName, string TravelDestination, string ObjectiveProgress,
        bool IsAlive, string ItineraryJson, string ObjectiveKind, string ObjectiveAssignmentId,
        string ObjectiveAssignedUtc, string ObjectivePhase, string ObjectiveExpiresUtc, long? RealmPoints = null);
    private sealed record LiveBotSnapshot(DateTime UpdatedUtc, bool Running, string RequestId, List<LiveBotStatus> Bots);

    private sealed record RvrObjective(string Kind, string Name, string Owner, string State, string Location, string Carrier, string Forces, string Id = "", long CooldownMilliseconds = 0, bool IsCatalogOnly = false, long PhaseRemainingMilliseconds = 0, string Phase = "")
    {
        // Countdown reaching zero does not end an event: arrivals, landing and
        // interior staging can still be in progress. Older keep/relic snapshots
        // provide State rather than Phase, so recognize their published states too.
        public bool IsActiveEvent => !IsCatalogOnly && !IsProtectedPortal &&
            (Phase is "Muster" or "Staging" or "Waiting" or "Battle" ||
             State.StartsWith("Raid rally", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("Keep siege rally", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("Relic siege rally", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("SIEGE", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("RAID —", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("ESCORT", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase) ||
             State.StartsWith("Under attack", StringComparison.OrdinalIgnoreCase));
        public string Marker => Kind == "Dragon" ? "◆ DRAGON" : Kind == "Epic dungeon" ? "◆ DUNGEON" : Kind == "Relic" ? "◆ RELIC" : Kind == "Relic keep" ? "▣ RELIC KEEP" : "▣ KEEP";
        // Compatibility with the last-known JSON generated by older servers.
        // Current publishers filter by the authoritative server keep flag.
        public bool IsProtectedPortal => Kind.Equals("Portal keep", StringComparison.OrdinalIgnoreCase) ||
            Name.EndsWith("Portal Keep", StringComparison.OrdinalIgnoreCase);
    }
    private sealed record EventParticipant(string EventId, string GroupId, string Name, string Realm, string Location, string Activity, int X, int Y, int Z);
    private sealed record RvrWorldSnapshot(DateTime UpdatedUtc, bool Running, List<RvrObjective> Objectives, List<EventParticipant>? Participants = null);

    private sealed record XpRateOption(double Multiplier, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record WorldSpeedOption(int Multiplier, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record LiveStateRequest(DateTime NowUtc, bool ProcessAlive, bool ReadSavedClock, bool ReadRvr);
    private sealed record WorldSpeedReading(bool Running, WorldSpeedStatus? Status, string? UnavailableReason, WorldSimulationClockRead? SavedClock);
    private sealed record LiveStateReading(WorldSpeedReading WorldSpeed, RvrWorldSnapshot? Rvr, bool RvrRead);
}
