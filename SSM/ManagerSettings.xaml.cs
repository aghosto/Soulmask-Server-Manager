using ModernWpf.Controls;
using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SoulmaskServerManager
{
    /// <summary>
    /// 管理器设置窗口
    /// </summary>
    public partial class ManagerSettings : Window
    {
        private readonly MainSettings _mainSettings;

        private MainWindow _mainWindow;


        public ManagerSettings(MainSettings mainSettings)
        {
            _mainSettings = mainSettings ?? throw new ArgumentNullException(nameof(mainSettings), "主设置数据不能为null");
            InitializeComponent();

            DataContext = _mainSettings;
            _mainWindow = Application.Current.MainWindow as MainWindow;
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
        /// 保存设置并关闭窗口
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MainSettings.Save(_mainSettings);
                _mainWindow.InitAutoRestartTimer();
                Close();
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