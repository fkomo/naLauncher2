using System.IO;
using System.Text.Json;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Launches games and follows them until they exit, filling in <see cref="Session.End"/>,
    /// <see cref="Session.Duration"/> and <see cref="Session.Via"/>.
    /// <para>
    /// Must be used from the UI thread: polling awaits on the captured synchronization context, so
    /// every change to the library happens on the UI thread; only the detector checks run in the background.
    /// </para>
    /// </summary>
    public sealed class SessionTracker
    {
        static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        /// <summary>A step longer than this means the computer was asleep; it isn't counted.</summary>
        static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(10);
        /// <summary>How long to wait for the game to show up after launch (a launcher may need to start first).</summary>
        static readonly TimeSpan AppearTimeout = TimeSpan.FromMinutes(3);
        /// <summary>How long the game must be gone before the session ends.</summary>
        static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(30);
        /// <summary>A session starting this soon after the previous one of the same game ended is merged into it.</summary>
        public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(2);
        static readonly TimeSpan StateWriteInterval = TimeSpan.FromSeconds(60);

        public static SessionTracker Instance { get; } = new();

        /// <summary>
        /// Raised (on the UI thread) after a session started, ended or was restored, once the library was saved.
        /// </summary>
        public event Action? SessionsChanged;

        /// <summary>
        /// Raised (on the UI thread) after a tracked session ended and was saved.
        /// </summary>
        public event Action<GameInfo>? SessionEnded;

        readonly List<TrackedSession> _active = [];
        DateTime _lastStateWrite;

        public bool IsTracking => _active.Count > 0;

        public IEnumerable<GameInfo> TrackedGames => _active.Select(t => t.Game);

        public bool IsTracked(GameInfo game) => _active.Any(t => t.Game == game);

        public bool IsTracked(Session session) => _active.Any(t => t.Session == session);

        enum TrackState { Waiting, Running }

        sealed class TrackedSession(GameInfo game, Session session, IRunningDetector detector, string? steamAppId, LaunchVia? via)
        {
            public GameInfo Game { get; } = game;
            public Session Session { get; } = session;
            public IRunningDetector Detector { get; } = detector;
            public string? SteamAppId { get; } = steamAppId;
            public LaunchVia? Via { get; set; } = via;
            public TrackState State { get; set; }
            public bool WasRunning { get; set; }
            public TimeSpan Accumulated { get; set; }
            public DateTime LastSeen { get; set; }
            public bool Stopped { get; set; }
        }

        /// <summary>
        /// Launches the game, records a new session for it right away and starts following it.
        /// </summary>
        /// <exception cref="Exception">The game could not be started; nothing is recorded then.</exception>
        public async Task Launch(string title, GameInfo game)
        {
            var info = ShortcutResolver.Resolve(game.Shortcut!);
            var job = GameLauncher.Launch(info);

            var session = new Session(DateTime.Now);
            game.Played.Add(session);

            var detector = CreateDetector(info, job, game);
            if (detector is null)
            {
                Log.WriteLine($"{nameof(SessionTracker)}: '{title}' can't be followed ({info.Kind}, no install folder) - session stays unmeasured");
            }
            else
            {
                var steamAppId = info.SteamAppId ?? game.Extensions.GetValueOrDefault(GameInfoExtension.SteamAppId.ToString());
                var via = info.Kind == LaunchKind.GogGalaxy ? LaunchVia.Gog : (LaunchVia?)null;
                var tracked = new TrackedSession(game, session, detector, steamAppId, via);
                _active.Add(tracked);

                Log.WriteLine($"{nameof(SessionTracker)}: tracking '{title}' via {detector}");
                _ = Follow(tracked, title);
            }

            await GameLibrary.Instance.Save();
            WriteState();
            SessionsChanged?.Invoke();
        }

        static IRunningDetector? CreateDetector(LaunchInfo info, IntPtr job, GameInfo game)
        {
            if (info.Kind == LaunchKind.Steam && info.SteamAppId is not null)
            {
                if (job != IntPtr.Zero)
                    Native.CloseHandle(job);
                return new SteamAppDetector(info.SteamAppId);
            }

            var folder = !string.IsNullOrEmpty(game.InstallDir) && Directory.Exists(game.InstallDir)
                ? new FolderDetector(game.InstallDir)
                : null;

            // the job covers the process tree; the folder additionally catches games that hand off to an
            // already-running instance of themselves, or that are restarted through their launcher
            if (job != IntPtr.Zero)
                return folder is null ? new JobDetector(job) : new AnyDetector(new JobDetector(job), folder);

            return folder;
        }

        async Task Follow(TrackedSession tracked, string title)
        {
            var lastTick = DateTime.Now;

            while (true)
            {
                await Task.Delay(PollInterval);
                if (tracked.Stopped)
                    return;

                bool running;
                string? steamRunning = null;
                try
                {
                    (running, steamRunning) = await Task.Run(() => (
                        tracked.Detector.IsRunning(),
                        tracked.SteamAppId is not null ? SteamLocal.RunningAppId : null));
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"{nameof(SessionTracker)}: detector for '{title}' failed: {ex.Message}");
                    running = false;
                }

                if (tracked.Stopped)
                    return;

                var now = DateTime.Now;
                var step = now - lastTick;
                lastTick = now;

                // a game Steam reports as running went through Steam (also games that restart themselves
                // through Steam when started directly), so Steam's own total already counts this session
                if (tracked.SteamAppId is not null && steamRunning == tracked.SteamAppId)
                    tracked.Via = LaunchVia.Steam;

                if (running)
                {
                    if (tracked.State == TrackState.Waiting)
                    {
                        tracked.State = TrackState.Running;
                        Log.WriteLine($"{nameof(SessionTracker)}: '{title}' is running");
                    }
                    else if (tracked.WasRunning && step <= MaxStep)
                        tracked.Accumulated += step;

                    tracked.LastSeen = now;
                }
                else if (tracked.State == TrackState.Waiting && now - tracked.Session.Start >= AppearTimeout)
                {
                    Log.WriteLine($"{nameof(SessionTracker)}: '{title}' never showed up - session stays unmeasured");
                    await End(tracked, measured: false);
                    return;
                }
                else if (tracked.State == TrackState.Running && now - tracked.LastSeen >= EndGrace)
                {
                    await End(tracked, measured: true);
                    return;
                }

                tracked.WasRunning = running;

                if (now - _lastStateWrite >= StateWriteInterval)
                    WriteState();
            }
        }

        async Task End(TrackedSession tracked, bool measured)
        {
            Finish(tracked, measured);

            await GameLibrary.Instance.Save();
            WriteState();
            SessionsChanged?.Invoke();
            SessionEnded?.Invoke(tracked.Game);
        }

        void Finish(TrackedSession tracked, bool measured)
        {
            tracked.Stopped = true;
            _active.Remove(tracked);
            tracked.Detector.Dispose();

            if (!measured)
                return;

            var session = tracked.Session;
            session.End = tracked.LastSeen;
            session.Duration = tracked.Accumulated;
            session.Via = tracked.Via;

            Log.WriteLine($"{nameof(SessionTracker)}: session ended after {PlayTimeFormat.Format(tracked.Accumulated)}");

            ApplySessionRules(tracked.Game.Played, session, MergeGap,
                TimeSpan.FromSeconds(Math.Max(0, AppSettings.Instance.MinSessionDurationSeconds)));
        }

        /// <summary>
        /// Merges a just-ended session into the previous one of the same game when it started within
        /// <paramref name="mergeGap"/> of that one's end (a quick relaunch), then drops the resulting
        /// session if it is shorter than <paramref name="minDuration"/> (a crashed or aborted launch).
        /// </summary>
        public static void ApplySessionRules(List<Session> played, Session session, TimeSpan mergeGap, TimeSpan minDuration)
        {
            int index = played.IndexOf(session);
            if (index < 0)
                return;

            if (index > 0 && played[index - 1] is { End: DateTime previousEnd, Duration: TimeSpan previousDuration } previous
                && session.Start - previousEnd <= mergeGap)
            {
                previous.End = session.End;
                previous.Duration = previousDuration + (session.Duration ?? TimeSpan.Zero);
                previous.Via ??= session.Via;
                played.RemoveAt(index);
                session = previous;
            }

            if (session.Duration < minDuration)
                played.Remove(session);
        }

        /// <summary>
        /// Ends every tracked session now (naLauncher is exiting, or Windows is shutting down) and saves.
        /// Sessions of games that are still running are cut off at this moment.
        /// </summary>
        public async Task StopAll()
        {
            if (_active.Count == 0)
                return;

            var now = DateTime.Now;
            foreach (var tracked in _active.ToArray())
            {
                bool measured = tracked.State == TrackState.Running;
                if (measured && tracked.WasRunning && now - tracked.LastSeen <= MaxStep)
                {
                    tracked.Accumulated += now - tracked.LastSeen;
                    tracked.LastSeen = now;
                }
                Finish(tracked, measured);
            }

            await GameLibrary.Instance.Save();
            DeleteState();
        }

        #region crash safety

        /// <summary>
        /// Open sessions, written next to the library while tracking so they survive a crash or kill.
        /// </summary>
        sealed class StateEntry
        {
            public string Title { get; set; } = string.Empty;
            public DateTime Start { get; set; }
            public TimeSpan Accumulated { get; set; }
            public DateTime? LastSeen { get; set; }
            public string? SteamAppId { get; set; }
            public bool SteamLaunch { get; set; }
            public LaunchVia? Via { get; set; }
        }

        static string? StatePath => GameLibrary.Instance.LibraryPath is string library
            ? Path.ChangeExtension(library, ".tracking.json")
            : null;

        void WriteState()
        {
            _lastStateWrite = DateTime.Now;

            var path = StatePath;
            if (path is null)
                return;

            if (_active.Count == 0)
            {
                DeleteState();
                return;
            }

            var entries = _active.Select(t => new StateEntry
            {
                Title = GameLibrary.Instance.Games.FirstOrDefault(g => g.Value == t.Game).Key ?? string.Empty,
                Start = t.Session.Start,
                Accumulated = t.Accumulated,
                LastSeen = t.State == TrackState.Running ? t.LastSeen : null,
                SteamAppId = t.SteamAppId,
                SteamLaunch = t.Detector is SteamAppDetector,
                Via = t.Via,
            }).ToArray();

            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonDefaults.Options));
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(SessionTracker)}: failed to write '{path}': {ex.Message}");
            }
        }

        static void DeleteState()
        {
            try
            {
                if (StatePath is string path && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(SessionTracker)}: failed to delete tracking state: {ex.Message}");
            }
        }

        /// <summary>
        /// Picks up sessions that were being tracked when naLauncher last stopped without ending them
        /// (crash, kill). A game that is still running is followed again; otherwise the session ends at
        /// the last time it was seen, or stays unmeasured if it never was. Call once after the library loads.
        /// </summary>
        public async Task Restore()
        {
            var path = StatePath;
            if (path is null || !File.Exists(path))
                return;

            StateEntry[] entries;
            try
            {
                entries = JsonSerializer.Deserialize<StateEntry[]>(File.ReadAllText(path), JsonDefaults.Options) ?? [];
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(SessionTracker)}: unreadable tracking state '{path}': {ex.Message}");
                DeleteState();
                return;
            }

            foreach (var entry in entries)
            {
                // the title may have changed while tracking; the session's start time identifies it as well
                var (title, game) = GameLibrary.Instance.Games.TryGetValue(entry.Title, out var byTitle)
                    ? (entry.Title, byTitle)
                    : GameLibrary.Instance.Games
                        .Where(g => g.Value.Played.Any(s => s.Start == entry.Start))
                        .Select(g => (g.Key, g.Value))
                        .FirstOrDefault();

                var session = game?.Played.FirstOrDefault(s => s.Start == entry.Start && s.End is null);
                if (game is null || session is null)
                    continue;

                IRunningDetector? detector = entry.SteamLaunch && entry.SteamAppId is not null
                    ? new SteamAppDetector(entry.SteamAppId)
                    : !string.IsNullOrEmpty(game.InstallDir) && Directory.Exists(game.InstallDir) ? new FolderDetector(game.InstallDir) : null;

                var tracked = new TrackedSession(game, session, detector ?? new AnyDetector(), entry.SteamAppId, entry.Via)
                {
                    Accumulated = entry.Accumulated,
                    LastSeen = entry.LastSeen ?? entry.Start,
                    State = entry.LastSeen.HasValue ? TrackState.Running : TrackState.Waiting,
                };

                bool stillRunning = detector is not null && await Task.Run(detector.IsRunning);
                if (stillRunning)
                {
                    // the time naLauncher wasn't running is unknown, so it isn't counted
                    tracked.State = TrackState.Running;
                    tracked.LastSeen = DateTime.Now;
                    _active.Add(tracked);
                    Log.WriteLine($"{nameof(SessionTracker)}: '{title}' is still running - tracking resumed");
                    _ = Follow(tracked, title);
                }
                else
                {
                    Log.WriteLine($"{nameof(SessionTracker)}: closing interrupted session of '{title}'");
                    Finish(tracked, measured: entry.LastSeen.HasValue);
                }
            }

            await GameLibrary.Instance.Save();
            WriteState();
            SessionsChanged?.Invoke();
        }

        #endregion
    }
}
