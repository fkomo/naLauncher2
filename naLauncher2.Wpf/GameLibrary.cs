using naLauncher2.Wpf.Common;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace naLauncher2.Wpf
{
    internal class GameLibrary
    {
        public ConcurrentDictionary<string, GameInfo> Games { get; private set; } = [];

        string? _libraryPath;

        static readonly GameLibrary _instance = new();
        
        static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields= true,
            WriteIndented = false
        };

        public static GameLibrary Instance => _instance;

        public async Task Load(string path)
        {
            if (!File.Exists(path))
            {
                Debug.WriteLine($"Game library file not found at {path}. Starting with an empty library.");

                Games = new ConcurrentDictionary<string, GameInfo>();
                _libraryPath = path;

                return;
            }

            using var tb = new TimedBlock($"{nameof(GameLibrary)}.{nameof(Load)}({path})");

            var libraryContent = await File.ReadAllTextAsync(path);

            Games = JsonSerializer.Deserialize<ConcurrentDictionary<string, GameInfo>>(libraryContent, options: _jsonSerializerOptions)
                ?? throw new InvalidOperationException("Failed to deserialize game library.");

            _libraryPath = path;

            Debug.WriteLine($"Loaded {Games.Count} games from library.");
        }

        public async Task Save()
        {
            if (!string.IsNullOrEmpty(_libraryPath))
                await File.WriteAllTextAsync(_libraryPath, JsonSerializer.Serialize(Games, options: _jsonSerializerOptions));
        }

        public async Task LoadSample(int count = 10)
        {
            var _random = new Random();

            var images = Directory
                .EnumerateFiles(@"c:\Users\filip\AppData\Roaming\Ujeby\naLauncher\ImageCache", "*", SearchOption.AllDirectories)
                .ToArray();

            string GetRandomImagePath()
            {
                string path = images[_random.Next(images.Length)];

                while (Games.ContainsKey(Path.GetFileNameWithoutExtension(path)))
                    path = images[_random.Next(images.Length)];

                return path;
            }

            for (var i = 0; i < count; i++)
            {
                var imagePath = GetRandomImagePath();
                var title = Path.GetFileNameWithoutExtension(imagePath);

                Games[title] = new GameInfo
                {
                    ImagePath = imagePath
                };
            }
        }
    }
}
