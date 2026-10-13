using System;
using System.IO;
using DOL.Logging;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>
/// The Gothwaite PvP world mode (docs/GOTHWAITE_PVP_MODE.md). Read once at server start from
/// <see cref="GothwaitePvpSettings.FileName"/>; switching needs a restart. While
/// <see cref="IsActive"/> is false every hook into the mode returns at once and does nothing.
/// </summary>
public static class GothwaitePvpMode
{
    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpMode));

    public static bool IsActive { get; private set; }
    public static GothwaitePvpSettings Settings { get; private set; } = GothwaitePvpSettings.Defaults;
    public static GothwaitePvpArena Arena { get; private set; } = GothwaitePvpArena.Gothwaite;

    /// <summary>Reads the switch once. A broken file keeps the mode off and logs why.</summary>
    public static void Initialize(string serverDirectory)
    {
        string path = Path.Combine(serverDirectory ?? string.Empty, GothwaitePvpSettings.FileName);
        GothwaitePvpSettings settings;
        try
        {
            settings = GothwaitePvpSettings.Load(path);
        }
        catch (Exception exception)
        {
            Log.Error($"GOTH_PVP_SETTINGS_INVALID path=\"{path}\" error=\"{exception.Message}\"; the mode stays off.");
            settings = GothwaitePvpSettings.Defaults;
        }
        Apply(settings);
        Log.Info($"GOTH_PVP_MODE active={IsActive} arena={Arena.Name} safe_radius={Settings.SafeRadius} " +
            $"low_band_max={Settings.LowBandMaxLevel} low_edge={Settings.LowBandEdge} edge={Settings.Edge}");
    }

    /// <summary>Sets the mode from settings. Tests use it; production calls it only from <see cref="Initialize"/>.</summary>
    public static void Apply(GothwaitePvpSettings settings)
    {
        Settings = settings ?? GothwaitePvpSettings.Defaults;
        Arena = GothwaitePvpArena.Gothwaite with { SafeRadius = Settings.SafeRadius };
        IsActive = Settings.Enabled;
    }

    /// <summary>Back to the default (mode off). Tests call it in their teardown.</summary>
    public static void Reset() => Apply(GothwaitePvpSettings.Defaults);

    /// <summary>
    /// An autonomous world bot that the mode drives: the mode is on and the bot is an
    /// autonomous world bot that is not a temporary companion helper and not in a group led by
    /// a real player. Companions, battleground encounter bots and players never are.
    /// </summary>
    public static bool IsModeBot(GameLiving living) =>
        IsActive && living is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsBattlegroundEncounterBot: false } bot &&
        !bot.IsPlayerLedGroup;

    /// <summary>Pure form of <see cref="IsModeBot"/> for tests.</summary>
    public static bool IsModeBot(bool active, bool autonomousWorldBot, bool temporaryHelper, bool encounterBot, bool playerLed) =>
        active && autonomousWorldBot && !temporaryHelper && !encounterBot && !playerLed;
}
