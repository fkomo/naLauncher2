using naLauncher2.Wpf.Api;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Ujeby.Tools;

namespace naLauncher2.Wpf
{
    internal class GameLibrary
    {
        public ConcurrentDictionary<string, GameInfo> Games { get; set; } = [];

        public IEnumerable<string> NewGames() => Games
            .Where(x => x.Value.Installed && x.Value.NotPlayed)
            .OrderByDescending(x => x.Value.Added)
            .Select(x => x.Key);

        public IEnumerable<string> RecentGames() => Games
            .Where(x => x.Value.Installed && !x.Value.NotPlayed)
            .OrderByDescending(x => x.Value.Played.Last())
            .Select(x => x.Key);

        public IEnumerable<string> Steam() => Games
            .Where(x => x.Value.Extensions?.ContainsKey(GameInfoExtension.SteamAppId.ToString()) == true)
            .Select(x => x.Key);

        public IEnumerable<string> Igdb() => Games
            .Where(x => x.Value.Extensions?.ContainsKey(GameInfoExtension.IgdbId.ToString()) == true)
            .Select(x => x.Key);

        public IEnumerable<string> Genres => Games
            .SelectMany(x => x.Value.Genres ?? [])
            .Distinct();

        public Dictionary<string, int> GenresWithCounts => Genres
            .ToDictionary(
                x => x,
                x => Games.Count(xx => xx.Value.Genres?.Contains(x) == true));

        public IEnumerable<string> ExtensionsUsed => Games
            .SelectMany(x => x.Value.Extensions.Keys)
            .Distinct();

        public readonly static string[] SupportedGameExtensions =
        [
            ".lnk",
            ".exe",
            ".url",
            ".cmd",
            ".bat"
        ];

        string? _libraryPath;

        static readonly GameLibrary _instance = new();

        public static GameLibrary Instance => _instance;

        public async Task Load(string path)
        {
            if (!File.Exists(path))
            {
                Log.WriteLine($"Game library file not found at '{path}' - starting with an empty library");

                Games = [];

                _libraryPath = path;

                await Save();

                return;
            }

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Load)}('{path}')", Log.WriteLine);

            var libraryContent = await File.ReadAllTextAsync(path);

            _libraryPath = path;

            Games = Deserialize(libraryContent);
        }

        static ConcurrentDictionary<string, GameInfo> Deserialize(string libraryContent)
        {
            return JsonSerializer.Deserialize<ConcurrentDictionary<string, GameInfo>>(libraryContent, options: App.JsonSerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize game library.");
        }

        public async Task Restore(string backupPath)
        {
            if (!File.Exists(backupPath))
            {
                Log.WriteLine($"Backup file not found at '{backupPath}' - cannot restore library");
                return;
            }

            if (_libraryPath is null)
                return;

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Restore)}('{backupPath}')", Log.WriteLine);

            var backupContent = await File.ReadAllBytesAsync(backupPath);
            var libraryContent = GZip.Decompress(backupContent);

            Games = Deserialize(libraryContent);

            Log.WriteLine($"Game library restored - {Games.Count} games");
        }

        public async Task Save(bool silent = false)
        {
            if (!string.IsNullOrEmpty(_libraryPath))
            {
                await File.WriteAllTextAsync(_libraryPath, JsonSerializer.Serialize(Games, options: App.JsonSerializerOptions));

                if (!silent)
                    Log.WriteLine($"{nameof(GameLibrary)}.{nameof(Save)}({Games.Count} games)");
            }
        }

        public async Task Backup()
        {
            if (_libraryPath is null)
                return;

            var backupDir = Path.GetDirectoryName(Path.GetFullPath(_libraryPath)) ?? ".";
            var backupBaseName = Path.GetFileName(_libraryPath.Trim(".json").ToString()) + "_";

            var serialized = JsonSerializer.Serialize(Games, options: App.JsonSerializerOptions);
            var compressed = GZip.Compress(serialized);
            var newHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(compressed));

            var lastBackup = Directory.GetFiles(backupDir, "*.bak")
                .Where(f => Path.GetFileName(f).StartsWith(backupBaseName))
                .Order()
                .LastOrDefault();

            if (lastBackup != null)
            {
                var lastBytes = await File.ReadAllBytesAsync(lastBackup);
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lastBytes)) == newHash)
                {
                    Log.WriteLine("Game library backup skipped - no changes since last backup");
                    return;
                }
            }

            var backupPath = $"{_libraryPath.Trim(".json")}_{DateTime.Now:yyyyMMddHHmmss}";

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Backup)}('{backupPath}')", Log.WriteLine);

#if DEBUG
            await File.WriteAllTextAsync(backupPath + ".json", serialized);
