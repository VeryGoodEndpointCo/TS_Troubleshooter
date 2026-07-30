using System.Windows;
using System.Management;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System;
using System.Windows.Controls;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace TS_Troubleshooter
{

    public partial class MainWindow : Window
    {

        TS_Integration TS = new TS_Integration();
        XmlDocument doc = App.xmlDoc;

        public MainWindow()
        {
            TS.TSProgressKill();
            InitializeComponent();
            DefineTextboxes();
            XMLContents();
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

        public void XMLContents()
        {
            XmlNodeList optionsNodes = doc.SelectNodes("/options/task");
            foreach (XmlNode optionsNode in optionsNodes)
            {
                CheckBox checkBox = new CheckBox
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 7, 0, 0),
                    Content = optionsNode.InnerText,
                    Tag = optionsNode.Attributes["TSV"].Value,
                    VerticalAlignment = VerticalAlignment.Top,
                    FontSize = 14
                };
                optionListBox.Children.Add(checkBox);
            }
        }

        public void Submit()
        {

            if (TS.IsTSEnv())
            {
                foreach (CheckBox checkBox in optionListBox.Children)
                {
                    if (checkBox.IsChecked == true)
                    {
                        string var = checkBox.Tag.ToString();
                        TS.SetTSVar(var, "True");
                    }
                }
            }
            else
            {
                List<string> myList = new List<string>();

                foreach (CheckBox checkBox in optionListBox.Children)
                {
                    if (checkBox.IsChecked == true)
                    {
                        string var = checkBox.Tag.ToString();
                        myList.Add(var);
                    }
                }
                if (myList.Count != 0)
                {
                    string listAsString = string.Join(Environment.NewLine, myList);
                    MessageBox.Show($"{listAsString}", "Complete", MessageBoxButton.OK, MessageBoxImage.None);
                }
            }
        }

        public void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            Submit();
            if (TS.IsTSEnv())
            {
                if (TS.GetTSVar("_SMSTSInWinPE").ToString().ToLower() == "true")
                {
                    TS.SetTSVar("PE_Reboot", "True");
                }
                else
                {
                    TS.SetTSVar("OS_Reboot", "True");
                }
            }
            else
            {
                MessageBox.Show("Restart PC", "Complete", MessageBoxButton.OK, MessageBoxImage.None);
                Environment.Exit(0);
            }
            Environment.Exit(0);
        }
        public void ShutDownButton_Click(object sender, RoutedEventArgs e)
        {
            Submit();
            if (TS.IsTSEnv())
            {
                if (TS.GetTSVar("_SMSTSInWinPE").ToString().ToLower() == "true")
                {
                    TS.SetTSVar("PE_Shutdown", "True");
                }
                else
                {
                    TS.SetTSVar("OS_Shutdown", "True");
                }
            }
            else
            {
                MessageBox.Show("Shutdown PC", "Complete", MessageBoxButton.OK, MessageBoxImage.None);
                Environment.Exit(0);
            }
            Environment.Exit(0);
        }
    }
}
