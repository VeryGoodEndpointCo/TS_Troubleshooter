using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>
    /// Wrapper around the Microsoft.SMS.TSEnvironment COM object.
    ///
    /// The original code created a brand new COM instance on every single get and set, and
    /// IsTSEnv() was called six or more times per run - so a normal run built and tore down
    /// the COM object a dozen times. Here the instance and the "are we in a task sequence"
    /// answer are both resolved once and cached.
    /// </summary>
    internal static class TsEnvironment
    {
        private const string ProgId = "Microsoft.SMS.TSEnvironment";

        private static readonly object Gate = new object();
        private static dynamic _com;
        private static bool _comResolved;
        private static bool? _isTaskSequence;

        private static dynamic Com
        {
            get
            {
                lock (Gate)
                {
                    if (_comResolved) return _com;
                    _comResolved = true;

                    try
                    {
                        Type type = Type.GetTypeFromProgID(ProgId);
                        if (type == null)
                        {
                            Log.Write("TSEnvironment ProgID is not registered - not in a task sequence.");
                            return null;
                        }

                        _com = Activator.CreateInstance(type);
                    }
                    catch (COMException ex)
                    {
                        Log.Write("TSEnvironment COM object unavailable (0x{0:X8}): {1}", ex.HResult, ex.Message);
                        _com = null;
                    }
                    catch (Exception ex)
                    {
                        Log.Exception("TsEnvironment.Com", ex);
                        _com = null;
                    }

                    return _com;
                }
            }
        }

        /// <summary>True when running inside an MECM task sequence.</summary>
        public static bool IsTaskSequence
        {
            get
            {
                if (_isTaskSequence.HasValue) return _isTaskSequence.Value;

                _isTaskSequence = Get(TsVariables.PackageName) != null;
                Log.Write("Task sequence environment: {0}", _isTaskSequence.Value ? "yes" : "no");
                return _isTaskSequence.Value;
            }
        }

        /// <summary>Reads a variable. Returns null when it is missing or we are not in a TS.</summary>
        public static string Get(string name)
        {
            dynamic com = Com;
            if (com == null) return null;

            try
            {
                string value = com[name];
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (Exception ex)
            {
                Log.Exception("TsEnvironment.Get(" + name + ")", ex);
                return null;
            }
        }

        /// <summary>
        /// Writes a variable. Returns false rather than throwing - the original Set had no
        /// try/catch at all, so a COM hiccup halfway through applying the user's selections
        /// took the whole app down with an unhandled exception.
        /// </summary>
        public static bool Set(string name, string value)
        {
            dynamic com = Com;
            if (com == null) return false;

            try
            {
                com[name] = value;
                Log.Write("Set TS variable {0} = {1}", name, value);
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception("TsEnvironment.Set(" + name + ")", ex);
                return false;
            }
        }

        /// <summary>True when the task sequence is running in WinPE rather than the full OS.</summary>
        public static bool IsWinPE
        {
            get
            {
                string value = Get(TsVariables.InWinPE);
                return value != null &&
                       value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Closes the progress dialog so it cannot sit on top of our window.</summary>
        public static void CloseProgressUi()
        {
            if (!IsTaskSequence) return;

            try
            {
                foreach (Process proc in Process.GetProcessesByName("TSProgressUI"))
                {
                    using (proc)
                    {
                        try
                        {
                            proc.Kill();
                            Log.Write("Closed TSProgressUI (pid {0}).", proc.Id);
                        }
                        catch (Exception ex)
                        {
                            Log.Exception("TsEnvironment.CloseProgressUi", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("TsEnvironment.CloseProgressUi", ex);
            }
        }

        /// <summary>
        /// The failing step name, resolved in priority order:
        /// the custom "failstep" variable, then MECM's own _SMSTSLastActionName, then the
        /// current action. The original only ever looked at "failstep", which meant the field
        /// was blank unless the admin had wired up a custom error handler to populate it.
        /// </summary>
        public static string FailingStep()
        {
            return Get(TsVariables.FailStep)
                ?? Get(TsVariables.LastActionName)
                ?? Get(TsVariables.CurrentActionName);
        }

        /// <summary>The failing return code, same fallback chain as <see cref="FailingStep"/>.</summary>
        public static string FailingCodeRaw()
        {
            return Get(TsVariables.FailCode)
                ?? Get(TsVariables.LastActionRetCode);
        }

        /// <summary>
        /// The return code as it is actually useful: decimal and hex side by side. MECM stores
        /// it as a signed decimal such as -2147024894, but every lookup table and every search
        /// result is keyed on the hex form (0x80070002), so showing only the decimal forces the
        /// person reading the screen to do the conversion by hand.
        /// </summary>
        public static string FormatReturnCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();

            int value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}  (0x{1:X8})", value, value);
            }

            // Already hex, or something non-numeric. Show it unchanged.
            return raw;
        }
    }
}
