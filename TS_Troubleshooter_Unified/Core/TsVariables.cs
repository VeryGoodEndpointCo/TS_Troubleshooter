namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// The task sequence variable names this app reads and writes, in one place so the
    /// contract with the task sequence is documented rather than scattered through the UI.
    /// </summary>
    internal static class TsVariables
    {
        // ---- Read: MECM built-ins -------------------------------------------------

        /// <summary>Present in any task sequence. Used to detect that we are running inside one.</summary>
        public const string PackageName = "_SMSTSPackageName";

        public const string InWinPE = "_SMSTSInWinPE";
        public const string LogPath = "_SMSTSLogPath";
        public const string AdvertId = "_SMSTSAdvertID";
        public const string MachineName = "_SMSTSMachineName";

        /// <summary>Set automatically by MECM when a step fails - no custom error handler required.</summary>
        public const string LastActionName = "_SMSTSLastActionName";

        /// <summary>Set automatically by MECM when a step fails. Signed decimal, e.g. -2147024894.</summary>
        public const string LastActionRetCode = "_SMSTSLastActionRetCode";

        public const string CurrentActionName = "_SMSTSCurrentActionName";

        // ---- Read: the app's own optional overrides -------------------------------
        // These keep compatibility with the original build, where the user was expected to
        // populate them from a custom "on error" step. If they are empty we fall back to
        // the MECM built-ins above.

        public const string FailStep = "failstep";
        public const string FailCode = "failcode";

        /// <summary>Optional banner override for when no config file is in play.</summary>
        public const string Message = "TSTS_Message";

        // ---- Written --------------------------------------------------------------

        public const string PeReboot = "PE_Reboot";
        public const string PeShutdown = "PE_Shutdown";
        public const string OsReboot = "OS_Reboot";
        public const string OsShutdown = "OS_Shutdown";

        /// <summary>Restart | Shutdown | Close - what the user actually picked.</summary>
        public const string Action = "TSTS_Action";
    }
}
