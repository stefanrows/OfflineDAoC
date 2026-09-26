using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace OfflineDaoc.Launcher;

internal sealed class JoinFriendForm : Form
{
    private const int LoginPort = 10300;
    private const int GamePort = 10400;
    private readonly string _root = Path.GetFullPath(AppContext.BaseDirectory);
    private readonly TextBox _hostAddress = InputBox(15);
    private readonly TextBox _hostAccount = InputBox(20);
    private readonly TextBox _guestAccount = InputBox(20);
    private readonly TextBox _password = InputBox(20, password: true);
    private readonly CheckBox _rememberPassword = new()
    {
        Text = "Remember my password (encrypted for this Windows user)",
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.Parchment,
        Font = new Font("Georgia", 8f),
        BackColor = Color.Transparent,
    };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = DaocTheme.GoldLight,
        Font = new Font("Georgia", 8.5f),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Button _joinButton = new RuneButton
    {
        Text = "JOIN FRIEND",
        Width = 170,
        Height = 34,
        Accent = Color.FromArgb(104, 111, 77),
        Margin = new Padding(4),
    };
    private readonly Button _closeButton = new RuneButton
    {
        Text = "CLOSE",
        Width = 110,
        Height = 34,
        Accent = DaocTheme.Iron,
        Margin = new Padding(4),
        DialogResult = DialogResult.Cancel,
    };

