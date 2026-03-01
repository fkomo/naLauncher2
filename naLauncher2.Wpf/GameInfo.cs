using naLauncher2.Wpf.Api;

namespace naLauncher2.Wpf
{
    public enum GameInfoExtension
    {
        SteamAppId,
        IgdbId
    }

    public class GameInfo
    {
        public DateTime Added { get; set; }
        public string? Shortcut { get; set; }
        public string? ImagePath { get; set; }

        public DateTime? Completed { get; set; }
        public List<DateTime> Played { get; set; } = [];
        public string? Summary { get; set; }
        public int? Rating { get; set; }
        public string? Developer { get; set; }
        public string[] Genres { get; set; } = [];

        public Dictionary<string, string> Extensions { get; set; } = [];

        public bool Installed => Shortcut is not null;
        public bool Removed => !Installed;
        public bool NotPlayed => Played == null || Played.Count == 0;
        public DateTime? LastPlayed => Played.Count > 0 ? Played.Last() : null;
        public bool MissingImage => ImagePath is null;

        public GameInfo()
        {
        }

        public GameInfo(string shortcut, DateTime? added = null) : this()
        {
            Shortcut = shortcut;
            Added = added ?? DateTime.Now;
        }

        internal void UpdateFromIgdb(IgdbGameData gameData)
        {
            if (gameData == null || string.IsNullOrEmpty(gameData.Id))
                return;

            Extensions ??= [];

            Extensions[GameInfoExtension.IgdbId.ToString()] = gameData.Id;

            Summary ??= gameData.Summary;
            Developer ??= gameData.Developer;
            ImagePath ??= gameData.ImagePath;

            if (Genres == null || Genres.Length == 0)
                Genres = gameData.Genres ?? [];
        }
    }
}