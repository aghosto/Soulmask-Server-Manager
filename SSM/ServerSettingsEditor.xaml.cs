using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Text.Json;
using System.Collections.ObjectModel;
using ModernWpf.Controls;
using SoulmaskServerManager.Controls;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Text.Encodings.Web;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace SoulmaskServerManager
{
    /// <summary>
    /// Interaction logic for ServerSettingsEditor.xaml
    /// </summary>
    public partial class ServerSettingsEditor : Window
    {
        private ServerSettings serverSettings;
        private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
                                                                            {
                                                                                WriteIndented = true,
                                                                                IncludeFields = true,
                                                                                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                                                            };
        private readonly ObservableCollection<Server> servers;
        private MainSettings mainSettings;
        private int _loadedServerIndex = -1;
        private bool _connectionPortEdited;
        private bool _serverIdEdited;

        public class ServerListItem
        {
            public Server Server { get; set; }
            public int Port { get; set; }
            public string UniqueId { get; set; }
            public string DisplayText => $"{Server.ssmServerName}  端口：{Port}";
        }

        public ServerSettingsEditor(ObservableCollection<Server> sentServers, bool autoLoad = false, int indexToLoad = -1)
        {
            servers = sentServers;
            serverSettings = new ServerSettings();
            DataContext = serverSettings;

            InitializeComponent();
            //ExportFilesToCsv(Path.Combine(servers[indexToLoad].Path, "WS", "Config", "GameplaySettings"), false, Path.Combine(Directory.GetCurrentDirectory(), "Output.csv"));

            CurrentServerComboBox.ItemsSource = servers;
            if (servers.Count > 0)
            {
                int selectedIndex = indexToLoad >= 0 && indexToLoad < servers.Count ? indexToLoad : 0;
                CurrentServerComboBox.SelectedItem = servers[selectedIndex];
            }
            CurrentServerComboBox.SelectionChanged += CurrentServerComboBox_SelectionChanged;
            UpdateServerIdConflictWarning();

            if (autoLoad && indexToLoad != -1 && servers.Count > 0)
            {
                CheckAndLoadSettingsFile(indexToLoad);
            }
            RefreshMainServerList();
        }

        public static ServerSettings LoadServerSettings(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    using (StreamReader sr = new StreamReader(filePath))
                    {
                        string SettingsJSON = sr.ReadToEnd();
                        ServerSettings LoadedSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(SettingsJSON);
                        return LoadedSettings;
                    }
                }
                catch
                {
                    ContentDialog errorDialog = new()
                    {
                        Content = $"服务器配置文件损坏，已使用默认配置。",
                        PrimaryButtonText = "确定",
                    };
                    errorDialog.ShowAsync();

                    return new ServerSettings();
                }
            }
            else
            {
                ContentDialog noDialog = new()
                {
                    Content = $"未找到服务器配置文件，已使用默认配置。",
                    PrimaryButtonText = "确定",
                };
                noDialog.ShowAsync();

                ServerSettings DefaultSettings = new ServerSettings();
                return DefaultSettings;
            }
        }

        public static async Task<ServerSettings> LoadServerSettingsAsync(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    string settingsJson = await File.ReadAllTextAsync(filePath);
                    return System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(settingsJson)
                        ?? throw new System.Text.Json.JsonException("配置文件内容为空或格式无效。");
                }
                catch
                {
                    var errorDialog = new ContentDialog
                    {
                        Content = "服务器配置文件损坏，已使用默认配置。",
                        PrimaryButtonText = "确定",
                    };
                    await errorDialog.ShowAsync();
                    return new ServerSettings();
                }
            }

            var noDialog = new ContentDialog
            {
                Content = "未找到服务器配置文件，已使用默认配置。",
                PrimaryButtonText = "确定",
            };
            await noDialog.ShowAsync();
            return new ServerSettings();
        }

        public static void SaveServerSettings(Server server, ServerSettings settings)
        {
            string settingPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

            try
            {
                string directory = Path.GetDirectoryName(settingPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string SettingsJSON = System.Text.Json.JsonSerializer.Serialize(settings, serializerOptions);
                using (StreamWriter sw = new StreamWriter(settingPath, false))
                {
                    sw.Write(SettingsJSON);
                }

            }
            catch
            {
                ContentDialog failDialog = new()
                {
                    Content = $"配置保存失败！",
                    PrimaryButtonText = "确定",
                };
                failDialog.ShowAsync();
            }
        }

        public static async Task SaveServerSettingsAsync(Server server, ServerSettings settings)
        {
            string settingPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

            try
            {
                string? directory = Path.GetDirectoryName(settingPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string settingsJson = System.Text.Json.JsonSerializer.Serialize(settings, serializerOptions);
                await File.WriteAllTextAsync(settingPath, settingsJson);
            }
            catch
            {
                var failDialog = new ContentDialog
                {
                    Content = "配置保存失败！",
                    PrimaryButtonText = "确定",
                };
                await failDialog.ShowAsync();
            }
        }

        private async void CheckAndLoadSettingsFile(int serverIndex)
        {
            _loadedServerIndex = serverIndex;
            string settingsFilePath = Path.Combine(servers[serverIndex].Path, @"SaveData\Settings\ServerSettings.json");

            try
            {
                if (File.Exists(settingsFilePath))
                {
                    LoadSettingsFileSafe(servers[serverIndex]);
                }
                else
                {
                    // 文件不存在，使用默认配置（不写入磁盘，等用户点保存再写入）
                    serverSettings = new ServerSettings();
                    DataContext = serverSettings;
                    InitializeEditorUI();
                }
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Owner = this,
                    Title = "错误",
                    Content = $"加载失败：{ex.Message}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                Close();
            }
        }

        private async void LoadSettingsFileSafe(Server server)
        {
            try
            {
                string json = File.ReadAllText(server.Path + @"\SaveData\Settings\ServerSettings.json").Trim();

                if (string.IsNullOrEmpty(json))
                {
                    // 文件为空，使用默认配置（不写入磁盘）
                    serverSettings = new ServerSettings();
                    DataContext = serverSettings;
                    InitializeEditorUI();
                    return;
                }

                serverSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                if (serverSettings == null)
                {
                    serverSettings = new ServerSettings();
                }

                DataContext = serverSettings;
                InitializeEditorUI();
            }
            catch
            {
                // 加载失败，使用默认配置（不写入磁盘）
                serverSettings = new ServerSettings();
                DataContext = serverSettings;
                InitializeEditorUI();
            }
        }

        private async void InitializeEditorUI()
        {
            MainServerListBox.IsEnabled = ClusterModeComboBox.SelectedIndex == 2;
            UpdateServerIdConflictWarning();
            UpdateConnectionPortConflictWarning();

            if (serverSettings.Map == "Level01_Main")
                MapSelectComboBox.SelectedIndex = 0;
            else
                MapSelectComboBox.SelectedIndex = 1;

            if (ManualIPCheckBox.IsChecked == true)
            {
                PublicIPTextBox.Text = serverSettings.PublicIP;
            }
            else
            {
                serverSettings.PublicIP = await GetPublicIPAsync();
                PublicIPTextBox.Text = serverSettings.PublicIP;

                if (PublicIPTextBox.Text == "fail")
                {
                    var dialog = new ContentDialog
                    {
                        Content = "自动获取公网ip失败，请检查网络连接或点左下角反馈\r已打开手动填写公网IP设置，请手动填写公网IP",
                        CloseButtonText = "确定"
                    }.ShowAsync();
                    ManualIPCheckBox.IsChecked = true;
                    PublicIPTextBox.Text = "";
                }
            }

            if (ManualMainPortCheckBox.IsChecked == true)
            {
                MainServerPortTextBox.Text = serverSettings.ManualMainPort.ToString();
            }
            else
            {
                serverSettings.MainPort = GetMainServerPortByUniqueId(serverSettings.MainServerUniqueId);
                MainServerPortTextBox.Text = serverSettings.MainPort.ToString();

                if (MainServerPortTextBox.Text == "fail")
                {
                    var dialog = new ContentDialog
                    {
                        Content = "自动获取主服务器端口失败，请检查网络连接或点左下角反馈\r已打开手动填写端口设置，请手动填写主服务器端口",
                        CloseButtonText = "确定"
                    }.ShowAsync();
                    ManualMainPortCheckBox.IsChecked = true;
                    MainServerPortTextBox.Text = "";
                }
            }

            RefreshMainServerList();

            if (!string.IsNullOrEmpty(serverSettings.MainServerUniqueId))
            {
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    SelectMainServerByUniqueId(serverSettings.MainServerUniqueId);
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void SelectMainServerByUniqueId(string targetUid)
        {
            foreach (var item in MainServerListBox.Items)
            {
                if (item is ServerListItem listItem && listItem.Server.UniqueId == targetUid)
                {
                    MainServerListBox.SelectedItem = item;
                    break;
                }
            }
        }

        public static void CreateDefaultSettingsFile(Server server)
        {
            var defaultSettings = new ServerSettings();
            string defaultJson = System.Text.Json.JsonSerializer.Serialize(defaultSettings, serializerOptions);
            File.WriteAllText(server.Path + @"\SaveData\Settings\ServerSettings.json", defaultJson);
        }

        private void FileMenuLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? FileToLoad = "temp";
                OpenFileDialog OpenSettingsDialog = new OpenFileDialog
                {
                    Filter = "\"JSON files\"|*.json",
                    DefaultExt = "json",
                    FileName = "ServerSettings.json",
                    InitialDirectory = Directory.GetCurrentDirectory()
                };
                if (OpenSettingsDialog.ShowDialog() == true && FileToLoad != null)
                {
                    FileToLoad = OpenSettingsDialog.FileName;
                }
                else
                {
                    return;
                }
                using (StreamReader reader = new StreamReader(FileToLoad))
                {
                    string LoadedJSON = reader.ReadToEnd();
                    ServerSettings LoadedSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(LoadedJSON)
                        ?? throw new System.Text.Json.JsonException("配置文件内容为空或格式无效。");
                    serverSettings = LoadedSettings;
                    DataContext = serverSettings;
                    InitializeEditorUI();
                }
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "导入失败",
                    Content = $"无法读取服务器设置文件：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private string SerializeCurrentState()
        {
            return System.Text.Json.JsonSerializer.Serialize(serverSettings, serializerOptions);
        }

        private async void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog resetDialog = new()
            {
                Title = "重置服务器连接配置",
                Content = "确定将当前服务器连接配置重置为默认值吗？",
                PrimaryButtonText = "重置",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary
            };

            if (await resetDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            serverSettings = new ServerSettings();
            DataContext = serverSettings;
            InitializeEditorUI();
        }

        private void FileMenuExport_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "JSON files|*.json",
                DefaultExt = "json",
                FileName = "ServerSettings.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = SerializeCurrentState();
                File.WriteAllText(dialog.FileName, json);
                _ = new ContentDialog
                {
                    Title = "导出成功",
                    Content = $"配置已导出至：\n{dialog.FileName}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "错误",
                    Content = $"导出失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private async void FileMenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedIndex == -1)
            {
                var yesNoDialog = new ContentDialog()
                {
                    Content = "你选择了作为副服务器，请选择一个绑定的主服务器！",
                    PrimaryButtonText = "是",
                }.ShowAsync();
                return;
            }

            mainSettings = MainSettings.LoadManagerSettings();
            if (servers.Count > 0)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = "是否自动保存到服务器？如果原始文件存在，将创建其备份。",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    Server? currentServer = CurrentServerComboBox.SelectedItem as Server ??
                        (_loadedServerIndex >= 0 && _loadedServerIndex < servers.Count
                            ? servers[_loadedServerIndex]
                            : servers.FirstOrDefault(server => server.UniqueId == serverSettings.SelfServerUniqueId));
                    EditorSaveDialog dialog = new(servers, currentServer)
                    {
                        PrimaryButtonText = "保存",
                        CloseButtonText = "取消"
                    };
                    Server server;

                    if (await dialog.ShowAsync() is ContentDialogResult.Primary)
                    {
                        server = dialog.GetServer();
                        string settingFolderPath = Path.Combine(server.Path, "SaveData", "Settings");
                        string settingPath = Path.Combine(settingFolderPath, "ServerSettings.json");
                        
                        if (!Directory.Exists(settingFolderPath))
                            Directory.CreateDirectory(settingFolderPath);

                        if (File.Exists(settingPath))
                            File.Copy(settingPath, settingFolderPath + @"\ServerSettings.bak", true);

                        if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedItem is ServerListItem listItem)
                        {
                            serverSettings.UseManualMainPort = ManualMainPortCheckBox.IsChecked == true;
                            if (int.TryParse(MainServerPortTextBox.Text, out int customPort))
                            {
                                serverSettings.ManualMainPort = customPort;
                            }

                            if (listItem.UniqueId == serverSettings.SelfServerUniqueId)
                            {
                                var warningDialog = new ContentDialog
                                {
                                    Owner = this,
                                    Title = "无效选择",
                                    Content = "不能同时作为主副服务器，请重新选择。",
                                    PrimaryButtonText = "确定"
                                };
                                await warningDialog.ShowAsync();
                                return;
                            }

                            if (serverSettings.UseManualMainPort && serverSettings.ManualMainPort > 0)
                                serverSettings.MainPort = serverSettings.ManualMainPort;
                            else
                                serverSettings.MainPort = listItem.Port;

                            serverSettings.MainServerUniqueId = listItem.Server.UniqueId;
                        }
                        else
                        {
                            serverSettings.MainPort = 0;
                            serverSettings.MainServerUniqueId = "";
                        }

                        if (MapSelectComboBox.SelectedIndex == 0)
                        {
                            serverSettings.Map = "Level01_Main";
                        }
                        else
                            serverSettings.Map = "DLC_Level01_Main";

                        SaveServerSettings(server, serverSettings);
                        server.ConnectionPort = serverSettings.Port;
                        RefreshMainServerList();

                        var closeFileDialog = new ContentDialog()
                        {
                            Content = "文件成功保存于：\n" + settingPath,
                            PrimaryButtonText = "是",
                        }.ShowAsync();

                        //serverSettings = LoadServerSettings(settingPath);
                        ServerIdMapping.EnsureAllServersHaveIds();
                        MainWindow.StartBackupCleanTimer(server, serverSettings.AutoCleanInterval, serverSettings.AutoSaveCount);
                        LoadSettingsFileSafe(server);

                        foreach (var index in mainSettings.Servers)
                        {
                            if (index.UniqueId == serverSettings.SelfServerUniqueId)
                            {
                                index.RconServerSettings.Port = serverSettings.Rcon.Port;
                                index.RconServerSettings.EchoPort = serverSettings.EchoPort;
                                index.RconServerSettings.Password = serverSettings.Rcon.Password;
                            }
                        }

                        MainSettings.Save(mainSettings);
                        return;
                    }
                    else
                    {
                        RefreshMainServerList();
                        return;
                    }
                }
                else
                    return;
            }
            RefreshMainServerList();
        }

        private void FileMenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshMainServerList()
        {
            MainServerListBox.Items.Clear();
            Server? currentServer = CurrentServerComboBox.SelectedItem as Server ??
                (_loadedServerIndex >= 0 && _loadedServerIndex < servers.Count
                    ? servers[_loadedServerIndex]
                    : servers.FirstOrDefault(server => server.UniqueId == serverSettings.SelfServerUniqueId));

            foreach (var server in servers)
            {
                if (currentServer != null && server.UniqueId == currentServer.UniqueId)
                    continue;

                try
                {
                    string serverSettingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

                    if (!File.Exists(serverSettingsPath))
                        continue;

                    ServerSettings settings = ServerSettingsEditor.LoadServerSettings(serverSettingsPath);

                    if (settings.ClusterMode == 1)
                    {
                        string serverName = server.ssmServerName;
                        bool exists = false;

                        foreach (var item in MainServerListBox.Items)
                        {
                            if (item.ToString() == serverName)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            MainServerListBox.Items.Add(new ServerListItem
                            {
                                Server = server,
                                Port = settings.Port,
                                UniqueId = settings.SelfServerUniqueId
                            });
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private void CurrentServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentServerComboBox.SelectedItem is Server selectedServer)
            {
                _connectionPortEdited = false;
                _serverIdEdited = false;
                int selectedIndex = servers.IndexOf(selectedServer);
                if (selectedIndex >= 0 && selectedIndex != _loadedServerIndex)
                    CheckAndLoadSettingsFile(selectedIndex);
            }
        }

        private void UpdateServerIdConflictWarning()
        {
            if (serverSettings == null || ServerIdConflictTextBlock == null)
                return;

            int serverId = serverSettings.ServerId;
            if (_loadedServerIndex < 0 && !_serverIdEdited && CurrentServerComboBox.SelectedItem is Server selectedServer)
            {
                string currentSettingsPath = Path.Combine(selectedServer.Path, "SaveData", "Settings", "ServerSettings.json");
                if (File.Exists(currentSettingsPath))
                {
                    try
                    {
                        serverId = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(currentSettingsPath))?.ServerId ?? serverId;
                    }
                    catch
                    {
                        // 当前配置不可读取时，使用编辑器中的值进行检查。
                    }
                }
            }

            if (serverId <= 0)
            {
                ServerIdConflictTextBlock.Visibility = Visibility.Collapsed;
                ServerIdConflictTextBlock.Text = string.Empty;
                return;
            }

            Server? currentServer = CurrentServerComboBox.SelectedItem as Server;
            var conflictingServers = new List<string>();

            foreach (Server? server in servers)
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Path))
                    continue;

                if (currentServer != null && server.UniqueId == currentServer.UniqueId)
                    continue;

                string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                if (!File.Exists(settingsPath))
                    continue;

                try
                {
                    ServerSettings? otherSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(settingsPath));
                    if (otherSettings?.ServerId == serverId)
                        conflictingServers.Add(string.IsNullOrWhiteSpace(server.ssmServerName)
                            ? Path.GetFileName(server.Path)
                            : server.ssmServerName);
                }
                catch
                {
                    // 忽略无法读取的其他服务器配置，继续检查其余服务器。
                }
            }

            if (conflictingServers.Count == 0)
            {
                ServerIdConflictTextBlock.Visibility = Visibility.Collapsed;
                ServerIdConflictTextBlock.Text = string.Empty;
                return;
            }

            ServerIdConflictTextBlock.Text = $"服务器ID {serverId} 与以下服务器重复：{string.Join("、", conflictingServers.Distinct())}";
            ServerIdConflictTextBlock.Visibility = Visibility.Visible;
        }

        private void ServerIdNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (IsLoaded)
                _serverIdEdited = true;
            UpdateServerIdConflictWarning();
        }

        private void ConnectionPortNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (IsLoaded)
                _connectionPortEdited = true;
            UpdateConnectionPortConflictWarning();
        }

        private void UpdateConnectionPortConflictWarning()
        {
            if (serverSettings == null || ConnectionPortConflictTextBlock == null)
                return;

            int port = serverSettings.Port;
            Server? currentServer = CurrentServerComboBox.SelectedItem as Server;
            if (_loadedServerIndex < 0 && !_connectionPortEdited && currentServer != null)
            {
                string currentSettingsPath = Path.Combine(currentServer.Path, "SaveData", "Settings", "ServerSettings.json");
                if (File.Exists(currentSettingsPath))
                {
                    try
                    {
                        port = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(currentSettingsPath))?.Port ?? port;
                    }
                    catch
                    {
                        // 当前配置不可读取时，使用编辑器中的值进行检查。
                    }
                }
            }

            if (port <= 0)
            {
                ConnectionPortConflictTextBlock.Visibility = Visibility.Collapsed;
                ConnectionPortConflictTextBlock.Text = string.Empty;
                return;
            }

            var conflictingServers = new List<string>();
            foreach (Server? server in servers)
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Path))
                    continue;

                if (currentServer != null && server.UniqueId == currentServer.UniqueId)
                    continue;

                string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                if (!File.Exists(settingsPath))
                    continue;

                try
                {
                    ServerSettings? otherSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(settingsPath));
                    if (otherSettings?.Port == port)
                        conflictingServers.Add(string.IsNullOrWhiteSpace(server.ssmServerName)
                            ? Path.GetFileName(server.Path)
                            : server.ssmServerName);
                }
                catch
                {
                    // 忽略无法读取的其他服务器配置，继续检查其余服务器。
                }
            }

            if (conflictingServers.Count == 0)
            {
                ConnectionPortConflictTextBlock.Visibility = Visibility.Collapsed;
                ConnectionPortConflictTextBlock.Text = string.Empty;
                return;
            }

            ConnectionPortConflictTextBlock.Text = $"连接端口 {port} 与以下服务器重复：{string.Join("、", conflictingServers.Distinct())}";
            ConnectionPortConflictTextBlock.Visibility = Visibility.Visible;
        }

        private void ClusterModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainServerListBox != null)
                MainServerListBox.IsEnabled = ClusterModeComboBox.SelectedIndex == 2;
        }

        private int GetMainServerPortByUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return -1;

            try
            {
                string serverSaveDataPath = Path.Combine(GetMainServerFolderByUniqueId(uniqueId), "SaveData", "Settings", "ServerSettings.json");
                if (!File.Exists(serverSaveDataPath))
                    return -1;

                string json = File.ReadAllText(serverSaveDataPath).Trim();
                ServerSettings readServerSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                return readServerSettings?.Port ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private string GetMainServerFolderByUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return string.Empty;

            if (mainSettings == null)
                mainSettings = MainSettings.LoadManagerSettings();

            var server = mainSettings.Servers.FirstOrDefault(s => s.UniqueId == uniqueId);
            return server?.Path ?? string.Empty;
        }

        private async Task<string> GetPublicIPAsync()
        {
            try
            {
                string publicIP = "";
                using (HttpClient client = new HttpClient())
                {
                    publicIP = await client.GetStringAsync("http://ipinfo.io/ip");
                }
                return publicIP;
            }
            catch
            {
                return "fail";
            }
        }

        public static int GetNextAvailableServerId(System.Collections.ObjectModel.ObservableCollection<Server> servers)
        {
            var usedIds = new HashSet<int>();
            foreach (var server in servers)
            {
                string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(settingsPath);
                        var settings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                        if (settings != null && settings.ServerId > 0)
                            usedIds.Add(settings.ServerId);
                    }
                    catch { }
                }
            }
            int id = 1;
            while (usedIds.Contains(id))
                id++;
            return id;
        }
    }
}
