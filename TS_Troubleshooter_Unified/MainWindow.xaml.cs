using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TS_Troubleshooter_Unified.Core;

namespace TS_Troubleshooter_Unified
{
    public partial class MainWindow : Window
    {
        private readonly AppOptions _options;

        private bool _actionTaken;

        internal MainWindow(AppOptions options)
        {
            _options = options ?? new AppOptions();

            // Do this before the window is shown, otherwise the progress dialog can end up
            // sitting on top of a Topmost window.
            TsEnvironment.CloseProgressUi();

            InitializeComponent();

            PopulateHeader();
            PopulateDetails();
            PopulateOptions();
            ConfigureForMode();

            Loaded += OnLoaded;
        }

        // ---- Setup ----------------------------------------------------------------

        private void PopulateHeader()
        {
            string version = "v" + AppInfo.Version;
            headerVersion.Text = version;

            // Same text on the left, hidden, so the centred title is actually centred rather
            // than pushed off by the width of the version string.
            headerSpacer.Text = version;
            headerSpacer.Visibility = Visibility.Hidden;
        }

        private void PopulateDetails()
        {
            host.Text = MachineInfo.HostName;
            ip.Text = MachineInfo.IPv4Display();
            banner.Text = _options.Message;

            if (TsEnvironment.IsTaskSequence)
            {
                failStep.Text = TsEnvironment.FailingStep() ?? "(not reported)";
                failCode.Text = TsEnvironment.FormatReturnCode(TsEnvironment.FailingCodeRaw())
                                ?? "(not reported)";
            }
            else
            {
                failStep.Text = "Not in a task sequence";
                failCode.Text = "Not in a task sequence";
            }

            LogEnvironment();
        }

        /// <summary>
        /// Everything a service desk would want off this screen, written to the log.
        ///
        /// There is deliberately no "copy to clipboard" button: inside a task sequence there is
        /// nowhere to paste to, so a clipboard is useless there. The log is the copy that can
        /// actually be retrieved, since in a task sequence it is written to _SMSTSLogPath and
        /// collected alongside smsts.log.
        /// </summary>
        private void LogEnvironment()
        {
            Log.Write("Host {0}, IP {1}.", host.Text, string.Join(", ", MachineInfo.IPv4Addresses()));
            Log.Write("Failing step \"{0}\", code \"{1}\".", failStep.Text, failCode.Text);

            if (!TsEnvironment.IsTaskSequence) return;

            Log.Write("Task sequence \"{0}\", deployment {1}, target name {2}, phase {3}.",
                TsEnvironment.Get(TsVariables.PackageName) ?? "(unknown)",
                TsEnvironment.Get(TsVariables.AdvertId) ?? "(unknown)",
                TsEnvironment.Get(TsVariables.MachineName) ?? "(unknown)",
                TsEnvironment.IsWinPE ? "WinPE" : "Full OS");
        }

        private void PopulateOptions()
        {
            foreach (TaskOption task in _options.Tasks)
            {
                optionList.Children.Add(new CheckBox
                {
                    Style = (Style)FindResource("OptionCheckBoxStyle"),
                    Content = task.Label,
                    Tag = task,
                    TabIndex = 20 + optionList.Children.Count
                });
            }
        }

        /// <summary>
        /// Lite mode is this window with the options block collapsed and Close as the only
        /// button. It is informational: it reports what failed and gets out of the way. Powering
        /// the machine off or restarting it is an action, and actions belong with the config file
        /// that describes them, so those two buttons only appear when there is a config.
        ///
        /// The header, the fields and the banner are shared between the modes, which is what
        /// lets the two original builds be one project.
        /// </summary>
        private void ConfigureForMode()
        {
            if (_options.IsLite)
            {
                optionsSection.Visibility = Visibility.Collapsed;
                shutdownButton.Visibility = Visibility.Collapsed;
                restartButton.Visibility = Visibility.Collapsed;

                // Restart carried IsDefault, so Close has to take it over or Enter does nothing.
                closeButton.IsDefault = true;

                // Without the other two beside it, Close should sit flush with the right margin.
                closeButton.Margin = new Thickness(0);
            }
            else
            {
                shutdownButton.Content = "Run Actions and Shut _Down";
                restartButton.Content = "Run Actions and _Restart";
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // A mouse is not guaranteed in WinPE - a missing USB or NIC driver is exactly the
            // sort of failure that lands people on this screen - so make sure the keyboard has
            // somewhere sensible to start.
            Activate();

            // In lite mode Restart is collapsed and cannot take focus, so Close is the target.
            CheckBox first = optionList.Children.OfType<CheckBox>().FirstOrDefault();

            if (first != null)
            {
                first.Focus();
            }
            else
            {
                closeButton.Focus();
            }
        }

        private static string DescribeAction(UserAction action)
        {
            switch (action)
            {
                case UserAction.Restart: return "restart";
                case UserAction.Shutdown: return "shut down";
                default: return "continue";
            }
        }

        // ---- Buttons --------------------------------------------------------------

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Apply(UserAction.Close);
        }

