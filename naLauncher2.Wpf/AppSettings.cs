using System.IO;
using System.Text.Json;

namespace naLauncher2.Wpf
{
    internal class AppSettings
    {
        public string? LibraryPath { get; set; }
        public UserGamesFilterMode UserGamesFilterMode { get; set; } = UserGamesFilterMode.Installed;


        static readonly AppSettings _instance = new();
        public static AppSettings Instance => _instance;

        string? _settingsPath;

        static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
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
            var loaded = JsonSerializer.Deserialize<AppSettings>(content, _jsonOptions);
            if (loaded != null)
            {
                LibraryPath = loaded.LibraryPath;
                UserGamesFilterMode = loaded.UserGamesFilterMode;
            }
        }

        /// <summary>
        /// Persists the current settings to the file loaded by <see cref="Load"/>.
        /// </summary>
        public async Task Save()
        {
            if (!string.IsNullOrEmpty(_settingsPath))
                await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(this, _jsonOptions));
        }
    }
}
