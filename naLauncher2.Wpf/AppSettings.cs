using System.IO;
using System.Text.Json;

namespace naLauncher2.Wpf
{
    internal class AppSettings
    {
        public string? LogPath { get; set; }
        public string? LibraryPath { get; set; }
        public string[] Sources { get; set; } = [];
        public bool TopLevelOnly { get; set; } = true;
        public string[] GameExtensions { get; set; } = GameLibrary.SupportedGameExtensions;
        public string? ImageCachePath { get; set; }
        public bool UserGamesSortDescending { get; set; } = false;
        public GamesSortMode UserGamesSortMode { get; set; } = GamesSortMode.Title;
        public UserGamesFilterMode UserGamesFilterMode { get; set; } = UserGamesFilterMode.Installed;
        public string[] UserGamesGenreFilter { get; set; } = [];
        public bool NewGamesCollapsed { get; set; } = false;
        public bool RecentGamesCollapsed { get; set; } = false;
        public bool RecentGamesInstalledOnly { get; set; } = true;
        public bool NewGamesSortDescending { get; set; } = true;
        public bool RecentGamesSortDescending { get; set; } = true;

        public class TwitchDevSettings
        {
            public string? ClientId { get; set; }
            public string? ClientSecret { get; set; }
        }
        public TwitchDevSettings? TwitchDev { get; set; }

        public bool LibraryPathMissing => LibraryPath == null || !File.Exists(LibraryPath);


        static readonly AppSettings _instance = new();
        public static AppSettings Instance => _instance;

        string? _settingsPath;

        /// <summary>
        /// Loads settings from the specified JSON file, or silently returns defaults if the file does not exist.
        /// </summary>
        public async Task Load(string path)
        {
            _settingsPath = path;
            if (!File.Exists(path))
                return;

            var content = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(content, App.JsonSerializerOptions);
            if (loaded != null)
            {
                LogPath = loaded.LogPath;
                LibraryPath = loaded.LibraryPath;
                UserGamesFilterMode = loaded.UserGamesFilterMode;
                UserGamesSortMode = loaded.UserGamesSortMode;
                UserGamesSortDescending = loaded.UserGamesSortDescending;
                UserGamesGenreFilter = loaded.UserGamesGenreFilter;
                NewGamesCollapsed = loaded.NewGamesCollapsed;
                RecentGamesCollapsed = loaded.RecentGamesCollapsed;
                RecentGamesInstalledOnly = loaded.RecentGamesInstalledOnly;
                NewGamesSortDescending = loaded.NewGamesSortDescending;
                RecentGamesSortDescending = loaded.RecentGamesSortDescending;
                Sources = loaded.Sources;
                TopLevelOnly = loaded.TopLevelOnly;
                GameExtensions = loaded.GameExtensions;
                ImageCachePath = loaded.ImageCachePath;

                if (loaded.TwitchDev != null)
                {
                    TwitchDev = new TwitchDevSettings
                    {
                        ClientId = loaded.TwitchDev.ClientId,
                        ClientSecret = loaded.TwitchDev.ClientSecret
                    };
                }
            }
        }

        /// <summary>
        /// Persists the current settings to the file loaded by <see cref="Load"/>.
        /// </summary>
        public async Task Save()
        {
            if (!string.IsNullOrEmpty(_settingsPath))
                await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(this, App.JsonSerializerOptions));
        }
    }
}
