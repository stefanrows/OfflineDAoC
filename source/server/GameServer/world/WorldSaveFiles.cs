using System;
using System.IO;
using DOL.Database.Connection;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>
/// The per-save side files of the running server (docs/RVR_WORLDS.md): realm event records, the open BG
/// status, the Gothwaite switch and the dashboard snapshots. Each save has its own set, named after the
/// save file in <c>DBConnectionString</c> by <see cref="WorldSaveLayout"/>: the RvR save
/// <c>opendaoc.sqlite3.db</c> keeps the plain names, the Camlann save <c>opendaoc-camlann.sqlite3.db</c>
/// gets the <c>-camlann</c> twins. Live request/status protocols (world speed, population mix, realm
/// event commands) are not per save: one server runs per installation and their session ids guard them.
/// </summary>
public static class WorldSaveFiles
{
    /// <summary>The save file the server opens; empty before the configuration exists (tools, unit tests).</summary>
    public static string DatabaseFileName
    {
        get
        {
            try
            {
                GameServerConfiguration configuration = GameServer.Instance?.Configuration;
                if (configuration == null || configuration.DBType != EConnectionType.DATABASE_SQLITE)
                    return string.Empty;
                return Path.GetFileName(WorldSaveLayout.DataSource(configuration.DBConnectionString) ?? string.Empty);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>A per-save side file in the server directory.</summary>
    public static string ServerFile(string baseFileName) => InDirectory(AppContext.BaseDirectory, baseFileName);

    /// <summary>A per-save side file in the given directory.</summary>
    public static string InDirectory(string directory, string baseFileName) =>
        WorldSaveLayout.SideFilePath(directory, baseFileName, DatabaseFileName);
}