        private void ShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            Apply(UserAction.Shutdown);
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            Apply(UserAction.Restart);
        }

        // ---- Applying the result --------------------------------------------------

        /// <summary>
        /// Only ever reached from a button click. Nothing dismisses this window on a timer -
        /// a build that fails on a Friday night has to still be showing its error on Monday.
        /// </summary>
        private void Apply(UserAction action)
        {
            if (_actionTaken) return;
            _actionTaken = true;

            List<TaskOption> selected = SelectedTasks();
            Log.Write("Action: {0}. Selected: {1}",
                action,
                selected.Count == 0 ? "(none)" : string.Join(", ", selected.Select(t => t.Variable)));

            if (TsEnvironment.IsTaskSequence)
            {
                ApplyToTaskSequence(action, selected);
            }
            else
            {
                ShowTestSummary(action, selected);
            }

            Close();
        }

        private void ApplyToTaskSequence(UserAction action, ICollection<TaskOption> selected)
        {
            // Unselected options are explicitly set to False rather than just left alone. A
            // task sequence variable survives for the life of the sequence, so on a second pass
            // through this screen a previously ticked option would otherwise still be True and
            // its step would run again without being asked for.
            foreach (TaskOption task in _options.Tasks)
            {
                bool chosen = selected.Contains(task);
                TsEnvironment.Set(task.Variable, chosen ? "True" : "False");
            }

            TsEnvironment.Set(TsVariables.Action, action.ToString());

            bool winPE = TsEnvironment.IsWinPE;

            switch (action)
            {
                case UserAction.Restart:
                    TsEnvironment.Set(winPE ? TsVariables.PeReboot : TsVariables.OsReboot, "True");
                    break;

                case UserAction.Shutdown:
                    TsEnvironment.Set(winPE ? TsVariables.PeShutdown : TsVariables.OsShutdown, "True");
                    break;

                case UserAction.Close:
                    // Nothing to set - the sequence just carries on to whatever comes next.
                    break;
            }
        }

        /// <summary>
        /// Outside a task sequence, show what would have been written. The README documents
        /// this as the way to test a config file, so it is worth keeping - but as one dialog
        /// listing everything rather than the original's two separate pop-ups.
        /// </summary>
        private void ShowTestSummary(UserAction action, ICollection<TaskOption> selected)
        {
            var text = new StringBuilder();
            text.AppendLine("Not running in a task sequence, so nothing was changed.");
            text.AppendLine();
            text.AppendLine("These task sequence variables would have been set:");
            text.AppendLine();

            foreach (TaskOption task in _options.Tasks)
            {
                text.AppendFormat(CultureInfo.CurrentCulture, "    {0} = {1}{2}",
                    task.Variable, selected.Contains(task) ? "True" : "False", Environment.NewLine);
            }

            text.AppendFormat(CultureInfo.CurrentCulture, "    {0} = {1}{2}",
                TsVariables.Action, action, Environment.NewLine);

            switch (action)
            {
                case UserAction.Restart:
                    text.AppendFormat(CultureInfo.CurrentCulture, "    {0} / {1} = True{2}",
                        TsVariables.PeReboot, TsVariables.OsReboot, Environment.NewLine);
                    break;
                case UserAction.Shutdown:
                    text.AppendFormat(CultureInfo.CurrentCulture, "    {0} / {1} = True{2}",
                        TsVariables.PeShutdown, TsVariables.OsShutdown, Environment.NewLine);
                    break;
            }

            text.AppendLine();
            text.AppendFormat(CultureInfo.CurrentCulture, "The PC would then {0}.", DescribeAction(action));

            // Topmost has to come off first or the dialog opens behind this window.
            Topmost = false;
            MessageBox.Show(this, text.ToString(), "TS Troubleshooter — test run",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private List<TaskOption> SelectedTasks()
        {
            // OfType rather than a cast: the original iterated the panel's children as CheckBox
            // directly, which would throw the moment anything else was added to that panel.
            return optionList.Children
                .OfType<CheckBox>()
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag)
                .OfType<TaskOption>()
                .ToList();
        }
    }
}
