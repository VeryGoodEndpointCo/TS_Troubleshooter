using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using TS_Troubleshooter_Unified.Core;

namespace TS_Troubleshooter_Unified
{
    public partial class App : Application
    {
        private bool _shuttingDown;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Every exit is explicit. The original relied on the default OnLastWindowClose and
            // had a path - two or more command line arguments - that showed an error dialog and
            // then simply fell out of Startup, leaving the splash screen up and the process
            // alive forever. Inside a task sequence that hangs the whole build.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Nothing below this line is allowed to reach the default WPF crash dialog, which
            // in WinPE is an unreadable stack trace on a machine with no way to dismiss it.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            Log.Start();

            try
            {
                Run(e.Args);
            }
            catch (ConfigException ex)
            {
                Log.Write("Config error: {0}", ex.Message);
                ShowError("Configuration problem", ex.Message);
                ShutdownWith(ExitCode.ConfigError);
            }
            catch (Exception ex)
            {
                Fail("startup", ex);
            }
        }

        private void Run(string[] rawArgs)
        {
            var arguments = Arguments.Parse(rawArgs);
            Log.Write("Command line: {0}", arguments);

            if (arguments.ShowHelp)
            {
                ShowUsage();
                ShutdownWith(ExitCode.Success);
                return;
            }

            if (arguments.Error != null)
            {
                Log.Write("Invalid command line: {0}", arguments.Error);
                ShowError("Invalid command line", arguments.Error + Environment.NewLine +
                    Environment.NewLine + Arguments.UsageText);
                ShutdownWith(ExitCode.BadArguments);
                return;
            }

            // Once the task sequence environment is reachable, mirror the log next to smsts.log
            // so it gets picked up by whatever already collects that folder.
            if (TsEnvironment.IsTaskSequence)
            {
                string logPath = TsEnvironment.Get(TsVariables.LogPath);
                if (!string.IsNullOrWhiteSpace(logPath)) Log.AddTarget(logPath);
            }

            if (SessionLauncher.TryHandOffToInteractiveSession(arguments.Passthrough, arguments.Relaunched))
            {
                Log.Write("Handed off to the interactive session; this process is done.");
                ShutdownWith(ExitCode.Success);
                return;
            }

            AppOptions options = arguments.ForceLite
                ? AppOptions.Lite()
                : ConfigLoader.Load(arguments.ConfigSource, arguments.ConfigSource != null);

            new MainWindow(options).ShowDialog();

            // The window has no failure path of its own - every button closes it normally, and
            // anything that throws is picked up by the handlers above.
            ShutdownWith(ExitCode.Success);
        }

        // ---- Failure handling -----------------------------------------------------

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Fail("UI thread", e.Exception);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Fail("background thread", e.ExceptionObject as Exception ?? new Exception("Non-CLR exception."));
        }

        private void Fail(string where, Exception ex)
        {
            if (_shuttingDown) return;

            Log.Exception(where, ex);

            ShowError("TS Troubleshooter could not continue",
                "An unexpected error occurred on the " + where + "." + Environment.NewLine +
                Environment.NewLine + ex.Message + Environment.NewLine +
                Environment.NewLine + "Details have been written to TS_Troubleshooter.log.");

            ShutdownWith(ExitCode.UnhandledException);
        }

        private static void ShowError(string title, string message)
        {
            // The progress dialog is Topmost, so it would sit over this message otherwise.
            TsEnvironment.CloseProgressUi();

            MessageBox.Show(message, "TS Troubleshooter — " + title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void ShowUsage()
        {
            MessageBox.Show(Arguments.UsageText, "TS Troubleshooter — usage",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShutdownWith(int exitCode)
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            Log.Write("Exiting with code {0}.", exitCode);
            Shutdown(exitCode);
        }
    }

    /// <summary>Command line parsing. Kept deliberately small and forgiving about slashes and dashes.</summary>
    internal sealed class Arguments
    {
        public const string UsageText =
            "TS_Troubleshooter.exe [config] [-lite]\n" +
            "\n" +
            "  config    Path, UNC path or URL of the options XML file.\n" +
            "            Defaults to config.xml beside the executable.\n" +
            "            If no config file is found the app runs in lite mode,\n" +
            "            showing the failure details with no options to select.\n" +
            "\n" +
            "  -lite     Skip the config file entirely and run in lite mode.\n" +
            "\n" +
            "Examples:\n" +
            "  TS_Troubleshooter.exe\n" +
            "  TS_Troubleshooter.exe -lite\n" +
            "  TS_Troubleshooter.exe C:\\Temp\\this.xml\n" +
            "  TS_Troubleshooter.exe \\\\fileshare\\folder1\\bing.xml\n" +
            "  TS_Troubleshooter.exe http://coolwebsite/config.xml";

        private Arguments()
        {
            Passthrough = new List<string>();
        }

        public string ConfigSource { get; private set; }
        public bool ForceLite { get; private set; }
        public bool ShowHelp { get; private set; }
        public bool Relaunched { get; private set; }
        public string Error { get; private set; }

        /// <summary>
        /// The arguments to hand to a child process during a session handoff. The original
        /// dropped these, so a config path passed on the command line was lost the moment the
        /// relaunch happened and the app fell back to looking for a local config.xml.
        /// </summary>
        public IList<string> Passthrough { get; private set; }

        public static Arguments Parse(IEnumerable<string> args)
        {
            var result = new Arguments();
            var positional = new List<string>();

            foreach (string raw in args ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string arg = raw.Trim();
                string flag = arg.TrimStart('-', '/').ToLowerInvariant();

                bool looksLikeFlag = arg.StartsWith("-", StringComparison.Ordinal) ||
                                     (arg.StartsWith("/", StringComparison.Ordinal) && arg.Length <= 8);

                if (looksLikeFlag && (flag == "?" || flag == "h" || flag == "help"))
                {
                    result.ShowHelp = true;
                    continue;
                }

                if (looksLikeFlag && flag == "lite")
                {
                    result.ForceLite = true;
                    result.Passthrough.Add(arg);
                    continue;
                }

                if (arg.Equals(SessionLauncher.RelaunchedSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    result.Relaunched = true;
                    continue;
                }

                positional.Add(arg);
            }

            if (positional.Count > 1)
            {
                result.Error = "Expected at most one config file, but got " + positional.Count + ".";
                return result;
            }

            if (positional.Count == 1)
            {
                result.ConfigSource = positional[0];
                result.Passthrough.Insert(0, positional[0]);
            }

            return result;
        }

        public override string ToString()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "config={0}, lite={1}, relaunched={2}",
                ConfigSource ?? "(default)", ForceLite, Relaunched);
        }
    }
}
