using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace naLauncher2.Core.Tracking
{
    public enum LaunchKind
    {
        /// <summary>An executable naLauncher can start itself (inside a Job Object).</summary>
        Executable,
        /// <summary>A .cmd/.bat script, started through cmd.exe (inside a Job Object).</summary>
        Script,
        /// <summary>A <c>steam://rungameid/</c> URL or a Steam client shortcut with <c>-applaunch</c>.</summary>
        Steam,
        /// <summary>A GOG Galaxy client shortcut (<c>/command=runGame</c>).</summary>
        GogGalaxy,
        /// <summary>Anything else - other URLs, launcher shortcuts, shortcuts that can't be resolved. Shell-executed as-is.</summary>
        Shell,
    }

    /// <summary>
    /// What a game shortcut actually launches, resolved from the <c>.lnk</c>/<c>.url</c>/<c>.cmd</c> file.
    /// </summary>
    public record LaunchInfo(
        string Shortcut,
        LaunchKind Kind,
        string? Target = null,
        string? Arguments = null,
        string? WorkingDirectory = null,
        bool RunAsAdmin = false,
        string? SteamAppId = null,
        string? GogId = null,
        string? GogPath = null,
        string? EpicAppName = null);

    public static partial class ShortcutResolver
    {
        public static LaunchInfo Resolve(string shortcut)
        {
            var extension = Path.GetExtension(shortcut).ToLowerInvariant();

            try
            {
                return extension switch
                {
                    ".lnk" => ResolveLink(shortcut),
                    ".url" => ResolveUrl(shortcut, ReadUrl(shortcut)),
                    ".exe" => new LaunchInfo(shortcut, LaunchKind.Executable, shortcut, null, Path.GetDirectoryName(shortcut)),
                    ".cmd" or ".bat" => new LaunchInfo(shortcut, LaunchKind.Script, shortcut, null, Path.GetDirectoryName(shortcut)),
                    _ => new LaunchInfo(shortcut, LaunchKind.Shell),
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{nameof(ShortcutResolver)}: failed to resolve '{shortcut}': {ex.Message}");
                return new LaunchInfo(shortcut, LaunchKind.Shell);
            }
        }

        static string? ReadUrl(string urlFile) => File.ReadLines(urlFile)
            .Where(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[4..].Trim())
            .FirstOrDefault();

        static LaunchInfo ResolveUrl(string shortcut, string? url)
        {
            if (url is null)
                return new LaunchInfo(shortcut, LaunchKind.Shell);

            var steam = SteamUrlRegex().Match(url);
            if (steam.Success)
                return new LaunchInfo(shortcut, LaunchKind.Steam, SteamAppId: steam.Groups[1].Value);

            // com.epicgames.launcher://apps/<namespace>%3A<catalogItemId>%3A<appName>?action=launch
            if (url.StartsWith("com.epicgames.launcher://apps/", StringComparison.OrdinalIgnoreCase))
            {
                var app = Uri.UnescapeDataString(url["com.epicgames.launcher://apps/".Length..].Split('?')[0]);
                return new LaunchInfo(shortcut, LaunchKind.Shell, EpicAppName: app.Split(':').Last());
            }

            return new LaunchInfo(shortcut, LaunchKind.Shell);
        }

        static LaunchInfo ResolveLink(string shortcut)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(shortcut, 0);

                var buffer = new StringBuilder(1024);
                link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
                var target = Environment.ExpandEnvironmentVariables(buffer.ToString());

                buffer.Clear();
                link.GetArguments(buffer, buffer.Capacity);
                var arguments = buffer.ToString();

                buffer.Clear();
                link.GetWorkingDirectory(buffer, buffer.Capacity);
                var workingDirectory = Environment.ExpandEnvironmentVariables(buffer.ToString());

                ((IShellLinkDataList)link).GetFlags(out uint flags);
                bool runAsAdmin = (flags & SLDF_RUNAS_USER) != 0;

                if (string.IsNullOrEmpty(target))
                    return new LaunchInfo(shortcut, LaunchKind.Shell);

                if (string.IsNullOrEmpty(workingDirectory))
                    workingDirectory = Path.GetDirectoryName(target);

                var targetName = Path.GetFileName(target);

                if (targetName.Equals("GalaxyClient.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var gogId = GogIdArgRegex().Match(arguments);
                    var gogPath = GogPathArgRegex().Match(arguments);
                    return new LaunchInfo(shortcut, LaunchKind.GogGalaxy, target, arguments, workingDirectory, runAsAdmin,
                        GogId: gogId.Success ? gogId.Groups[1].Value : null,
                        GogPath: gogPath.Success ? gogPath.Groups[1].Value.TrimEnd('\\') : null);
                }

                if (targetName.Equals("steam.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var appId = SteamAppLaunchArgRegex().Match(arguments);
                    if (!appId.Success)
                        appId = SteamUrlRegex().Match(arguments);

                    return appId.Success
                        ? new LaunchInfo(shortcut, LaunchKind.Steam, target, arguments, workingDirectory, runAsAdmin, SteamAppId: appId.Groups[1].Value)
                        : new LaunchInfo(shortcut, LaunchKind.Shell);
                }

                var kind = Path.GetExtension(target).ToLowerInvariant() switch
                {
                    ".exe" => LaunchKind.Executable,
                    ".cmd" or ".bat" => LaunchKind.Script,
                    _ => LaunchKind.Shell,
                };

                return new LaunchInfo(shortcut, kind, target, arguments, workingDirectory, runAsAdmin);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        [GeneratedRegex(@"steam://(?:rungameid|run)/(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex SteamUrlRegex();

        [GeneratedRegex(@"-applaunch\s+(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex SteamAppLaunchArgRegex();

        [GeneratedRegex(@"/gameId=(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex GogIdArgRegex();

        [GeneratedRegex(@"/path=""([^""]+)""", RegexOptions.IgnoreCase)]
        private static partial Regex GogPathArgRegex();

        const uint SLDF_RUNAS_USER = 0x2000;

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1")]
        interface IShellLinkDataList
        {
            void AddDataBlock(IntPtr pDataBlock);
            void CopyDataBlock(uint dwSig, out IntPtr ppDataBlock);
            void RemoveDataBlock(uint dwSig);
            void GetFlags(out uint pdwFlags);
            void SetFlags(uint dwFlags);
        }
    }
}
