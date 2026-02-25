namespace naLauncher2.Wpf
{
    public class GameInfo
    {
        public DateTime Added { get; set; }
        public DateTime? Completed { get; set; }
        public string? Shortcut { get; set; }
        public string? ImagePath { get; set; }
        public List<DateTime> Played { get; set; } = [];
        public string? Summary { get; set; }
        public int? Rating { get; set; }
        public string? Developer { get; set; }
        public string[] Genres { get; set; } = [];

        public bool Installed => Shortcut is not null;
        public bool Removed => !Installed;
        public bool NotPlayed => Played == null || Played.Count == 0;
        public bool Finished => Completed.HasValue;
        public DateTime? LastPlayed => Played.Count > 0 ? Played.Last() : null;
    }
}