#endif
            await File.WriteAllBytesAsync(backupPath + ".bak", compressed);

            const int maxBackups = 10;

            var backupFiles = Directory.GetFiles(backupDir, "*.bak")
                .Where(f => Path.GetFileName(f).StartsWith(backupBaseName))
                .Order()
                .ToArray();

            foreach (var old in backupFiles.Take(Math.Max(0, backupFiles.Length - maxBackups)))
            {
                File.Delete(old);
#if DEBUG
                var jsonBackup = old[..^".bak".Length] + ".json";
                if (File.Exists(jsonBackup))
                    File.Delete(jsonBackup);
#endif
            }
        }

        public async Task<bool> RefreshSources(string[]? sources, string[]? extensions = null)
        {
            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshSources)}()", Log.WriteLine);

            if (sources == null || sources.Length == 0)
                return false;

            extensions ??= SupportedGameExtensions;

            var sourceGames = sources
                .SelectMany(x => Directory.GetFiles(x, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f))))
                .ToDictionary(x => Path.GetFileNameWithoutExtension(x), x => x);

            bool changed = false;

            // add new games from sources
            foreach (var newGame in sourceGames.Where(x => !Games.Any(xx => xx.Value.Shortcut == x.Value)))
            {
                Log.WriteLine($"New game found '{newGame.Key}'");

                var newGameInfo = new GameInfo(newGame.Value, added: new FileInfo(newGame.Value).CreationTime);

                // add new game
                Games.AddOrUpdate(newGame.Key, newGameInfo, (key, oldGameInfo) => newGameInfo);

                changed = true;
            }

            // mark games as uninstalled if their shortcuts no longer exist
            foreach (var uninstalledGame in Games.Where(x => x.Value.Installed && !File.Exists(x.Value.Shortcut)).Select(x => x.Key))
            {
                Log.WriteLine($"Shortcut for '{uninstalledGame}' not found");

                Games[uninstalledGame].Shortcut = null;

                changed = true;
            }

            // update shortcuts for games that have changed paths
            foreach (var updatedGame in Games.Where(x => sourceGames.ContainsKey(x.Key) && x.Value.Shortcut != sourceGames[x.Key]).Select(x => x.Key))
            {
                Log.WriteLine($"Game shortcut updated for '{updatedGame}'");

                // update shortcut
                Games[updatedGame].Shortcut = sourceGames[updatedGame];

                changed = true;
            }

            if (changed)
                await Save();

            return changed;
        }

        public async Task<bool> RefreshMissingGameImagesFromCache()
        {
            if (AppSettings.Instance.ImageCachePath is null)
            {
                Log.WriteLine("Image cache not set");
                return false;
            }

            var cachedImages = Directory.GetFiles(AppSettings.Instance.ImageCachePath, "*.*", SearchOption.AllDirectories);
            if (cachedImages.Length == 0)
            {
                Log.WriteLine("No cached images found");
                return false;
            }

            var gamesToUpdate = Games
                .Where(x => x.Value.ImagePath is null)
                .Select(x => x.Key)
                .ToArray();

            if (gamesToUpdate.Length == 0)
            {
                Log.WriteLine("No games with missing images");
                return false;
            }

            Log.WriteLine($"{gamesToUpdate.Length} games are missing image ...");

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshMissingGameImagesFromCache)}()", Log.WriteLine);

            var changed = false;

            foreach (var game in gamesToUpdate)
            {
                var cachedImage = cachedImages.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(game, StringComparison.OrdinalIgnoreCase));
                if (cachedImage is null)
                    continue;

                Games[game].ImagePath = cachedImage;

                Log.WriteLine($"'{game}' image updated with '{cachedImage}'");

                changed = true;
                await Save(silent: true);
            }

            return changed;
        }

        public async Task<bool> RefreshMissingGameData(IProgress<(int current, int total, string gameTitle)>? progress = null)
        {
            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshMissingGameData)}()", Log.WriteLine);

            var changed = false;

            var gamesToUpdate = Games.Keys.ToArray();

            for (var i = 0; i < gamesToUpdate.Length; i++)
            {
                var game = gamesToUpdate[i];

                progress?.Report((i, gamesToUpdate.Length, game));

                if (await RefreshGameData(game, silent: true))
                    changed = true;
            }

            return changed;
        }

        public async Task<bool> RefreshGameData(string gameTitle, bool silent = false)
        {
            if (!Games.ContainsKey(gameTitle))
            {
                Log.WriteLine($"Game '{gameTitle}' not found in library");
                return false;
            }

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshGameData)}('{gameTitle}')", Log.WriteLine);

            var changed = false;
            
            changed |= await RefreshIgdbGameData(gameTitle);
            changed |= await RefreshSteamGameData(gameTitle);

            if (changed)
                await Save(silent: silent);

            return changed;
        }

        async Task<bool> RefreshSteamGameData(string gameTitle)
        {
            var gameInfo = Games[gameTitle];

            gameInfo.Extensions.TryGetValue(GameInfoExtension.SteamAppId.ToString(), out string? steamAppId);
            if (steamAppId != null)
                return false;

            steamAppId = await SteamClient.GetAppId(gameTitle);
            if (steamAppId == null)
                return false;

            gameInfo.Extensions.Add(GameInfoExtension.SteamAppId.ToString(), steamAppId);

            Log.WriteLine($"'{gameTitle}' updated with SteamAppId={steamAppId}");

            return true;
        }

        async Task<bool> RefreshIgdbGameData(string gameTitle)
        {
            if (App.IgdbClient == null)
                return false;

            var gameInfo = Games[gameTitle];

            gameInfo.Extensions.TryGetValue(GameInfoExtension.IgdbId.ToString(), out string? igdbId);

            var igdbGameData = await App.IgdbClient.GetGameData(gameTitle, gameId: igdbId, getImage: true);
            if (igdbGameData == null)
                return false;

            gameInfo.UpdateFromIgdb(igdbGameData);

            Log.WriteLine($"'{gameTitle}' updated with IgdbId={igdbGameData.Id}");

            return true;
        }

        public async Task RemoveExtensions(params string[] values)
        {
            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RemoveExtensions)}({string.Join(", ", values)})", Log.WriteLine);

            foreach (var game in Games)
            {
                if (game.Value.Extensions is null)
                    continue;

                foreach (var value in values)
                    game.Value.Extensions.Remove(value);
            }

            await Save();
        }
    }
}
