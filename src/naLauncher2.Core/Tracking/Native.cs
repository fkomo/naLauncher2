using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace naLauncher2.Core.Tracking
{
    /// <summary>
    /// Win32 interop for starting processes inside a Job Object and inspecting other processes.
    /// </summary>
    internal static class Native
    {
        public const int ERROR_ELEVATION_REQUIRED = 740;

        const uint CREATE_SUSPENDED = 0x00000004;
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const int JobObjectBasicAccountingInformation = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
        {
            public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcess(string? lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryInformationJobObject(IntPtr hJob, int infoClass,
            out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info, int length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        /// <summary>
        /// Starts a process suspended, puts it into a new Job Object (so every process it spawns
        /// lands in the job too), then lets it run. Returns the job handle; the caller owns it.
        /// The job is not kill-on-close, so the game survives naLauncher exiting.
        /// </summary>
        /// <exception cref="Win32Exception">Process creation failed, e.g. <see cref="ERROR_ELEVATION_REQUIRED"/>.</exception>
        public static IntPtr StartInJob(string commandLine, string? workingDirectory)
        {
            var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

            if (!CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, workingDirectory, ref startupInfo, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero || !AssignProcessToJobObject(job, pi.hProcess))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (job != IntPtr.Zero)
                        CloseHandle(job);

                    // never leave a suspended process behind; the caller falls back to a plain launch
                    TerminateProcess(pi.hProcess, 1);
                    throw new Win32Exception(error);
                }

                ResumeThread(pi.hThread);
                return job;
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
        }

        /// <summary>
        /// Number of processes currently alive in the job.
        /// </summary>
        public static int ActiveJobProcesses(IntPtr job)
        {
            return QueryInformationJobObject(job, JobObjectBasicAccountingInformation, out var info,
                Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(), IntPtr.Zero)
                ? (int)info.ActiveProcesses
                : 0;
        }

        /// <summary>
        /// Full path of a process' executable, or null when the process can't be opened
        /// (protected processes, or it already exited).
        /// </summary>
        public static string? GetProcessImagePath(int processId)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
                return null;

            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
