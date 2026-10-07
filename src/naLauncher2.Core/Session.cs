namespace naLauncher2.Core
{
    /// <summary>
    /// One play session of a game.
    /// </summary>
    public class Session
    {
        public DateTime Start { get; set; }

        /// <summary>
        /// Wall-clock time the game was last seen running. Null while the session is being tracked,
        /// and for sessions that were never measured (all sessions migrated from the old
        /// <c>List&lt;DateTime&gt;</c> format, and launches the tracker could not follow).
        /// </summary>
        public DateTime? End { get; set; }

        /// <summary>
        /// Measured running time. Stored rather than derived from <see cref="End"/>, because time
        /// spent in sleep/hibernate is left out and DST changes would skew <c>End - Start</c>.
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// The launcher that saw this session and therefore already counts it in its own playtime
        /// total (see <see cref="GameInfo.TotalPlayTime"/>). Null when no launcher was involved.
        /// </summary>
        public LaunchVia? Via { get; set; }

        public bool Measured => Duration.HasValue;

        public Session()
        {
        }

        public Session(DateTime start, DateTime? end = null, TimeSpan? duration = null)
        {
            Start = start;
            End = end;
            Duration = duration;
        }
    }

    /// <summary>
    /// Game launchers whose own playtime records are imported.
    /// </summary>
    public enum LaunchVia
    {
        Steam,
        Gog,
    }

    /// <summary>
    /// Playtime a launcher reports for a game, as last read from its local data.
    /// </summary>
    public class LauncherPlayTime
    {
        public LaunchVia Source { get; set; }

        public TimeSpan Total { get; set; }

        /// <summary>
        /// When <see cref="Total"/> was last seen to change. Sessions through <see cref="Source"/>
        /// that ended before this are already part of <see cref="Total"/>; later ones are not yet.
        /// </summary>
        public DateTime ReadAt { get; set; }
    }
}
