using System.Windows;
using System.Management;
using System.Net.NetworkInformation;


namespace TS_Troubleshooter_Lite
{

    public partial class MainWindow : Window
    {

        TS_Integration TS = new TS_Integration();

        public MainWindow()
        {
            TS.TSProgressKill();
            InitializeComponent();
            DefineTextboxes();
        }

        public void DefineTextboxes()
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                foreach (ManagementObject obj in searcher.Get())
                {
                    host.Text = obj["Name"].ToString();
                }

            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                // Check if the interface is operational and not loopback or tunnel
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    // Get IP properties for this interface
                    IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();

                    // Get the IPv4 addresses
                    foreach (UnicastIPAddressInformation ipInfo in ipProperties.UnicastAddresses)
                    {
                        if (ipInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            ip.Text = ipInfo.Address.ToString();
                        }
                    }
                }
            }

            if (TS.IsTSEnv())
            {
                fail_step.Text = TS.GetTSVar("failstep");
                fail_code.Text = TS.GetTSVar("failcode");
            }
            else
            {
                fail_step.Text = "Not in TS, placeholder";
                fail_code.Text = "Not in TS, placeholder";
            }
        }

        public void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
