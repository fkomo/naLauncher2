using System.IO;
using naLauncher2.Core.Tools;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Fills in what naLauncher learns from the games' shortcuts and the launchers' local data:
    /// each game's <see cref="GameInfo.InstallDir"/> (unless set by hand) and its
    /// <see cref="GameInfo.LauncherPlayTime"/> from Steam / GOG Galaxy.
    /// </summary>
    public static class LauncherSync
    {
        /// <summary>
        /// Executables that host many games, so their folder says nothing about which game is running.
        /// </summary>
        static readonly HashSet<string> SharedRuntimes = new(StringComparer.OrdinalIgnoreCase)
        {
            "dosbox.exe", "scummvm.exe", "java.exe", "javaw.exe", "cmd.exe", "steam.exe", "GalaxyClient.exe",
            "EpicGamesLauncher.exe", "EADesktop.exe", "upc.exe", "retroarch.exe", "explorer.exe",
        };

        record GameSnapshot(string Title, GameInfo Game, string? Shortcut, string? ManualInstallDir, string? ExtensionSteamAppId);

        record GameResult(GameInfo Game, string? InstallDir, LauncherPlayTime? PlayTime);

        /// <summary>
        /// Resolves everything in the background, then applies the results on the caller's thread.
        /// Games with a session being tracked keep their launcher playtime untouched, so the launcher's
        /// total is never read in the middle of a session. Returns true when anything changed.
        /// </summary>
        public static async Task<bool> Refresh()
        {
            using var tb = new TimedBlock($"{nameof(LauncherSync)}.{nameof(Refresh)}()", Log.WriteLine);

            var snapshot = GameLibrary.Instance.Games
                .Select(g => new GameSnapshot(g.Key, g.Value, g.Value.Shortcut,
                    g.Value.InstallDirIsManual ? g.Value.InstallDir : null,
                    g.Value.Extensions.GetValueOrDefault(GameInfoExtension.SteamAppId.ToString())))
                .ToArray();

            var results = await Task.Run(() => Resolve(snapshot));

            bool changed = false;
            var now = DateTime.Now;

            foreach (var result in results)
            {
                var game = result.Game;

                if (!game.InstallDirIsManual && game.Shortcut is not null && game.InstallDir != result.InstallDir)
                {
                    game.InstallDir = result.InstallDir;
                    changed = true;
                }

                if (result.PlayTime is null || SessionTracker.Instance.IsTracked(game))
                    continue;

                var existing = game.LauncherPlayTime;
                if (existing is null || existing.Source != result.PlayTime.Source || existing.Total != result.PlayTime.Total)
                {
                    // ReadAt marks when the total last changed: sessions through this launcher that ended
                    // before it are inside the total, later ones not (yet)
                    game.LauncherPlayTime = new LauncherPlayTime { Source = result.PlayTime.Source, Total = result.PlayTime.Total, ReadAt = now };
                    changed = true;
                }
            }

            if (changed)
                await GameLibrary.Instance.Save();

            return changed;
        }

        static List<GameResult> Resolve(GameSnapshot[] games)
        {
            var steamTimes = Read("Steam playtime", SteamLocal.ReadPlayTimes);
            var gogTimes = Read("GOG Galaxy playtime", GogGalaxy.ReadPlayTimes);

            var infos = games.ToDictionary(g => g, g => g.Shortcut is not null && File.Exists(g.Shortcut)
                ? ShortcutResolver.Resolve(g.Shortcut)
                : null);

            // a folder that more than one game's executable lives in identifies none of them
            var executableDirs = infos.Values
                .Where(i => i is { Kind: LaunchKind.Executable, Target: not null })
                .Select(i => Path.GetDirectoryName(i!.Target)!)
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var results = new List<GameResult>();

            foreach (var game in games)
            {
                var info = infos[game];

                string? installDir = null;
                if (info is not null)
                {
                    try
                    {
                        installDir = AutoInstallDir(info, executableDirs);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"{nameof(LauncherSync)}: install folder of '{game.Title}' not resolved: {ex.Message}");
                    }
                }

                var steamAppId = info?.SteamAppId ?? game.ExtensionSteamAppId;
                var gogId = info?.GogId ?? GogGalaxy.GetGameIdFromInstallDir(game.ManualInstallDir ?? installDir);

                LauncherPlayTime? playTime = null;
                if (steamAppId is not null && steamTimes.TryGetValue(steamAppId, out int steamMinutes))
                    playTime = new LauncherPlayTime { Source = LaunchVia.Steam, Total = TimeSpan.FromMinutes(steamMinutes) };
                else if (gogId is not null && gogTimes.TryGetValue(gogId, out int gogMinutes))
                    playTime = new LauncherPlayTime { Source = LaunchVia.Gog, Total = TimeSpan.FromMinutes(gogMinutes) };

                results.Add(new GameResult(game.Game, installDir, playTime));
            }

            return results;
        }

        static string? AutoInstallDir(LaunchInfo info, Dictionary<string, int> executableDirs)
        {
            switch (info.Kind)
            {
                case LaunchKind.GogGalaxy:
                    return info.GogPath;

                case LaunchKind.Steam:
                    return info.SteamAppId is null ? null : SteamLocal.GetInstallDir(info.SteamAppId);

                case LaunchKind.Shell when info.EpicAppName is not null:
                    return EpicLocal.GetInstallDir(info.EpicAppName);

                case LaunchKind.Executable when info.Target is not null:
                    var dir = Path.GetDirectoryName(info.Target);
                    if (dir is null || SharedRuntimes.Contains(Path.GetFileName(info.Target)))
                        return null;

                    // drive roots and top-level folders like "D:\Games" hold more than one game
                    var depth = dir.TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (depth < 3)
                        return null;

                    return executableDirs.GetValueOrDefault(dir) > 1 ? null : dir;

                default:
                    return null;
            }
        }

        static Dictionary<string, int> Read(string what, Func<Dictionary<string, int>> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(LauncherSync)}: reading {what} failed: {ex.Message}");
                return [];
            }
        }
    }
}
