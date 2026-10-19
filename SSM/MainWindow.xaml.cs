using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using ModernWpf;
using ModernWpf.Controls;
using Newtonsoft.Json;
using SoulmaskServerManager;
using SoulmaskServerManager.Controls;
using SoulmaskServerManager.RCON;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static SoulmaskServerManager.Log;

namespace SoulmaskServerManager;

/// <summary>
/// 主窗口，负责服务器管理、日志显示以及应用级操作。
/// </summary>
public partial class MainWindow : Window
{
    #region FieldsAndInitialization

    public MainSettings SsmSettings = new();
    private static dWebhook DiscordSender = new();
    private static HttpClient HttpClient = new();
    private PeriodicTimer? AutoUpdateTimer;
    private RemoteConClient RCONClient = new();
    private DispatcherTimer _autoRestartTimer;
    private DispatcherTimer? _scheduledRconMessageTimer;
    private readonly Dictionary<string, (int IntervalMinutes, DateTime NextSendAt)> _scheduledRconMessages = new();
    private bool _isSendingScheduledRconMessage;
    private readonly Dictionary<string, ScheduledRestartState> _scheduledRestartStates = new();
    private bool _isProcessingScheduledRestart;
    private DispatcherTimer _playerRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };

    private string _activeLogType;
    private const int MAX_LOG_LINES = 500;
    private DispatcherTimer? _logUpdateTimer;
    private Dictionary<string, LogType> _logTagToType;
    private Dictionary<LogType, RichTextBox> _logTypeToTexbox;
    private Dictionary<LogType, CheckBox> _logTypeToCheckbox;
    private readonly Dictionary<string, LogFileReadState> _logFileStates = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _activeLogWatcher;
    private string? _watchedLogPath;
    private string? _displayedLogPath;
    private bool _isLoadingLog;
    private bool _pendingLogRefresh;
    private bool _pendingForceLogReload;
    private long _logRequestVersion;

    private readonly SemaphoreSlim _serverUpdateSemaphore = new(1, 1);
    private Server? _serverBeingUpdated;
    private CancellationTokenSource? _serverUpdateCancellation;
    private static readonly Regex SteamCmdProgressRegex = new(
        @"progress:\s*(?<percentage>\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SteamCmdAppStateErrorRegex = new(
        @"Error!\s*(?:App\s+'(?<appId>[^']+)'\s+state\s+is|State\s+is)\s+(?<state>0x[0-9A-F]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 当前选中的服务器
    private Server _currentServer;
    private SSMPathManager _ssmPathManager;
    private ObservableCollection<PlayerInfo> _players = new();
    private ObservableCollection<PlayerInfo> _bannedPlayers = new();

    /// <summary>
    /// 创建主窗口并初始化应用设置、界面事件和后台任务。
    /// </summary>
    public MainWindow()
    {
        Process currentProcess = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);

        if (processes.Length > 1)
        {
            foreach (Process process in processes)
            {
                if (process.Id != currentProcess.Id)
                {
                    ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);

                    Environment.Exit(0);
                    return;
                }
            }
        }

        if (!File.Exists(Directory.GetCurrentDirectory() + @"\SSMSettings.json"))
            MainSettings.Save(SsmSettings);
        else
        {
            SsmSettings = MainSettings.LoadManagerSettings();
            ServerIdMapping.EnsureAllServersHaveIds();
        }
        if (SSMPathManager.MigrateLegacyServerDirectories(SsmSettings.Servers, Directory.GetCurrentDirectory()))
            MainSettings.Save(SsmSettings);
        MigrateScheduledSettings();
        DataContext = SsmSettings;

        if (!Directory.Exists(BackgroundManagerWindow.BackgroundsDir))
            Directory.CreateDirectory(BackgroundManagerWindow.BackgroundsDir);

        if (SsmSettings.AppSettings.DarkMode == true)
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        else
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;

        InitializeComponent();
        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;

        _logTypeToTexbox = new Dictionary<LogType, RichTextBox>
        {
            { LogType.WSServer, SoulmaskLogTextBox },
            { LogType.MainConsole, MainMenuConsoleTextBox },
        };

        _logTypeToCheckbox = new Dictionary<LogType, CheckBox>
        {
            { LogType.WSServer, AutoScrollSoulmaskLog },
            { LogType.MainConsole, AutoScrollMainConsole },
        };
        
        _logTagToType = new Dictionary<string, LogType>
        {
            { "WSServer", LogType.WSServer },
            { "PlayerData", LogType.PlayerData },
            { "MainConsole", LogType.MainConsole },
        };

        // 绑定自动滚动复选框事件
        AutoScrollSoulmaskLog.Checked += AutoScrollCheckBox_CheckedChanged;
        AutoScrollSoulmaskLog.Unchecked += AutoScrollCheckBox_CheckedChanged;
        SsmSettings.Servers.CollectionChanged += Servers_CollectionChanged;
        SsmSettings.AppSettings.PropertyChanged += AppSettings_PropertyChanged;

        ServerTabControl.SelectionChanged += async (s, e) =>
        {
            if (SsmSettings.Servers.Count == 0)
                return;
            if (ServerTabControl.SelectedItem is Server selectedServer)
            {
                UpdateServerConnectionPort(selectedServer);
                StopActiveLogMonitoring();
                _logRequestVersion++;
                _currentServer = selectedServer;
                _ssmPathManager = new (Directory.GetCurrentDirectory(), _currentServer);
                UpdateServerUpdateButtonState(selectedServer);
                if (!string.IsNullOrEmpty(_activeLogType))
                {
                    if (_activeLogType == "PlayerData")
                    {
                        StopActiveLogMonitoring();
                        LoadBannedPlayersFromFile();
                        await RefreshPlayersAsync();
                        return;
                    }
                    else
                    {
                        ConfigureActiveLogMonitoring();
                        await LoadLogByTypeAsync(_logTagToType[_activeLogType], forceReload: true);
                    }
                }
            }
        };

        //SsmSettings.AppSettings.Version = new AppSettings().Version;

        ShowLogDefault($"灵魂面甲服务端管理器(SSM)启动成功。");
        ShowLogMsg(((SsmSettings.Servers.Count > 0) ? 
            $"{SsmSettings.Servers.Count} 个服务器从设置中加载成功。" : $"未找到服务器，请点击“添加服务器”以开始使用。"), 
            SsmSettings.Servers.Count > 0 ? Brushes.Lime : Brushes.Yellow);

        SetupServerAutoUpdateTimer();
        InitAutoRestartTimer();
        InitScheduledRconMessagesTimer();
        InitPlayerRefreshTimer();

        if (SsmSettings.AppSettings.AutoUpdateApp == true)
            LookForAppUpdate();
    }

    #endregion FieldsAndInitialization

    #region WindowLifecycle

    /// <summary>
    /// 初始化主窗口、恢复运行中的服务器并启动日志监控。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ScheduleUpdateTempCleanupFromArguments();

        if (ServerTabControl.SelectedItem is Server selectedServer)
            UpdateServerConnectionPort(selectedServer);

        UpdateWallpaper();
        await RestoreRunningServers();
        RefreshActiveLogMonitoringForStateChange();
    }

    /// <summary>
    /// 在更新器退出后清理其临时更新目录，避免删除仍被使用的文件。
    /// </summary>
    private static void ScheduleUpdateTempCleanupFromArguments()
    {
        string[] args = Environment.GetCommandLineArgs();
        int directoryIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--cleanup-update-dir", StringComparison.OrdinalIgnoreCase));
        int processIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--wait-for-pid", StringComparison.OrdinalIgnoreCase));
        if (directoryIndex < 0 || directoryIndex + 1 >= args.Length ||
            processIndex < 0 || processIndex + 1 >= args.Length ||
            !int.TryParse(args[processIndex + 1], out int updaterProcessId) || updaterProcessId <= 0)
        {
            return;
        }

        string cleanupDirectory;
        try
        {
            cleanupDirectory = Path.GetFullPath(args[directoryIndex + 1]);
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!cleanupDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(cleanupDirectory).StartsWith("SoulmaskServerManagerUpdate-", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            DateTime timeout = DateTime.UtcNow.AddMinutes(2);
            while (IsProcessRunning(updaterProcessId) && DateTime.UtcNow < timeout)
                await Task.Delay(250);

            if (!IsProcessRunning(updaterProcessId) && Directory.Exists(cleanupDirectory))
            {
                try
                {
                    Directory.Delete(cleanupDirectory, recursive: true);
                }
                catch
                {
                    // 更新器临时文件清理失败不影响管理器运行。
                }
            }
        });
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void UpdateServerConnectionPort(Server server)
    {
        string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
        try
        {
            if (File.Exists(settingsPath))
            {
                ServerSettings? settings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(settingsPath));
                server.ConnectionPort = settings?.Port ?? 8777;
            }
            else
            {
                server.ConnectionPort = 8777;
            }
        }
        catch
        {
            server.ConnectionPort = 8777;
        }
    }

    /// <summary>
    /// 根据应用关闭设置隐藏主窗口或结束托盘状态。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        MainSettings mainSettings = MainSettings.LoadManagerSettings();

        switch (mainSettings.AppSettings.CloseExecuteSelect)
        {
            case 0:
                TrayIcon.Visibility = Visibility.Collapsed;
                TrayIcon.Dispose();
                break;

            case 1:
                e.Cancel = true;
                MinimizeToTray();
                break;
        }
    }

    /// <summary>
    /// 根据主窗口当前状态显示或隐藏窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void TrayIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
            Hide();
        else
        {
            Show();
            Activate();
        }
    }

    #region MinimizeAndClose

    /// <summary>
    /// 隐藏主窗口并将程序保留在系统托盘中。
    /// </summary>
    private void MinimizeToTray()
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
        TrayIcon.ShowBalloonTip("已最小化", "程序在托盘运行中", BalloonIcon.Info);
    }

    /// <summary>
    /// 从系统托盘恢复并激活主窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void TrayIcon_ShowWindow(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        TrayIcon.Visibility = Visibility.Visible;
        Activate();
    }

    /// <summary>
    /// 退出系统托盘图标并关闭应用程序。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void TrayIcon_Exit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Visibility = Visibility.Collapsed;
        TrayIcon.Dispose();

        Closing -= MainWindow_Closing;
        Close();
    }

    /// <summary>
    /// 释放日志监控、玩家刷新计时器和托盘资源。
    /// </summary>
    /// <param name="e">事件参数。</param>
    protected override void OnClosed(EventArgs e)
    {
        StopActiveLogMonitoring();
        if (_playerRefreshTimer != null)
            _playerRefreshTimer.Stop();

        base.OnClosed(e);

        if (TrayIcon != null)
        {
            TrayIcon.Visibility = Visibility.Collapsed;
            TrayIcon.Dispose();
            TrayIcon = null;
        }
        Application.Current.Shutdown();
    }

    #endregion MinimizeAndClose

    #endregion WindowLifecycle

    #region LogTabNavigation

    /// <summary>
    /// 启用自动滚动时将当前日志视图滚动到末尾。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void AutoScrollCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && !string.IsNullOrEmpty(_activeLogType))
        {
            // 找到与当前复选框关联的日志类型
            LogType logType = new();
            foreach (var pair in _logTypeToCheckbox)
            {
                if (pair.Value == checkBox)
                {
                    logType = pair.Key;
                    break;
                }
            }

            if (logType == _logTagToType[_activeLogType] && checkBox.IsChecked == true)
            {
                _logTypeToTexbox[logType].ScrollToEnd();
            }
        }
    }

    /// <summary>
    /// 切换日志页时刷新服务器日志或玩家数据并更新监控。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void LogTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is not TabControl)
            return;

        if (_currentServer == null)
            return;

        if (LogTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is string logType)
        {
            _logRequestVersion++;
            StopActiveLogMonitoring();
            _activeLogType = logType;
            if (_activeLogType == "PlayerData")
            {
                if (File.Exists(_ssmPathManager.BanListPath))
                    LoadBannedPlayersFromFile();
                await RefreshPlayersAsync();
                return;
            }

            if (_logTagToType.TryGetValue(_activeLogType, out LogType selectedLogType))
            {
                ConfigureActiveLogMonitoring();
                await LoadLogByTypeAsync(selectedLogType, forceReload: true);
            }
        }
    }

    #endregion LogTabNavigation

    #region TimersAndScheduledTasks

    private sealed class ScheduledRestartState
    {
        public DateOnly Date { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public int Second { get; set; }
        public bool Sent10MinuteWarning { get; set; }
        public bool Sent5MinuteWarning { get; set; }
        public bool Sent1MinuteWarning { get; set; }
        public bool RestartTriggered { get; set; }
    }

    private void MigrateScheduledSettings()
    {
        bool changed = false;
        if (!SsmSettings.AppSettings.ServerAutoUpdateMigrated)
        {
            if (SsmSettings.AppSettings.AutoUpdate)
            {
                foreach (Server server in SsmSettings.Servers)
                    server.AutoUpdateEnabled = true;
            }
            SsmSettings.AppSettings.ServerAutoUpdateMigrated = true;
            changed = true;
        }

        if (!SsmSettings.AppSettings.ScheduledRconMessagesDefaultDisabledMigrated)
        {
            // 旧版本的默认值为启用，存入 JSON 后仅修改属性初始值无法覆盖旧值。
            foreach (Server server in SsmSettings.Servers)
                server.RconServerSettings.Enabled = false;

            SsmSettings.AppSettings.ScheduledRconMessagesDefaultDisabledMigrated = true;
            changed = true;
        }

        foreach (Server server in SsmSettings.Servers)
        {
            server.RconServerSettings.ScheduledMessages ??= new ObservableCollection<RconScheduledMessage>();
            if (server.RconServerSettings.ScheduledMessages.Count == 0 &&
                !string.IsNullOrWhiteSpace(server.RconServerSettings.AutoMessageText))
            {
                server.RconServerSettings.ScheduledMessages.Add(new RconScheduledMessage
                {
                    Text = server.RconServerSettings.AutoMessageText,
                    IntervalMinutes = Math.Clamp(server.RconServerSettings.AutoMessageIntervalMinutes, 1, 1440)
                });
                server.RconServerSettings.AutoMessageText = string.Empty;
                changed = true;
            }
        }

        if (!SsmSettings.AppSettings.ServerScheduledAutoRestartMigrated &&
            (!SsmSettings.AppSettings.EnableAutoRestart || SsmSettings.Servers.Count > 0))
        {
            if (SsmSettings.AppSettings.EnableAutoRestart)
            {
                foreach (Server server in SsmSettings.Servers)
                {
                    server.ScheduledAutoRestartEnabled = true;
                    server.ScheduledAutoRestartHour = Math.Clamp(SsmSettings.AppSettings.AutoRestartHour, 0, 23);
                    server.ScheduledAutoRestartMin = Math.Clamp(SsmSettings.AppSettings.AutoRestartMin, 0, 59);
                    server.ScheduledAutoRestartSec = Math.Clamp(SsmSettings.AppSettings.AutoRestartSec, 0, 59);
                }
            }
            SsmSettings.AppSettings.ServerScheduledAutoRestartMigrated = true;
            changed = true;
        }

        if (changed)
            MainSettings.Save(SsmSettings);
    }

    #region LogRefreshFallbackTimer

    /// <summary>
    /// 定期检查当前日志文件状态，作为文件监听的兜底。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void LogUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_currentServer == null || _activeLogType != "WSServer"
                || _currentServer.Runtime?.State != ServerRuntime.ServerState.运行中)
                return;

            string fullPath = Path.GetFullPath(_ssmPathManager.LogsPath);
            if (!File.Exists(fullPath))
            {
                if (_logFileStates.ContainsKey(fullPath))
                    RequestLogRefresh(forceReload: true);
                if (_activeLogWatcher == null)
                    ConfigureActiveLogMonitoring();
                return;
            }

            var info = new FileInfo(fullPath);
            bool changed = !_logFileStates.TryGetValue(fullPath, out LogFileReadState? state)
                || info.Length != state.FileLength
                || info.LastWriteTimeUtc != state.LastWriteTimeUtc;
            if (changed)
                RequestLogRefresh(forceReload: false);
        }
        catch (Exception ex)
        {
            ShowLogError($"定时器检查日志更新失败：{ex.Message}");
        }
    }

    #endregion LogRefreshFallbackTimer

    #region ApplicationAndServerTimers

    /// <summary>
    /// 初始化玩家数据页的定时刷新。
    /// </summary>
    public void InitPlayerRefreshTimer()
    {
        if (_playerRefreshTimer != null)
        {
            _playerRefreshTimer.Stop();
            _playerRefreshTimer.Tick -= AutoRestartTimer_Tick;
            _playerRefreshTimer = null;
        }

        _playerRefreshTimer = new DispatcherTimer();
        _playerRefreshTimer.Interval = TimeSpan.FromSeconds(30);
        _playerRefreshTimer.Tick += async (_, _) =>
        {
            if (LogTabControl.SelectedItem == PlayerTab && _currentServer.Runtime.State == ServerRuntime.ServerState.运行中 && AutoRefreshPlayerCheckBox.IsChecked == true)
            {
                await RefreshPlayersAsync();
            }
        };
        _playerRefreshTimer.Start();
    }

    /// <summary>
    /// 按服务器自动更新开关和全局间隔初始化自动更新计时器。
    /// </summary>
    public void SetupServerAutoUpdateTimer()
    {
        AutoUpdateTimer?.Dispose();
        AutoUpdateTimer = null;

        if (!SsmSettings.Servers.Any(server => server.AutoUpdateEnabled))
            return;

        int intervalMinutes = Math.Clamp(SsmSettings.AppSettings.AutoUpdateGameInterval, 1, 1440);
        PeriodicTimer timer = new(TimeSpan.FromMinutes(intervalMinutes));
        AutoUpdateTimer = timer;
        AutoUpdateLoop(timer);
    }

    private async void AutoUpdateLoop(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (!ReferenceEquals(AutoUpdateTimer, timer))
                    return;

                try
                {
                    bool foundUpdate = await CheckForGameUpdate();
                    if (foundUpdate && SsmSettings.Servers.Any(server => server.AutoUpdateEnabled))
                    {
                        ShowLogMsg("检测到服务器新版本，即将执行自动更新", Brushes.Orange);
                        await AutoUpdate();
                    }
                }
                catch (Exception ex)
                {
                    ShowLogWarning($"自动更新循环异常：{ex.Message}");
                }
        }
        }
        catch (ObjectDisposedException)
        {
            // 计时器在设置更新或窗口关闭时被替换。
        }
    }

    /// <summary>
    /// 初始化服务器自动重启计时器。
    /// </summary>
    public void InitAutoRestartTimer()
    {
        if (_autoRestartTimer != null)
        {
            _autoRestartTimer.Stop();
            _autoRestartTimer.Tick -= AutoRestartTimer_Tick;
            _autoRestartTimer = null;
        }

        var scheduledServers = SsmSettings.Servers.Where(server => server.ScheduledAutoRestartEnabled).ToList();
        if (scheduledServers.Count == 0)
        {
            _scheduledRestartStates.Clear();
            //ShowLogMsg("所有服务器的定时自动重启均已关闭", Brushes.Gray);
            return;
        }

        _autoRestartTimer = new DispatcherTimer();
        _autoRestartTimer.Interval = TimeSpan.FromSeconds(1);
        _autoRestartTimer.Tick += AutoRestartTimer_Tick;
        _autoRestartTimer.Start();

        //ShowLogMsg($"已启用 {scheduledServers.Count} 台服务器的定时自动重启", Brushes.LimeGreen);
    }

    /// <summary>
    /// 初始化各服务器的 RCON 定时消息调度器。
    /// </summary>
    public void InitScheduledRconMessagesTimer()
    {
        _scheduledRconMessageTimer?.Stop();
        if (_scheduledRconMessageTimer != null)
            _scheduledRconMessageTimer.Tick -= ScheduledRconMessageTimer_Tick;

        _scheduledRconMessages.Clear();
        if (!SsmSettings.Servers.Any(server => server.RconServerSettings.Enabled))
        {
            _scheduledRconMessageTimer = null;
            return;
        }

        _scheduledRconMessageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scheduledRconMessageTimer.Tick += ScheduledRconMessageTimer_Tick;
        _scheduledRconMessageTimer.Start();
    }

    /// <summary>
    /// 按服务器的消息间隔，通过 RCON 向运行中的服务器发送定时公告。
    /// </summary>
    private async void ScheduledRconMessageTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSendingScheduledRconMessage)
            return;

        _isSendingScheduledRconMessage = true;
        try
        {
            DateTime now = DateTime.Now;
            var activeServerKeys = new HashSet<string>();

            foreach (Server server in SsmSettings.Servers)
            {
                RCONServerSettings settings = server.RconServerSettings;
                string serverKey = string.IsNullOrWhiteSpace(server.UniqueId) ? server.Path : server.UniqueId;
                if (!settings.Enabled || server.Runtime.State != ServerRuntime.ServerState.运行中)
                    continue;

                foreach (RconScheduledMessage scheduledMessage in settings.ScheduledMessages ?? new ObservableCollection<RconScheduledMessage>())
                {
                    string message = scheduledMessage.Text?.Trim() ?? string.Empty;
                    int intervalMinutes = scheduledMessage.IntervalMinutes;
                    string key = $"{serverKey}:{scheduledMessage.Id}";
                    if (string.IsNullOrWhiteSpace(message) || intervalMinutes < 1)
                        continue;

                    activeServerKeys.Add(key);
                    if (!_scheduledRconMessages.TryGetValue(key, out var schedule) || schedule.IntervalMinutes != intervalMinutes)
                    {
                        _scheduledRconMessages[key] = (intervalMinutes, now.AddMinutes(intervalMinutes));
                        continue;
                    }

                    if (now < schedule.NextSendAt)
                        continue;

                    _scheduledRconMessages[key] = (intervalMinutes, now.AddMinutes(intervalMinutes));
                    await RCONClient.SendScheduledRconMessageAsync(server, message);
                    //if (!sent)
                    //    ShowLogWarning($"[{server.ssmServerName}] RCON 定时消息发送失败，请检查服务器状态和 RCON 连接配置。");
                }
            }

            foreach (string key in _scheduledRconMessages.Keys.Where(key => !activeServerKeys.Contains(key)).ToList())
                _scheduledRconMessages.Remove(key);
        }
        catch (Exception ex)
        {
            ShowLogWarning($"RCON 定时消息处理失败：{ex.Message}");
        }
        finally
        {
            _isSendingScheduledRconMessage = false;
        }
    }

    /// <summary>
    /// 检查重启计划并重启符合条件的服务器。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void AutoRestartTimer_Tick(object sender, EventArgs e)
    {
        if (_isProcessingScheduledRestart)
            return;

        _isProcessingScheduledRestart = true;
        try
        {
            var now = DateTime.Now;
            foreach (Server server in SsmSettings.Servers.Where(server => server.ScheduledAutoRestartEnabled))
            {
                string key = string.IsNullOrWhiteSpace(server.UniqueId) ? server.Path : server.UniqueId;
                int targetHour = Math.Clamp(server.ScheduledAutoRestartHour, 0, 23);
                int targetMinute = Math.Clamp(server.ScheduledAutoRestartMin, 0, 59);
                int targetSecond = Math.Clamp(server.ScheduledAutoRestartSec, 0, 59);
                if (!_scheduledRestartStates.TryGetValue(key, out ScheduledRestartState? state) ||
                    state.Date != DateOnly.FromDateTime(now) || state.Hour != targetHour ||
                    state.Minute != targetMinute || state.Second != targetSecond)
                {
                    _scheduledRestartStates[key] = state = new ScheduledRestartState
                    {
                        Date = DateOnly.FromDateTime(now),
                        Hour = targetHour,
                        Minute = targetMinute,
                        Second = targetSecond
                    };
                }

                if (server.Runtime.State != ServerRuntime.ServerState.运行中 || state.RestartTriggered)
                    continue;

                DateTime targetTime = new(now.Year, now.Month, now.Day, targetHour, targetMinute, targetSecond);
                TimeSpan left = targetTime - now;

                if (left.TotalMinutes <= 10 && left.TotalMinutes > 5 && !state.Sent10MinuteWarning)
                {
                    state.Sent10MinuteWarning = true;
                    await RCONClient.SendRconMessageAsync(server, "【服务器通知】服务器将在 10 分钟后自动重启，请尽快安全下线！");
                }
                if (left.TotalMinutes <= 5 && left.TotalMinutes > 1 && !state.Sent5MinuteWarning)
                {
                    state.Sent5MinuteWarning = true;
                    await RCONClient.SendRconMessageAsync(server, "【服务器通知】服务器将在 5 分钟后自动重启！");
                }
                if (left.TotalMinutes <= 1 && left.TotalSeconds > 10 && !state.Sent1MinuteWarning)
                {
                    state.Sent1MinuteWarning = true;
                    await RCONClient.SendRconMessageAsync(server, "【服务器通知】服务器将在 1 分钟后立即重启，请立刻下线！");
                }
                if (now >= targetTime && now < targetTime.AddMinutes(1))
                {
                    state.RestartTriggered = true;
                    await AutoRestartServerAsync(server);
                }
            }
        }
        catch (Exception ex) { ShowLogWarning($"定时自动重启处理失败：{ex.Message}"); }
        finally { _isProcessingScheduledRestart = false; }
    }

    /// <summary>
    /// 启动服务器存档备份清理计时器。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <param name="deleteInterval">备份清理计时器间隔（分钟）。</param>
    /// <param name="backupAmount">要保留的备份数量。</param>
    public static void StartBackupCleanTimer(Server server, int deleteInterval, int backupAmount)
    {
        try
        {
            if (server.Runtime.BackupCleanTimer != null)
            {
                server.Runtime.BackupCleanTimer.Stop();
                server.Runtime.BackupCleanTimer.Dispose();
            }

            double checkMinutes = Convert.ToDouble(deleteInterval);
            int keepCount = Convert.ToInt32(backupAmount);

            if (checkMinutes <= 0)
                checkMinutes = 10;
            if (keepCount <= 0)
                keepCount = 5;

            server.Runtime.BackupCleanTimer = new System.Timers.Timer();
            server.Runtime.BackupCleanTimer.Interval = checkMinutes * 60 * 1000;
            server.Runtime.BackupCleanTimer.AutoReset = true;

            server.Runtime.BackupCleanTimer.Elapsed += (s, ev) => CleanOldBackups(server, keepCount);
            server.Runtime.BackupCleanTimer.Start();
        }
        catch
        {

        }
    }

    /// <summary>
    /// 停止并释放服务器存档备份清理计时器。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    private void StopBackupCleanTimer(Server server)
    {
        if (server.Runtime.BackupCleanTimer != null)
        {
            server.Runtime.BackupCleanTimer.Stop();
            server.Runtime.BackupCleanTimer.Dispose();
            server.Runtime.BackupCleanTimer = null;
        }
    }

    #endregion ApplicationAndServerTimers

    #endregion TimersAndScheduledTasks

    #region ServerLogLoading

    /// <summary>
    /// 异步读取当前服务器日志并更新日志视图。
    /// </summary>
    /// <param name="logType">日志视图类型。</param>
    /// <param name="forceReload">是否强制重新读取完整日志快照。</param>
    /// <returns>表示异步操作。</returns>
    private async Task LoadLogByTypeAsync(LogType logType, bool forceReload = false)
    {
        if (logType == LogType.MainConsole)
        {
            if (_logTypeToCheckbox[logType].IsChecked == true)
                _logTypeToTexbox[logType].ScrollToEnd();
            return;
        }

        if (logType != LogType.WSServer || _currentServer == null || _activeLogType != "WSServer")
            return;

        _logRequestVersion++;
        if (_isLoadingLog)
        {
            _pendingLogRefresh = true;
            _pendingForceLogReload |= forceReload;
            return;
        }

        _isLoadingLog = true;
        bool reload = forceReload;
        try
        {
            do
            {
                _pendingLogRefresh = false;
                _pendingForceLogReload = false;
                Server server = _currentServer;
                string fullPath = Path.GetFullPath(_ssmPathManager.LogsPath);
                long requestVersion = _logRequestVersion;
                bool pathChanged = !string.Equals(_displayedLogPath, fullPath, StringComparison.OrdinalIgnoreCase);

                if (pathChanged)
                {
                    _displayedLogPath = fullPath;
                    _logTypeToTexbox[LogType.WSServer].Document.Blocks.Clear();
                    SetServerLogStatus("正在读取服务器日志…");
                    reload = true;
                }

                _logFileStates.TryGetValue(fullPath, out LogFileReadState? previousState);
                LogReadResult result = await Task.Run(() => ReadLogFile(fullPath, previousState, reload));

                bool isStillCurrent = requestVersion == _logRequestVersion
                    && ReferenceEquals(server, _currentServer)
                    && _activeLogType == "WSServer"
                    && string.Equals(fullPath, Path.GetFullPath(_ssmPathManager.LogsPath), StringComparison.OrdinalIgnoreCase);

                if (isStillCurrent)
                {
                    if (result.Error != null)
                    {
                        _logTypeToTexbox[LogType.WSServer].Document.Blocks.Clear();
                        SetServerLogStatus(result.Error);
                        if (result.State == null)
                            _logFileStates.Remove(fullPath);
                    }
                    else if (result.State != null)
                    {
                        _logFileStates[fullPath] = result.State;
                        if (result.Reloaded || pathChanged)
                        {
                            RichTextBox logBox = _logTypeToTexbox[LogType.WSServer];
                            logBox.Document.Blocks.Clear();
                            foreach (string line in result.State.Lines)
                                AppendLogLine(LogType.WSServer, line);
                        }
                        else
                        {
                            foreach (string line in result.NewLines)
                                AppendLogLine(LogType.WSServer, line);
                            TrimDisplayedLogLines(_logTypeToTexbox[LogType.WSServer]);
                        }

                        SetServerLogStatus(null);
                        if (_logTypeToCheckbox[LogType.WSServer].IsChecked == true)
                            _logTypeToTexbox[LogType.WSServer].ScrollToEnd();
                    }
                }

                reload = _pendingForceLogReload;
            }
            while (_pendingLogRefresh && _activeLogType == "WSServer" && _currentServer != null);
        }
        catch (Exception ex)
        {
            if (_activeLogType == "WSServer")
                SetServerLogStatus($"读取服务器日志失败：{ex.Message}");
        }
        finally
        {
            _isLoadingLog = false;
        }
    }

    /// <summary>
    /// 请求刷新服务器日志，并合并读取期间产生的重复请求。
    /// </summary>
    /// <param name="forceReload">是否强制重新读取完整日志快照。</param>
    private void RequestLogRefresh(bool forceReload)
    {
        if (_activeLogType != "WSServer" || _currentServer == null)
            return;
        _ = LoadLogByTypeAsync(LogType.WSServer, forceReload);
    }

    /// <summary>
    /// 读取日志快照或上次读取位置之后新增的完整行。
    /// </summary>
    /// <param name="filePath">要读取或检查的文件路径。</param>
    /// <param name="previousState">上次读取该文件时保存的状态。</param>
    /// <param name="forceReload">是否强制重新读取完整日志快照。</param>
    /// <returns>包含日志状态、新行或错误信息的读取结果。</returns>
    private LogReadResult ReadLogFile(string filePath, LogFileReadState? previousState, bool forceReload)
    {
        if (!File.Exists(filePath))
            return new LogReadResult(null, Array.Empty<string>(), false, $"日志文件不存在：{filePath}\n请确保服务器至少成功启动过一次。");

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long fileLength = stream.Length;
            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
            DateTime creationTimeUtc = File.GetCreationTimeUtc(filePath);
            bool fileWasReplaced = previousState != null
                && previousState.CreationTimeUtc != creationTimeUtc;
            bool sameSizeRewrite = previousState != null
                && fileLength == previousState.FileLength
                && lastWriteTimeUtc != previousState.LastWriteTimeUtc;
            bool checkpointChanged = previousState != null
                && !MatchesLogCheckpoint(stream, previousState.ReadPosition, previousState.Checkpoint);
            bool shouldReload = forceReload || previousState == null || fileWasReplaced
                || sameSizeRewrite || checkpointChanged || fileLength < previousState.FileLength;

            if (shouldReload)
            {
                var snapshot = ReadLogSnapshot(stream, fileLength, MAX_LOG_LINES);
                if (!IsSameLogFileSnapshot(filePath, fileLength, lastWriteTimeUtc, creationTimeUtc))
                    throw new IOException("日志文件在读取期间被修改或替换，请稍后重试。");
                var state = new LogFileReadState(fileLength, lastWriteTimeUtc, creationTimeUtc,
                    snapshot.ReadPosition, snapshot.PendingBytes, snapshot.Lines.ToList(), snapshot.FirstLinePending,
                    ReadLogCheckpoint(stream, snapshot.ReadPosition));
                return new LogReadResult(state, Array.Empty<string>(), true, null);
            }

            var nextState = previousState!.Clone();
            stream.Position = previousState.ReadPosition;
            long snapshotLength = stream.Length;
            if (snapshotLength < previousState.FileLength)
            {
                var snapshot = ReadLogSnapshot(stream, snapshotLength, MAX_LOG_LINES);
                DateTime resetLastWriteTime = File.GetLastWriteTimeUtc(filePath);
                DateTime resetCreationTime = File.GetCreationTimeUtc(filePath);
                if (!IsSameLogFileSnapshot(filePath, snapshotLength, resetLastWriteTime, resetCreationTime))
                    throw new IOException("日志文件在读取期间被修改或替换，请稍后重试。");
                var resetState = new LogFileReadState(snapshotLength, File.GetLastWriteTimeUtc(filePath),
                    File.GetCreationTimeUtc(filePath), snapshot.ReadPosition, snapshot.PendingBytes,
                    snapshot.Lines.ToList(), snapshot.FirstLinePending, ReadLogCheckpoint(stream, snapshot.ReadPosition));
                return new LogReadResult(resetState, Array.Empty<string>(), true, null);
            }
            byte[] appendedBytes = ReadRemainingBytes(stream, snapshotLength - stream.Position);
            byte[] combined = new byte[previousState.PendingBytes.Length + appendedBytes.Length];
            Buffer.BlockCopy(previousState.PendingBytes, 0, combined, 0, previousState.PendingBytes.Length);
            Buffer.BlockCopy(appendedBytes, 0, combined, previousState.PendingBytes.Length, appendedBytes.Length);

            var newLines = new List<string>();
            int lineStart = 0;
            for (int i = 0; i < combined.Length; i++)
            {
                if (combined[i] != (byte)'\n')
                    continue;

                int lineLength = i - lineStart;
                if (lineLength > 0 && combined[i - 1] == (byte)'\r')
                    lineLength--;
                newLines.Add(DecodeLogLine(combined, lineStart, lineLength, nextState.IsFirstLinePending));
                nextState.IsFirstLinePending = false;
                lineStart = i + 1;
            }

            nextState.PendingBytes = combined.Skip(lineStart).ToArray();
            nextState.ReadPosition = snapshotLength;
            nextState.FileLength = snapshotLength;
            nextState.LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath);
            nextState.CreationTimeUtc = File.GetCreationTimeUtc(filePath);
            nextState.Lines.AddRange(newLines);
            if (nextState.Lines.Count > MAX_LOG_LINES)
                nextState.Lines.RemoveRange(0, nextState.Lines.Count - MAX_LOG_LINES);

            DateTime appendedLastWriteTime = File.GetLastWriteTimeUtc(filePath);
            DateTime appendedCreationTime = File.GetCreationTimeUtc(filePath);
            if (!IsSameLogFileSnapshot(filePath, snapshotLength, appendedLastWriteTime, appendedCreationTime))
                throw new IOException("日志文件在读取期间被修改或替换，请稍后重试。");
            nextState.LastWriteTimeUtc = appendedLastWriteTime;
            nextState.CreationTimeUtc = appendedCreationTime;
            nextState.Checkpoint = ReadLogCheckpoint(stream, snapshotLength);

            return new LogReadResult(nextState, newLines.ToArray(), false, null);
        }
        catch (Exception ex)
        {
            return new LogReadResult(null, Array.Empty<string>(), false, $"读取服务器日志失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 判断读取前后的日志文件是否保持相同状态。
    /// </summary>
    /// <param name="filePath">要读取或检查的文件路径。</param>
    /// <param name="expectedLength">预期的文件长度。</param>
    /// <param name="expectedLastWriteTimeUtc">预期的 UTC 修改时间。</param>
    /// <param name="expectedCreationTimeUtc">预期的 UTC 创建时间。</param>
    /// <returns>操作是否成功。</returns>
    private static bool IsSameLogFileSnapshot(string filePath, long expectedLength,
        DateTime expectedLastWriteTimeUtc, DateTime expectedCreationTimeUtc)
    {
        var info = new FileInfo(filePath);
        return info.Exists
            && info.Length == expectedLength
            && info.LastWriteTimeUtc == expectedLastWriteTimeUtc
            && info.CreationTimeUtc == expectedCreationTimeUtc;
    }

    /// <summary>
    /// 读取日志指定位置附近的校验字节。
    /// </summary>
    /// <param name="stream">要操作的文件流。</param>
    /// <param name="position">文件中的字节位置。</param>
    /// <returns>读取到的字节数据。</returns>
    private static byte[] ReadLogCheckpoint(FileStream stream, long position)
    {
        const int checkpointLength = 64;
        int length = (int)Math.Min(checkpointLength, position);
        if (length == 0)
            return Array.Empty<byte>();

        byte[] checkpoint = new byte[length];
        stream.Position = position - length;
        int read = 0;
        while (read < length)
        {
            int count = stream.Read(checkpoint, read, length - read);
            if (count == 0)
                break;
            read += count;
        }
        return read == length ? checkpoint : checkpoint.Take(read).ToArray();
    }

    /// <summary>
    /// 比较当前日志校验字节与此前保存的校验值。
    /// </summary>
    /// <param name="stream">要操作的文件流。</param>
    /// <param name="position">文件中的字节位置。</param>
    /// <param name="checkpoint">用于比较的日志校验字节。</param>
    /// <returns>操作是否成功。</returns>
    private static bool MatchesLogCheckpoint(FileStream stream, long position, byte[] checkpoint)
    {
        if (checkpoint.Length == 0)
            return true;
        if (position < checkpoint.Length || stream.Length < position)
            return false;

        byte[] current = ReadLogCheckpoint(stream, position);
        return checkpoint.AsSpan().SequenceEqual(current);
    }

    /// <summary>
    /// 从日志文件快照中读取最近指定数量的完整日志行。
    /// </summary>
    /// <param name="stream">要读取的日志文件流。</param>
    /// <param name="snapshotLength">本次读取快照的字节长度。</param>
    /// <param name="lineCount">要保留的最近日志行数。</param>
    /// <returns>包含日志行、读取位置、未完成行字节和首行状态的结果。</returns>
    private static (string[] Lines, long ReadPosition, byte[] PendingBytes, bool FirstLinePending) ReadLogSnapshot(
        FileStream stream, long snapshotLength, int lineCount)
    {
        var lines = new Queue<string>(lineCount);
        var currentLine = new List<byte>();
        var chunk = new byte[64 * 1024];
        long startPosition = FindSnapshotStartPosition(stream, snapshotLength, lineCount);
        long remaining = snapshotLength - startPosition;
        long bytesReadTotal = 0;
        long readPosition = startPosition;
        stream.Position = startPosition;

        while (remaining > 0)
        {
            int read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, remaining));
            if (read == 0)
                break;
            bytesReadTotal += read;
            remaining -= read;

            for (int i = 0; i < read; i++)
            {
                byte value = chunk[i];
                if (value != (byte)'\n')
                {
                    currentLine.Add(value);
                    continue;
                }

                int lineLength = currentLine.Count;
                if (lineLength > 0 && currentLine[lineLength - 1] == (byte)'\r')
                    lineLength--;
                string line = DecodeLogLine(currentLine.ToArray(), 0, lineLength, lines.Count == 0 && readPosition == 0);
                if (lines.Count == lineCount)
                    lines.Dequeue();
                lines.Enqueue(line);
                currentLine.Clear();
                readPosition = startPosition + bytesReadTotal - (read - i - 1);
            }
        }

        if (bytesReadTotal != snapshotLength - startPosition)
            throw new IOException("读取期间日志文件长度发生变化，请稍后重试。");
        return (lines.ToArray(), snapshotLength, currentLine.ToArray(), startPosition == 0 && lines.Count == 0);
    }

    /// <summary>
    /// 从日志末尾定位最近指定行数的起始位置。
    /// </summary>
    /// <param name="stream">要操作的文件流。</param>
    /// <param name="snapshotLength">本次日志快照长度。</param>
    /// <param name="lineCount">要保留或读取的最大行数。</param>
    /// <returns>最近指定行数对应的起始字节位置。</returns>
    private static long FindSnapshotStartPosition(FileStream stream, long snapshotLength, int lineCount)
    {
        const int chunkSize = 8192;
        var buffer = new byte[chunkSize];
        long position = snapshotLength;
        int newlineCount = 0;

        while (position > 0)
        {
            int count = (int)Math.Min(chunkSize, position);
            position -= count;
            stream.Position = position;
            int bytesRead = stream.Read(buffer, 0, count);
            for (int i = bytesRead - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n' && ++newlineCount > lineCount)
                    return position + i + 1;
            }
        }
        return 0;
    }

    /// <summary>
    /// 将指定字节范围解码为一行日志文本。
    /// </summary>
    /// <param name="bytes">日志字节数组。</param>
    /// <param name="offset">字节数组中的起始偏移量。</param>
    /// <param name="count">要处理的字节数。</param>
    /// <param name="isFirstLine">当前文本是否为文件第一行。</param>
    /// <returns>生成的文本结果。</returns>
    private static string DecodeLogLine(byte[] bytes, int offset, int count, bool isFirstLine)
    {
        if (isFirstLine && count >= 3 && bytes[offset] == 0xEF && bytes[offset + 1] == 0xBB && bytes[offset + 2] == 0xBF)
        {
            offset += 3;
            count -= 3;
        }
        return Encoding.UTF8.GetString(bytes, offset, count);
    }

    /// <summary>
    /// 从文件流当前位置读取指定字节数。
    /// </summary>
    /// <param name="stream">要操作的文件流。</param>
    /// <param name="byteCount">要读取的字节数。</param>
    /// <returns>读取到的字节数据。</returns>
    private static byte[] ReadRemainingBytes(FileStream stream, long byteCount)
    {
        if (byteCount <= 0)
            return Array.Empty<byte>();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        long remaining = byteCount;
        while (remaining > 0)
        {
            int read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, remaining));
            if (read == 0)
                break;
            buffer.Write(chunk, 0, read);
            remaining -= read;
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 删除日志视图中超过保留上限的旧行。
    /// </summary>
    /// <param name="logBox">要裁剪行数的日志文本框。</param>
    private void TrimDisplayedLogLines(RichTextBox logBox)
    {
        while (logBox.Document.Blocks.Count > MAX_LOG_LINES)
            logBox.Document.Blocks.Remove(logBox.Document.Blocks.FirstBlock);
    }

    /// <summary>
    /// 设置服务器日志状态提示的内容和可见性。
    /// </summary>
    /// <param name="message">要显示或发送的消息内容。</param>
    private void SetServerLogStatus(string? message)
    {
        ServerLogStatusTextBlock.Text = message ?? string.Empty;
        ServerLogStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    #endregion ServerLogLoading

    #region RunningServerRestore

    /// <summary>
    /// 关联已经运行的服务器进程并启动配置要求的服务器。
    /// </summary>
    /// <returns>表示异步操作。</returns>
    private async Task RestoreRunningServers()
    {
        foreach (var server in SsmSettings.Servers)
        {
            try
            {
                string windowTitle = $@"{server.Path}\WS\Binaries\Win64\WSServer-Win64-Shipping.exe";
                ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
                int pid = GetProcessIdByWindowTitle(windowTitle);

                if (pid > 0)
                {
                    Process realProcess = Process.GetProcessById(pid);

                    if (!realProcess.HasExited)
                    {
                        realProcess.EnableRaisingEvents = true;
                        realProcess.Exited += (s, e) => ServerProcessExited(s, e, server);

                        server.Runtime.Process = realProcess;
                        server.Runtime.Pid = pid;
                        server.Runtime.State = ServerRuntime.ServerState.运行中;

                        StartBackupCleanTimer(server, serverSettings.AutoCleanInterval, serverSettings.AutoSaveCount);
                        //ShowLogMsg($"【{server.ssmServerName}】检测到正在运行，已关联进程", Brushes.Cyan);
                    }
                }
                else
                {
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                }
            }
            catch { }
        }

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.AutoStart == true && server.Runtime.State == ServerRuntime.ServerState.已停止)
            {
                await StartServer(server);
                ShowLogDefault($"{server.ssmServerName} 正在自启动");
            }
        }
        await Task.CompletedTask;
    }

    #endregion RunningServerRestore

    #region DialogHelpers

    /// <summary>
    /// 显示指定错误内容的确认对话框。
    /// </summary>
    /// <param name="message">要显示或发送的消息内容。</param>
    /// <returns>表示异步操作。</returns>
    public async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "操作失败",
            Content = message,
            CloseButtonText = "确定"
        };
        await dialog.ShowAsync();
    }

    #endregion DialogHelpers

    #region LogRenderingAndMonitoring

    /// <summary>
    /// 将服务器日志行添加到界面并按内容着色。
    /// </summary>
    /// <param name="logType">日志视图类型。</param>
    /// <param name="line">待添加到日志视图的文本行。</param>
    private void AppendLogLine(LogType logType, string line)
    {
        var paragraph = new Paragraph();
        paragraph.Foreground = GetLogColor(line);
        paragraph.Inlines.Add(new Run(line));
        paragraph.Margin = new Thickness(2);
        _logTypeToTexbox[logType].Document.Blocks.Add(paragraph);
    }

    /// <summary>
    /// 根据日志文字中的错误或警告标记返回显示颜色。
    /// </summary>
    /// <param name="line">待检查的日志文本行。</param>
    /// <returns>用于显示该日志行的文字颜色。</returns>
    private Brush GetLogColor(string line)
    {
        if (string.IsNullOrEmpty(line)) return Brushes.AliceBlue;
        string lowerLine = line.ToLower();

        if (lowerLine.Contains("error:") || lowerLine.Contains("exception"))
            return Brushes.Red;
        if (lowerLine.Contains("warning:") || lowerLine.Contains("warn:") || lowerLine.Contains("internal:") || lowerLine.Contains("debug:"))
            return Brushes.Yellow;
        return Brushes.White;
    }

    /// <summary>
    /// 监听当前服务器日志文件并启用兜底刷新。
    /// </summary>
    private void ConfigureActiveLogMonitoring()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(ConfigureActiveLogMonitoring));
            return;
        }

        StopActiveLogMonitoring();
        if (_currentServer == null || _activeLogType != "WSServer"
            || _currentServer.Runtime?.State != ServerRuntime.ServerState.运行中)
            return;

        string logPath = Path.GetFullPath(_ssmPathManager.LogsPath);
        string? logDirectory = Path.GetDirectoryName(logPath);
        if (string.IsNullOrWhiteSpace(logDirectory))
            return;

        _watchedLogPath = logPath;
        if (Directory.Exists(logDirectory))
        {
            try
            {
                _activeLogWatcher = new FileSystemWatcher(logDirectory, Path.GetFileName(logPath))
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = false
                };
                _activeLogWatcher.Changed += (_, _) => QueueLogFileChange(logPath, forceReload: false);
                _activeLogWatcher.Created += (_, _) => QueueLogFileChange(logPath, forceReload: true);
                _activeLogWatcher.Deleted += (_, _) => QueueLogFileChange(logPath, forceReload: true);
                _activeLogWatcher.Renamed += (_, _) => QueueLogFileChange(logPath, forceReload: true);
                _activeLogWatcher.Error += (_, _) => QueueLogFileChange(logPath, forceReload: true);
                _activeLogWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                ShowLogWarning($"无法监听服务器日志，将使用定时检查：{ex.Message}");
                _activeLogWatcher?.Dispose();
                _activeLogWatcher = null;
            }
        }

        _logUpdateTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _logUpdateTimer.Tick -= LogUpdateTimer_Tick;
        _logUpdateTimer.Tick += LogUpdateTimer_Tick;
        _logUpdateTimer.Start();
    }

    /// <summary>
    /// 停止并释放当前日志文件监听器及定时器。
    /// </summary>
    private void StopActiveLogMonitoring()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(StopActiveLogMonitoring));
            return;
        }

        _logUpdateTimer?.Stop();
        if (_activeLogWatcher != null)
        {
            _activeLogWatcher.EnableRaisingEvents = false;
            _activeLogWatcher.Dispose();
            _activeLogWatcher = null;
        }
        _watchedLogPath = null;
    }

    /// <summary>
    /// 处理日志文件系统变更并请求增量刷新。
    /// </summary>
    /// <param name="sourcePath">发生变化的日志文件路径。</param>
    /// <param name="forceReload">是否强制重新读取完整日志快照。</param>
    private void QueueLogFileChange(string sourcePath, bool forceReload)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_watchedLogPath == null || _activeLogType != "WSServer"
                || !string.Equals(_watchedLogPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                return;
            RequestLogRefresh(forceReload);
        }));
    }

    /// <summary>
    /// 服务器运行状态变化时重新配置日志监听。
    /// </summary>
    private void RefreshActiveLogMonitoringForStateChange()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshActiveLogMonitoringForStateChange));
            return;
        }

        ConfigureActiveLogMonitoring();
    }

    /// <summary>
    /// 保存日志文件的读取位置、文件标识和最近读取内容。
    /// </summary>
    private sealed class LogFileReadState
    {
        public long FileLength { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public DateTime CreationTimeUtc { get; set; }
        public long ReadPosition { get; set; }
        public byte[] PendingBytes { get; set; }
        public List<string> Lines { get; set; }
        public bool IsFirstLinePending { get; set; }
        public byte[] Checkpoint { get; set; }

        /// <summary>
        /// 创建日志文件读取状态对象。
        /// </summary>
        /// <param name="fileLength">当前日志文件长度。</param>
        /// <param name="lastWriteTimeUtc">日志文件最后修改时间（UTC）。</param>
        /// <param name="creationTimeUtc">日志文件创建时间（UTC）。</param>
        /// <param name="readPosition">下一次增量读取的文件位置。</param>
        /// <param name="pendingBytes">尚未构成完整日志行的字节。</param>
        /// <param name="lines">当前缓存的日志行。</param>
        /// <param name="isFirstLinePending">是否仍需处理文件首行的编码标记。</param>
        /// <param name="checkpoint">用于识别文件变化的校验字节。</param>
        public LogFileReadState(long fileLength, DateTime lastWriteTimeUtc, DateTime creationTimeUtc,
            long readPosition, byte[] pendingBytes, List<string> lines, bool isFirstLinePending, byte[] checkpoint)
        {
            FileLength = fileLength;
            LastWriteTimeUtc = lastWriteTimeUtc;
            CreationTimeUtc = creationTimeUtc;
            ReadPosition = readPosition;
            PendingBytes = pendingBytes;
            Lines = lines;
            IsFirstLinePending = isFirstLinePending;
            Checkpoint = checkpoint;
        }

        /// <summary>
        /// 复制日志读取状态，避免后续增量读取修改原状态。
        /// </summary>
        /// <returns>包含相同读取数据的新状态对象。</returns>
        public LogFileReadState Clone() => new(FileLength, LastWriteTimeUtc, CreationTimeUtc,
            ReadPosition, PendingBytes.ToArray(), Lines.ToList(), IsFirstLinePending, Checkpoint.ToArray());
    }

    /// <summary>
    /// 保存一次日志读取操作的结果信息。
    /// </summary>
    private sealed class LogReadResult
    {
        public LogFileReadState? State { get; }
        public string[] NewLines { get; }
        public bool Reloaded { get; }
        public string? Error { get; }

        /// <summary>
        /// 创建日志读取结果对象。
        /// </summary>
        /// <param name="state">读取后的文件状态。</param>
        /// <param name="newLines">本次新增的日志行。</param>
        /// <param name="reloaded">是否重新读取完整快照。</param>
        /// <param name="error">读取失败时的错误说明。</param>
        public LogReadResult(LogFileReadState? state, string[] newLines, bool reloaded, string? error)
        {
            State = state;
            NewLines = newLines;
            Reloaded = reloaded;
            Error = error;
        }
    }

    #endregion LogRenderingAndMonitoring

    #region AppVersionCheck

    /// <summary>
    /// 在线检查管理器版本并在主控制台显示结果。
    /// </summary>
    private async void LookForAppUpdate()
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "SSM-Client/1.0");

        try
        {
            string latestVersion = null;

            try
            {
                var response = await httpClient.GetAsync("https://raw.githubusercontent.com/aghosto/Soulmask-Server-Manager/refs/heads/main/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                var response = await httpClient.GetAsync("https://gitee.com/aGHOSToZero/Soulmask-Server-Manager/raw/main/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }

            latestVersion = latestVersion.Trim();

            string currentVersion = AppVersion.Text
            .Replace("软件版本：", "") 
            .Trim();

            if (latestVersion != currentVersion)
            {
                SsmSettings.AppSettings.HasNewVersion = true;
                SsmSettings.AppSettings.NewVersion = latestVersion;
                ShowLogWarning($"发现新版本：{latestVersion}，可点击左下角版本号进行更新");
            }
            else
            {
                SsmSettings.AppSettings.HasNewVersion = false;
                ShowLogDefault($"当前软件已是最新版本：{latestVersion}");
            }
        }
        catch (HttpRequestException ex)
        {
            string errorMessage = "检查更新失败：网络异常";

            if (ex.InnerException != null)
            {
                string inner = ex.InnerException.Message.ToLower();
                if (inner.Contains("eof") || inner.Contains("closed")) 
                    errorMessage = "服务器连接关闭";
                else if (inner.Contains("timeout")) 
                    errorMessage = "连接超时";
                else if (inner.Contains("host") || inner.Contains("resolve")) 
                    errorMessage = "无法连接服务器";
                else if (ex.InnerException is System.Security.Authentication.AuthenticationException) 
                    errorMessage = "SSL安全认证失败";
            }
            else if (ex.StatusCode.HasValue)
            {
                errorMessage = $"服务器错误：{ex.StatusCode}";
            }

            ShowLogError(errorMessage);
            ShowLogError("无法检查更新，请检查网络后重试");
        }
        catch (Exception ex)
        {
            ShowLogError($"检查更新出错：{ex.Message}");
        }
    }
    #endregion AppVersionCheck

    #region BackupRetention

    /// <summary>
    /// 按保留数量删除较旧的服务器地图存档备份。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <param name="keepCount">要保留的最新备份数量。</param>
    private static void CleanOldBackups(Server server, int keepCount)
    {
        try
        {
            ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
            string savePath = Path.Combine(server.Path, "WS", "Saved", "Worlds", "Dedicated", $"{serverSettings.Map}");

            if (!Directory.Exists(savePath))
                return;

            var backupFiles = Directory.GetFiles(savePath, "*.*", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (backupFiles.Count <= keepCount)
                return;

            var filesToDelete = backupFiles.Skip(keepCount).ToList();

            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                }
                catch { }
            }

        }
        catch
        { }
    }

    #endregion BackupRetention

    #region AutomaticUpdateAndRestart

    /// <summary>
    /// 并行处理符合自动重启条件的服务器。
    /// </summary>
    private async Task AutoRestartServerAsync(Server server)
    {
        if (server.Runtime.State != ServerRuntime.ServerState.运行中)
            return;
        ShowLogWarning($"正在定时重启服务器：{server.ssmServerName}");
        await StopServer(server);
        bool started = await StartServer(server);
        ShowLogMsg(started ? $"服务器 {server.ssmServerName} 定时重启完成。" : $"服务器 {server.ssmServerName} 定时重启失败。",
            started ? Brushes.LimeGreen : Brushes.Red);
    }

    #endregion AutomaticUpdateAndRestart

    #region PlayerDataRefresh

    /// <summary>
    /// 异步刷新当前服务器的在线玩家列表。
    /// </summary>
    /// <returns>表示异步操作。</returns>
    private async Task RefreshPlayersAsync()
    {
        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        try
        {
            var players = await RCONClient.GetPlayersAsync("127.0.0.1", serverSettings.EchoPort);

            if (players == null) 
                return;

            _players.Clear();
            foreach (var p in players)
                _players.Add(p);

            PlayerDataGrid.ItemsSource = _players;
            PlayerCountText.Text = players.Count.ToString();
            LastUpdatedText.Text = $"最后刷新：{DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            PlayerCountText.Text = $"获取失败：{ex.Message}";
        }
    }

    #endregion PlayerDataRefresh

    #region WebhookNotifications

    /// <summary>
    /// 通过已配置的 Discord Webhook 发送消息。
    /// </summary>
    /// <param name="message">要显示或发送的消息内容。</param>
    private void SendDiscordMessage(string message)
    {
        if (SsmSettings.WebhookSettings.Enabled == false || message == "")
            return;

        if (SsmSettings.WebhookSettings.URL == "")
        {
            //ShowLogWarning("Discord webhook尝试发送消息，但URL未定义。");
            return;
        }

        if (DiscordSender.WebHook == null)
        {
            DiscordSender.WebHook = SsmSettings.WebhookSettings.URL;
        }

        DiscordSender.SendMessage(message);
    }

    #endregion WebhookNotifications

    #region GameServerUpdate

    /// <summary>
    /// 检查并更新 SteamCMD 工具。
    /// </summary>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> UpdateSteamCMD()
    {
        string workingDir = Directory.GetCurrentDirectory();
        ShowLogWarning("未找到SteamCMD，正在下载...");
        byte[] fileBytes = await HttpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        await File.WriteAllBytesAsync(workingDir + @"\steamcmd.zip", fileBytes);
        if (File.Exists(workingDir + @"\SteamCMD\steamcmd.exe") == true)
        {
            File.Delete(workingDir + @"\SteamCMD\steamcmd.exe");
        }
        ShowLogWarning("解压中...");
        ZipFile.ExtractToDirectory(workingDir + @"\steamcmd.zip", workingDir + @"\SteamCMD");
        if (File.Exists(workingDir + @"\steamcmd.zip"))
        {
            File.Delete(workingDir + @"\steamcmd.zip");
        }

        ShowLogDefault("正在获取Soulmask Dedicated Server应用信息。");
        await CheckForGameUpdate();

        return true;
    }

    /// <summary>
    /// 使用 SteamCMD 更新指定服务器的游戏文件。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> UpdateGame(Server server)
    {
        // 自动更新等内部流程也经过这里，避免多个服务器同时启动 SteamCMD。
        await _serverUpdateSemaphore.WaitAsync();
        _serverBeingUpdated = server;
        var cancellation = new CancellationTokenSource();
        _serverUpdateCancellation = cancellation;

        try
        {
            return await UpdateGameCore(server, cancellation.Token);
        }
        finally
        {
            if (ReferenceEquals(_serverUpdateCancellation, cancellation))
                _serverUpdateCancellation = null;
            cancellation.Dispose();
            Dispatcher.Invoke(() =>
            {
                InstallationProgressBar.IsIndeterminate = false;
                InstallationProgressBar.Value = 0;
                InstallationProgressBar.Visibility = Visibility.Collapsed;
                InstallationProgressText.Text = string.Empty;
                InstallationProgressText.Visibility = Visibility.Collapsed;
            });
            _serverBeingUpdated = null;
            _serverUpdateSemaphore.Release();
        }
    }

    /// <summary>
    /// 执行单个服务器的 SteamCMD 更新流程。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> UpdateGameCore(Server server, CancellationToken cancellationToken)
    {
        if (server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogWarning($"服务器 {server.ssmServerName} 正在更新中，尝试终止现有SteamCMD进程...");
            KillServerSteamcmd(server);
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        if (server.Runtime.State != ServerRuntime.ServerState.已停止)
        {
            ShowLogError($"服务器 {server.ssmServerName} 状态为 {server.Runtime.State}，无法更新（仅允许已停止状态）");
            return false;
        }
        server.Runtime.State = ServerRuntime.ServerState.更新中;
        if (cancellationToken.IsCancellationRequested)
        {
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        Process steamcmd = null;
        bool showSteamCMDWindow = SsmSettings.AppSettings?.ShowSteamCMDWindow ?? true;

        Dispatcher.Invoke(() =>
        {
            InstallationProgressBar.IsIndeterminate = true;
            InstallationProgressBar.Value = 0;
            InstallationProgressBar.Visibility = showSteamCMDWindow ? Visibility.Collapsed : Visibility.Visible;
            InstallationProgressText.Text = "准备中";
            InstallationProgressText.Visibility = showSteamCMDWindow ? Visibility.Collapsed : Visibility.Visible;
        });

        if (!Directory.Exists(server.Path))
        {
            ShowLogDefault($"服务器目录不存在，正在创建: {server.Path}");
            Directory.CreateDirectory(server.Path);
        }

        if (server.Runtime.Process != null && !server.Runtime.Process.HasExited)
        {
            ShowLogWarning($"服务器 {server.ssmServerName} 仍在运行中，无法更新");
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        string steamCmdDir = Path.Combine(Directory.GetCurrentDirectory(), @"SteamCMD");
        string steamCmdPath = Path.Combine(steamCmdDir, "steamcmd.exe");

        if (!File.Exists(steamCmdPath))
        {
            ShowLogDefault("未找到SteamCMD，正在下载...");

            try
            {
                using var httpClient = new HttpClient();
                byte[] fileBytes = await httpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", cancellationToken);
                string zipPath = Path.Combine(Directory.GetCurrentDirectory(), "steamcmd.zip");
                await File.WriteAllBytesAsync(zipPath, fileBytes, cancellationToken);

                if (!Directory.Exists(steamCmdDir))
                    Directory.CreateDirectory(steamCmdDir);

                ZipFile.ExtractToDirectory(zipPath, steamCmdDir);
                File.Delete(zipPath);
                ShowLogDefault("SteamCMD下载并安装成功");
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ShowLogWarning($"已取消服务器 {server.ssmServerName} 的 SteamCMD 下载");
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                    return false;
                }

                ShowLogError($"SteamCMD下载失败：{ex.Message}");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            ShowLogWarning($"已取消服务器 {server.ssmServerName} 的更新");
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        bool isNewInstall = !Directory.EnumerateFiles(server.Path).Any();
        string action = isNewInstall ? "下载" : "更新";

        ShowLogWarning($"正在{action}游戏服务器：{server.ssmServerName}，请等待...");

        if (SsmSettings.AppSettings == null)
        {
            ShowLogWarning("警告：应用设置未初始化，使用默认值");
            SsmSettings.AppSettings = new AppSettings();
        }

        string[] installScript = {
            $"force_install_dir \"{server.Path}\"",
            "login anonymous",
            $"app_update 3017310 {(SsmSettings.AppSettings.VerifyUpdates ? "validate" : "")}",
            "quit"
        };

        string scriptPath = Path.Combine(server.Path, "steamcmd.txt");
        if (File.Exists(scriptPath))
            File.Delete(scriptPath);
        File.WriteAllLines(scriptPath, installScript);

        string parameters = $@"+runscript ""{scriptPath}""";

        int hasError = 0;
        CancellationTokenSource cts = new CancellationTokenSource(); // 用于取消读取
#if DEBUG
        string? steamCmdOutputLogPath = null;
#endif

        void HandleSteamCmdOutputLine(string? outputLine, bool isStandardOutput)
        {
            if (string.IsNullOrWhiteSpace(outputLine))
                return;

#if DEBUG
            if (isStandardOutput)
                AppendSteamCmdOutputLog(steamCmdOutputLogPath, outputLine);
#endif

            string? errorMessage = GetSteamCmdErrorMessage(outputLine);
            if (errorMessage != null)
            {
                if (Interlocked.Exchange(ref hasError, 1) == 0)
                    ShowLogError(errorMessage);
                return;
            }

            if (!isStandardOutput || showSteamCMDWindow || Volatile.Read(ref hasError) != 0)
                return;

            Match progressMatch = SteamCmdProgressRegex.Match(outputLine);
            if (progressMatch.Success && double.TryParse(
                    progressMatch.Groups["percentage"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double percentage))
            {
                UpdateInstallationProgress(percentage);
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = steamCmdPath,
                Arguments = parameters,
                WorkingDirectory = server.Path,
                CreateNoWindow = !showSteamCMDWindow,
                UseShellExecute = showSteamCMDWindow
            };

            if (!showSteamCMDWindow)
            {
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.UTF8;
                startInfo.StandardErrorEncoding = Encoding.UTF8;
#if DEBUG
                try
                {
                    steamCmdOutputLogPath = CreateSteamCmdOutputLog(server);
                    File.WriteAllText(
                        steamCmdOutputLogPath,
                        $"服务器：{server.ssmServerName}{Environment.NewLine}路径：{server.Path}{Environment.NewLine}开始时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}{Environment.NewLine}--- standardOutput ---{Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    ShowLogWarning($"创建 SteamCMD 调试日志失败：{ex.Message}");
                }
#endif
            }

            steamcmd = new Process
            {
                StartInfo = startInfo
            };

            if (!showSteamCMDWindow)
            {
                steamcmd.OutputDataReceived += (_, e) => HandleSteamCmdOutputLine(e.Data, isStandardOutput: true);
                steamcmd.ErrorDataReceived += (_, e) => HandleSteamCmdOutputLine(e.Data, isStandardOutput: false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            steamcmd.Start();
            server.Runtime.Process = steamcmd;
            if (!showSteamCMDWindow)
            {
                steamcmd.BeginOutputReadLine();
                steamcmd.BeginErrorReadLine();
            }
            await steamcmd.WaitForExitAsync();

            if (cancellationToken.IsCancellationRequested)
            {
                ShowLogWarning($"已取消服务器 {server.ssmServerName} 的更新");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }

            if (Volatile.Read(ref hasError) != 0 || steamcmd.ExitCode != 0)
            {
                ShowLogError($"{action}失败（ExitCode: {steamcmd.ExitCode}）");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
            else
            {
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ShowLogWarning($"已取消服务器 {server.ssmServerName} 的更新");
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        catch (Exception ex)
        {
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        finally
        {
            cts.Cancel();
            Dispatcher.Invoke(() =>
            {
                InstallationProgressBar.IsIndeterminate = false;
                InstallationProgressBar.Visibility = Visibility.Collapsed;
                InstallationProgressText.Visibility = Visibility.Collapsed;
            });
            steamcmd?.Dispose();
            server.Runtime.Process = null;
            if (File.Exists(scriptPath))
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch{ }
            }
        }
    }
    /// <summary>
    /// 终止指定服务器正在执行的 SteamCMD 更新进程。
    /// </summary>
    /// <param name="server">要终止更新的服务器。</param>
    private void KillServerSteamcmd(Server server)
    {

        try
        {
            var process = server.Runtime.Process;
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                ShowLogError($"已终止服务器 {server.ssmServerName} 的更新");
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"终止当前服务器 SteamCMD 失败：{ex.Message}");
        }
    }

    #endregion GameServerUpdate

    #region ServerLifecycle

    /// <summary>
    /// 准备并启动服务器，更新其运行状态并保存应用配置。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> StartServer(Server server)
    {
        if (server.Runtime.Process != null || server.Runtime.State == ServerRuntime.ServerState.启动中)
        {
            ShowLogError($"错误：{server.ssmServerName} 已在运行中");
            return false;
        }

        server.Runtime.State = ServerRuntime.ServerState.启动中;
        try
        {
            var ssmPath = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            ServerSettings jsonObject = await ServerSettingsEditor.LoadServerSettingsAsync(ssmPath.ServerSettings);
            server = SsmSettings.Servers.FirstOrDefault(s => s.ssmServerName == server.ssmServerName) ?? server;

            ShowLogWarning($"启动服务器：{server.ssmServerName}{(server.Runtime.RestartAttempts > 0 ? $" 尝试 {server.Runtime.RestartAttempts}/3" : "")}");

            string serverExePath = Path.Combine(server.Path, "StartServer.bat");
            string soulmaskExe = ssmPath.ServerExePath;

            if (!File.Exists(serverExePath))
            {
                if (!File.Exists(soulmaskExe))
                {
                    ShowLogError($"错误：未找到 {serverExePath} 且服务器程序不存在");
                    return false;
                }
                ShowLogWarning("未找到 StartServer.bat，正在自动创建...");
                await Task.Run(() => TryCreateStartServerBatFromSettings(server));
            }
            else
            {
                await Task.Run(() => TryCreateStartServerBatFromSettings(server));
            }

            if (jsonObject.ServerId <= 0)
            {
                jsonObject.ServerId = ServerSettingsEditor.GetNextAvailableServerId(SsmSettings.Servers);
            }
            //if (File.Exists(ssmPath.EngineIniPath))
                //ServerSettingsEditor.IniWriteName(jsonObject, ssmPath.EngineIniPath);
            await ServerSettingsEditor.SaveServerSettingsAsync(server, jsonObject);

            if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StartServer) && server.WebhookMessages.Enabled)
            {
                SendDiscordMessage(server.WebhookMessages.StartServer);
            }

            var paramBuilder = new StringBuilder();
            paramBuilder.Append(jsonObject.Map);
            paramBuilder.Append(" -server");
            paramBuilder.Append($" -serverid={jsonObject.ServerId}");
            paramBuilder.Append(" -log -UTF8Output -MULTIHOME=0.0.0.0");
            paramBuilder.Append($" -EchoPort={jsonObject.EchoPort}");
            paramBuilder.Append(" -forcepassthrough");

            if (jsonObject.ClusterMode == 1)
                paramBuilder.Append($" -mainserverport={jsonObject.Port}");
            else if (jsonObject.ClusterMode == 2)
                paramBuilder.Append($" -clientserverconnect={jsonObject.PublicIP}:{jsonObject.MainPort}");

            //if (jsonObject.PVP)
            //    paramBuilder.Append(" -pvp");
            //else
            //    paramBuilder.Append(" -pve");

            paramBuilder.Append($" -SteamServerName={jsonObject.SteamServerName}");
            paramBuilder.Append($" -PORT={jsonObject.Port}");
            paramBuilder.Append($" -QueryPort={jsonObject.QueryPort}");
            paramBuilder.Append($" -MaxPlayers={jsonObject.MaxPlayers}");
            paramBuilder.Append($" -saving={jsonObject.Saving}");
            paramBuilder.Append($" -backup={jsonObject.Backup}");
            paramBuilder.Append($" -backupinterval={jsonObject.AutoSaveInterval}");

            if (!string.IsNullOrWhiteSpace(jsonObject.Password))
                paramBuilder.Append($" -PSW={jsonObject.Password}");
            if (!string.IsNullOrWhiteSpace(jsonObject.GMPassword))
                paramBuilder.Append($" -adminpsw={jsonObject.GMPassword}");

            paramBuilder.Append($" -rconport={jsonObject.Rcon.Port}");
            if (!string.IsNullOrWhiteSpace(jsonObject.Rcon.Password))
                paramBuilder.Append($" -rconpsw={jsonObject.Rcon.Password}");

            paramBuilder.Append(" -serverpm=2");
            paramBuilder.Append($" -mod=\\\"{jsonObject.Mods}\\\"");

            Process serverProcess = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    WindowStyle = server.RunWithoutWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                    FileName = soulmaskExe,
                    UseShellExecute = true,
                    Arguments = paramBuilder.ToString()
                },
                EnableRaisingEvents = true
            };

            var processStateReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            serverProcess.Exited += async (sender, e) =>
            {
                await processStateReady.Task.ConfigureAwait(false);
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    await Dispatcher.InvokeAsync(() => ServerProcessExited(sender, e, server));
            };

            try
            {
                bool processStarted = await Task.Run(() => serverProcess.Start());
                if (!processStarted)
                    throw new InvalidOperationException("Windows 未能创建服务器进程。");

                server.Runtime.State = ServerRuntime.ServerState.运行中;
                server.Runtime.UserStopped = false;
                server.Runtime.Process = serverProcess;
            }
            finally
            {
                processStateReady.TrySetResult(true);
            }

            RefreshActiveLogMonitoringForStateChange();

            await Task.Delay(3000);
            ShowWindow(serverProcess.MainWindowHandle, SW_MINIMIZE);

            if (server.RunWithoutWindow)
                HideWindow(serverProcess.MainWindowHandle);

            StartBackupCleanTimer(server, jsonObject.AutoCleanInterval, jsonObject.AutoSaveCount);
            ShowLogDefault($"启动成功：{server.ssmServerName} | {(jsonObject.Map == "Level01_Main" ? "云雾之森" : "金色浮沙")} | {jsonObject.SteamServerName}");

            await MainSettings.SaveAsync(SsmSettings);
            return true;
        }
        catch (Exception ex)
        {
            ShowLogError($"启动服务器失败：{ex.Message}");
            return false;
        }
        finally
        {
            if (server.Runtime.State == ServerRuntime.ServerState.启动中 &&
                (server.Runtime.Process == null || server.Runtime.Process.HasExited))
            {
                server.Runtime.State = ServerRuntime.ServerState.已停止;
            }
        }
    }

    /// <summary>
    /// 根据服务器设置创建或更新启动批处理文件。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    private static void TryCreateStartServerBatFromSettings(Server server)
    {
        string batPath = Path.Combine(server.Path, "StartServer.bat");
        string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

        if (!File.Exists(settingsPath)) return;

        try
        {
            string json = File.ReadAllText(settingsPath);
            var jsonObject = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
            if (jsonObject == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.Append("pushd \"%~dp0\"");
            sb.AppendLine();
            sb.Append("WSServer.exe ");
            sb.Append(jsonObject.Map);
            sb.Append(" -server");
            sb.Append($" -serverid={jsonObject.ServerId}");
            sb.Append(" -log -UTF8Output -MULTIHOME=0.0.0.0");
            sb.Append($" -EchoPort={jsonObject.EchoPort}");
            sb.Append(" -forcepassthrough");

            if (jsonObject.ClusterMode == 1)
                sb.Append($" -mainserverport={jsonObject.Port}");
            else if (jsonObject.ClusterMode == 2)
                sb.Append($" -clientserverconnect={jsonObject.PublicIP}:{jsonObject.MainPort}");

            //if (jsonObject.PVP)
            //    sb.Append(" -pvp");
            //else
            //    sb.Append(" -pve");

            sb.Append($" -SteamServerName={jsonObject.SteamServerName}");
            sb.Append($" -PORT={jsonObject.Port}");
            sb.Append($" -QueryPort={jsonObject.QueryPort}");
            sb.Append($" -MaxPlayers={jsonObject.MaxPlayers}");
            sb.Append($" -saving={jsonObject.Saving}");
            sb.Append($" -backup={jsonObject.Backup}");
            sb.Append($" -backupinterval={jsonObject.AutoSaveInterval}");

            if (!string.IsNullOrWhiteSpace(jsonObject.Password))
                sb.Append($" -PSW={jsonObject.Password}");
            if (!string.IsNullOrWhiteSpace(jsonObject.GMPassword))
                sb.Append($" -adminpsw={jsonObject.GMPassword}");

            sb.Append($" -rconport={jsonObject.Rcon.Port}");
            if (!string.IsNullOrWhiteSpace(jsonObject.Rcon.Password))
                sb.Append($" -rconpsw={jsonObject.Rcon.Password}");

            sb.Append(" -serverpm=2");
            sb.Append($" -mod=\\\"{jsonObject.Mods}\\\"");
            sb.AppendLine();
            sb.Append("popd");
            sb.AppendLine();
            sb.Append("exit /B");

            File.WriteAllText(batPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 尝试正常关闭指定服务器，并在必要时清理进程。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> StopServer(Server server)
    {
        string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(settingsPath);
        if (server.Runtime.Process == null || server.Runtime.Process.HasExited)
        {
            ShowLogWarning($"服务器 {server.ssmServerName} 未运行或已退出");
            server.Runtime.Process = null;
            return true;
        }

        if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StopServer) && server.WebhookMessages.Enabled)
        {
            SendDiscordMessage(server.WebhookMessages.StopServer);
        }

        server.Runtime.UserStopped = true;
        try
        {
            bool closedGracefully = false;

            if (await RCONClient.SaveWorldAsync("127.0.0.1", serverSettings.EchoPort))
            {
                await RCONClient.ShutdownAsync("127.0.0.1", serverSettings.EchoPort, 1);
                closedGracefully = await WaitForProcessExitAsync(server.Runtime.Process, 120);
            }

            if (!closedGracefully && !server.Runtime.Process.HasExited)
            {
                ShowLogWarning($"服务器 {server.ssmServerName} 正在执行强制优雅关闭 (Ctrl+C)...");
                closedGracefully = await TryGracefulShutdownAsync(server.Runtime.Process, 120);
            }

            if (!closedGracefully && !server.Runtime.Process.HasExited)
            {
                ShowLogWarning($"服务器 {server.ssmServerName} 关闭超时，强制终止进程...");
                server.Runtime.Process.Kill();
                await WaitForProcessExitAsync(server.Runtime.Process, 5);
            }

            server.Runtime.State = ServerRuntime.ServerState.已停止;
            server.Runtime.Process = null;
            if (ReferenceEquals(_currentServer, server))
                RefreshActiveLogMonitoringForStateChange();
            ShowLogDefault($"服务器 {server.ssmServerName} 已完全关闭");
            return true;
        }
        catch (Exception ex)
        {
            ShowLogError($"关闭服务器异常：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 停止指定服务器，并在成功后重新启动。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> RestartServer(Server server)
    {
        ShowLogWarning($"正在重启服务器：" + server.ssmServerName);
        try
        {
            bool success = await StopServer(server);
            if (success)
            {
                if (!WriteServerCrashLog(server))
                    ShowLogError($"备份 {server.ssmServerName} 服务器日志失败");
                else
                    ShowLogDefault($"已备份 {server.ssmServerName} 服务器日志");

                if (File.Exists(server.Path + @"\StartServer.bat"))
                {
                    success = await StartServer(server);
                }
                else
                {
                    success = false;
                    ShowLogError($"未找到服务器启动脚本，请检查服务器安装是否有误");
                    return success;
                }
                return true;
            }
            else
            {
                ShowLogError($"无法停止服务器：{server.ssmServerName}");
                WriteServerCrashLog(server);
                return false;
            }

        }
        catch (Exception ex)
        {
            ShowLogError($"重启服务器发生错误：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 异步等待指定进程退出，直到成功退出或达到超时时间。
    /// </summary>
    /// <param name="process">要等待或关闭的进程。</param>
    /// <param name="timeoutSeconds">等待超时时间（秒）。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> WaitForProcessExitAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private const int SW_MINIMIZE = 2; // 最小化

    /// <summary>
    /// 调用 Windows API 按类名和标题查找窗口句柄。
    /// </summary>
    /// <param name="lpClassName">要匹配的窗口类名。</param>
    /// <param name="lpWindowName">要匹配的窗口标题。</param>
    /// <returns>窗口句柄。</returns>
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    /// <summary>
    /// 调用 Windows API 获取窗口对应的线程和进程标识。
    /// </summary>
    /// <param name="hWnd">要操作的窗口句柄。</param>
    /// <param name="lpdwProcessId">接收进程标识的输出参数。</param>
    /// <returns>Windows API 返回的无符号整数。</returns>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// 调用 Windows API 按给定命令显示或隐藏窗口。
    /// </summary>
    /// <param name="hWnd">要操作的窗口句柄。</param>
    /// <param name="nCmdShow">窗口显示状态命令。</param>
    /// <returns>操作是否成功。</returns>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// 隐藏指定窗口句柄对应的窗口。
    /// </summary>
    /// <param name="hwnd">要操作的窗口句柄。</param>
    private void HideWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, 0); 
    }

    /// <summary>
    /// 根据窗口标题查找进程标识。
    /// </summary>
    /// <param name="windowTitle">要查找的窗口标题。</param>
    /// <returns>操作返回的整数结果。</returns>
    private int GetProcessIdByWindowTitle(string windowTitle)
    {
        try
        {
            IntPtr hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero) 
                return -1;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
        catch { return -1; }
    }


    /// <summary>
    /// 停止服务器、更新游戏文件并尝试恢复服务器运行。
    /// </summary>
    /// <returns>表示异步操作。</returns>
    private async Task AutoUpdate()
    {
        SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);

        List<Server> serversToUpdate = SsmSettings.Servers.Where(server => server.AutoUpdateEnabled).ToList();
        if (serversToUpdate.Count == 0)
        {
            ShowLogDefault("没有启用自动更新的服务器，本次跳过更新。");
            return;
        }

        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "SteamCMD", "steamcmd.exe")))
        {
            await UpdateSteamCMD();
        }

        var runningServers = serversToUpdate
            .Where(s => s.Runtime.State == ServerRuntime.ServerState.运行中)
            .ToList();

        if (runningServers.Count == 0)
        { 
            ShowLogDefault("无运行中的服务器，直接执行更新");
            foreach (var server in serversToUpdate)
                await UpdateGame(server);
            ShowLogDefault("自动更新完成");
            return;
        }

        foreach (var server in runningServers)
            await RCONClient.SendRconMessageAsync(server, "【服务器更新公告】服务器将在 5分钟 后立即更新重启，更新耗时预计为 30 分钟，请尽快下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(4));
        foreach (var server in runningServers)
            await RCONClient.SendRconMessageAsync(server, "【服务器更新公告】服务器将在 1分钟 后立即更新重启，更新耗时预计为 30 分钟，请立刻下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(1));
        var stopTasks = runningServers.Select(StopServer).ToList();
        await Task.WhenAll(stopTasks);

        ShowLogDefault("所有服务器已关闭，开始更新...");
        foreach (var server in serversToUpdate)
            await UpdateGame(server);

        ShowLogDefault("更新完成，开始启动服务器...");
        var startTasks = runningServers.Select(StartServer).ToList();
        await Task.WhenAll(startTasks);

        //SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);
        ShowLogDefault("所有服务器自动更新并重启完成");
    }

    /// <summary>
    /// 向服务器进程发送关闭信号并等待其退出。
    /// </summary>
    /// <param name="process">要等待或关闭的进程。</param>
    /// <param name="timeoutSeconds">等待超时时间（秒）。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> TryGracefulShutdownAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        FocusWindowAndSendCtrlC(process);

        return await WaitForProcessExitAsync(process, timeoutSeconds);
    }

    /// <summary>
    /// 调用 Windows API 将指定窗口置于前台。
    /// </summary>
    /// <param name="hWnd">要操作的窗口句柄。</param>
    /// <returns>操作是否成功。</returns>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// 激活服务器窗口并向其发送 Ctrl+C 信号。
    /// </summary>
    /// <param name="targetProcess">要激活并发送按键的进程。</param>
    public void FocusWindowAndSendCtrlC(Process targetProcess)
    {
        if (targetProcess == null || targetProcess.HasExited)
            return;

        IntPtr targetHwnd = targetProcess.MainWindowHandle;
        if (targetHwnd == IntPtr.Zero)
            return;

        IntPtr mainWindowHandle = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        try
        {
            ShowWindow(targetHwnd, SW_RESTORE);
            SetForegroundWindow(targetHwnd);
            Thread.Sleep(100);
            System.Windows.Forms.SendKeys.SendWait("^c");
        }
        finally
        {
            ShowWindow(mainWindowHandle, SW_RESTORE);
            SetForegroundWindow(mainWindowHandle);
        }
    }

    #endregion ServerLifecycle

    #region ServerRemovalAndLogProcessing

    /// <summary>
    /// 移除指定服务器实例及其关联文件。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> RemoveServer(Server server)
    {
        int serverIndex = SsmSettings.Servers.IndexOf(server);
        string workingDir = Directory.GetCurrentDirectory();
        string serverName = server.ssmServerName.Replace(" ", "_");

        bool success;
        ContentDialog yesNoDialog = new()
        {
            Content = $"确认要移除服务器 {server.ssmServerName}？\n此动作将永久移除该服务器及其文件。",
            PrimaryButtonText = "是",
            SecondaryButtonText = "否"
        };
        if (await yesNoDialog.ShowAsync() is ContentDialogResult.Secondary)
            return false;

        if (serverIndex != -1)
        {
            ContentDialog bakDialog = new()
            {
                Content = $@"是否为该服务器连接设置创建备份？{Environment.NewLine}备份将保存于：{workingDir}\Backups\{serverName}_Bak.zip",
                PrimaryButtonText = "是",
                SecondaryButtonText = "否"
            };
            if (await bakDialog.ShowAsync() is ContentDialogResult.Primary)
            {
                if (!Directory.Exists(workingDir + @"\Backups"))
                    Directory.CreateDirectory(workingDir + @"\Backups");

                if (Directory.Exists(server.Path + @"\SaveData\"))
                {
                    if (File.Exists(workingDir + @"\Backups\" + serverName + "_Bak.zip"))
                        File.Delete(workingDir + @"\Backups\" + serverName + "_Bak.zip");

                    ZipFile.CreateFromDirectory(server.Path + @"\SaveData\", workingDir + @"\Backups\" + serverName + "_Bak.zip");
                }
            }
            SsmSettings.Servers.RemoveAt(serverIndex);
            if (Directory.Exists(server.Path))
                Directory.Delete(server.Path, true);
            success = true;
            return success;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// 检查服务器是否存在可用更新。
    /// </summary>
    /// <returns>异步操作是否成功。</returns>
    private async Task<bool> CheckForGameUpdate()
    {
        bool foundUpdate = false;
        //ShowLogWarning($"正在查询服务器更新...");

        string json;
        try
        {
            json = await HttpClient.GetStringAsync("https://api.steamcmd.net/v1/info/3017310");
        }
        catch (Exception ex)
        {
            ShowLogWarning($"检查服务器更新失败（网络/SSL 异常）：{ex.Message}");
            return false;
        }

        JsonNode jsonNode;
        try
        {
            jsonNode = JsonNode.Parse(json);
        }
        catch
        {
            ShowLogWarning("检查服务器更新失败：无法解析响应数据");
            return false;
        }

        var version = jsonNode!["data"]["3017310"]["depots"]["branches"]["public"]["timeupdated"]!.ToString();

        if (version == SsmSettings.AppSettings.LastUpdateTimeUNIX)
        {
            SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
            foundUpdate = false;
            if (SsmSettings.AppSettings.LastUpdateTimeUNIX != "")
                SsmSettings.AppSettings.LastUpdateTime = "服务器最近更新时间：" + DateTimeOffset.FromUnixTimeSeconds(long.Parse(SsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToLocalTime().ToString();

            MainSettings.Save(SsmSettings);
            //ShowLogDefault($"当前游戏服务器已是最新版本。");
            //return foundUpdate;
        }

        if (version != SsmSettings.AppSettings.LastUpdateTimeUNIX)
        {
            SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
            foundUpdate = true;
        }

        if (SsmSettings.AppSettings.LastUpdateTimeUNIX == "")
        {
            SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
            foundUpdate = true;
        }

        if (SsmSettings.AppSettings.LastUpdateTimeUNIX != "")
            SsmSettings.AppSettings.LastUpdateTime = "服务器上一次更新的时间：" + DateTimeOffset.FromUnixTimeSeconds(long.Parse(SsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToString();

        MainSettings.Save(SsmSettings);
        return foundUpdate;
    }

    // 读取服务器日志并处理特定事件
    /// <summary>
    /// 读取服务器运行日志并处理首次启动和日志事件。
    /// </summary>
    /// <param name="server">要处理的服务器实例。</param>
    private async void ReadLog(Server server)
    {
        if (server == null)
        {
            ShowLogError($"传入的服务器为空！");
            return;
        }

        ServerSettings jsonObject = await ServerSettingsEditor.LoadServerSettingsAsync(
            Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
        string logPath = Path.Combine(server.Path, "WS", "Saved", "Logs", "WS.log");
        
        try
        {
            if (!server.LogFileExists)
            {
                if (!File.Exists(logPath))
                {
                    ShowLogWarning($"【{server.ssmServerName}】日志文件不存在，请确保服务器已成功启动过一次");
                    await Task.Delay(5000);
                    if (!File.Exists(logPath))
                    {
                        ShowLogError($"【{server.ssmServerName}】日志文件仍不存在，请手动启动服务器一次");
                        return;
                    }
                }
                server.LogFileExists = true;
                ShowLogMsg($"【{server.ssmServerName}】已检测到日志文件：{logPath}", Brushes.Green);
            }

            using FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true);
            using StreamReader sr = new StreamReader(fs);

            while (server.FirstStart)
            {
                string line = await sr.ReadLineAsync();
                if (line != null)
                {
                    if (line.Contains("Game Engine Initialized"))
                    {
                        ShowLogWarning("首次启动服务器，正在关闭以便进行配置");
                        server.FirstStart = false;
                        await StopServer(server);
                    }
                }
                else
                {
                    await Task.Delay(100);
                }
            }

            await MainSettings.SaveAsync(SsmSettings);
            fs.Seek(0, SeekOrigin.End);
            long initialPosition = fs.Position;
        }
        catch (FileNotFoundException ex)
        {
            server.LogFileExists = false;
            ShowLogError($"【{server.ssmServerName}】日志文件已被删除，请重启服务器: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowLogError($"【{server.ssmServerName}】日志处理错误：{ex.Message}");
        }

    }

    #endregion ServerRemovalAndLogProcessing

    #region Events
    /// <summary>
    /// 处理服务器进程退出，更新状态并执行必要的恢复操作。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    /// <param name="server">要处理的服务器实例。</param>
    private async void ServerProcessExited(object sender, EventArgs e, Server server)
    {
        if (server == null)
        {
            ShowLogError("错误：服务器实例为空，无法处理进程退出事件");
            return;
        }

        if (server.Runtime == null)
        {
            ShowLogError($"错误：[{server.ssmServerName}] 运行时对象未初始化");
            return;
        }

        int exitCode = -1;
        Process exitedProcess = sender as Process;
        if (exitedProcess != null && !exitedProcess.HasExited)
        {
            try
            {
                exitCode = exitedProcess.ExitCode; 
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }
        }

        server.Runtime.State = ServerRuntime.ServerState.已停止;
        server.Runtime.Process = null;
        if (ReferenceEquals(_currentServer, server))
            RefreshActiveLogMonitoringForStateChange();

        StopBackupCleanTimer(server);

        try
        {
            switch (exitCode)
            {
                case 1:
                    ShowLogError($"{server.ssmServerName} 崩溃了。");
                    break;
                case -2147483645:
                    ShowLogError($"{server.ssmServerName} 已中断（代码：-2147483645），可能是端口被占用。");
                    break;
                default:
                    //ShowLogWarning($"{server.ssmServerName} 已停止（退出码：{exitCode}）");
                    break;
            }

            if (server.Runtime.RestartAttempts >= 3)
            {
                ShowLogError($"服务器 '{server.ssmServerName}' 已尝试重启3次失败，禁用自动重启。");

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.AttemptStart3) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.AttemptStart3);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (WriteServerCrashLog(server))
                    {
                        ShowLogWarning($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}");
                    }
                }

                ShowLogDefault("尝试最后一次重启服务器...");
                await Task.Delay(5000);

                bool restartSuccess = await StartServer(server);
                if (restartSuccess)
                {
                    ShowLogMsg($"{server.ssmServerName} 重启成功，重新启用自动重启。", Brushes.Green);
                    server.AutoRestart = true;
                    server.Runtime.RestartAttempts = 0;
                }
                else
                {
                    ShowLogError($"{server.ssmServerName} 最后一次重启失败，请手动检查。");
                }
                return;
            }

            if (server.AutoRestart && !server.Runtime.UserStopped)
            {
                server.Runtime.RestartAttempts++;
                ShowLogDefault($"{server.ssmServerName} 将自动重启（尝试 {server.Runtime.RestartAttempts}/3）");

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.ServerCrash) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.ServerCrash);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (WriteServerCrashLog(server))
                    {
                        ShowLogWarning($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}");
                    }
                }

                await Task.Delay(3000);
                await StartServer(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"[{server.ssmServerName}] 处理进程退出时出错：{ex.Message}");
        }
    }

    /// <summary>
    /// 响应应用设置变化并更新主题、壁纸或更新任务。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void AppSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.WallpaperPath) ||
            e.PropertyName == nameof(AppSettings.WallpaperEnabled))
        {
            UpdateWallpaper();
            return;
        }

        switch (e.PropertyName)
        {
            case "AutoUpdateGameInterval":
                SetupServerAutoUpdateTimer();
                break;
            case "DarkMode":
                if (SsmSettings.AppSettings.DarkMode == true)
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                else
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                break;
        }
    }

    /// <summary>
    /// 服务器集合变化后选中最新添加的服务器。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void Servers_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        int serversLength = ServerTabControl.Items.Count;
        if (serversLength > 0)
        {
            ServerTabControl.SelectedIndex = serversLength - 1;
        }
    }


    #endregion


    #region Buttons

    #region ServerControlButtons

    /// <summary>
    /// 处理启动服务器按钮并在启动成功后开始读取日志。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Server server)
        {
            ShowLogError("启动服务器失败：无效的按钮或服务器实例");
            return;
        }

        try
        {
            string batPath = Path.Combine(server.Path, "StartServer.bat");
            if (!File.Exists(batPath))
            {
                ShowLogError($"{server.ssmServerName} 启动失败：未找到启动文件（{batPath}）");
                return;
            }
            bool started = await StartServer(server);

            if (started == true)
            {
                ReadLog(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"{server.ssmServerName} 启动异常：{ex.Message}");
        }
    }

    /// <summary>
    /// 处理服务器更新按钮，并支持取消正在执行的更新。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogError($"错误：未找到服务器信息");
            return;
        }

        try
        {
            if (server.Runtime.State == ServerRuntime.ServerState.更新中)
            {
                ShowLogWarning($"正在取消服务器 {server.ssmServerName} 的更新...");
                _serverUpdateCancellation?.Cancel();
                KillServerSteamcmd(server);
                while (ReferenceEquals(_serverBeingUpdated, server))
                    await Task.Delay(50);
                return;
            }

            if (_serverBeingUpdated is Server activeServer)
            {
                var dialog = new ContentDialog
                {
                    Title = "服务器正在更新",
                    Content = $"服务器“{activeServer.ssmServerName}”正在更新或下载，请等待任务完成后再更新其他服务器。",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
                return;
            }

            button.IsEnabled = false;
            UpdateButtonText.Text = "取消更新";
            button.IsEnabled = true;

            bool success = await UpdateGame(server);

            if (success)
                ShowLogDefault($"服务器 {server.ssmServerName} 更新成功！");
        }
        catch (Exception ex)
        {
            ShowLogError($"更新过程中发生错误：{ex.Message}");
        }
        finally
        {
            UpdateServerUpdateButtonState(_currentServer ?? server);
        }
    }

    /// <summary>
    /// 根据服务器运行状态和是否已安装游戏文件更新主界面的下载/更新按钮。
    /// </summary>
    /// <param name="server">当前选中的服务器。</param>
    private void UpdateServerUpdateButtonState(Server server)
    {
        if (server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            UpdateButtonText.Text = "取消更新";
            UpdateButton.IsEnabled = true;
            return;
        }

        UpdateButtonText.Text = File.Exists(Path.Combine(server.Path, "WSServer.exe"))
            ? "更新服务器"
            : "下载服务器";
        UpdateButton.IsEnabled = server.Runtime.State != ServerRuntime.ServerState.运行中;
    }

    /// <summary>
    /// 处理停止服务器按钮并报告停止过程中的异常。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Button button = (Button)sender;
            Server server = button.DataContext as Server;

            if (server == null)
            {
                ShowLogError($"未找到服务器信息，请确认服务器有正常运行过至少一次");
                return;
            }

            ShowLogWarning($"正在停止服务器：{server.ssmServerName}");
            bool wasRunning = server.Runtime?.State == ServerRuntime.ServerState.运行中;
            await StopServer(server);

             //if (wasRunning)
             //   WriteServerCrashLog(server);
        }
        catch (Exception ex)
        {
            ShowLogError($"停止服务器时出错：{ex.Message}");
            if (sender is Button button && button.DataContext is Server server)
                if (server.Runtime?.State == ServerRuntime.ServerState.运行中)
                    WriteServerCrashLog(server);
        }
    }

    /// <summary>
    /// 处理重启服务器按钮并在成功后读取服务器日志。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void RestartServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogError($"未找到服务器信息，请确认服务器有正常运行过至少一次");
            return;
        }
        bool restartSuccess = await RestartServer(server);
        if (restartSuccess == true)
            ReadLog(server);
    }

    #endregion ServerControlButtons

    #region AppearanceButtons

    /// <summary>
    /// 切换应用主题并保存主题设置。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ThemeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
        {
            SsmSettings.AppSettings.DarkMode = true;
        }
        else
        {
            SsmSettings.AppSettings.DarkMode = false;
        }
        SsmSettings.AppSettings.ConsoleOpacity = SsmSettings.AppSettings.WallpaperOpacity;
        UpdateWallpaper();
        MainSettings.Save(SsmSettings);
    }

    /// <summary>
    /// 打开背景图片管理窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ChangeWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var manager = new BackgroundManagerWindow
        {
            Owner = this
        };
        manager.ShowDialog();
    }

    #endregion AppearanceButtons

    #region ServerManagementButtons

    /// <summary>
    /// 确认服务器状态允许后移除服务器并保存设置。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void RemoveServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (server == null)
        {
            ShowLogError($"错误：找不到要删除的选定服务器");
            return;
        }
        if (server.Runtime.State == ServerRuntime.ServerState.运行中 || server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogError($"错误：服务器正在运行或者更新中，请先停止服务器！");
            return;
        }
        try
        {
            bool success = await RemoveServer(server);
            if (!success)
                ShowLogError($"删除服务器时出错，或操作已中止。");
            else
                MainSettings.Save(SsmSettings);
        }
        catch { }
    }

    /// <summary>
    /// 打开服务器重命名对话框并在确认后保存。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void RenameServerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        var server = menuItem?.DataContext as Server;
        if (server == null)
        {
            ShowLogError("未选择要修改名称的服务器");
            return;
        }

        var dialog = new ModifySsmNameDialog(server, SsmSettings.Servers);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            MainSettings.Save(SsmSettings);
            //ShowLogDefault($"服务器名称已修改为: {server.ssmServerName}");
        }
    }

    /// <summary>
    /// 打开或激活服务器导入窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ImportServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        if (server == null) 
            return;

        var window = Application.Current.Windows.OfType<ImportServerWindow>().FirstOrDefault();

        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        else
        {
            window = new (server);
            window.Owner = this;
            window.ShowDialog();
        }
    }

    /// <summary>
    /// 打开或激活存档导入窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ChangeSaveButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        if (server == null) 
            return;

        var window = Application.Current.Windows.OfType<ChangeSaveWindow>().FirstOrDefault();

        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        else
        {
            window = new (server);
            window.Owner = this;
            window.ShowDialog();
        }
    }

    /// <summary>
    /// 打开或激活集群玩家数据转移窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ClusterPlayerDataTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not Server server)
            return;

        var window = Application.Current.Windows.OfType<ClusterPlayerDataTransferWindow>().FirstOrDefault();
        if (window != null)
        {
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            return;
        }

        window = new ClusterPlayerDataTransferWindow(server, SsmSettings)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    #endregion ServerManagementButtons

    #region ConfigurationEditorButtons

    /// <summary>
    /// 打开或激活服务器连接配置编辑器。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ServerSettingsEditorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var serverSettingsEditor = Application.Current.Windows.OfType<ServerSettingsEditor>().FirstOrDefault();

            if (serverSettingsEditor != null)
            {
                serverSettingsEditor.Activate();
                serverSettingsEditor.Topmost = true;
                serverSettingsEditor.Topmost = false;
            }
            else
            {
                try
                {
                    if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
                    {
                        ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
                        sSettingsEditor.Show();
                    }
                    else
                    {
                        ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers, false, ServerTabControl.SelectedIndex);
                        sSettingsEditor.Show();
                    }
                }
                catch (Exception ex)
                {
                    ShowLogError($"错误：{ex}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"错误：{ex}");
            return;
        }
    }

    /// <summary>
    /// 打开或激活服务器游戏设置编辑器。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void GameSettingsButtonEditor_Click(object sender, RoutedEventArgs e)
    {
        var editor = Application.Current.Windows.OfType<GameSettingsEditor>().FirstOrDefault();
        if (editor != null)
        {
            editor.Activate();
            editor.Topmost = true;
            editor.Topmost = false;
        }
        else
        {
            if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
            {
                GameSettingsEditor newEditor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
                newEditor.Show();
            }
            else
            {
                GameSettingsEditor newEditor = new(SsmSettings.Servers);
                newEditor.Show();
            }
        }
    }

    /// <summary>
    /// 更新主窗口底部的服务器下载进度和百分比。
    /// </summary>
    /// <param name="percentage">SteamCMD 输出的下载百分比。</param>
    private void UpdateInstallationProgress(double percentage)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => UpdateInstallationProgress(percentage));
            return;
        }

        percentage = Math.Clamp(percentage, 0, 100);
        InstallationProgressBar.IsIndeterminate = false;
        InstallationProgressBar.Value = percentage;
        InstallationProgressText.Text = $"{percentage:0.0}%";
    }

    /// <summary>
    /// 根据 SteamCMD 输出识别常见更新错误并生成面向用户的说明。
    /// </summary>
    /// <param name="outputLine">SteamCMD 输出的一行文本。</param>
    /// <returns>识别到错误时返回说明，未发现错误时返回空值。</returns>
    private static string? GetSteamCmdErrorMessage(string outputLine)
    {
        if (outputLine.Contains("默认文件夹", StringComparison.OrdinalIgnoreCase))
            return "SteamCMD更新失败：服务器路径包含中文，请将服务器目录移动到不含中文的路径后重试。";

        if (outputLine.Contains("FAILED (No Connection)", StringComparison.OrdinalIgnoreCase) ||
            outputLine.Contains("No connection to content servers", StringComparison.OrdinalIgnoreCase))
        {
            return "SteamCMD无法连接更新内容服务器（网络或Steam节点暂时不可用），请稍后重试，并检查网络、防火墙或代理。";
        }

        if (outputLine.Contains("No subscription", StringComparison.OrdinalIgnoreCase))
            return "SteamCMD下载权限不足：当前登录方式没有该应用或Depot的授权。";

        Match appStateMatch = SteamCmdAppStateErrorRegex.Match(outputLine);
        if (appStateMatch.Success)
        {
            string appId = appStateMatch.Groups["appId"].Value;
            string state = appStateMatch.Groups["state"].Value.ToUpperInvariant();
            string target = string.IsNullOrEmpty(appId) ? "SteamCMD更新" : $"应用 {appId} 更新";

            return state switch
            {
                "0X402" => $"{target}失败（状态码 0x402）：Steam连接或内容下载节点异常，通常可稍后重试；请同时检查SteamCMD的connection_log和content_log。",
                "0X202" => $"{target}失败（状态码 0x202）：目标磁盘空间不足，请检查可用空间。",
                "0X606" => $"{target}失败（状态码 0x606）：SteamCMD无法写入目标目录，请检查目录权限、磁盘状态或安全软件拦截。",
                "0X626" => $"{target}失败（状态码 0x626）：SteamCMD缺少更新文件，可能是下载中断或缓存不完整，请重试并检查SteamCMD日志。",
                "0X6A6" => $"{target}失败（状态码 0x6A6）：检测到更新文件损坏，请重试验证或重新下载。",
                "0X6" => $"{target}失败（状态码 0x6）：可能无法连接内容服务器，或当前登录方式无权访问所需Depot；请检查网络及下载权限。",
                _ => $"{target}失败（SteamCMD状态码 {state}），请查看SteamCMD的content_log和connection_log获取详细原因。"
            };
        }

        if (outputLine.Contains("Error!", StringComparison.OrdinalIgnoreCase) ||
            outputLine.Contains("Failed to install app", StringComparison.OrdinalIgnoreCase))
        {
            return $"SteamCMD报告更新失败：{outputLine}。请查看SteamCMD的content_log和connection_log获取详细原因。";
        }

        return null;
    }

