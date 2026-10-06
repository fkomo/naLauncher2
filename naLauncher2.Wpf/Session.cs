namespace naLauncher2.Wpf
{
    /// <summary>
    /// One play session of a game. <see cref="End"/> is null while the session is running,
    /// or when its end was never recorded (all sessions migrated from the old <c>List&lt;DateTime&gt;</c> format).
    /// </summary>
    public class Session
    {
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }

        public TimeSpan? Duration => End - Start;

        public Session()
        {
        }

        public Session(DateTime start, DateTime? end = null)
        {
            Start = start;
            End = end;
        }
    }
}
