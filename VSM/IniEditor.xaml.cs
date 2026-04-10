using ModernWpf.Controls;
using SoulMaskServerManager;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace SoulmaskServerManager
{

    /// <summary>
    /// IniEditor.xaml 的交互逻辑
    /// </summary>
    public partial class IniEditor : Window
    {
        private readonly string _iniPath;
        private List<string> _originalLines;
        private MainSettings _settings = MainSettings.LoadManagerSettings();
        private Server _currentServer;
        
        // 接收文件路径
        public IniEditor(ObservableCollection<Server> sentServers, bool autoLoad = false, int indexToLoad = -1)
        {
            string iniPath = Path.Combine(sentServers[indexToLoad].Path, "WS", "Saved", "Config", "WindowsServer", "Engine.ini");
            InitializeComponent();
            _iniPath = iniPath;
            _currentServer = sentServers[indexToLoad];
            LoadAndFillIni(); // 打开窗口自动加载
        }

        
        // 自动读取并填充界面
        private void LoadAndFillIni()
        {
            if (!File.Exists(_iniPath))
            {
                MessageBox.Show("文件不存在");
                Close();
                return;
            }

            _originalLines = File.ReadAllLines(_iniPath).ToList();
            string section = "";
            RefreshMainServerList();

            foreach (var line in _originalLines)
            {
                var t = line.Trim();
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith(";")) continue;

                if (t.StartsWith("["))
                {
                    section = t.Trim('[', ']');
                    continue;
                }

                var kv = t.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                var val = kv[1].Trim();

                if (section == "Dedicated.Settings")
                {
                    if (key == "SteamServerName") TxtServerName.Text = val;
                    if (key == "MaxPlayers") TxtMaxPlayers.Text = val;
                    if (key == "backup") TxtBackup.Text = val;
                    if (key == "saving") TxtSaving.Text = val;
                    if (key == "pvp")
                    {
                        if (val == "False")
                        {
                            pvpModSelect.SelectedIndex = 0;
                        }
                        else
                        {
                            pvpModSelect.SelectedIndex = 1;

                        }
                    }
                }
                if (section == "URL" && key == "port") TxtPort.Text = val;
                if (section == "OnlineSubsystemSteam" && key == "GameServerQueryPort") QueryPort.Text = val;
            }

            _settings = MainSettings.LoadManagerSettings();
            foreach (var server in _settings.Servers)
            {
                if (server.Port == TxtPort.Text)
                {
                    ClusterModeComboBox.SelectedIndex = server.CrossServer == "None" ? 0 : (server.CrossServer.Contains("-mainserver") ? 1 : 2);
                    if (ClusterModeComboBox.SelectedIndex == 2)
                    {
                        string mainServerName = FindServerByPort(server.MainPort);
                        if (!string.IsNullOrEmpty(mainServerName))
                        {
                            foreach (var item in MainServerListBox.Items)
                            {
                                if (item.ToString() == mainServerName)
                                {
                                    MainServerListBox.SelectedItem = item;
                                    break;
                                }
                            }
                        }
                    }

                    EchoPort.Text = server.EchoPort;
                    Psw.Text = server.PassWord;
                    AdminPsw.Text = server.GmPassWord;
                    BackupInterval.Text = server.BackupInterval;
                    break;
                }
            }
        }

        // 保存
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var output = new List<string>();
            string currentSection = "";

            foreach (var line in _originalLines)
            {
                var t = line.Trim();
                if (t.StartsWith("[")) currentSection = t.Trim('[', ']');

                // 完全保留 Core.System
                if (currentSection == "Core.System")
                {
                    output.Add(line);
                    continue;
                }

                // 跳过旧节点
                if (currentSection is "Dedicated.Settings" or "URL" or "OnlineSubsystemSteam")
                    continue;

                output.Add(line);
            }

            // 写入新节点
            output.Add("[Dedicated.Settings]");
            output.Add($"SteamServerName={TxtServerName.Text}");
            output.Add($"MaxPlayers={TxtMaxPlayers.Text}");
            output.Add($"pvp={(pvpModSelect.SelectedIndex == 0 ? "False" : "True")}");

            output.Add($"backup={TxtBackup.Text}");
            output.Add($"saving={TxtSaving.Text}");

            output.Add("");
            output.Add("[URL]");
            output.Add($"port={TxtPort.Text}");

            output.Add("");
            output.Add("[OnlineSubsystemSteam]");
            output.Add($"GameServerQueryPort={QueryPort.Text}");

            File.WriteAllLines(_iniPath, output);
            RefreshMainServerList();

            foreach (var server in _settings.Servers)
            {
                if (server.vsmServerName == _currentServer.vsmServerName)
                {
                    string crossServerValue = "None";
                    int mode = ClusterModeComboBox.SelectedIndex;
                    server.MainPort = "";

                    if (mode == 1)
                    {
                        // 主服务器
                        crossServerValue = $"-mainserverport={TxtPort.Text}";
                    }
                    else if (mode == 2)
                    {
                        string mainServerName = MainServerListBox.SelectedItem?.ToString().Trim() ?? "";

                        if (string.IsNullOrEmpty(mainServerName))
                        {
                            MessageBox.Show("请在列表中点击选择一个主服务器！");
                            return;
                        }

                        // 正常获取端口和IP
                        string mainPort = GetMainServerPort(mainServerName);
                        string publicIP = await GetPublicIPAsync();
                        crossServerValue = $"-clientserverconnect={publicIP}:{mainPort}";
                        server.MainPort = mainPort;
                        
                    }

                    server.EchoPort = EchoPort.Text;
                    server.PassWord = Psw.Text;
                    server.GmPassWord = AdminPsw.Text;
                    server.BackupInterval = BackupInterval.Text;
                    server.BackupAmount = BackupAmount.Text;
                    server.CrossServer = crossServerValue;
                    server.Port = TxtPort.Text;
                }
            }
            MainSettings.Save(_settings);

            var contentDialog = new ContentDialog()
            {
                Content = "保存成功！",
                PrimaryButtonText = "确定",
                SecondaryButtonText = "退出"
            };
            if (await contentDialog.ShowAsync() != ContentDialogResult.Primary)
                Close();
        }

        private void ClusterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClusterModeComboBox.SelectedIndex == 2) 
                MainServerListBox.Visibility = Visibility.Visible; 
            else
                MainServerListBox.Visibility = Visibility.Collapsed;
        }

        void RefreshMainServerList()
        {
            foreach (var server in _settings.Servers)
            {
                if (server.CrossServer == "None" || server.CrossServer == "-clientserverconnect")
                    continue;
                if (server.CrossServer.Contains("-mainserverport"))
                {
                    foreach (var name in MainServerListBox.Items)
                    {
                        if (name.ToString() == server.vsmServerName)
                            return;
                    }
                    MainServerListBox.Items.Add(server.vsmServerName);
                }
            }
        }
        public async Task<string> GetPublicIPAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    return await client.GetStringAsync("https://api.ipify.org");
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public string GetMainServerPort(string mainServerVsmName)
        {
            try
            {
                foreach (var server in _settings.Servers)
                {
                    if (server.vsmServerName == mainServerVsmName)
                        return server.Port;
                }
                return "8777";
            }
            catch
            {
                return "8777";
            }
        }

        public string FindServerByPort(string mainServerPort)
        {
            try
            {
                foreach (var server in _settings.Servers)
                {
                    if (server.Port == mainServerPort)
                        return server.vsmServerName;
                }
                return mainServerPort;
            }
            catch
            {
                return mainServerPort;
            }
        }

    }

}

