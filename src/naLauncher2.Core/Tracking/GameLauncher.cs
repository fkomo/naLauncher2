using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Starts games. Executables and scripts are started by naLauncher itself inside a Job Object
    /// so the whole process tree can be followed; everything else is shell-executed as before.
    /// A launch never fails because of tracking: any problem with the job path falls back to the shell.
    /// </summary>
    public static class GameLauncher
    {
        /// <summary>
        /// Launches the game. Returns the Job Object handle when the game was started inside one
        /// (the caller owns it), otherwise <see cref="IntPtr.Zero"/>.
        /// </summary>
        /// <exception cref="Exception">The game could not be started at all.</exception>
        public static IntPtr Launch(LaunchInfo info)
        {
            if (info.Kind is LaunchKind.Executable or LaunchKind.Script && !info.RunAsAdmin && info.Target is not null)
            {
                var commandLine = info.Kind == LaunchKind.Executable
                    ? $"\"{info.Target}\" {info.Arguments}".TrimEnd()
                    // /s strips the outer quotes, leaving "target" args for cmd to run
                    : $"\"{Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"}\" /d /s /c \"\"{info.Target}\" {info.Arguments}\"";

                var workingDirectory = Directory.Exists(info.WorkingDirectory) ? info.WorkingDirectory : null;

                try
                {
                    return Native.StartInJob(commandLine, workingDirectory);
                }
                catch (Win32Exception ex)
                {
                    Log.WriteLine($"{nameof(GameLauncher)}: starting '{info.Target}' in a job failed ({ex.NativeErrorCode}: {ex.Message}), falling back to shell execute");
                }
            }

            Process.Start(new ProcessStartInfo(info.Shortcut) { UseShellExecute = true })?.Dispose();
            return IntPtr.Zero;
        }
    }
}
