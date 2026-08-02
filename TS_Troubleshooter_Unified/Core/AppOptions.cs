using System;
using System.Collections.Generic;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// What the user chose to do.
    ///
    /// There is deliberately no automatic action and no timeout. This screen exists to hold a
    /// failed build until somebody looks at it - if a task sequence fails at 7pm on a Friday the
    /// error still has to be on the screen on Monday morning, so nothing may dismiss it except a
    /// person pressing a button.
    /// </summary>
    internal enum UserAction
    {
        Close,
        Shutdown,
        Restart
    }

    /// <summary>One checkbox: the label shown, and the task sequence variable it sets.</summary>
    internal sealed class TaskOption
    {
        public TaskOption(string variable, string label)
        {
            Variable = variable;
            Label = label;
        }

        public string Variable { get; private set; }
        public string Label { get; private set; }
    }

    /// <summary>
    /// The parsed config, plus the defaults used when there is no config at all.
    ///
    /// "Lite mode" is simply this object with no tasks in it - which is why the two builds
    /// could be merged. The old Lite app was the full app minus a config file, and nothing else.
    /// </summary>
    internal sealed class AppOptions
    {
        public const string DefaultMessage = "THIS PC HAS FAILED THE BUILD TASK SEQUENCE";

        public AppOptions()
        {
            Tasks = new List<TaskOption>();
            Message = DefaultMessage;
        }

        public IList<TaskOption> Tasks { get; private set; }

        /// <summary>The red banner text.</summary>
        public string Message { get; set; }

        /// <summary>Where the config came from, for the log and the error message. Null in lite mode.</summary>
        public string Source { get; set; }

        /// <summary>True when there are no options to show, i.e. the old Lite layout.</summary>
        public bool IsLite { get { return Tasks.Count == 0; } }

        /// <summary>
        /// The screen shown when no config file is involved. The banner can still be overridden
        /// through a task sequence variable so a lite deployment is not stuck with the default
        /// wording.
        /// </summary>
        public static AppOptions Lite()
        {
            var options = new AppOptions();

            string message = TsEnvironment.Get(TsVariables.Message);
            if (!string.IsNullOrWhiteSpace(message))
            {
                options.Message = message.Trim();
            }

            return options;
        }
    }

    /// <summary>A config problem worth showing the user verbatim.</summary>
    internal sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }
}
