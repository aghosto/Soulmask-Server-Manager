using SoulMaskServerManager.RCON;
using SoulMaskServerManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Net;
using System.Collections.ObjectModel;

namespace SoulmaskServerManager
{
    /// <summary>
    /// Interaction logic for RconConsole.xaml
    /// </summary>
    public partial class RconConsole : Window
    {
        RemoteConClient rClient;
        Server server;
        //RCONServerSettings RCONServerSettings;
        MainSettings MainSettings = MainSettings.LoadManagerSettings();

        public RconConsole(int indexToLoad = -1)
        {
            //server = MainSettings.Servers[indexToLoad];

            DataContext = MainSettings.Servers[indexToLoad];
            InitializeComponent();

            MainSettings.Servers[indexToLoad].RconServerSettings.IPAddress = Ipaddress.Text;
            MainSettings.Servers[indexToLoad].RconServerSettings.Port = (int)Port.Value;
            MainSettings.Servers[indexToLoad].RconServerSettings.Password = Password.Text;
            MainSettings.Save(MainSettings);

            RconConsoleOutput.AppendText("远程控制(RCON)客户端已就绪。");
            CommandList.SelectedIndex = 0;
        }

        private void LogToConsole(string output)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                RconConsoleOutput.AppendText("\r" + output);
                RconConsoleOutput.ScrollToEnd();
            }));
        }

        private void Connect()
        {
            rClient = new()
            {
                UseUtf8 = true
            };
            Dispatcher.Invoke(new Action(() =>
            {
                ConnectButton.IsEnabled = false;
            }));
            rClient.OnLog += message =>
            {
                LogToConsole(message);
            };
            rClient.OnConnectionStateChange += state =>
            {
                if (state == RemoteConClient.ConnectionStateChange.Connected)
                {
                    rClient.Authenticate(server.RconServerSettings.Password);
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = true;
                        SendCommandButton.IsEnabled = true;
                    }));
                }
                if (state == RemoteConClient.ConnectionStateChange.Disconnected)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("已连接。");
                }
                if (state == RemoteConClient.ConnectionStateChange.ConnectionLost)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("连接丢失。");
                }
                if (state == RemoteConClient.ConnectionStateChange.ConnectionTimeout)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("连接超时。");
                }
                if (state == RemoteConClient.ConnectionStateChange.NoConnection)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole($"未能连接至 {server.RconServerSettings.IPAddress}:{server.RconServerSettings.Port}。");
                }
            };
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(Connect);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            await Task.Run(() =>
            {
                rClient.Connect(server.RconServerSettings.IPAddress, server.RconServerSettings.Port);
                {
                    while (!rClient.Connected)
                    {
                        Thread.Sleep(10);
                    }
                }
            });
        }

        private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (CommandList.SelectedIndex)
            {
                default:
                    break;
            }
        }
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (rClient.Connected)
            {
                rClient.Disconnect();
            }
        }
        private void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {

            if (CommandList.SelectedIndex != -1 && rClient.Connected == true)
            {
                try
                {
                    rClient.SendCommand(string.Format("{0} {1}", (CommandList.SelectedItem as ListBoxItem).Content.ToString(), ParamaterTextbox.Text), result => { LogToConsole(result); });
                    LogToConsole(string.Format("已发送 {0}，参数 {1}。", (CommandList.SelectedItem as ListBoxItem).Content.ToString(), ParamaterTextbox.Text));

                }
                catch
                {

                }
            }
            else
            {
                LogToConsole("发送失败\r请确认远程控制是否连接以及是否已选择了指令？");
            }
        }
    }
}
