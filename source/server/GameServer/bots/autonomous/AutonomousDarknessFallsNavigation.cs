using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>Nine audited stair links on the shipped DF mesh. The installed file is never modified.</summary>
    public static class AutonomousDarknessFallsNavigation
    {
        private sealed record Patch(int Offset, int Length, byte[] Data);
        private sealed record Manifest(string SourceSha256, string ResultSha256, Patch[] Patches);
        public static bool Ready { get; internal set; }

        public static bool TryPrepare(string installedPath, out string path)
        {
            path = installedPath;
            Ready = false;
            try
            {
                var assembly = typeof(AutonomousDarknessFallsNavigation).Assembly;
                string resource = assembly.GetManifestResourceNames().Single(name =>
                    name.EndsWith("darkness_falls_navigation_patch.json", StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(resource);
                Manifest manifest = JsonSerializer.Deserialize<Manifest>(stream);
                byte[] original = File.ReadAllBytes(installedPath);
                string sourceHash = Hash(original);
                if (sourceHash == manifest.ResultSha256) return true;
                if (sourceHash != manifest.SourceSha256)
                {
                    LoggerManager.Create(typeof(AutonomousDarknessFallsNavigation)).Warn(
                        "DF navigation differs from the audited mesh; autonomous DF destinations remain disabled.");
                    return false;
                }

                using var output = new MemoryStream();
                int cursor = 0;
                foreach (Patch patch in manifest.Patches)
                {
                    if (patch.Offset < cursor || patch.Length < 0 || patch.Offset > original.Length - patch.Length)
                        throw new InvalidDataException("Invalid DF navigation patch bounds.");
                    output.Write(original, cursor, patch.Offset - cursor);
                    output.Write(patch.Data);
                    cursor = patch.Offset + patch.Length;
                }
                output.Write(original, cursor, original.Length - cursor);
                byte[] repaired = output.ToArray();
                if (Hash(repaired) != manifest.ResultSha256)
                    throw new InvalidDataException("DF navigation repair hash mismatch.");
                string directory = Path.Combine(Path.GetTempPath(), "OfflineDAoC-navigation");
                Directory.CreateDirectory(directory);
                string cache = Path.Combine(directory, manifest.ResultSha256 + ".nav");
                if (!File.Exists(cache) || Hash(File.ReadAllBytes(cache)) != manifest.ResultSha256)
                {
                    string temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, repaired);
                        File.Move(temporary, cache, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                path = cache;
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.Create(typeof(AutonomousDarknessFallsNavigation)).Warn(
                    "DF navigation repair unavailable; autonomous DF destinations remain disabled.", ex);
                return false;
            }
        }

        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
