using naLauncher2.Core.Api;

namespace naLauncher2.Core
{
    public enum GameInfoExtension
    {
        SteamAppId,
        IgdbId,
        IgdbUrl
    }

    public class GameInfo
    {
        public DateTime Added { get; set; }
        public string? Shortcut { get; set; }
        public string? ImagePath { get; set; }

        public DateTime? Completed { get; set; }
        public bool Starred { get; set; }
        public List<Session> Played { get; set; } = [];
        public string? Summary { get; set; }
        public int? Rating { get; set; }
        public string? Developer { get; set; }
        public string[] Genres { get; set; } = [];
        public DateTime? ReleaseDate { get; set; }

        public Dictionary<string, string> Extensions { get; set; } = [];

        /// <summary>
        /// Folder the game's processes run from, used to detect a running game that wasn't
        /// started as a child of naLauncher (launcher-mediated games, fallbacks).
        /// </summary>
        public string? InstallDir { get; set; }

        /// <summary>
        /// Set when <see cref="InstallDir"/> was entered by hand; automatic resolution never overwrites it then.
        /// </summary>
        public bool InstallDirIsManual { get; set; }

        /// <summary>
        /// Playtime recorded by Steam or GOG Galaxy for this game, if any.
        /// </summary>
        public LauncherPlayTime? LauncherPlayTime { get; set; }

        public bool Installed => Shortcut is not null;
        public bool Removed => !Installed;
        public bool NotPlayed => Played == null || Played.Count == 0;
        public DateTime? LastPlayed => Played.Count > 0 ? Played.Last().Start : null;
        public bool MissingImage => ImagePath is null;

        /// <summary>
        /// Sum of all measured sessions tracked by naLauncher.
        /// </summary>
        public TimeSpan TrackedPlayTime => Played.Aggregate(TimeSpan.Zero, (sum, s) => sum + (s.Duration ?? TimeSpan.Zero));

        /// <summary>
        /// Tracked and launcher-reported playtime combined, without counting anything twice: the
        /// launcher total already contains every session that went through that launcher before
        /// its total was last updated, so only the remaining sessions are added to it.
        /// Null when nothing is known.
        /// </summary>
        public TimeSpan? TotalPlayTime
        {
            get
            {
                var launcher = LauncherPlayTime;
                var total = launcher?.Total ?? TimeSpan.Zero;

                foreach (var session in Played)
                {
                    if (session.Duration is not TimeSpan duration)
                        continue;

                    bool seenByLauncher = launcher is not null
                        && session.Via == launcher.Source
                        && session.End is DateTime end && end <= launcher.ReadAt;

                    if (!seenByLauncher)
                        total += duration;
                }

                return total > TimeSpan.Zero ? total : null;
            }
        }

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

            if (gameData.Url is not null)
                Extensions[GameInfoExtension.IgdbUrl.ToString()] = gameData.Url;

            Summary ??= gameData.Summary;
            ImagePath ??= gameData.ImagePath;

            Developer ??= gameData.Developer;
            if (Genres == null || Genres.Length == 0)
                Genres = gameData.Genres ?? [];
        }

        internal void UpdateFromSteam(SteamGameData gameData)
        {
            if (gameData == null || string.IsNullOrEmpty(gameData.Id))
                return;

            Extensions ??= [];

            Extensions[GameInfoExtension.SteamAppId.ToString()] = gameData.Id;

            if (gameData.Description != null)
                Summary = gameData.Description;

            if (gameData.ImagePath != null)
                ImagePath = gameData.ImagePath;

            Rating ??= gameData.MetacriticScore;
            ReleaseDate ??= gameData.ReleaseDate;
        }
    }
}