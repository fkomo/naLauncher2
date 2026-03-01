using System.Diagnostics;
using System.IO;

namespace naLauncher2.Wpf.Common
{
    public class Log
    {
        const string LogFileName = "naLauncher2.log";

        public static void WriteLine(string message)
        {
            Debug.WriteLine(message);

            var logPath = AppSettings.Instance.LogPath;
            if (logPath == null)
                return;

            using var writer = File.AppendText(Path.Combine(logPath, LogFileName));

            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
    }
}
