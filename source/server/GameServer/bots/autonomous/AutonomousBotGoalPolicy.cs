using System.IO;
using DOL.GS.ServerProperties;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>One startup snapshot; saved bot tasks and personal progress are untouched.</summary>
public static class AutonomousBotGoalPolicy
{
    public static BotGoalSettings Settings { get; private set; } = BotGoalSettings.Defaults;
    public static bool IsConfigured { get; private set; }
    public static bool LegacyFileMapped { get; private set; }
    public static string ServerDirectory { get; private set; } = string.Empty;
    public static AutonomousLevelingDanger Danger => IsConfigured
        ? (AutonomousLevelingDanger)Settings.Danger
        : AutonomousPlayerBehavior.Danger(Properties.CAMLANN_BOT_LEVELING_DANGER);

    public static void Initialize(string serverDirectory)
    {
        ServerDirectory = serverDirectory;
        string path = Path.Combine(serverDirectory, BotGoalSettings.FileName);
        BotGoalLoadResult loaded = BotGoalSettings.LoadDetailed(path);
        Settings = loaded.Settings;
        IsConfigured = File.Exists(path);
        LegacyFileMapped = loaded.MigratedFromV1;
    }

    public static void ApplyLiveSettings(BotGoalSettings settings)
    {
        settings.Validate();
        Settings = settings;
        IsConfigured = true;
        LegacyFileMapped = false;
    }
}
