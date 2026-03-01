using naLauncher2.Wpf.Common;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

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

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Load)}('{path}')");

            var libraryContent = await File.ReadAllTextAsync(path);

            var loaded = JsonSerializer.Deserialize<GameLibrary>(libraryContent, options: App.JsonSerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize game library.");

            _libraryPath = path;

            Games = loaded.Games ?? [];

            Log.WriteLine($"Game library loaded - {Games.Count} games");
        }

        public async Task Save(bool silent = false)
        {
            if (!string.IsNullOrEmpty(_libraryPath))
            {
                await File.WriteAllTextAsync(_libraryPath, JsonSerializer.Serialize(this, options: App.JsonSerializerOptions));
                
                if (!silent)
                    Log.WriteLine($"Game library saved with {Games.Count} games");
            }
        }

        static string[] SupportedGameExtensions { get; set; } =
        [
            ".lnk",
            ".exe",
            ".url",
            ".cmd",
            ".bat"
        ];

        public async Task<bool> RefreshSources(string[]? sources)
        {
            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshSources)}()");

            if (sources == null || sources.Length == 0)
                return false;

            var sourceGames = sources
                .SelectMany(x => Directory.GetFiles(x, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedGameExtensions.Contains(Path.GetExtension(f))))
                .ToDictionary(x => Path.GetFileNameWithoutExtension(x), x => x);

            bool changed = false;

            // add new games from sources
            foreach (var newGame in sourceGames.Where(x => !Games.ContainsKey(x.Key)))
            {

                var newGameInfo = new GameInfo(newGame.Value, added: new FileInfo(newGame.Value).CreationTime);

                // add new game
                Games.AddOrUpdate(newGame.Key, newGameInfo, (key, oldGameInfo) => newGameInfo);
                Log.WriteLine($"'{newGame.Key}' added to library");

                changed = true;
            }

            // remove games that are no longer in sources
            foreach (var removedGameFromSource in Games.Where(x => x.Value.Installed && !sourceGames.ContainsKey(x.Key)).Select(x => x.Key))
            {
                Log.WriteLine($"'{removedGameFromSource}' not found in source");

                // mark game as removed
                Games[removedGameFromSource].Shortcut = null;

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

            // mark games as uninstalled if their shortcuts no longer exist
            foreach (var uninstalledGame in Games.Where(x => x.Value.Installed && !File.Exists(x.Value.Shortcut)).Select(x => x.Key))
            {
                Log.WriteLine($"Shortcut for '{uninstalledGame}' not found");

                Games[uninstalledGame].Shortcut = null;

                changed = true;
            }

            if (changed)
                await Save();

            return changed;
        }

        public async Task<bool> RefreshMissingGameImages()
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

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshMissingGameImages)}()");

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

        public async Task<bool> RefreshMissingGameData()
        {
            if (AppSettings.Instance.TwitchDev == null)
                return false;

            var gamesToUpdate = Games
                .Where(x => x.Value.Extensions != null && !x.Value.Extensions.ContainsKey(GameInfoExtension.IgdbId.ToString()))
                .Select(x => x.Key)
                .ToArray();

            if (gamesToUpdate.Length == 0)
            {
                Log.WriteLine("No games with missing IGDB data found");
                return false;
            }

            Log.WriteLine($"{gamesToUpdate.Length} games are missing IGDB data ...");

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(RefreshMissingGameData)}()");

            var igdbClient = new Api.IgdbClient(AppSettings.Instance.TwitchDev);
            
            var changed = false;

            foreach (var game in gamesToUpdate)
            {
                var gameData = await igdbClient.GetGameData(game);
                if (gameData != null)
                {
                    Games[game].UpdateFromIgdb(gameData);

                    changed = true;

                    await Save(silent: true);
                }
            }

            return changed;
        }
    }
}