    public JoinFriendForm()
    {
        Text = "Join a Friend — Offline DAoC";
        ClientSize = new Size(620, 568);
        MinimumSize = new Size(590, 548);
        StartPosition = FormStartPosition.CenterScreen;
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
            RowCount = 9,
            Padding = new Padding(4),
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        for (int index = 0; index < 4; index++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        layout.Controls.Add(new Label
        {
            Text = "JOIN A FRIEND'S WORLD",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "This client-only mode starts the game client without starting the local server or opening the local world save.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Parchment,
            Font = new Font("Georgia", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);
        layout.Controls.Add(Field("Host Tailscale IPv4 address", _hostAddress), 0, 2);
        layout.Controls.Add(Field("Host's local account name (ask the host; do not use their password)", _hostAccount), 0, 3);
        layout.Controls.Add(Field("Your guest account name", _guestAccount), 0, 4);
        layout.Controls.Add(Field("Your guest account password", _password), 0, 5);
        layout.Controls.Add(_rememberPassword, 0, 6);

        var connectionNote = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(2, 4, 2, 2),
        };
        connectionNote.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        connectionNote.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        connectionNote.Controls.Add(new Label
        {
            Text = $"Host setup: disable UPnP and external IP detection. Allow TCP {LoginPort} and UDP {GamePort} through Windows Firewall for Tailscale only.",
            Dock = DockStyle.Fill,
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        connectionNote.Controls.Add(_status, 0, 1);
        layout.Controls.Add(connectionNote, 0, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_joinButton);
        layout.Controls.Add(buttons, 0, 8);
        frame.Controls.Add(layout);

        _joinButton.Click += async (_, _) => await JoinAsync();
        _closeButton.Click += (_, _) => Close();
        AcceptButton = _joinButton;
        CancelButton = _closeButton;
        _status.Text = "Enter your friend's Tailscale IPv4 address and your own account credentials.";
        LoadSavedProfile();
    }

    private void LoadSavedProfile()
    {
        JoinFriendProfile saved = JoinFriendProfile.Load(JoinFriendProfile.DefaultPath);
        _hostAddress.Text = saved.HostAddress;
        _hostAccount.Text = saved.HostAccount;
        _guestAccount.Text = saved.GuestAccount;
        _password.Text = saved.Password;
        _rememberPassword.Checked = saved.RememberPassword;
        if (saved.GuestAccount.Length > 0)
            _status.Text = saved.RememberPassword
                ? "Saved details loaded. Press JOIN FRIEND to connect."
                : "Saved details loaded. Enter your password to connect.";
    }

    private void SaveProfile(IPAddress hostAddress, string guestAccount, string password)
    {
        try
        {
            JoinFriendProfile.Save(JoinFriendProfile.DefaultPath, hostAddress.ToString(), _hostAccount.Text.Trim(),
                guestAccount, password, _rememberPassword.Checked);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Joining matters more than remembering; the next session asks again.
        }
    }

    private async Task JoinAsync()
    {
        if (!TryReadInputs(out IPAddress? hostAddress, out string guestAccount, out string password, out string error))
        {
            SetStatus(error, DaocTheme.Danger);
            return;
        }

        string clientDirectory = ResolveClientDirectory(_root);
        string connector = Path.Combine(clientDirectory, "connect.exe");
        if (!File.Exists(connector) || !File.Exists(Path.Combine(clientDirectory, "game.dll")))
        {
            SetStatus("The compatible client is missing. Restore connect.exe and game.dll in the client folder.", DaocTheme.Danger);
            return;
        }

        _joinButton.Enabled = false;
        SetStatus("Checking the host's login port…", DaocTheme.GoldLight);
        bool reachable = await CanReachLoginServerAsync(hostAddress);
        if (IsDisposed || Disposing)
            return;
        if (!reachable)
        {
            SetStatus(
                $"Cannot reach {hostAddress}:{LoginPort}. Check that both Tailscale devices are online, the host server is running, and its firewall permits TCP {LoginPort} over Tailscale.",
                DaocTheme.Danger);
            _joinButton.Enabled = true;
            return;
        }

        try
        {
            ClientDisplayPreferences.EnsureIsolatedLaunchProfile(clientDirectory);
            string logsDirectory = Path.Combine(_root, "logs");
            ClientSessionDiagnostics.Prepare(logsDirectory);
            Process? client = Process.Start(new ProcessStartInfo(connector)
            {
                WorkingDirectory = clientDirectory,
                UseShellExecute = false,
                ArgumentList = { "game.dll", hostAddress.ToString(), guestAccount, password },
            });
            if (client == null)
            {
                SetStatus("Windows did not start the game client. Check the client installation and try again.", DaocTheme.Danger);
                _joinButton.Enabled = true;
                return;
            }

            ClientSessionDiagnostics.Start(client, clientDirectory, logsDirectory, hostAddress);
            SaveProfile(hostAddress, guestAccount, password);
            _password.Clear();
            _hostAddress.Enabled = false;
            _hostAccount.Enabled = false;
            _guestAccount.Enabled = false;
            _rememberPassword.Enabled = false;
            _joinButton.Text = "CLIENT STARTED";
            SetStatus("Client started. Keep this window open for session diagnostics; close it when you finish playing.", DaocTheme.Success);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not start the game client ({exception.GetType().Name}). Check the client files and try again.", DaocTheme.Danger);
            _joinButton.Enabled = true;
        }
    }

    private bool TryReadInputs(out IPAddress hostAddress, out string guestAccount, out string password, out string error)
    {
        string hostText = _hostAddress.Text.Trim();
        if (!IPAddress.TryParse(hostText, out IPAddress? parsedAddress) ||
            parsedAddress.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(parsedAddress.ToString(), hostText, StringComparison.Ordinal))
        {
            hostAddress = IPAddress.None;
            guestAccount = string.Empty;
            password = string.Empty;
            error = "Enter the host's Tailscale IPv4 address in dotted form, such as 100.90.12.34.";
            return false;
        }

        byte[] addressBytes = parsedAddress.GetAddressBytes();
        if (addressBytes[0] != 100 || addressBytes[1] < 64 || addressBytes[1] > 127)
        {
            hostAddress = IPAddress.None;
            guestAccount = string.Empty;
            password = string.Empty;
            error = "The address must be in Tailscale's IPv4 range (100.64.0.0–100.127.255.255).";
            return false;
        }

        string hostAccount = _hostAccount.Text.Trim();
        if (!IsValidAccount(hostAccount))
        {
            hostAddress = IPAddress.None;
            guestAccount = string.Empty;
            password = string.Empty;
            error = "Enter the host's local account name using 1–20 letters or numbers.";
            return false;
        }

        guestAccount = _guestAccount.Text.Trim();
        if (!IsValidAccount(guestAccount))
        {
            hostAddress = IPAddress.None;
            password = string.Empty;
            error = "Your guest account name must contain 1–20 letters or numbers.";
            return false;
        }
        if (string.Equals(hostAccount, guestAccount, StringComparison.OrdinalIgnoreCase))
        {
            hostAddress = IPAddress.None;
            password = string.Empty;
            error = "Use a guest account name different from the host's local account.";
            return false;
        }

        password = _password.Text;
        if (password.Length is < 1 or > 20 || password.Any(character => character < ' ' || character > '~'))
        {
            hostAddress = IPAddress.None;
            error = "The legacy client accepts a password of 1–20 printable ASCII characters.";
            return false;
        }

        hostAddress = parsedAddress;
        error = string.Empty;
        return true;
    }

    private static bool IsValidAccount(string account) =>
        account.Length is >= 1 and <= 20 && account.All(char.IsAsciiLetterOrDigit);

    private static async Task<bool> CanReachLoginServerAsync(IPAddress hostAddress)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(hostAddress, LoginPort, timeout.Token);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static string ResolveClientDirectory(string root)
    {
        string officialClientDirectory = Path.Combine(root, "client-opendaoc", "app");
        return File.Exists(Path.Combine(officialClientDirectory, "game.dll"))
            ? officialClientDirectory
            : Path.Combine(root, "client");
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private static TextBox InputBox(int maxLength, bool password = false) => new()
    {
        Dock = DockStyle.Bottom,
        BackColor = DaocTheme.PanelAlt,
        ForeColor = DaocTheme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Georgia", 9f),
        MaxLength = maxLength,
        UseSystemPasswordChar = password,
        Margin = new Padding(0, 3, 0, 0),
    };

    private static Control Field(string caption, TextBox input)
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
}
