using Microsoft.Win32;
using System.IO;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Reads the local Steam client's data: where it's installed, which game is running,
    /// where games are installed, and the playtime it recorded.
    /// </summary>
    public static class SteamLocal
    {
        const string SteamKey = @"Software\Valve\Steam";
        const long SteamId64Base = 76561197960265728;

        public static string? SteamPath
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
                return key?.GetValue("SteamPath") is string path ? Path.GetFullPath(path) : null;
            }
        }

        /// <summary>
        /// Appid of the game Steam currently reports as running, or null.
        /// </summary>
        public static string? RunningAppId
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(SteamKey);
                return key?.GetValue("RunningAppID") is int id && id != 0 ? id.ToString() : null;
            }
        }

        /// <summary>
        /// Whether Steam flags this app as running (<c>Apps\&lt;appid&gt;\Running</c>).
        /// </summary>
        public static bool IsAppRunning(string appId)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{SteamKey}\Apps\{appId}");
            return key?.GetValue("Running") is int running && running != 0;
        }

        /// <summary>
        /// Install folder of a Steam game, from <c>libraryfolders.vdf</c> and the game's <c>appmanifest_&lt;appid&gt;.acf</c>.
        /// </summary>
        public static string? GetInstallDir(string appId)
        {
            var steamPath = SteamPath;
            if (steamPath is null)
                return null;

            var libraryFolders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            var libraries = new List<string> { steamPath };
            if (File.Exists(libraryFolders))
            {
                var root = VdfNode.Parse(File.ReadAllText(libraryFolders));
                foreach (var library in root["libraryfolders"]?.Children.Values ?? Enumerable.Empty<VdfNode>())
                    if (library["path"]?.Value is string path)
                        libraries.Add(path);
            }

            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(manifest))
                    continue;

                var installDir = VdfNode.Parse(File.ReadAllText(manifest)).Get("AppState", "installdir")?.Value;
                if (installDir is not null)
                    return Path.Combine(library, "steamapps", "common", installDir);
            }

            return null;
        }

        /// <summary>
        /// Playtime per appid, in minutes, from the most recently logged-in account's <c>localconfig.vdf</c>.
        /// Steam updates it when a game exits; the value includes play on other computers.
        /// </summary>
        public static Dictionary<string, int> ReadPlayTimes()
        {
            var result = new Dictionary<string, int>();

            var steamPath = SteamPath;
            if (steamPath is null)
                return result;

            var accountId = GetMostRecentAccountId(steamPath);
            if (accountId is null)
                return result;

            var localConfig = Path.Combine(steamPath, "userdata", accountId, "config", "localconfig.vdf");
            if (!File.Exists(localConfig))
                return result;

            var apps = VdfNode.Parse(File.ReadAllText(localConfig))
                .Get("UserLocalConfigStore", "Software", "Valve", "Steam", "apps");

            foreach (var (appId, app) in apps?.Children ?? [])
                if (int.TryParse(app["Playtime"]?.Value, out int minutes) && minutes > 0)
                    result[appId] = minutes;

            return result;
        }

        /// <summary>
        /// Account id (the <c>userdata</c> folder name) of the most recently logged-in user, from
        /// <c>loginusers.vdf</c>; falls back to the only <c>userdata</c> folder when there is just one.
        /// </summary>
        static string? GetMostRecentAccountId(string steamPath)
        {
            var loginUsers = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (File.Exists(loginUsers))
            {
                var users = VdfNode.Parse(File.ReadAllText(loginUsers))["users"]?.Children ?? [];
                foreach (var (steamId64, user) in users)
                    if (user["MostRecent"]?.Value == "1" && long.TryParse(steamId64, out long id))
                        return (id - SteamId64Base).ToString();
            }

            var userData = Path.Combine(steamPath, "userdata");
            var accounts = Directory.Exists(userData) ? Directory.GetDirectories(userData) : [];
            return accounts.Length == 1 ? Path.GetFileName(accounts[0]) : null;
        }
    }
}
