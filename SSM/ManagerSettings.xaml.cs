using ModernWpf.Controls;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace SoulmaskServerManager
{
    /// <summary>
    /// 管理器设置窗口
    /// </summary>
    public partial class ManagerSettings : Window
    {
        private static readonly JsonSerializerOptions SettingsJsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private readonly MainSettings _mainSettings;

        private MainWindow _mainWindow;


        public ManagerSettings(MainSettings mainSettings)
        {
            _mainSettings = mainSettings ?? throw new ArgumentNullException(nameof(mainSettings), "主设置数据不能为null");
            InitializeComponent();

            DataContext = _mainSettings;
            _mainWindow = Application.Current.MainWindow as MainWindow;
            if (_mainWindow != null)
            {
                Owner = _mainWindow;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            UpdateServerComboState();
            _mainSettings.Servers.CollectionChanged += Servers_CollectionChanged;
        }

        /// <summary>
        /// 当服务器集合变化时，更新控件状态
        /// </summary>
        private void Servers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateServerComboState();
        }

        /// <summary>
        /// 更新服务器选择下拉框的启用状态和默认选中项
        /// </summary>
        private void UpdateServerComboState()
        {
            bool hasServers = _mainSettings.Servers.Count > 0;

            ServerCombo1.IsEnabled = hasServers;
            ServerCombo2.IsEnabled = hasServers;
            ResetServerButton.IsEnabled = hasServers;

            if (hasServers && ServerCombo1.SelectedIndex == -1)
            {
                ServerCombo1.SelectedIndex = 0;
            }
            if (hasServers && ServerCombo2.SelectedIndex == -1)
            {
                ServerCombo2.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 向主窗口控制台输出日志（带颜色）
        /// </summary>
        private void ShowLogMsg(string logMessage, Brush color)
        {
            if (_mainWindow == null)
            {
                _mainWindow = Application.Current.MainWindow as MainWindow;
                if (_mainWindow == null)
                {
                    return;
                }
            }
            _mainWindow.ShowLogMsg(logMessage, color);
        }

        /// <summary>
        /// 确认后保存设置并保持窗口打开。
        /// </summary>
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new()
            {
                Title = "保存管理器设置",
                Content = "确定保存当前设置吗？",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                Owner = this
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                MainSettings.Save(_mainSettings);
                _mainWindow?.SetupServerAutoUpdateTimer();
                _mainWindow?.InitAutoRestartTimer();
                _mainWindow?.InitScheduledRconMessagesTimer();
            }
            catch (Exception ex)
            {
                //ShowLogMsg($"软件保存设置失败：{ex.Message}", Brushes.Red);

                _ = new ContentDialog
                {
                    Title = "保存失败",
                    Content = $"保存设置时出错：{ex.Message}",
                    PrimaryButtonText = "确定",
                    Owner = this
                }.ShowAsync();
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void FileMenuLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "JSON 文件|*.json",
                DefaultExt = "json",
                FileName = "SSMSettings.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };
            if (dialog.ShowDialog(this) != true)
                return;

            ContentDialog confirmDialog = new()
            {
                Title = "导入管理器设置",
                Content = "导入将替换当前管理器设置和服务器列表，是否继续？",
                PrimaryButtonText = "导入",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                Owner = this
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                MainSettings imported = JsonSerializer.Deserialize<MainSettings>(File.ReadAllText(dialog.FileName), SettingsJsonOptions)
                    ?? throw new JsonException("文件内容为空或格式无效。");

                _mainSettings.Servers.CollectionChanged -= Servers_CollectionChanged;
                _mainSettings.Servers.Clear();
                foreach (Server server in imported.Servers ?? new())
                    _mainSettings.Servers.Add(server);
                _mainSettings.AppSettings = imported.AppSettings ?? new AppSettings();
                _mainSettings.WebhookSettings = imported.WebhookSettings ?? new Webhook();
                _mainSettings.DownloadedMods = imported.DownloadedMods ?? new List<Mod>();
                _mainSettings.MainServers = imported.MainServers ?? new List<string>();
                if (!_mainSettings.AppSettings.ServerAutoUpdateMigrated)
                {
                    if (_mainSettings.AppSettings.AutoUpdate)
                    {
                        foreach (Server server in _mainSettings.Servers)
                            server.AutoUpdateEnabled = true;
                    }
                    _mainSettings.AppSettings.ServerAutoUpdateMigrated = true;
                }
                _mainSettings.Servers.CollectionChanged += Servers_CollectionChanged;

                DataContext = null;
                DataContext = _mainSettings;
                UpdateServerComboState();
                _mainWindow?.SetupServerAutoUpdateTimer();
                _mainWindow?.InitAutoRestartTimer();
                _mainWindow?.InitScheduledRconMessagesTimer();
                _ = new ContentDialog
                {
                    Title = "导入成功",
                    Content = "管理器设置已导入。点击“保存”以写入当前配置文件。",
                    CloseButtonText = "确定",
                    Owner = this
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "导入失败",
                    Content = $"无法读取管理器设置：{ex.Message}",
                    CloseButtonText = "确定",
                    Owner = this
                }.ShowAsync();
            }
        }

        private void FileMenuExport_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "JSON 文件|*.json",
                DefaultExt = "json",
                FileName = "SSMSettings.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_mainSettings, SettingsJsonOptions));
                _ = new ContentDialog
                {
                    Title = "导出成功",
                    Content = $"管理器设置已导出到：\n{dialog.FileName}",
                    CloseButtonText = "确定",
                    Owner = this
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "导出失败",
                    Content = $"无法导出管理器设置：{ex.Message}",
                    CloseButtonText = "确定",
                    Owner = this
                }.ShowAsync();
            }
        }

        private async void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmDialog = new()
            {
                Title = "重置管理器设置",
                Content = "将重置此窗口中的选项，包括全局偏好、定时消息、自动重启和通知模板；服务器列表、路径及连接数据会保留。是否继续？",
                PrimaryButtonText = "重置",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                Owner = this
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            _mainSettings.AppSettings = new AppSettings
            {
                ServerScheduledAutoRestartMigrated = true,
                ScheduledRconMessagesDefaultDisabledMigrated = true,
                ServerAutoUpdateMigrated = true
            };
            _mainSettings.WebhookSettings = new Webhook();
            foreach (Server server in _mainSettings.Servers)
            {
                server.AutoStart = false;
                server.AutoUpdateEnabled = false;
                server.ScheduledAutoRestartEnabled = false;
                server.ScheduledAutoRestartHour = 0;
                server.ScheduledAutoRestartMin = 0;
                server.ScheduledAutoRestartSec = 0;
                server.WebhookMessages = new ServerWebhook();
                server.RconServerSettings.Enabled = false;
                server.RconServerSettings.ScheduledMessages.Clear();
            }

            DataContext = null;
            DataContext = _mainSettings;
            _mainWindow?.SetupServerAutoUpdateTimer();
            _mainWindow?.InitAutoRestartTimer();
            _mainWindow?.InitScheduledRconMessagesTimer();
        }

        private void AddScheduledMessage_Click(object sender, RoutedEventArgs e)
        {
            if (ServerCombo2.SelectedItem is Server server)
                server.RconServerSettings.ScheduledMessages.Add(new RconScheduledMessage());
        }

        private void RemoveScheduledMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: RconScheduledMessage message } && ServerCombo2.SelectedItem is Server server)
                server.RconServerSettings.ScheduledMessages.Remove(message);
        }

        private void RconMessagesScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer innerScrollViewer)
                return;

            bool canScrollInner = e.Delta < 0
                ? innerScrollViewer.VerticalOffset < innerScrollViewer.ScrollableHeight
                : innerScrollViewer.VerticalOffset > 0;
            if (canScrollInner)
                return;

            DependencyObject? parent = VisualTreeHelper.GetParent(innerScrollViewer);
            while (parent != null && parent is not ScrollViewer)
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ScrollViewer outerScrollViewer)
            {
                double wheelNotches = e.Delta / 120.0;
                outerScrollViewer.ScrollToVerticalOffset(outerScrollViewer.VerticalOffset - wheelNotches * 48);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 重置选中服务器的Webhook消息设置
        /// </summary>
        private void ResetServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ServerCombo1.SelectedIndex < 0 || ServerCombo1.SelectedIndex >= _mainSettings.Servers.Count)
            {
                ShowLogMsg("重置Webhook设置失败：未选择有效服务器", Brushes.Red);

                _ = new ContentDialog
                {
                    Title = "操作失败",
                    Content = "请先选择一个有效的服务器",
                    PrimaryButtonText = "确定",
                    Owner = this
                }.ShowAsync();
                return;
            }

            var server = _mainSettings.Servers[ServerCombo1.SelectedIndex];
            server.WebhookMessages = new ServerWebhook();
            ShowLogMsg($"已重置服务器 {server.ssmServerName} 的Webhook设置", Brushes.LimeGreen);
        }

        /// <summary>
        /// 重置全局Webhook设置
        /// </summary>
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var defaultWebhook = new Webhook();
            _mainSettings.WebhookSettings.UpdateFound = defaultWebhook.UpdateFound;
            _mainSettings.WebhookSettings.UpdateWait = defaultWebhook.UpdateWait;
            ShowLogMsg("已重置全局Webhook设置", Brushes.LimeGreen);
        }
    }

    /// <summary>
    /// 服务器ID映射（时间戳ID <-> 服务器名称）
    /// 改名也不会丢失关联
    /// </summary>
    public static class ServerIdMapping
    {
        private static readonly string _savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SSMSettings.json");
        private static MainSettings SsmSettings = new();
        private static ServerSettings ServerSettings = new();

        private static JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void Load()
        {
            try
            {
                if (File.Exists(_savePath))
                {
                    string json = File.ReadAllText(_savePath);
                    SsmSettings = MainSettings.LoadManagerSettings();
                }
            }
            catch
            {

            }
        }

        public static string NewId() => DateTime.Now.Ticks.ToString();

        public static void EnsureServerHasId(Server server)
        {
            if (server == null) 
                return;

            string serverSettingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
            ServerSettings = ServerSettingsEditor.LoadServerSettings(serverSettingsPath);

            if (ServerSettings.SelfServerUniqueId == "" || ServerSettings.SelfServerUniqueId != server.UniqueId)
            {
                string brandNewId = NewId();
                server.UniqueId = brandNewId;
                ServerSettings.SelfServerUniqueId = brandNewId;
            }
            ServerSettingsEditor.SaveServerSettings(server, ServerSettings);
            MainSettings.Save(SsmSettings);
        }

        public static void EnsureAllServersHaveIds()
        {
            SsmSettings = MainSettings.LoadManagerSettings();
            if (SsmSettings.Servers.Count > 0)
            {
                foreach (var s in SsmSettings.Servers)
                {
                    EnsureServerHasId(s);
                }
            }
        }
    }
}
