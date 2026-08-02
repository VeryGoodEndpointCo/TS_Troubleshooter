using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace TS_Troubleshooter_Unified.Core
{
    /// <summary>Version of this assembly, shown in the header.</summary>
    internal static class AppInfo
    {
        private static readonly Lazy<string> Lazy = new Lazy<string>(() =>
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch
            {
                return "?";
            }
        });

        public static string Version { get { return Lazy.Value; } }
    }

    /// <summary>
    /// Hostname and IP details.
    ///
    /// The hostname deliberately comes from <see cref="Environment.MachineName"/> rather than
    /// a Win32_ComputerSystem WMI query. WMI returns the same string but costs hundreds of
    /// milliseconds (which is what the old splash screen was hiding), can be slow or absent in
    /// a trimmed boot image, and pulling in System.Management dragged two extra DLLs into the
    /// deployed payload. This way the app ships as a single exe.
    /// </summary>
    internal static class MachineInfo
    {
        public static string HostName
        {
            get
            {
                try
                {
                    return Environment.MachineName;
                }
                catch (Exception ex)
                {
                    Log.Exception("MachineInfo.HostName", ex);
                    return "(unknown)";
                }
            }
        }

        /// <summary>
        /// Every usable IPv4 address, best candidate first.
        ///
        /// The old code looped over every interface and every address, overwriting the same
        /// textbox each time, so it displayed whichever address happened to come last - very
        /// often a Hyper-V vEthernet address or a 169.254.x.x APIPA address. Here interfaces
        /// that have a default gateway sort first and APIPA sorts last, so the address shown
        /// is the one that can actually reach the network.
        /// </summary>
        public static IList<string> IPv4Addresses()
        {
            var ranked = new List<Tuple<int, string>>();

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props;
                    try
                    {
                        props = nic.GetIPProperties();
                    }
                    catch (NetworkInformationException)
                    {
                        continue;
                    }

                    bool hasGateway = props.GatewayAddresses
                        .Any(g => g.Address != null &&
                                  g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                  !g.Address.Equals(IPAddress.Any));

                    foreach (UnicastIPAddressInformation info in props.UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        string text = info.Address.ToString();
                        bool isApipa = text.StartsWith("169.254.", StringComparison.Ordinal);

                        // Lower rank sorts first.
                        int rank = isApipa ? 3 : (hasGateway ? 0 : 1);
                        ranked.Add(Tuple.Create(rank, text));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("MachineInfo.IPv4Addresses", ex);
            }

            return ranked
                .OrderBy(t => t.Item1)
                .Select(t => t.Item2)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The addresses formatted for display: the best one, with any others appended so a
        /// multi-homed machine does not hide the address the network team needs.
        /// </summary>
        public static string IPv4Display()
        {
            IList<string> addresses = IPv4Addresses();

            if (addresses.Count == 0) return "(no network)";
            if (addresses.Count == 1) return addresses[0];

            return addresses[0] + "  (also " + string.Join(", ", addresses.Skip(1)) + ")";
        }
    }
}
