namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// Process exit codes.
    ///
    /// Everything the user can legitimately do exits 0, because a non-zero exit marks the
    /// task sequence step as failed and MECM would stop before reaching whatever reboot or
    /// cleanup steps come next. To branch on what the user chose, read the TSTS_Action task
    /// sequence variable instead (see <see cref="TsVariables.Action"/>).
    /// </summary>
    internal static class ExitCode
    {
        /// <summary>User picked an action, or the parent process handed off to a child in another session.</summary>
        public const int Success = 0;

        /// <summary>The config file could not be found, downloaded, or parsed.</summary>
        public const int ConfigError = 1;

        /// <summary>Something threw that we did not anticipate. Details are in the log.</summary>
        public const int UnhandledException = 2;

        /// <summary>The command line made no sense.</summary>
        public const int BadArguments = 3;
    }
}
