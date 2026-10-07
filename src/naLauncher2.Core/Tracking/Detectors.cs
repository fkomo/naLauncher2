using System.Diagnostics;
using System.IO;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Answers "is the game running right now?" for one tracked session. Polled from a background thread.
    /// </summary>
    public interface IRunningDetector : IDisposable
    {
        bool IsRunning();
    }

    /// <summary>
    /// Running while any process of the Job Object naLauncher started the game in is alive.
    /// Covers bootstrappers that start the real game and exit, and wrappers like DOSBox/ScummVM.
    /// </summary>
    sealed class JobDetector(IntPtr job) : IRunningDetector
    {
        IntPtr _job = job;

        public bool IsRunning() => _job != IntPtr.Zero && Native.ActiveJobProcesses(_job) > 0;

        public void Dispose()
        {
            if (_job != IntPtr.Zero)
                Native.CloseHandle(_job);
            _job = IntPtr.Zero;
        }

        public override string ToString() => "job";
    }

    /// <summary>
    /// Running while Steam flags the app as running.
    /// </summary>
    sealed class SteamAppDetector(string appId) : IRunningDetector
    {
        public bool IsRunning() => SteamLocal.IsAppRunning(appId);

        public void Dispose() { }

        public override string ToString() => $"steam app {appId}";
    }

    /// <summary>
    /// Running while any process runs an executable from inside the install folder,
    /// ignoring helpers that may linger there (crash handlers, redistributable installers, …).
    /// </summary>
    sealed class FolderDetector : IRunningDetector
    {
        static readonly string[] ExcludedPrefixes =
        [
            "UnityCrashHandler", "CrashReportClient", "CrashSender", "crashpad_handler",
            "vc_redist", "vcredist", "DXSETUP", "dotNetFx", "unins", "Uninstall",
            "EasyAntiCheat", "BEService", "BattlEye",
        ];

        readonly string _folder;

        public FolderDetector(string folder)
        {
            _folder = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        }

        public bool IsRunning()
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var path = Native.GetProcessImagePath(process.Id);
                    if (path is null || !path.StartsWith(_folder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = Path.GetFileName(path);
                    if (!ExcludedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            return false;
        }

        public void Dispose() { }

        public override string ToString() => $"folder '{_folder}'";
    }

    /// <summary>
    /// Running while any of the given detectors says so.
    /// </summary>
    sealed class AnyDetector(params IRunningDetector[] detectors) : IRunningDetector
    {
        public bool IsRunning() => detectors.Any(d => d.IsRunning());

        public void Dispose()
        {
            foreach (var d in detectors)
                d.Dispose();
        }

        public override string ToString() => string.Join(" or ", detectors.Select(d => d.ToString()));
    }
}
