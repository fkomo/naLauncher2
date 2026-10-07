using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Reads the playtime GOG Galaxy recorded, from its local SQLite database.
    /// </summary>
    public static partial class GogGalaxy
    {
        static readonly string DatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

        /// <summary>
        /// Playtime per GOG game id, in minutes.
        /// Galaxy also stores time copied in from its integrations (<c>steam_…</c>, <c>epic_…</c>, …);
        /// only native <c>gog_&lt;id&gt;</c> rows are read, or Steam time would be counted a second time.
        /// </summary>
        public static Dictionary<string, int> ReadPlayTimes()
        {
            var result = new Dictionary<string, int>();
            if (!File.Exists(DatabasePath))
                return result;

            // Galaxy keeps the database open; read a copy (with its WAL, if any) rather than the live file
            var copy = Path.Combine(Path.GetTempPath(), $"naLauncher2-galaxy-{Guid.NewGuid():N}.db");
            try
            {
                CopyShared(DatabasePath, copy);
                if (File.Exists(DatabasePath + "-wal"))
                    CopyShared(DatabasePath + "-wal", copy + "-wal");

                using (var connection = new SqliteConnection($"Data Source={copy}"))
                {
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT releaseKey, MAX(minutesInGame) FROM GameTimes WHERE releaseKey LIKE 'gog\\_%' ESCAPE '\\' GROUP BY releaseKey";

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetString(0)["gog_".Length..];
                        int minutes = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        if (minutes > 0)
                            result[id] = minutes;
                    }
                }

                SqliteConnection.ClearAllPools();
            }
            finally
            {
                TryDelete(copy);
                TryDelete(copy + "-wal");
                TryDelete(copy + "-shm");
            }

            return result;
        }

        /// <summary>
        /// GOG game id of a game installed in the given folder, from its <c>goggame-&lt;id&gt;.info</c> file.
        /// </summary>
        public static string? GetGameIdFromInstallDir(string? installDir)
        {
            if (installDir is null || !Directory.Exists(installDir))
                return null;

            return Directory.EnumerateFiles(installDir, "goggame-*.info")
                .Select(f => GogInfoFileRegex().Match(Path.GetFileName(f)))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .FirstOrDefault();
        }

        static void CopyShared(string source, string destination)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = File.Create(destination);
            input.CopyTo(output);
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        [GeneratedRegex(@"^goggame-(\d+)\.info$", RegexOptions.IgnoreCase)]
        private static partial Regex GogInfoFileRegex();
    }

    /// <summary>
    /// Reads the Epic Games Launcher's install manifests.
    /// </summary>
    public static class EpicLocal
    {
        static readonly string ManifestsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

        public static string? GetInstallDir(string appName)
        {
            if (!Directory.Exists(ManifestsPath))
                return null;

            foreach (var manifest in Directory.EnumerateFiles(ManifestsPath, "*.item"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("AppName", out var name) && string.Equals(name.GetString(), appName, StringComparison.OrdinalIgnoreCase)
                        && root.TryGetProperty("InstallLocation", out var location))
                        return location.GetString();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"{nameof(EpicLocal)}: failed to read '{manifest}': {ex.Message}");
                }
            }

            return null;
        }
    }
}
