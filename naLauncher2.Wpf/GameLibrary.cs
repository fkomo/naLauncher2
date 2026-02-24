using naLauncher2.Wpf.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace naLauncher2.Wpf
{
    internal class GameLibrary
    {
        public Dictionary<string, GameInfo> Games { get; private set; } = [];

        public IEnumerable<string> NewGames() => Games
            .Where(x => x.Value.Installed && x.Value.NotPlayed)
            .OrderByDescending(x => x.Value.Added)
            .Select(x => x.Key);

        public IEnumerable<string> RecentGames() => Games
            .Where(x => x.Value.Installed && !x.Value.NotPlayed)
            .OrderByDescending(x => x.Value.Played.Last())
            .Select(x => x.Key);

        public IEnumerable<string> InstalledGames() => Games
            .Where(x => x.Value.Installed)
            .OrderBy(x => x.Key)
            .Select(x => x.Key);

        string? _libraryPath;

        static readonly GameLibrary _instance = new();

        static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields = true,
            WriteIndented = false
        };

        public static GameLibrary Instance => _instance;

        public async Task Load(string path)
        {
            if (!File.Exists(path))
            {
                Debug.WriteLine($"Game library file not found at {path}. Starting with an empty library.");

                Games = new Dictionary<string, GameInfo>();
                _libraryPath = path;

                return;
            }

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Load)}({path})");

            var libraryContent = await File.ReadAllTextAsync(path);

            Games = JsonSerializer.Deserialize<Dictionary<string, GameInfo>>(libraryContent, options: _jsonSerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize game library.");

            _libraryPath = path;

            Debug.WriteLine($"Loaded {Games.Count} games from library.");
        }

        public async Task Save()
        {
            if (!string.IsNullOrEmpty(_libraryPath))
                await File.WriteAllTextAsync(_libraryPath, JsonSerializer.Serialize(Games, options: _jsonSerializerOptions));
        }
    }
}