#if DEBUG
    /// <summary>
    /// 创建当前服务器更新过程的 SteamCMD 标准输出日志文件。
    /// </summary>
    /// <param name="server">正在更新的服务器。</param>
    /// <returns>日志文件的完整路径。</returns>
    private static string CreateSteamCmdOutputLog(Server server)
    {
        string localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDirectory = Path.Combine(localDataPath, "SoulmaskServerManager", "Logs", "SteamCMD");
        Directory.CreateDirectory(logDirectory);

        string safeServerName = string.Concat(server.ssmServerName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string fileName = $"SteamCMD_{safeServerName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
        return Path.Combine(logDirectory, fileName);
    }

    /// <summary>
    /// 将 SteamCMD 标准输出追加到当前更新的调试日志。
    /// </summary>
    /// <param name="logPath">调试日志路径。</param>
    /// <param name="outputLine">标准输出的一行内容。</param>
    private static void AppendSteamCmdOutputLog(string? logPath, string outputLine)
    {
        if (string.IsNullOrEmpty(logPath))
            return;

        try
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {outputLine}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // 调试日志写入失败不应中断服务器更新。
        }
    }
#endif

    #endregion ConfigurationEditorButtons

    #region NavigationAndManagerButtons

    /// <summary>
    /// 打开选定服务器目录并报告路径错误。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void ServerFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;
        string path = server?.Path;

        try
        {
            if (string.IsNullOrEmpty(path))
            {
                await ShowErrorDialog("路径为空");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowLogError(ex.Message.ToString());
        }

        if (Directory.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"打开失败：{ex.Message}");
            }
        }
        else
        {
            await ShowErrorDialog("找不到服务器文件夹。");
        }
    }

    /// <summary>
    /// 打开新建服务器窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Application.Current.Windows.OfType<CreateServer>().Any())
        {
            CreateServer cServer = new(SsmSettings);
            cServer.Show();
        }
    }

    /// <summary>
    /// 打开模组管理窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ManageModsButton_Click(object sender, RoutedEventArgs e)
    {
        Server? selectedServer = ServerTabControl.SelectedItem as Server;
        ModManagerWindows? modManagerWindows = Application.Current.Windows
            .OfType<ModManagerWindows>()
            .FirstOrDefault();

        if (modManagerWindows == null)
        {
            modManagerWindows = new ModManagerWindows(SsmSettings, selectedServer);
            modManagerWindows.Show();
        }
        else
        {
            modManagerWindows.SelectServer(selectedServer);
            modManagerWindows.Activate();
        }
    }

    /// <summary>
    /// 打开或激活管理器设置窗口。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ManagerSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var mSettings = Application.Current.Windows.OfType<ManagerSettings>().FirstOrDefault();
        if (mSettings != null)
        {
            mSettings.Activate();
            mSettings.Topmost = true;
            mSettings.Topmost = false;
        }
        else
        {
            mSettings = new(SsmSettings);
            mSettings.ShowDialog();
        }
    }

    #endregion NavigationAndManagerButtons

    #region VersionAndRconButtons

    /// <summary>
    /// 检查管理器版本并询问是否运行更新程序。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void VersionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string latestVersion = null;

            try
            {
                latestVersion = await HttpClient.GetStringAsync("https://raw.githubusercontent.com/aghosto/Soulmask-Server-Manager/refs/heads/main/VERSION");
            }
            catch
            {
                latestVersion = await HttpClient.GetStringAsync("https://gitee.com/aGHOSToZero/Soulmask-Server-Manager/raw/main/VERSION");
            }

            latestVersion = latestVersion.Trim();

            string currentVersion = AppVersion.Text
            .Replace("软件版本：", "") 
            .Trim();

            if (latestVersion != currentVersion)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = $"软件有新版本可用于下载，需要关闭软件进行更新，是否更新？\r\r当前版本：{currentVersion}\r最新版本：{latestVersion}",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    var updaterStartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(applicationDirectory, "SSMUpdater.exe"),
                        WorkingDirectory = applicationDirectory,
                        UseShellExecute = true
                    };
                    updaterStartInfo.ArgumentList.Add("--wait-for-pid");
                    updaterStartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                    Process.Start(updaterStartInfo);
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    ShowLogWarning($"用户取消了本次软件更新。");
                }
            }
            else
            {
                ShowLogDefault($"当前软件已是最新版本：{latestVersion}");
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("不知道这样的主机") || ex.Message.Contains("无法连接") || ex.Message.Contains("404"))
            {
                ShowLogError($"检查更新失败：网络异常或服务器不可用");
            }
            else
            {
                ShowLogError($"检查更新错误：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 打开选定服务器的 RCON 控制台。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void RconServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (!Application.Current.Windows.OfType<RconConsole>().Any())
        {
            RconConsole rConsole = new(server);
            rConsole.Show();
        }
    }

    // 修复工具
    /// <summary>
    /// 修复工具按钮的预留处理入口，供后续功能开发。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void FixTools_Click(object sender, RoutedEventArgs e)
    {
        
    }

    #endregion VersionAndRconButtons

    #region SupportButtons

    /// <summary>
    /// 打开项目支持页面并处理打开失败的情况。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string newDonateUrl = "https://afdian.com/a/aGHOSToZero/plan";

            Process.Start(new ProcessStartInfo(newDonateUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"无法打开爱发电页面：{ex.Message}");
        }
    }

    /// <summary>
    /// 打开问题反馈页面并处理打开失败的情况。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string newIssueUrl = "https://github.com/aghosto/Soulmask-Server-Manager/issues/new";

            Process.Start(new ProcessStartInfo(newIssueUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"无法打开问题反馈页面：{ex.Message}");
        }
    }

    #endregion SupportButtons

    #region LogButtons

    /// <summary>
    /// 按按钮关联的日志类型重新加载日志。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void RefreshLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType)
        {
            if (_logTagToType.TryGetValue(logType, out LogType selectedLogType))
                _ = LoadLogByTypeAsync(selectedLogType, forceReload: true);
        }
    }

    /// <summary>
    /// 清空当前选中的日志视图。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType && _logTagToType.ContainsKey(logType))
        {
            _logTypeToTexbox[_logTagToType[logType]].Document.Blocks.Clear();
            ShowLogMsg("日志已清空", Brushes.Gray, _logTagToType[logType]);
        }
    }

    /// <summary>
    /// 使用系统默认程序打开服务器日志文件。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
        {
            await ShowErrorDialog($"未找到对应的服务器实例");
            return;
        }

        if (sender is not Button btn || btn.Tag is not string logType || !_logTagToType.ContainsKey(logType))
        {
            ShowLogError($"日志类型配置错误");
            return;
        }

        try
        {
            string logPath = string.Empty;
            switch (_logTagToType[logType])
            {
                case LogType.WSServer:
                    logPath = _ssmPathManager.LogsPath;
                    break;
                case LogType.MainConsole:
                    break;
                default:
                    await ShowErrorDialog($"不支持的日志类型");
                    return;
            }

            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            {
                await ShowErrorDialog($"日志文件不存在：{logPath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"打开日志失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 使用文件管理器打开服务器日志目录。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(_ssmPathManager.LogsDir))
            {
                await ShowErrorDialog("路径为空");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowLogError(ex.Message.ToString());
        }

        if (Directory.Exists(_ssmPathManager.LogsDir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _ssmPathManager.LogsDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"打开失败：{ex.Message}");
            }
        }
        else
        {
            await ShowErrorDialog("找不到服务器文件夹。");
        }
    }

    #endregion LogButtons

    #region PlayerButtons

    /// <summary>
    /// 手动刷新在线玩家和封禁玩家列表。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void RefreshPlayerList_Click(object sender, RoutedEventArgs e) 
    {
        await RefreshPlayersAsync();
        LoadBannedPlayersFromFile();
    }

    /// <summary>
    /// 封禁选中的在线玩家并刷新玩家列表。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void BanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerInfo selectedPlayer) return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.BanPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    /// <summary>
    /// 踢出选中的在线玩家并刷新玩家列表。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void KickPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerInfo selectedPlayer) return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.KickPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        await RefreshPlayersAsync();
    }

    /// <summary>
    /// 解除选中玩家的封禁并刷新列表。
    /// </summary>
    /// <param name="sender">触发事件的对象。</param>
    /// <param name="e">事件参数。</param>
    private async void UnBanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (BanListDataGrid.SelectedItem is not PlayerInfo selectedPlayer)
            return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.UnbanPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    #endregion PlayerButtons

    #endregion

    #region BanListManagement

    /// <summary>
    /// 从服务器封禁名单文件重新加载玩家列表。
    /// </summary>
    private void LoadBannedPlayersFromFile()
    {
        _bannedPlayers.Clear();
        if (!File.Exists(_ssmPathManager.BanListPath)) return;

        var lines = File.ReadAllLines(_ssmPathManager.BanListPath);

        foreach (var line in lines)
        {
            string steamId = line.Trim();
            if (string.IsNullOrWhiteSpace(steamId)) continue;

            _bannedPlayers.Add(new PlayerInfo("[已封禁]", steamId));
        }
        BanListDataGrid.ItemsSource = _bannedPlayers;
    }

    #endregion BanListManagement

    #region WallpaperManagement

    /// <summary>
    /// 根据应用设置加载或清除壁纸和日志背景。
    /// </summary>
    public void UpdateWallpaper()
    {
        if (SsmSettings.AppSettings.WallpaperEnabled &&
            !string.IsNullOrEmpty(SsmSettings.AppSettings.WallpaperPath) &&
            File.Exists(SsmSettings.AppSettings.WallpaperPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(SsmSettings.AppSettings.WallpaperPath);
                bitmap.EndInit();
                BackgroundImage.Source = bitmap;

                // 控制台背景使用独立的不透明度设置（默认黑色底）
                double opacity = SsmSettings.AppSettings.ConsoleOpacity;
                byte alpha = (byte)Math.Round(255 * opacity);
                var consoleBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                MainMenuConsoleTextBox.Background = consoleBrush;
                SoulmaskLogTextBox.Background = consoleBrush;
            }
            catch
            {
                BackgroundImage.Source = null;
                MainMenuConsoleTextBox.Background = Brushes.Black;
                SoulmaskLogTextBox.Background = Brushes.Black;
            }
        }
        else
        {
            BackgroundImage.Source = null;
            MainMenuConsoleTextBox.Background = Brushes.Black;
            SoulmaskLogTextBox.Background = Brushes.Black;
        }
    }

    #endregion WallpaperManagement

    #region TimestampAndLogMessages

    /// <summary>
    /// 生成时间戳字符串
    /// </summary>
    /// <param name="format">时间戳格式名称，例如 file、log、unix 或 unix-ms。</param>
    /// <returns>格式化后的当前本地时间字符串。</returns>
    public static string GetTimestamp(string format = "file")
    {
        DateTime now = DateTime.Now;

        return format.ToLower() switch
        {
            "file" => now.ToString("yyyyMMdd_HHmmss"),
            "log" => now.ToString("yyyy/MM/dd HH:mm:ss"),
            "unix" => ((long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds).ToString(),
            "unix-ms" => ((long)(now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds).ToString(),
            _ => now.ToString(format)
        };
    }

    public void ShowLogError(string message) => ShowLogMsg($"{message}", Brushes.Red);
    public void ShowLogWarning(string message) => ShowLogMsg($"{message}", Brushes.Yellow);
    public void ShowLogDefault(string message) => ShowLogMsg($"{message}", Brushes.Lime);
    /// <summary>
    /// 按指定颜色和日志类型输出消息。
    /// </summary>
    /// <param name="message">要显示或发送的消息。</param>
    /// <param name="color">消息显示颜色。</param>
    /// <param name="logType">日志视图类型。</param>
    public void ShowLogMsg(string message, Brush color, LogType logType = LogType.MainConsole)
    {
        if (Dispatcher.CheckAccess())
            InternalShowLogMsg(logType, message, color);
        else
            Dispatcher.Invoke(() => InternalShowLogMsg(logType, message, color));
    }

    /// <summary>
    /// 将带时间戳和指定颜色的消息写入目标日志视图。
    /// </summary>
    /// <param name="logType">日志视图类型。</param>
    /// <param name="message">要显示或发送的消息内容。</param>
    /// <param name="color">消息显示颜色。</param>
    public void InternalShowLogMsg(LogType logType, string message, Brush color)
    {
        RichTextBox targetTextBox = _logTypeToTexbox[logType];
        if (targetTextBox != null)
        {
            string timestampedMessage = $"[{GetTimestamp("log")}]  {message}";
            Paragraph paragraph = new Paragraph(new Run(timestampedMessage));
            paragraph.Foreground = color;
            paragraph.Margin = new Thickness(0);

            targetTextBox.Document.Blocks.Add(paragraph);
            if (_logTypeToCheckbox[logType]?.IsChecked == true)
            {
                targetTextBox.ScrollToEnd();
            }
        }

    }

    #endregion TimestampAndLogMessages
}
