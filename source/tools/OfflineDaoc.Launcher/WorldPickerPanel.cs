using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>
/// What a launcher window hands to the next one when it closes to switch worlds: the world, and the window's place,
/// state and tab, so the switch looks like a refresh rather than a new program.
/// </summary>
internal sealed record LauncherReopen(WorldSaveKind World, Rectangle Bounds, FormWindowState WindowState, int TabIndex);

/// <summary>
/// The world picker (package R5b, docs/RVR_WORLDS.md "World picker"): the "World" dropdown beside START SERVER,
/// the WORLD label in the status line, "Create world" in place of START SERVER when the picked world has no save,
/// and "Reset world…" in the status line. A window works on exactly one world: every save path is fixed when it
/// is built, and switching closes it and opens a fresh window for the other world (<see cref="Reopen"/>).
/// </summary>
internal sealed partial class MainForm
{
    private const string StartServerText = "START SERVER";
    private const string CreateWorldText = "CREATE WORLD…";

    private readonly WorldSaveKind _world;
    private readonly string _serverConfigPath;
    private readonly ComboBox _worldPicker = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DrawMode = DrawMode.OwnerDrawFixed,
        Width = 132,
        DropDownWidth = 190,
        Enabled = false,
        AccessibleName = "World",
    };
    private readonly Label _worldStatusLabel = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        BackColor = DaocTheme.StoneDark,
        Font = new Font("Georgia", 8f, FontStyle.Bold),
        Padding = new Padding(8, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
    };
    private Button? _resetWorldButton;
    private DaocTabControl? _mainTabs;
    private WorldSaveStatus? _selectedWorldStatus;
    private bool _loadingWorldPicker;
    private bool _creatingWorld;
    // Set when the window cannot offer START or CREATE WORLD until something is fixed (a failed move of today's
    // Camlann save); the status line shows it instead.
    private string? _worldAttention;

    /// <summary>Set when this window closes to open the launcher again for another (or a freshly created) world.</summary>
    internal LauncherReopen? Reopen { get; private set; }

    /// <summary>The picked world's save cannot start yet; START SERVER reads "CREATE WORLD…" and builds it instead.</summary>
    private bool StartCreatesWorld => _selectedWorldStatus is { State: not WorldSaveState.Ready };

    /// <summary>
    /// Test-only seam: replaces the running-server check a new window makes when it picks its world, so window tests
    /// do not depend on a server listening on this machine. Production code never assigns it.
    /// </summary>
    internal static Func<bool>? ServerRunningProbeForTests;

    private WorldSelection ResolveLauncherWorld(WorldSaveKind? requested)
    {
        bool running = ServerRunningProbeForTests?.Invoke() ?? (IsServerRunning() || HasExactServerProcess());
        string? runningDatabase = null;
        if (running)
        {
            try { runningDatabase = CamlannServerConfig.ReadDatabaseFileName(_serverConfigPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            runningDatabase ??= WorldSaveLayout.RealmDatabaseFileName;
        }
        WorldSaveKind? remembered = requested;
        if (remembered is null)
        {
            try { remembered = WorldChoiceStore.Load(WorldChoiceStore.DefaultPath, _root); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return WorldPicker.Resolve(_root, remembered, runningDatabase);
    }

    private void ConfigureWorldPicker()
    {
        StyleInput(_worldPicker);
        _worldPicker.Width = 132;
        _worldPicker.Height = 26;
        _worldPicker.ItemHeight = 20;
        _worldPicker.Margin = new Padding(2, 4, 2, 2);
        _loadingWorldPicker = true;
        foreach (WorldSaveKind world in WorldPicker.Worlds)
            _worldPicker.Items.Add(world);
        _worldPicker.SelectedItem = _world;
        _loadingWorldPicker = false;
        _worldPicker.DrawItem += DrawWorldPickerItem;
        _worldPicker.SelectedIndexChanged += (_, _) => OnWorldPicked();
        _helpTip.SetToolTip(_worldPicker,
            "World: RvR (Realm vs Realm, the default) or PvP (Camlann). Each world keeps its own save; switching loses nothing. " +
            "Switch only while the server is stopped. START SERVER starts the world shown here.");
        _worldStatusLabel.Text = WorldPicker.StatusText(_world);
        _helpTip.SetToolTip(_worldStatusLabel, $"This launcher shows and starts the {WorldPicker.Name(_world)}. Save: {Path.GetFileName(_database)}");
    }

    private void DrawWorldPickerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _worldPicker.Items.Count)
            return;
        var world = (WorldSaveKind)_worldPicker.Items[e.Index]!;
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = (e.State & DrawItemState.Selected) != 0 && !edit;
        Color back = selected ? DaocTheme.GridSelection : DaocTheme.Input;
        Color fore = _worldPicker.Enabled ? (edit ? DaocTheme.GoldLight : DaocTheme.Text) : DaocTheme.Muted;
        using (var brush = new SolidBrush(back))
            e.Graphics.FillRectangle(brush, e.Bounds);
        string text = edit ? WorldPicker.ShortText(world) : WorldPicker.PickerText(world);
        TextRenderer.DrawText(e.Graphics, text, edit ? DaocTheme.Fonts.Bold : DaocTheme.Fonts.Body,
            Rectangle.Inflate(e.Bounds, -3, 0), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private Button BuildResetWorldButton()
    {
        Button reset = ActionButton("RESET WORLD…", Color.FromArgb(126, 74, 40));
        reset.Dock = DockStyle.Fill;
        reset.Margin = new Padding(0);
        reset.Enabled = false;
        reset.AccessibleName = "Reset world";
        reset.Click += async (_, _) => await CreateOrResetWorldAsync();
        _helpTip.SetToolTip(reset, $"Start the {WorldPicker.Name(_world)} over. Its old save moves to the backups; the other world is not changed. Server must be stopped.");
        _resetWorldButton = reset;
        return reset;
    }

    /// <summary>The world switch is allowed only while nothing is running or waiting to be saved.</summary>
    private string? WorldSwitchRefusal()
    {
        if (_creatingWorld || _resettingKeepsRelics || _savingXpRates || _stoppingServer || !BotGoalsServerStopped())
            return "Stop the server completely before switching worlds.";
        if (_botGoalsSettings?.HasUnsavedChanges == true)
            return "Save or undo your Server population edits before switching worlds.";
        if (_openBgSettings?.HasUnsavedChanges == true)
            return "Save or undo your open BG edits before switching worlds.";
        if (_toaPendingNumbers.Count > 0)
            return "A Trials of Atlantis setting is still being saved. Switch again in a moment.";
        return null;
    }

    /// <summary>
    /// CREATE WORLD and RESET WORLD work only on the world's own file, never on a save that still holds today's
    /// Camlann world in the RvR file or on the save a running server was started with: the launcher has to move or
    /// reopen first, as before START SERVER. Returns the reason, or null.
    /// </summary>
    internal static string? WorldLayoutRefusal(string runtimeRoot, WorldSaveKind world, string database)
    {
        if (!string.Equals(Path.GetFullPath(WorldFiles.For(runtimeRoot, world).Database), Path.GetFullPath(database), StringComparison.OrdinalIgnoreCase))
            return $"This window still shows the save the server ran with ({Path.GetFileName(database)}). The launcher reopens on the {WorldPicker.Name(world)} first.";
        if (WorldPicker.IsLegacyCamlannLayout(runtimeRoot))
            return "The PvP world (Camlann) still sits in the RvR save file. With the server stopped, reopen the launcher so it moves that world to its own save first.";
        return null;
    }

    private void OnWorldPicked()
    {
        if (_loadingWorldPicker || _worldPicker.SelectedItem is not WorldSaveKind picked || picked == _world || Reopen is not null)
            return;
        string? refusal = WorldSwitchRefusal();
        if (refusal is not null)
        {
            SelectPickerWorld(_world);
            MessageBox.Show(this, refusal, "World", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? configError = PointServerConfigAtWorld(_root, _serverConfigPath, picked);
        if (configError is not null)
        {
            SelectPickerWorld(_world);
            MessageBox.Show(this, configError, "Cannot prepare server configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try { WorldChoiceStore.Save(WorldChoiceStore.DefaultPath, _root, picked); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The launcher switches to the {WorldPicker.Name(picked)}, but could not remember the choice for next time:\n{exception.Message}",
                "World", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        ReopenForWorld(picked);
    }

    private void SelectPickerWorld(WorldSaveKind world)
    {
        _loadingWorldPicker = true;
        try { _worldPicker.SelectedItem = world; }
        finally { _loadingWorldPicker = false; }
    }

    /// <summary>
    /// Points serverconfig.xml at the world's save (and pins GameType PvP) when that save is ready to start. A world
    /// without a ready save is never written into the configuration: a server started by hand would otherwise create
    /// an empty save in its place. Returns an error text, or null.
    /// </summary>
    internal static string? PointServerConfigAtWorld(string runtimeRoot, string configPath, WorldSaveKind world)
    {
        if (WorldPicker.Inspect(runtimeRoot, world).State != WorldSaveState.Ready)
            return null;
        try
        {
            CamlannServerConfig.EnsureWorld(configPath, world);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// The last step before START SERVER launches CoreServer: the window must work on the world's own save, that save
    /// must carry the world's marker, and serverconfig.xml is pointed at it. Returns an error text, or null.
    /// </summary>
    internal static string? PrepareWorldForStart(string runtimeRoot, string configPath, WorldSaveKind world, string database)
    {
        string own = WorldFiles.For(runtimeRoot, world).Database;
        if (!string.Equals(Path.GetFullPath(own), Path.GetFullPath(database), StringComparison.OrdinalIgnoreCase))
            return $"This window still shows the save the server ran with ({Path.GetFileName(database)}). The launcher reopens on the {WorldPicker.Name(world)}; then press START SERVER again.";
        WorldSaveStatus status = WorldPicker.InspectDatabase(world, database);
        if (status.State != WorldSaveState.Ready)
            return $"The {WorldPicker.Name(world)} has no save that can start yet. Use CREATE WORLD… first.";
        try
        {
            CamlannServerConfig.EnsureWorld(configPath, world);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return "The server configuration could not be pointed at the world's save: " + exception.Message;
        }
    }

    /// <summary>Called by START SERVER right before CoreServer starts; false when the start must not go ahead.</summary>
    private bool PrepareSelectedWorldForStart()
    {
        string? error = PrepareWorldForStart(_root, _serverConfigPath, _world, _database);
        if (error is null)
            return true;
        MessageBox.Show(this, error, "World", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (!string.Equals(Path.GetFullPath(WorldFiles.For(_root, _world).Database), Path.GetFullPath(_database), StringComparison.OrdinalIgnoreCase))
            ReopenForWorld(_world);
        return false;
    }

    /// <summary>Closes this window so Program opens a new one on the world, in the same place and tab.</summary>
    private void ReopenForWorld(WorldSaveKind world)
    {
        if (Reopen is not null)
            return;
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        FormWindowState state = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
        Reopen = new LauncherReopen(world, bounds, state, _mainTabs?.SelectedIndex ?? 0);
        _worldPicker.Enabled = false;
        if (IsHandleCreated)
            BeginInvoke(Close);
        else
            Close();
    }

    private void ApplyReopenPlacement(LauncherReopen reopen)
    {
        if (reopen.Bounds.Width > 0 && reopen.Bounds.Height > 0 &&
            Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(reopen.Bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = reopen.Bounds;
        }
        WindowState = reopen.WindowState;
        if (_mainTabs is not null && reopen.TabIndex >= 0 && reopen.TabIndex < _mainTabs.TabCount)
            _mainTabs.SelectedIndex = reopen.TabIndex;
    }

    /// <summary>
    /// The launcher's first step when its window opens: a Camlann world that still sits in the RvR file gets its own
    /// files (the window then reopens), the picked world's save is checked, and a ready save is written into the
    /// server configuration. Replaces the one-time in-place Camlann conversion: a save without a world marker is now
    /// the seed of "Create world". Returns true when START SERVER can start the world.
    /// </summary>
    private async Task<bool> PrepareSelectedWorldAsync()
    {
        bool stopped = BotGoalsServerStopped();
        if (stopped && WorldPicker.IsLegacyCamlannLayout(_root))
        {
            _creatingWorld = true;
            try
            {
                await Task.Run(() => WorldPicker.RelocateLegacyCamlann(_root, BotGoalsServerStopped));
                _creatingWorld = false;
                MessageBox.Show(this,
                    "Your PvP world (Camlann) now has its own save file, " + WorldSaveLayout.CamlannDatabaseFileName + ", with its own side files. " +
                    "Nothing was deleted or reset.\n\nPick \"RvR (Realm vs Realm)\" in the World dropdown to create the RvR world beside it.",
                    "PvP world moved to its own save", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ReopenForWorld(_world);
                return false;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.Data.SQLite.SQLiteException)
            {
                // Never fall through to CREATE WORLD here: the RvR file still holds the PvP world.
                _worldAttention = "NEEDS ATTENTION: the PvP world (Camlann) could not be moved to its own save. Fix the files named in the message, then reopen the launcher.";
                MessageBox.Show(this, exception.Message, "PvP world not moved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                _creatingWorld = false;
            }
        }

        _selectedWorldStatus = await Task.Run(() => WorldPicker.InspectDatabase(_world, _database));
        if (_selectedWorldStatus.State == WorldSaveState.Unsupported)
        {
            MessageBox.Show(this, $"The {WorldPicker.Name(_world)} save has an unsupported world marker: {_selectedWorldStatus.Marker}.",
                "World unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        if (_selectedWorldStatus.State != WorldSaveState.Ready)
            return false;
        if (stopped)
        {
            string? error = PointServerConfigAtWorld(_root, _serverConfigPath, _world);
            if (error is not null)
            {
                MessageBox.Show(this, error, "Cannot prepare server configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        return true;
    }

    /// <summary>The window's state when the picked world cannot start yet: START SERVER becomes CREATE WORLD….</summary>
    private void ShowWorldCreationState()
    {
        WorldSaveStatus status = _selectedWorldStatus ?? WorldPicker.InspectDatabase(_world, _database);
        bool stopped = !_creatingWorld && BotGoalsServerStopped();
        string? seed = WorldReset.DefaultSeed(_root, _world);
        string? blocked = _worldAttention ?? WorldLayoutRefusal(_root, _world, _database);
        bool creatable = status.CanCreate && seed is not null && blocked is null;
        _worldReady = false;
        // No save to start: nothing may write a world setting either, or it would create an empty save file.
        SetWorldSettingWritersEnabled(false);
        _startButton.Text = status.State == WorldSaveState.Unsupported || blocked is not null ? StartServerText : CreateWorldText;
        _startButton.Enabled = stopped && creatable;
        _helpTip.SetToolTip(_startButton, creatable ? $"Create a fresh {WorldPicker.Name(_world)}. Asks first." : string.Empty);
        _playButton.Enabled = false;
        _worldPicker.Enabled = stopped;
        if (_resetWorldButton is not null)
            _resetWorldButton.Enabled = stopped && creatable && status.HasSave;
        _footer.Text = blocked ?? WorldCreationFooter(status, seed is not null);
        if (_worldAttention is not null)
        {
            _worldStatusLabel.Text = WorldPicker.StatusText(_world) + " — NEEDS ATTENTION";
            _worldStatusLabel.ForeColor = DaocTheme.Danger;
        }
    }

    /// <summary>Every control that writes into the save (or its world settings) at once.</summary>
    private void SetWorldSettingWritersEnabled(bool enabled)
    {
        _playerXpRate.Enabled = _botXpRate.Enabled = _playerRpRate.Enabled = _botRpRate.Enabled = enabled;
        _makeMeGm.Enabled = _realmRankNameplates.Enabled = _botRealmRankNameplates.Enabled = enabled;
        SetToaControlsEnabled(enabled);
        SetRealmGenerationEnabled(enabled);
        if (_openBgSettings is not null) _openBgSettings.Enabled = enabled;
        if (_botGoalsSettings is not null) _botGoalsSettings.Enabled = enabled;
        if (_resetKeepsRelics is not null) _resetKeepsRelics.Enabled = enabled && _resetKeepsRelics.Enabled;
        if (_deleteAllBotsButton is not null) _deleteAllBotsButton.Enabled = enabled && _deleteAllBotsButton.Enabled;
        if (!enabled)
            _xpSettingsStatus.Text = $"The {WorldPicker.Name(_world)} has no save yet: settings unlock once it exists.";
    }

    internal static string WorldCreationFooter(WorldSaveStatus status, bool hasSeed)
    {
        string world = WorldPicker.Name(status.World);
        return status.State switch
        {
            WorldSaveState.Missing when !hasSeed => "The world database is missing; install the server data before starting.",
            WorldSaveState.Missing => $"The {world} has no save yet. CREATE WORLD… builds a fresh one; the {WorldPicker.Name(WorldSaveLayout.Other(status.World))} is kept.",
            WorldSaveState.NotCreated => $"This save has no world yet. CREATE WORLD… builds a fresh {world} from it; the old save moves to the backups.",
            WorldSaveState.OtherWorld => $"The {world} file holds the {WorldPicker.Name(WorldSaveLayout.Other(status.World))}. CREATE WORLD… builds a fresh {world}; the old save moves to the backups.",
            WorldSaveState.Unsupported => $"The {world} save has an unsupported world marker: {status.Marker}.",
            _ => string.Empty,
        };
    }

    /// <summary>The world controls while the dashboard shows a ready world: switchable and resettable only when fully stopped.</summary>
    private void UpdateWorldControls(bool fullyStopped)
    {
        if (Reopen is not null)
            return;
        _worldPicker.Enabled = fullyStopped && !_creatingWorld;
        if (_resetWorldButton is not null)
            _resetWorldButton.Enabled = fullyStopped && !_creatingWorld && _selectedWorldStatus is { HasSave: true } &&
                WorldLayoutRefusal(_root, _world, _database) is null;
    }

    /// <summary>CREATE WORLD… and RESET WORLD…: builds a fresh save for the picked world after one confirmation.</summary>
    private async Task CreateOrResetWorldAsync()
    {
        if (_creatingWorld)
            return;
        string? refusal = WorldSwitchRefusal();
        if (refusal is not null)
        {
            MessageBox.Show(this, refusal.Replace("switching worlds", "creating a world", StringComparison.Ordinal), "World", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? layout = _worldAttention ?? WorldLayoutRefusal(_root, _world, _database);
        if (layout is not null)
        {
            MessageBox.Show(this, layout, "World", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // A window on the running server's save reopens on the world's own file; a failed move needs a fix first.
            if (_worldAttention is null && !string.Equals(Path.GetFullPath(WorldFiles.For(_root, _world).Database), Path.GetFullPath(_database), StringComparison.OrdinalIgnoreCase))
                ReopenForWorld(_world);
            return;
        }
        WorldSaveStatus status = WorldPicker.Inspect(_root, _world);
        if (status.State == WorldSaveState.Unsupported)
        {
            MessageBox.Show(this, WorldCreationFooter(status, true), "World unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string? seed = WorldReset.DefaultSeed(_root, _world);
        if (seed is null)
        {
            MessageBox.Show(this, "This installation has no save to build a world from. Install the server data first.", "World unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string backupRoot = WorldFiles.DefaultBackupRoot(_root);
        if (MessageBox.Show(this, WorldPicker.CreationPrompt(status, seed, backupRoot),
                status.HasSave ? $"Reset the {WorldPicker.Name(_world)}" : $"Create the {WorldPicker.Name(_world)}",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _creatingWorld = true;
        _startButton.Enabled = _playButton.Enabled = _worldPicker.Enabled = false;
        if (_resetWorldButton is not null) _resetWorldButton.Enabled = false;
        if (_resetKeepsRelics is not null) _resetKeepsRelics.Enabled = false;
        _footer.Text = $"Creating the {WorldPicker.Name(_world)}… this takes up to a few minutes.";
        try
        {
            string account = ReadCredentials().Account;
            WorldSaveKind world = _world;
            string root = _root;
            WorldReset.Result result = await Task.Run(() => WorldReset.CreateFresh(new WorldReset.Request(world, root, account, BotGoalsServerStopped)));
            string? configError = PointServerConfigAtWorld(_root, _serverConfigPath, _world);
            string bots = string.Join(", ", result.BotsCreated.OrderBy(pair => pair.Key)
                .Select(pair => $"{(pair.Key switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "realm " + pair.Key })} {pair.Value:N0}"));
            MessageBox.Show(this,
                $"The {WorldPicker.Name(_world)} is ready.\n\nBots: {bots}\nKeeps held by a realm: {result.RealmKeeps}\nRelics home: {result.Relics}\n\n" +
                (result.Backup is null ? "No old save was replaced." : $"The old save and its files are in:\n{result.Backup}") +
                (configError is null ? string.Empty : $"\n\nThe server configuration could not be updated: {configError}"),
                "World created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _creatingWorld = false;
            ReopenForWorld(_world);
        }
        catch (Exception exception)
        {
            _creatingWorld = false;
            MessageBox.Show(this, exception.Message, "World not created", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _selectedWorldStatus = WorldPicker.InspectDatabase(_world, _database);
            if (StartCreatesWorld)
                ShowWorldCreationState();
            else
                await RefreshDashboardAsync();
        }
        finally
        {
            _creatingWorld = false;
        }
    }
}
