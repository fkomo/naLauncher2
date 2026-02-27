using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace naLauncher2.Wpf
{
    internal class AppSettings
    {
        public string? LibraryPath { get; set; }
        public string[] Sources { get; set; } = [];
        public string? ImageCachePath { get; set; }
        public bool UserGamesSortDescending { get; set; } = false;
        public GamesSortMode UserGamesSortMode { get; set; } = GamesSortMode.Title;
        public UserGamesFilterMode UserGamesFilterMode { get; set; } = UserGamesFilterMode.Installed;

        public bool LibraryPathMissing => LibraryPath == null || !File.Exists(LibraryPath);


        static readonly AppSettings _instance = new();
        public static AppSettings Instance => _instance;


        string? _settingsPath;

        static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
        };

        /// <summary>
        /// Loads settings from the specified JSON file, or silently returns defaults if the file does not exist.
        /// </summary>
        public async Task Load(string path)
        {
            _settingsPath = path;
            if (!File.Exists(path))
                return;

            var content = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(content, _jsonSerializerOptions);
            if (loaded != null)
            {
                LibraryPath = loaded.LibraryPath;
                UserGamesFilterMode = loaded.UserGamesFilterMode;
                UserGamesSortMode = loaded.UserGamesSortMode;
                UserGamesSortDescending = loaded.UserGamesSortDescending;
                Sources = loaded.Sources;
                ImageCachePath = loaded.ImageCachePath;
            }
        }

        /// <summary>
        /// Persists the current settings to the file loaded by <see cref="Load"/>.
        /// </summary>
        public async Task Save()
        {
            if (!string.IsNullOrEmpty(_settingsPath))
                await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(this, _jsonSerializerOptions));
        }
    }
}
