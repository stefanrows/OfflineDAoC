using System.ComponentModel;
using System.Diagnostics;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Modal dialog that saves one character's quickbar and window layout as a
/// preset and applies it to other characters. Every action refuses to touch
/// the profile while the game client is running, because the client rewrites
/// its layout files on exit.
/// </summary>
internal sealed class ClientUiPresetForm : Form
{
    private static readonly string[] ClientProcessNames = ["connect", "camelot"];
    private readonly string _clientDirectory;
    private readonly string _profileDirectory;
    private bool _hasPreset;
    private readonly ComboBox _characters = new()
    {
        Dock = DockStyle.Bottom,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        Font = new Font("Georgia", 9f),
        Margin = new Padding(0, 3, 0, 0),
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        Font = new Font("Georgia", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button _saveButton = new RuneButton
    {
        Text = "SAVE AS PRESET",
        Width = 150,
        Height = 34,
        Accent = Color.FromArgb(104, 111, 77),
        Margin = new Padding(4),
    };
    private readonly Button _applyButton = new RuneButton
    {
        Text = "APPLY TO CHARACTER",
        Width = 180,
        Height = 34,
        Accent = DaocTheme.Gold,
        Margin = new Padding(4),
    };
    private readonly Button _clearButton = new RuneButton
    {
        Text = "CLEAR PRESET",
        Width = 130,
        Height = 34,
        Accent = Color.FromArgb(151, 67, 57),
        Margin = new Padding(4),
    };
    private readonly Button _closeButton = new RuneButton
    {
        Text = "CLOSE",
        Width = 100,
        Height = 34,
        Accent = DaocTheme.Iron,
        Margin = new Padding(4),
        DialogResult = DialogResult.Cancel,
    };

    public ClientUiPresetForm(string clientDirectory)
    {
        _clientDirectory = clientDirectory;
        _profileDirectory = ClientUiPreset.ProfileDirectory(clientDirectory);

        Text = "UI PRESET";
        ClientSize = new Size(640, 340);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = DaocTheme.Void;
        ForeColor = DaocTheme.Text;
        Font = new Font("Georgia", 8.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var frame = new StoneSurface { Dock = DockStyle.Fill, Padding = new Padding(14) };
        Controls.Add(frame);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(4),
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "UI PRESET",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Log out of the game first. Saves one character's window layout and macro slots as a preset. Spell slots are class-specific and are not copied.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);
        layout.Controls.Add(Field("Character (newest first)", _characters), 0, 2);
        layout.Controls.Add(_status, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_clearButton);
        buttons.Controls.Add(_applyButton);
        buttons.Controls.Add(_saveButton);
        layout.Controls.Add(buttons, 0, 5);
        frame.Controls.Add(layout);

        _characters.SelectedIndexChanged += (_, _) => UpdateButtons();
        _saveButton.Click += (_, _) => SaveAsPreset();
        _applyButton.Click += (_, _) => ApplyToSelectedCharacter();
        _clearButton.Click += (_, _) => ClearPreset();
        CancelButton = _closeButton;

        RefreshState();
    }

    private void SaveAsPreset()
    {
        if (!CanChangeProfileNow() || _characters.SelectedItem is not CharacterChoice choice)
            return;

        try
        {
            ClientUiPreset.Save(_profileDirectory, choice.FilePath);
            string? installed = ClientUiPreset.InstallAsNewCharacterDefault(_clientDirectory, _profileDirectory);
            RefreshState(choice.FilePath);
            SetStatus(installed != null
                    ? $"Preset saved from {choice.Name} and installed as the new-character default ({Path.GetFileName(installed)})."
                    : $"Preset saved from {choice.Name}. No screen width was found in user.dat, so no new-character default was installed.",
                DaocTheme.Success);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowFileError("Saving the preset", exception);
        }
    }

    private void ApplyToSelectedCharacter()
    {
        if (!CanChangeProfileNow() || _characters.SelectedItem is not CharacterChoice choice)
            return;

        DialogResult answer = MessageBox.Show(
            this,
            $"Overwrite {choice.Name}'s window layout and macro slots with the saved preset?\n\n" +
            "The preset has no spell slots, so this character's spell slots on the quickbars will be removed. " +
            $"The original file is kept as {Path.GetFileName(choice.FilePath)}.uipreset-backup (an existing backup is never overwritten).",
            "Apply UI preset",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            ClientUiPreset.ApplyToCharacter(_profileDirectory, choice.FilePath);
            RefreshState(choice.FilePath);
            SetStatus($"Preset applied to {choice.Name}. The original file is kept as a .uipreset-backup.", DaocTheme.Success);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowFileError("Applying the preset", exception);
        }
    }

    private void ClearPreset()
    {
        if (!CanChangeProfileNow())
            return;

        DialogResult answer = MessageBox.Show(
            this,
            "Delete the saved preset and put back any original new-character default file that the preset replaced? Character files are not changed.",
            "Clear UI preset",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            ClientUiPreset.Clear(_clientDirectory, _profileDirectory);
            RefreshState();
            SetStatus("Preset cleared.", DaocTheme.Success);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowFileError("Clearing the preset", exception);
        }
    }

    private void RefreshState(string? keepFilePath = null)
    {
        try
        {
            IReadOnlyList<string> files = ClientUiPreset.ListCharacterFiles(_profileDirectory);
            _hasPreset = ClientUiPreset.HasPreset(_profileDirectory);

            _characters.BeginUpdate();
            try
            {
                _characters.Items.Clear();
                foreach (string file in files)
                    _characters.Items.Add(new CharacterChoice(file));
                int keepIndex = keepFilePath == null
                    ? -1
                    : files.ToList().FindIndex(file => string.Equals(file, keepFilePath, StringComparison.OrdinalIgnoreCase));
                _characters.SelectedIndex = keepIndex >= 0 ? keepIndex : (files.Count > 0 ? 0 : -1);
            }
            finally
            {
                _characters.EndUpdate();
            }

            if (files.Count == 0)
                SetStatus("No character layout files were found in the profile yet. Log in to a character once, then reopen this dialog.", DaocTheme.GoldLight);
            else if (_hasPreset)
                SetStatus("A preset is saved. APPLY TO CHARACTER copies it onto the selected character; CLEAR PRESET removes it.", DaocTheme.GoldLight);
            else
                SetStatus("No preset saved yet. Select a character with the layout you want, then press SAVE AS PRESET.", DaocTheme.GoldLight);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            _hasPreset = false;
            _characters.Items.Clear();
            SetStatus($"The client profile could not be read ({exception.GetType().Name}). Check the profile folder and try again.", DaocTheme.Danger);
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasCharacter = _characters.SelectedItem is CharacterChoice;
        _saveButton.Enabled = hasCharacter;
        _applyButton.Enabled = hasCharacter && _hasPreset;
        _clearButton.Enabled = _hasPreset;
    }

    private bool CanChangeProfileNow()
    {
        if (!IsClientRunning())
            return true;

        MessageBox.Show(
            this,
            "Log out of the game client first, then try again. The client rewrites its layout files while it runs.",
            "Game client running",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private bool IsClientRunning()
    {
        string clientRoot = Path.GetFullPath(_clientDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Process[] processes = ClientProcessNames.SelectMany(name => Process.GetProcessesByName(name)).ToArray();
        try
        {
            return processes.Any(process => ProcessRunsFrom(process, clientRoot));
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    private static bool ProcessRunsFrom(Process process, string clientRoot)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return path != null && path.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Unreadable (access denied or already exited) is treated as not one of ours.
            return false;
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void ShowFileError(string action, Exception exception)
    {
        SetStatus($"{action} failed: {exception.Message}", DaocTheme.Danger);
        MessageBox.Show(
            this,
            $"{action} failed. Check that the client profile folder is writable and not open in another program.\n\n{exception.Message}",
            "UI preset",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool IsFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException;

    private static Control Field(string caption, Control input)
    {
        var field = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(2, 2, 2, 3) };
        field.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        field.Controls.Add(input);
        return field;
    }

    private sealed class CharacterChoice
    {
        public CharacterChoice(string filePath)
        {
            FilePath = filePath;
            string fileName = Path.GetFileName(filePath);
            Name = fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
            LastWrite = File.GetLastWriteTime(filePath);
        }

        public string FilePath { get; }

        public string Name { get; }

        public DateTime LastWrite { get; }

        public override string ToString() => $"{Name}    {LastWrite:yyyy-MM-dd HH:mm}";
    }
}
