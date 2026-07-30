using System;
using System.Windows;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace TS_Troubleshooter_Lite
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hHandle);
        TS_Integration TS = new TS_Integration();

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            string targetProcess = DetermineTargetProcess(e);
            if (!IsInCorrectSession(targetProcess))
            {
                RestartInCorrectSession(targetProcess);
                Shutdown();
                return;
            }

            MainWindow mainWindow = new MainWindow();
            mainWindow.ShowDialog();

        }


        private string DetermineTargetProcess(StartupEventArgs e)
        {
            // Default to Explorer
            string targetProcess = "explorer";

            // No arguments, in TS environment
            if (TS.IsTSEnv())
            {
                targetProcess = "TSProgressUI";
            }
            // Single argument is -testing, not in TS environment
            else
            {
                targetProcess = "explorer";
            }

            return targetProcess;
        }

        private bool IsInCorrectSession(string targetProcessName)
        {
            uint targetSessionId = GetSessionIdForProcess(targetProcessName);
            uint currentSessionId;
            ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, out currentSessionId);
            return targetSessionId == currentSessionId;
        }

        private uint GetSessionIdForProcess(string processName)
        {
            Process[] processes = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(processName));
            if (processes.Length > 0)
            {
                uint sessionId;
                if (ProcessIdToSessionId((uint)processes[0].Id, out sessionId))
                {
                    return sessionId;
                }
            }
            return uint.MaxValue;
        }

        private void RestartInCorrectSession(string targetProcessName)
        {
            string executablePath = Process.GetCurrentProcess().MainModule.FileName;
            ProcessLauncher.LaunchAppInSameSessionAs(targetProcessName + ".exe", executablePath);
        }

        public static class ProcessLauncher
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct STARTUPINFO
            {
                public int cb;
                public string lpReserved;
                public string lpDesktop;
                public string lpTitle;
                public uint dwX;
                public uint dwY;
                public uint dwXSize;
                public uint dwYSize;
                public uint dwXCountChars;
                public uint dwYCountChars;
                public uint dwFillAttribute;
                public uint dwFlags;
                public short wShowWindow;
                public short cbReserved2;
                public IntPtr lpReserved2;
                public IntPtr hStdInput;
                public IntPtr hStdOutput;
                public IntPtr hStdError;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_INFORMATION
            {
                public IntPtr hProcess;
                public IntPtr hThread;
                public uint dwProcessId;
                public uint dwThreadId;
            }

            [DllImport("kernel32.dll")]
            public static extern bool CreateProcess(
                string lpApplicationName,
                string lpCommandLine,
                IntPtr lpProcessAttributes,
                IntPtr lpThreadAttributes,
                bool bInheritHandles,
                uint dwCreationFlags,
                IntPtr lpEnvironment,
                string lpCurrentDirectory,
                ref STARTUPINFO lpStartupInfo,
                out PROCESS_INFORMATION lpProcessInformation);

            [DllImport("kernel32.dll")]
            private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

            [DllImport("kernel32.dll")]
            private static extern bool CloseHandle(IntPtr hObject);

            public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
            public const uint CREATE_NEW_CONSOLE = 0x00000010;

            public static void LaunchAppInSameSessionAs(string targetProcessName, string appToLaunch)
            {
                uint targetSessionId = GetSessionIdForProcess(targetProcessName);

                if (targetSessionId == uint.MaxValue)
                {
                    throw new Exception($"Could not find process: {targetProcessName}");
                }

                STARTUPINFO si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(si);

                PROCESS_INFORMATION pi;

                bool success = CreateProcess(
                    null,
                    appToLaunch,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    NORMAL_PRIORITY_CLASS | CREATE_NEW_CONSOLE,
                    IntPtr.Zero,
                    null,
                    ref si,
                    out pi
                );

                if (success)
                {
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"Failed to start process. Error code: {error}");
                }
            }

            private static uint GetSessionIdForProcess(string processName)
            {
                processName = System.IO.Path.GetFileNameWithoutExtension(processName);
                Process[] processes = Process.GetProcessesByName(processName);

                if (processes.Length > 0)
                {
                    uint sessionId;
                    if (ProcessIdToSessionId((uint)processes[0].Id, out sessionId))
                    {
                        return sessionId;
                    }
                }

                return uint.MaxValue;
            }
        }
    }
}
