namespace OfflineDaoc.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--prepare-portable-account")
        {
            PortableCredentials.ReadOrCreate(AppContext.BaseDirectory);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportUnhandled(e.Exception, interactive: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportUnhandled(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), interactive: false);
        if (args.Length == 1 && args[0] == "--join")
        {
            Application.Run(new JoinFriendForm());
            return;
        }
        // Switching worlds closes the launcher window and opens a new one on the other world (WorldPickerPanel.cs).
        LauncherReopen? reopen = null;
        do
        {
            using var form = new MainForm(reopen);
            Application.Run(form);
            reopen = form.Reopen;
        }
        while (reopen is not null);
    }

    // Bug 70: an unhandled launcher exception used to show only its message.
    // Keep the full stack trace beside the other launcher logs so it can be fixed.
    private static void ReportUnhandled(Exception exception, bool interactive)
    {
        string? logPath = null;
        try
        {
            string logs = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logs);
            logPath = Path.Combine(logs, "launcher-errors.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never hide the original error.
        }
        if (interactive)
        {
            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                (logPath == null ? "The details could not be saved." : $"Details were saved to {logPath}"),
                "Offline DAoC launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
