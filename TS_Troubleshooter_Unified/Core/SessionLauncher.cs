using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// Relaunches this app in the interactive session when the task sequence engine started
    /// it in session 0, where a window would never be visible.
    ///
    /// The original implementation had a few problems worth spelling out, because the fix
    /// here looks more involved than the code it replaces:
    ///
    ///  * It used CreateProcess, which always creates the child in the *caller's* session.
    ///    In a full-OS task sequence the engine runs as SYSTEM in session 0 while TSProgressUI
    ///    runs in session 1, so the child landed back in session 0, decided it was in the wrong
    ///    session, and spawned another child - an endless respawn loop with no UI ever shown.
    ///    Actually crossing a session boundary needs WTSQueryUserToken plus CreateProcessAsUser.
    ///  * The DllImport had no SetLastError, so the error code reported on failure was junk.
    ///  * lpCommandLine was declared as string. CreateProcess is documented as being able to
    ///    write to that buffer, so it has to be a mutable StringBuilder.
    ///  * The exe path was not quoted, so any install path containing a space was mis-parsed.
    ///  * The original command line arguments were dropped, which silently defeated the
    ///    documented "pass a config URL" feature every time the relaunch happened.
    ///
    /// If anything in the handoff fails we log it and carry on in the current session. A
    /// window in the wrong session is a bad outcome; a crash or a respawn loop is a worse one.
    /// </summary>
    internal static class SessionLauncher
    {
        /// <summary>
        /// Passed to the child so a bug in the session detection can never cost more than one
        /// extra process.
        /// </summary>
        public const string RelaunchedSwitch = "--relaunched";

        /// <summary>
        /// Returns true when the caller should exit immediately because a child has been
        /// started in the interactive session.
        /// </summary>
        public static bool TryHandOffToInteractiveSession(IList<string> originalArgs, bool alreadyRelaunched)
        {
            // Outside a task sequence we were started by a user who already has a desktop,
            // so there is nothing to hand off to. The original code ran this logic in every
            // case and targeted explorer.exe, which does not exist in WinPE - so a bare WinPE
            // test run threw "Could not find process: explorer.exe" as an unhandled exception.
            if (!TsEnvironment.IsTaskSequence)
            {
                return false;
            }

            if (alreadyRelaunched)
            {
                Log.Write("Already relaunched once; staying in this session.");
                return false;
            }

            uint currentSession;
            if (!ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, out currentSession))
            {
                Log.Write("ProcessIdToSessionId failed for the current process; staying put.");
                return false;
            }

            uint targetSession;
            if (!TryGetInteractiveSession(out targetSession))
            {
                Log.Write("No interactive session found; staying in session {0}.", currentSession);
                return false;
            }

            if (targetSession == currentSession)
            {
                Log.Write("Already in the interactive session ({0}).", currentSession);
                return false;
            }

            Log.Write("Running in session {0}, interactive session is {1}. Handing off.",
                currentSession, targetSession);

            var args = new List<string>(originalArgs ?? new string[0]) { RelaunchedSwitch };

            if (LaunchInSession(targetSession, args))
            {
                return true;
            }

            Log.Write("Handoff failed; continuing in session {0} instead.", currentSession);
            return false;
        }

        /// <summary>
        /// The session hosting the task sequence UI. TSProgressUI is the reliable marker while
        /// a task sequence is running; the active console session is the fallback.
        /// </summary>
        private static bool TryGetInteractiveSession(out uint sessionId)
        {
            sessionId = uint.MaxValue;

            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName("TSProgressUI");
            }
            catch (Exception ex)
            {
                Log.Exception("SessionLauncher.TryGetInteractiveSession", ex);
                processes = new Process[0];
            }

            try
            {
                foreach (Process proc in processes)
                {
                    uint id;
                    if (ProcessIdToSessionId((uint)proc.Id, out id))
                    {
                        sessionId = id;
                        return true;
                    }
                }
            }
            finally
            {
                foreach (Process proc in processes) proc.Dispose();
            }

            uint console = WTSGetActiveConsoleSessionId();
            if (console != 0xFFFFFFFF)
            {
                sessionId = console;
                return true;
            }

            return false;
        }

        private static bool LaunchInSession(uint sessionId, IList<string> args)
        {
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr environment = IntPtr.Zero;
            var processInfo = new PROCESS_INFORMATION();

            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                {
                    Log.Write("WTSQueryUserToken failed for session {0}: {1}",
                        sessionId, new System.ComponentModel.Win32Exception().Message);
                    return false;
                }

                if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                        SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                        TOKEN_TYPE.TokenPrimary, out primaryToken))
                {
                    Log.Write("DuplicateTokenEx failed: {0}", new System.ComponentModel.Win32Exception().Message);
                    return false;
                }

                // Not fatal if it fails - the child just inherits a bare environment.
                if (!CreateEnvironmentBlock(out environment, primaryToken, false))
                {
                    Log.Write("CreateEnvironmentBlock failed: {0}", new System.ComponentModel.Win32Exception().Message);
                    environment = IntPtr.Zero;
                }

                string exePath = CurrentExecutablePath();
                if (exePath == null) return false;

                // Mutable buffer: CreateProcessAsUser may write to lpCommandLine.
                var commandLine = new StringBuilder(BuildCommandLine(exePath, args));

                var startupInfo = new STARTUPINFO();
                startupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                startupInfo.lpDesktop = @"winsta0\default";

                uint flags = NORMAL_PRIORITY_CLASS;
                if (environment != IntPtr.Zero) flags |= CREATE_UNICODE_ENVIRONMENT;

                bool ok = CreateProcessAsUser(
                    primaryToken,
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    flags,
                    environment,
                    null,
                    ref startupInfo,
                    out processInfo);

                if (!ok)
                {
                    Log.Write("CreateProcessAsUser failed: {0}", new System.ComponentModel.Win32Exception().Message);
                    return false;
                }

                Log.Write("Started pid {0} in session {1}: {2}",
                    processInfo.dwProcessId, sessionId, commandLine);
                return true;
            }
            catch (Exception ex)
            {
                // DllNotFoundException lands here on a boot image without userenv/wtsapi32.
                Log.Exception("SessionLauncher.LaunchInSession", ex);
                return false;
            }
            finally
            {
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        private static string CurrentExecutablePath()
        {
            try
            {
                using (Process current = Process.GetCurrentProcess())
                {
                    return current.MainModule.FileName;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("SessionLauncher.CurrentExecutablePath", ex);
                return null;
            }
        }

        /// <summary>Builds a command line that CommandLineToArgvW will parse back correctly.</summary>
        private static string BuildCommandLine(string exePath, IEnumerable<string> args)
        {
            var builder = new StringBuilder();
            builder.Append(Quote(exePath));

            foreach (string arg in args ?? Enumerable.Empty<string>())
            {
                builder.Append(' ').Append(Quote(arg));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Quotes a single argument per the CommandLineToArgvW rules: backslashes are only
        /// special when they immediately precede a quote.
        /// </summary>
        private static string Quote(string argument)
        {
            if (argument == null) return "\"\"";

            if (argument.Length > 0 &&
                argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return argument;
            }

            var builder = new StringBuilder("\"");

            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Trailing backslashes precede the closing quote, so they must be doubled.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes).Append(argument[i]);
                }
            }

            return builder.Append('"').ToString();
        }

        // ---- P/Invoke ---------------------------------------------------------------

        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const uint NORMAL_PRIORITY_CLASS = 0x00000020;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        private enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
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
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken,
            uint dwDesiredAccess,
            IntPtr lpTokenAttributes,
            SECURITY_IMPERSONATION_LEVEL impersonationLevel,
            TOKEN_TYPE tokenType,
            out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            string lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);
    }
}
