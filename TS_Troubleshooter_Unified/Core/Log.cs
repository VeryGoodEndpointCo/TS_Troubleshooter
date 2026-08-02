using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// Append-only text log. Nothing here is allowed to throw: a logging failure must never
    /// be the reason the failure screen fails to appear.
    /// </summary>
    internal static class Log
    {
        private const string FileName = "TS_Troubleshooter.log";

        private static readonly object Gate = new object();
        private static readonly List<string> Targets = new List<string>();
        private static bool _initialised;

        /// <summary>
        /// Starts logging to %TEMP%. Safe to call more than once.
        /// </summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (_initialised) return;
                _initialised = true;

                AddTargetCore(Path.GetTempPath());
            }

            Write("---- TS Troubleshooter {0} starting (pid {1}, session {2}) ----",
                AppInfo.Version,
                Process.GetCurrentProcess().Id,
                CurrentSessionIdOrUnknown());
        }

        /// <summary>
        /// Adds a second destination, used for _SMSTSLogPath so the log is picked up by the
        /// same collection that gathers smsts.log.
        /// </summary>
        public static void AddTarget(string directory)
        {
            lock (Gate)
            {
                AddTargetCore(directory);
            }
        }

        private static void AddTargetCore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;

            try
            {
                string full = Path.GetFullPath(directory);
                if (!Directory.Exists(full)) return;

                string path = Path.Combine(full, FileName);
                if (!Targets.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    Targets.Add(path);
                }
            }
            catch
            {
                // An unreachable or malformed path is not worth reporting to the user.
            }
        }

        public static void Write(string format, params object[] args)
        {
            string message;
            try
            {
                message = args == null || args.Length == 0
                    ? format
                    : string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                message = format;
            }

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff}  {1}{2}",
                DateTime.Now,
                message,
                Environment.NewLine);

            Debug.Write(line);

            lock (Gate)
            {
                foreach (string target in Targets)
                {
                    try
                    {
                        File.AppendAllText(target, line, Encoding.UTF8);
                    }
                    catch
                    {
                        // Read-only media, a locked file, an X: drive that went away - all
                        // survivable. Keep going so the other target still gets the line.
                    }
                }
            }
        }

        public static void Exception(string context, Exception ex)
        {
            Write("EXCEPTION in {0}: {1}", context, ex);
        }

        private static string CurrentSessionIdOrUnknown()
        {
            try
            {
                return Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "?";
            }
        }
    }
}
