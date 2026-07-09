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
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainSettings SsmSettings = new();
    private static dWebhook DiscordSender = new();
    private static HttpClient HttpClient = new();
    private PeriodicTimer? AutoUpdateTimer;
    private RemoteConClient RCONClient = new();
    private DispatcherTimer _autoRestartTimer;
    private bool _sentRestart10Min = false;
    private bool _sentRestart5Min = false;
    private bool _sentRestart1Min = false;
    private DispatcherTimer _playerRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };

    private string _activeLogType;
    private const int MAX_LOG_LINES = 500;
    private DispatcherTimer _logUpdateTimer;
    private Dictionary<string, LogType> _logTagToType;
    private Dictionary<LogType, RichTextBox> _logTypeToTexbox;
    private Dictionary<LogType, CheckBox> _logTypeToCheckbox;
    private Dictionary<LogType, FileSystemWatcher> _logWatchers = new Dictionary<LogType, FileSystemWatcher>();
    private Dictionary<LogType, long> _lastFileSizes = new Dictionary<LogType, long>();

    // 当前选中的服务器
    private Server _currentServer;

    private SSMPathManager _ssmPathManager;

    private ObservableCollection<PlayerInfo> _players = new();
    private ObservableCollection<PlayerInfo> _bannedPlayers = new();

    public MainWindow()
    {
        // 启用 TLS 1.2 以确保 HTTPS 连接稳定
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
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

        if (SsmSettings.Servers.Count != 0)
        {
            _logUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();
        }

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
                _currentServer = selectedServer;
                _ssmPathManager = new (Directory.GetCurrentDirectory(), _currentServer);
                if (!string.IsNullOrEmpty(_activeLogType))
                {
                    if (_activeLogType == "PlayerData")
                    {
                        LoadBannedPlayersFromFile();
                        await RefreshPlayersAsync();
                        return;
                    }
                    else
                        LoadLogByType(_logTagToType[_activeLogType], true);
                }
                if (_currentServer.Runtime.State == ServerRuntime.ServerState.更新中)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateButtonText.Text = "取消更新";
                }
                else if (_currentServer.Runtime.State == ServerRuntime.ServerState.已停止)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateButtonText.Text = "更新服务器";
                }
                else if (_currentServer.Runtime.State == ServerRuntime.ServerState.运行中)
                {
                    UpdateButton.IsEnabled = false;
                    UpdateButtonText.Text = "更新服务器";
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
        InitPlayerRefreshTimer();

        //if (File.Exists("SSMUpdater.exe") && File.Exists("SSMUpdater.deps.json") && File.Exists("SSMUpdater.dll") && File.Exists("SSMUpdater.runtimeconfig.json"))
        //{
        //    File.Delete("SSMUpdater.exe");
        //    File.Delete("SSMUpdater.dll");
        //    File.Delete("SSMUpdater.deps.json");
        //    File.Delete("SSMUpdater.runtimeconfig.json");
        //    ShowLogMsg($"旧版更新程序清理完成。", Brushes.Gray);
        //}

        if (SsmSettings.AppSettings.AutoUpdateApp == true)
            LookForAppUpdate();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWallpaper();
        await RestoreRunningServers();
    }

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

    #region MinimizeAndClose

    private void MinimizeToTray()
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
        TrayIcon.ShowBalloonTip("已最小化", "程序在托盘运行中", BalloonIcon.Info);
    }

    private void TrayIcon_ShowWindow(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        TrayIcon.Visibility = Visibility.Visible;
        Activate();
    }

    private void TrayIcon_Exit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Visibility = Visibility.Collapsed;
        TrayIcon.Dispose();

        Closing -= MainWindow_Closing;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
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

    // 日志标签页切换事件
    private async void LogTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is not TabControl)
            return;

        if (_currentServer == null)
            return;

        if (LogTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is string logType)
        {
            foreach (var watcher in _logWatchers.Values)
            {
                watcher.EnableRaisingEvents = false;
            }
            _activeLogType = logType;
            if (_activeLogType == "PlayerData")
            {
                if (File.Exists(_ssmPathManager.BanListPath))
                    LoadBannedPlayersFromFile();
                await RefreshPlayersAsync();
                return;
            }
            else
                LoadLogByType(_logTagToType[_activeLogType], forceRefresh: true);

            if (_currentServer.Runtime.State == ServerRuntime.ServerState.运行中)
                StartActiveLogWatcher();
        }
    }

    #region Timers

    private void LogUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_logTagToType.TryGetValue(_activeLogType, out LogType logType))
                return;

            if (logType == LogType.MainConsole)
                return;

            if (!File.Exists(_ssmPathManager.LogsPath))
                return;

            //if (_currentServer == null || _currentServer.Runtime?.State != ServerRuntime.ServerState.运行中)
            //    return;

            //string relativePath = Path.Combine(_currentServer.Path, _logPath);

            if (File.Exists(_ssmPathManager.LogsPath))
            {
                long currentSize = new FileInfo(_ssmPathManager.LogsPath).Length;
                bool sizeChanged = !_lastFileSizes.TryGetValue(logType, out long lastSize) || currentSize != lastSize;
                bool forceUpdate = DateTime.Now.Second % 10 == 0;

                if (sizeChanged || forceUpdate)
                {
                    _lastFileSizes[logType] = currentSize;
                    OnLogFileChanged(logType);
                }
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"定时器检查日志更新失败：{ex.Message}");
        }
    }

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

    public void SetupServerAutoUpdateTimer()
    {
        if (SsmSettings.AppSettings.AutoUpdate == true)
        {
            AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
            AutoUpdateLoop();
        }
    }

    public void InitAutoRestartTimer()
    {
        if (_autoRestartTimer != null)
        {
            _autoRestartTimer.Stop();
            _autoRestartTimer.Tick -= AutoRestartTimer_Tick;
            _autoRestartTimer = null;
        }

        if (!SsmSettings.AppSettings.EnableAutoRestart)
        {
            ShowLogMsg("全局自动重启：已关闭", Brushes.Gray);
            return;
        }

        _autoRestartTimer = new DispatcherTimer();
        _autoRestartTimer.Interval = TimeSpan.FromSeconds(1);
        _autoRestartTimer.Tick += AutoRestartTimer_Tick;
        _autoRestartTimer.Start();

        // 每天重置公告标记
        _sentRestart10Min = false;
        _sentRestart5Min = false;
        _sentRestart1Min = false;

        int h = SsmSettings.AppSettings.AutoRestartHour;
        int m = SsmSettings.AppSettings.AutoRestartMin;
        int s = SsmSettings.AppSettings.AutoRestartSec;

        ShowLogMsg($"全局自动重启已启用 → 每天 {h:D2}:{m:D2}:{s:D2}", Brushes.LimeGreen);
    }

    private async void AutoRestartTimer_Tick(object sender, EventArgs e)
    {
        try
        {
            if (!SsmSettings.AppSettings.EnableAutoRestart)
                return;

            var now = DateTime.Now;
            int targetH = SsmSettings.AppSettings.AutoRestartHour;
            int targetM = SsmSettings.AppSettings.AutoRestartMin;
            int targetS = SsmSettings.AppSettings.AutoRestartSec;

            DateTime targetTime = new DateTime(now.Year, now.Month, now.Day, targetH, targetM, targetS);
            TimeSpan left = targetTime - now;

            var runningServers = SsmSettings.Servers
                .Where(s => s.Runtime.State == ServerRuntime.ServerState.运行中)
                .ToList();

            if (left.TotalMinutes <= 10 && left.TotalMinutes > 5 && !_sentRestart10Min)
            {
                _sentRestart10Min = true;
                string msg = "【服务器通知】服务器将在 10 分钟后自动重启，请尽快安全下线！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (left.TotalMinutes <= 5 && left.TotalMinutes > 1 && !_sentRestart5Min)
            {
                _sentRestart5Min = true;
                string msg = "【服务器通知】服务器将在 5 分钟后自动重启！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (left.TotalMinutes <= 1 && left.TotalSeconds > 10 && !_sentRestart1Min)
            {
                _sentRestart1Min = true;
                string msg = "【服务器通知】服务器将在 1 分钟后立即重启，请立刻下线！";
                foreach (var server in runningServers)
                    await RCONClient.SendRestartAnnounceToSingleServer(server, msg);
            }

            if (now.Hour == targetH && now.Minute == targetM && now.Second == targetS)
            {
                _sentRestart10Min = false;
                _sentRestart5Min = false;
                _sentRestart1Min = false;

                AutoRestart();

                _autoRestartTimer.Stop();
                await Task.Delay(1000);
                _autoRestartTimer.Start();
            }
        }
        catch { }
    }

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

    private void StopBackupCleanTimer(Server server)
    {
        if (server.Runtime.BackupCleanTimer != null)
        {
            server.Runtime.BackupCleanTimer.Stop();
            server.Runtime.BackupCleanTimer.Dispose();
            server.Runtime.BackupCleanTimer = null;
        }
    }

    #endregion Timer

    private bool _isLoadingLog = false;
    // 根据窗口类型加载日志文件
    private async void LoadLogByType(LogType logType, bool forceRefresh = false)
    {
        if (_isLoadingLog) return;
        if (_currentServer == null) return;

        if (logType == LogType.MainConsole)
        {
            RichTextBox mainLogTextBox = _logTypeToTexbox[logType];
            if (_logTypeToCheckbox[logType].IsChecked == true)
            {
                mainLogTextBox.ScrollToEnd();
            }
            return;
        }
        else if (logType == LogType.WSServer)
        {
            try
            {
                _isLoadingLog = true;
                RichTextBox logBox = _logTypeToTexbox[logType];
                string fullPath = _ssmPathManager.LogsPath;

                bool needLoad = await Task.Run(() =>
                {
                    if (forceRefresh) return true;
                    if (!_lastFileSizes.TryGetValue(logType, out long lastSize)) return true;

                    try
                    {
                        return new FileInfo(fullPath).Length != lastSize;
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (!needLoad)
                {
                    if (_logTypeToCheckbox[logType].IsChecked == true)
                    {
                        logBox.ScrollToEnd();
                    }
                    _isLoadingLog = false;
                    return;
                }

                string[] lines = await Task.Run(() =>
                {
                    if (!File.Exists(fullPath))
                    {
                        return new[] {$"日志文件不存在：{fullPath} 请确保服务器有正常启动过至少一次"};
                    }
                    return ReadLastNLines(fullPath, MAX_LOG_LINES);
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        logBox.Document.Blocks.Clear();

                        foreach (string line in lines)
                        {
                            AppendLogLine(logType, line);
                        }

                        _lastFileSizes[logType] = new FileInfo(fullPath).Length;

                        ShowLogMsg($"已加载最近 {lines.Length} 行日志", Brushes.Gray, logType);

                        if (_logTypeToCheckbox[logType].IsChecked == true)
                        {
                            logBox.ScrollToEnd();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowLogMsg($"加载失败：{ex.Message}", Brushes.Red, logType);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                ShowLogMsg($"加载失败：{ex.Message}", Brushes.Red, logType);
            }
            finally
            {
                _isLoadingLog = false;
            }
        }
    }

    // 读取文件的最后N行
    private string[] ReadLastNLines(string filePath, int lineCount)
    {
        List<string> lines = new List<string>();

        try
        {
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                // 强制刷新流，确保读取最新内容
                stream.Position = 0;
                reader.DiscardBufferedData();

                string[] buffer = new string[lineCount];
                int bufferIndex = 0;
                int totalLines = 0;

                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    if (line != null)
                    {
                        buffer[bufferIndex] = line;
                        bufferIndex = (bufferIndex + 1) % lineCount;
                        totalLines++;
                    }
                }

                int startIndex = totalLines > lineCount ? bufferIndex : 0;
                int count = Math.Min(lineCount, totalLines);

                for (int i = 0; i < count; i++)
                {
                    string line = buffer[(startIndex + i) % lineCount];
                    if (!string.IsNullOrEmpty(line))
                    {
                        lines.Add(line);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lines.Clear();
            lines.Add($"[警告] 读取日志失败：{ex.Message}");
        }

        return lines.ToArray();
    }

    private void OnLogFileChanged(LogType logType)
    {
        if (_currentServer?.Runtime?.State != ServerRuntime.ServerState.运行中)
            return;

        if (!File.Exists(_ssmPathManager.LogsPath)) return;

        try
        {
            var logTag = _logTagToType.FirstOrDefault(t => t.Value == logType).Key;
            if (!string.IsNullOrEmpty(logTag) && _activeLogType == logTag)
            {
                Dispatcher.Invoke(() => LoadLogByType(logType, forceRefresh: true));
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"更新 {logType} 日志失败: {ex.Message}");
        }
    }

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

    private void AppendLogLine(LogType logType, string line)
    {
        var paragraph = new Paragraph();
        paragraph.Foreground = GetLogColor(line);
        paragraph.Inlines.Add(new Run(line));
        paragraph.Margin = new Thickness(2);
        _logTypeToTexbox[logType].Document.Blocks.Add(paragraph);
    }

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

    private void StartActiveLogWatcher()
    {
        if (_logWatchers.TryGetValue(_logTagToType[_activeLogType], out var watcher))
        {
            watcher.EnableRaisingEvents = true;
        }
    }

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
                var response = await httpClient.GetAsync("https://raw.githubusercontent.com/aghosto/Soulmask-Server-Manager/refs/heads/master/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                var response = await httpClient.GetAsync("https://gitee.com/aGHOSToZero/Soulmask-Server-Manager/raw/master/VERSION");
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
                ShowLogWarning($"发现新版本：{latestVersion}，可点击左下角更新");
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

    private async void AutoUpdateLoop()
    {
        while (await AutoUpdateTimer.WaitForNextTickAsync())
        {
            try
            {
                bool foundUpdate = await CheckForUpdate();
                if (foundUpdate && SsmSettings.Servers.Count > 0)
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

    private async void AutoRestart()
    {
        List<Task> serverTasks = new List<Task>();
        List<Server> runningServers = new List<Server>();

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.Runtime.State == ServerRuntime.ServerState.运行中)
            {
                server.Runtime.UserStopped = true;

                runningServers.Add(server);
            }
        }

        if (runningServers.Count > 0)
        {
            //SendDiscordMessage(SsmSettings.WebhookSettings.UpdateWait);
            await Task.Delay(TimeSpan.FromSeconds(0));
        }
        else
        {
            ShowLogWarning($"当前无正在运行的服务器，自动重启未生效。");
            return;
        }

        ShowLogWarning($"正在自动重启 {runningServers.Count} 个服务器" + ((runningServers.Count > 0) ? $"，在此之前即将关闭 {runningServers.Count} 个服务器" : ""));
        var stopTasks = runningServers.Select(StopServer).ToList();
        await Task.WhenAll(stopTasks);
        var startTasks = runningServers.Select(StartServer).ToList();
        await Task.WhenAll(startTasks);
        ShowLogDefault($"自动重启完成。");
    }

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

    /// <summary>
    /// Updates SteamCMD, used when the executable could not be found
    /// </summary>
    /// <returns><see cref="bool"/> true if succeeded</returns>
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
        await CheckForUpdate();

        return true;
    }

    private async Task<bool> UpdateGame(Server server)
    {
        if (server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogWarning($"服务器 {server.ssmServerName} 正在更新中，尝试终止现有SteamCMD进程...");
            KillCurrentServerSteamcmd();
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        if (server.Runtime.State != ServerRuntime.ServerState.已停止)
        {
            ShowLogError($"服务器 {server.ssmServerName} 状态为 {server.Runtime.State}，无法更新（仅允许已停止状态）");
            return false;
        }
        server.Runtime.State = ServerRuntime.ServerState.更新中;
        Process steamcmd = null;

        Dispatcher.Invoke(() =>
        {
            InstallationProgressBar.IsIndeterminate = true;
            InstallationProgressBar.Visibility = Visibility.Visible;
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

        string steamCmdDir = Path.Combine(Directory.GetCurrentDirectory(), @"SteamCMD");
        string steamCmdPath = Path.Combine(steamCmdDir, "steamcmd.exe");

        if (!File.Exists(steamCmdPath))
        {
            ShowLogDefault("未找到SteamCMD，正在下载...");

            try
            {
                using var httpClient = new HttpClient();
                byte[] fileBytes = await httpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
                string zipPath = Path.Combine(Directory.GetCurrentDirectory(), "steamcmd.zip");
                await File.WriteAllBytesAsync(zipPath, fileBytes);

                if (!Directory.Exists(steamCmdDir))
                    Directory.CreateDirectory(steamCmdDir);

                ZipFile.ExtractToDirectory(zipPath, steamCmdDir);
                File.Delete(zipPath);
                ShowLogDefault("SteamCMD下载并安装成功");
            }
            catch (Exception ex)
            {
                ShowLogError($"SteamCMD下载失败：{ex.Message}");
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
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

        bool hasError = false;
        CancellationTokenSource cts = new CancellationTokenSource(); // 用于取消读取

        try
        {
            if (!SsmSettings.AppSettings.ShowSteamWindow)
            {
                steamcmd = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = steamCmdPath,
                        Arguments = parameters,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = server.Path,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                steamcmd.Start();
                server.Runtime.Process = steamcmd;
                steamcmd.OutputDataReceived += (sender, e) =>
                {
                    if (hasError)
                        return;

                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        if (e.Data.Contains("默认文件夹"))
                        {
                            ShowLogError("错误：路径包含中文，请不要在带有中文的目录中使用！");
                            hasError = true;
                            KillCurrentServerSteamcmd();
                            return;
                        }

                        if (e.Data.Contains("FAILED (No Connection)"))
                        {
                            ShowLogError("错误：服务器更新失败，请检查你的网络连接！");
                            hasError = true;
                            KillCurrentServerSteamcmd();
                            return;
                        }
                    }
                };

                steamcmd.BeginOutputReadLine();
                steamcmd.BeginErrorReadLine();
                await steamcmd.WaitForExitAsync();

                if (hasError || steamcmd.ExitCode != 0)
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
            else
            {
                steamcmd = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = steamCmdPath,
                        Arguments = parameters,
                        CreateNoWindow = false
                    }
                };
                steamcmd.Start();
                server.Runtime.Process = steamcmd;
                await steamcmd.WaitForExitAsync();

                if (steamcmd.ExitCode != 0)
                {
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                    return false;
                }
                else
                {
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                    return true;
                }
            }
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
    private void KillCurrentServerSteamcmd()
    {
        if (_currentServer == null) return;

        try
        {
            var process = _currentServer.Runtime.Process;
            if (process != null && !process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1000);
                process.Dispose();
                _currentServer.Runtime.Process = null;
                ShowLogError($"已终止当前服务器的更新");
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"终止当前服务器 SteamCMD 失败：{ex.Message}");
        }
    }

    private async Task<bool> StartServer(Server server)
    {
        if (server.Runtime.Process != null)
        {
            ShowLogError($"错误：{server.ssmServerName} 已在运行中");
            return false;
        }

        try
        {
            var ssmPath = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            ServerSettings jsonObject = ServerSettingsEditor.LoadServerSettings(ssmPath.ServerSettings);
            server = SsmSettings.Servers.FirstOrDefault(s => s.ssmServerName == server.ssmServerName) ?? server;

            ShowLogWarning($"启动服务器：{server.ssmServerName}{(server.Runtime.RestartAttempts > 0 ? $" 尝试 {server.Runtime.RestartAttempts}/3" : "")}");

            string serverExePath = Path.Combine(server.Path, "StartServer.bat");
            string soulmaskExe = _ssmPathManager.ServerExePath;

            if (!File.Exists(serverExePath))
            {
                if (!File.Exists(soulmaskExe))
                {
                    ShowLogError($"错误：未找到 {serverExePath} 且服务器程序不存在");
                    return false;
                }
                ShowLogWarning("未找到 StartServer.bat，正在自动创建...");
                TryCreateStartServerBatFromSettings(server);
            }
            else
            {
                TryCreateStartServerBatFromSettings(server);
            }

            if (jsonObject.ServerId <= 0)
            {
                jsonObject.ServerId = ServerSettingsEditor.GetNextAvailableServerId(SsmSettings.Servers);
                ServerSettingsEditor.SaveServerSettings(server, jsonObject);
            }
            //if (File.Exists(ssmPath.EngineIniPath))
                //ServerSettingsEditor.IniWriteName(jsonObject, ssmPath.EngineIniPath);
            ServerSettingsEditor.SaveServerSettings(server, jsonObject);

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

            serverProcess.Exited += (sender, e) => ServerProcessExited(sender, e, server);
            serverProcess.Start();

            server.Runtime.State = ServerRuntime.ServerState.运行中;
            server.Runtime.UserStopped = false;
            server.Runtime.Process = serverProcess;

            await Task.Delay(3000);
            ShowWindow(serverProcess.MainWindowHandle, SW_MINIMIZE);

            if (server.RunWithoutWindow)
                HideWindow(serverProcess.MainWindowHandle);

            StartBackupCleanTimer(server, jsonObject.AutoCleanInterval, jsonObject.AutoSaveCount);
            ShowLogDefault($"启动成功：{server.ssmServerName} | {(jsonObject.Map == "Level01_Main" ? "云雾之森" : "金色浮沙")} | {jsonObject.SteamServerName}");

            MainSettings.Save(SsmSettings);
            return true;
        }
        catch (Exception ex)
        {
            ShowLogError($"启动服务器失败：{ex.Message}");
            return false;
        }
    }

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
            ShowLogDefault($"服务器 {server.ssmServerName} 已完全关闭");
            return true;
        }
        catch (Exception ex)
        {
            ShowLogError($"关闭服务器异常：{ex.Message}");
            return false;
        }
    }

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

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void HideWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, 0); 
    }

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


    private bool IsProcessRunning(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch { return false; }
    }


    private async Task AutoUpdate()
    {
        SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);

        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "SteamCMD", "steamcmd.exe")))
        {
            await UpdateSteamCMD();
        }

        var runningServers = SsmSettings.Servers
            .Where(s => s.Runtime.State == ServerRuntime.ServerState.运行中)
            .ToList();

        if (runningServers.Count == 0)
        { 
            ShowLogDefault("无运行中的服务器，直接执行更新");
            foreach (var server in SsmSettings.Servers)
                await UpdateGame(server);
            ShowLogDefault("自动更新完成");
            return;
        }

        foreach (var server in runningServers)
            await RCONClient.SendRestartAnnounceToSingleServer(server, "【服务器更新公告】服务器将在 5分钟 后立即更新重启，更新耗时预计为 30 分钟，请尽快下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(4));
        foreach (var server in runningServers)
            await RCONClient.SendRestartAnnounceToSingleServer(server, "【服务器更新公告】服务器将在 1分钟 后立即更新重启，更新耗时预计为 30 分钟，请立刻下线并更新玩家客户端后重新加入游戏！");

        await Task.Delay(TimeSpan.FromMinutes(1));
        var stopTasks = runningServers.Select(StopServer).ToList();
        await Task.WhenAll(stopTasks);

        ShowLogDefault("所有服务器已关闭，开始更新...");
        foreach (var server in SsmSettings.Servers)
            await UpdateGame(server);

        ShowLogDefault("更新完成，开始启动服务器...");
        var startTasks = runningServers.Select(StartServer).ToList();
        await Task.WhenAll(startTasks);

        //SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);
        ShowLogDefault("所有服务器自动更新并重启完成");
    }

    private async Task<bool> TryGracefulShutdownAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        FocusWindowAndSendCtrlC(process);

        return await WaitForProcessExitAsync(process, timeoutSeconds);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

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

    private async Task<bool> CheckForUpdate()
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
    private async void ReadLog(Server server)
    {
        ServerSettings jsonObject = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
         
        if (server == null)
        {
            ShowLogError($"传入的服务器为空！");
            return;
        }
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

            using FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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

            MainSettings.Save(SsmSettings);
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

    #region Events
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
            case "AutoUpdate":
                if (SsmSettings.AppSettings.AutoUpdate == true)
                {
#if DEBUG
                    //AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
#else
//                        AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
                    //AutoUpdateLoop();
                    LookForAppUpdate();
                }
                else
                {
                    if (AutoUpdateTimer != null)
                    {
                        AutoUpdateTimer.Dispose();
                    }
                }
                break;
            case "AutoUpdateInterval":
                if (SsmSettings.AppSettings.AutoUpdate == true && AutoUpdateTimer != null)
                {
                    AutoUpdateTimer.Dispose();
#if DEBUG
                    AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
#else
                    AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
                    AutoUpdateLoop();
                }
                break;
            case "DarkMode":
                if (SsmSettings.AppSettings.DarkMode == true)
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                else
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                break;
        }
    }

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
    private async void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Server server)
        {
            ShowLogError("启动服务器失败：无效的按钮或服务器实例");
            return;
        }

        try
        {
            button.IsEnabled = false;
            string batPath = Path.Combine(server.Path, "StartServer.bat");
            if (!File.Exists(batPath))
            {
                ShowLogError($"{server.ssmServerName} 启动失败：未找到启动文件（{batPath}）");
                return;
            }
            bool started = await StartServer(server);
            await Task.Delay(3000);

            if (started == true)
            {
                ReadLog(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogError($"{server.ssmServerName} 启动异常：{ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

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
                KillCurrentServerSteamcmd();
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
            UpdateButtonText.Text = "更新服务器";
            button.IsEnabled = true;
        }
    }

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

    private void ThemeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
            SsmSettings.AppSettings.DarkMode = true;
        else
            SsmSettings.AppSettings.DarkMode = false;

        MainSettings.Save(SsmSettings);
    }

    private void ChangeWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var manager = new BackgroundManagerWindow
        {
            Owner = this
        };
        manager.ShowDialog();
    }

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
            window.Show();
        }
    }

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
            window.Show();
        }
    }

    private async void ServerAccountExchangeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Server server = ((Button)sender).DataContext as Server;
            if (server == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = "玩家数据转移",
                Content = "确定要执行玩家数据转移吗？\n\n冲突时优先使用源存档数据",
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消"
            };
            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            string mainPath = Path.Combine(_ssmPathManager.DedicatedPath, "Level01_Main", "world.db");
            string dlcPath = Path.Combine(_ssmPathManager.DedicatedPath, "DLC_Level01_Main", "world.db");
            string targetDb = Path.Combine(_ssmPathManager.SavedDir, "Accounts", "account.db");
            string copyRolesExe = Path.Combine(_ssmPathManager.PluginDir, "DBAgent", "ThirdParty", "Binaries", "CopyRoles.exe");

            if (!File.Exists(copyRolesExe))
            {
                await new ContentDialog
                {
                    Title = "错误",
                    Content = $"未找到转移工具：\n{copyRolesExe}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                return;
            }

            bool hasMain = File.Exists(mainPath);
            bool hasDLC = File.Exists(dlcPath);

            if (!hasMain && !hasDLC)
            {
                await new ContentDialog
                {
                    Title = "无存档",
                    Content = $"未找到任何地图的 world.db 存档",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                return;
            }

            string selectedSourceDb = null;
            string mapName = "";

            if (hasMain && hasDLC)
            {
                var mapDialog = new ContentDialog
                {
                    Title = "选择源地图",
                    Content = "请选择要从哪个地图读取玩家数据：",
                    PrimaryButtonText = "云雾之森",
                    SecondaryButtonText = "金色浮沙"
                };
                var res = await mapDialog.ShowAsync();

                if (res == ContentDialogResult.Primary)
                {
                    selectedSourceDb = mainPath;
                    mapName = "云雾之森";
                }
                else
                {
                    selectedSourceDb = dlcPath;
                    mapName = "金色浮沙";
                }
            }
            else
            {
                if (hasMain)
                {
                    selectedSourceDb = mainPath;
                    mapName = "云雾之森";
                }
                else
                {
                    selectedSourceDb = dlcPath;
                    mapName = "金色浮沙";
                }
            }

            var finalConfirm = new ContentDialog
            {
                Title = "开始转移",
                Content = $"源地图：{mapName}\n\n确定执行转移？",
                PrimaryButtonText = "开始",
                SecondaryButtonText = "取消"
            };
            if (await finalConfirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            var processing = new ContentDialog
            {
                Title = "转移中",
                Content = "正在执行玩家数据转移...\n请勿关闭程序",
                IsPrimaryButtonEnabled = false
            };
            var _ = processing.ShowAsync();

            var psi = new ProcessStartInfo
            {
                FileName = copyRolesExe,
                Arguments = $"-src=\"{selectedSourceDb}\" -dst=\"{targetDb}\" -type=1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            await Task.Run(() => process.WaitForExit());
            processing.Hide();

            if (process.ExitCode == 0)
            {
                await new ContentDialog
                {
                    Title = "转移成功",
                    Content = $"从【{mapName}】转移玩家数据完成！",
                    PrimaryButtonText = "确定"
                }.ShowAsync(); 
            }
            else
            {
                string err = process.StandardError.ReadToEnd();
                await new ContentDialog
                {
                    Title = "转移失败",
                    Content = $"错误代码：{process.ExitCode}\n{err}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "异常",
                Content = $"ex.Message",
                PrimaryButtonText = "确定"
            }.ShowAsync();
        }
    }

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
                    string engineIniFilePath = Path.Combine(SsmSettings.Servers[ServerTabControl.SelectedIndex].Path, "WS", "Saved", "Config", "WindowsServer", "Engine.ini");
                    if (!File.Exists(engineIniFilePath))
                    {
                        var dialog = new ContentDialog
                        {
                            Owner = this,
                            Title = "服务器初始配置文件不存在",
                            Content = "未找到服务器初始配置文件，请先启动一次服务器后再进行配置！",
                            PrimaryButtonText = "确定",
                        }.ShowAsync();
                        return;
                    }

                    if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
                    {
                        ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
                        sSettingsEditor.Show();
                    }
                    else
                    {
                        ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers);
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

    private void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Application.Current.Windows.OfType<CreateServer>().Any())
        {
            CreateServer cServer = new(SsmSettings);
            cServer.Show();
        }
    }

    private void ManageModsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Application.Current.Windows.OfType<ModManagerWindows>().Any())
        {
            ModManagerWindows modManagerWindows = new ModManagerWindows(SsmSettings);
            modManagerWindows.Show();
        }
    }

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
            if (!Application.Current.Windows.OfType<ManagerSettings>().Any())
            {
                mSettings = new(SsmSettings);
                mSettings.Show();
            }
        }
    }

    private async void VersionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string latestVersion = null;

            try
            {
                latestVersion = await HttpClient.GetStringAsync("https://raw.githubusercontent.com/aghosto/Soulmask-Server-Manager/refs/heads/master/VERSION");
            }
            catch
            {
                latestVersion = await HttpClient.GetStringAsync("https://gitee.com/aGHOSToZero/Soulmask-Server-Manager/raw/master/VERSION");
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
                    Process.Start("SSMUpdater.exe");
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
    private void FixTools_Click(object sender, RoutedEventArgs e)
    {
        
    }

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

    private void RefreshLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType)
        {
            LoadLogByType(_logTagToType[logType], true);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType && _logTagToType.ContainsKey(logType))
        {
            _logTypeToTexbox[_logTagToType[logType]].Document.Blocks.Clear();
            ShowLogMsg("日志已清空", Brushes.Gray, _logTagToType[logType]);
        }
    }

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

    // 点击托盘显示或隐藏
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

    private async void RefreshPlayerList_Click(object sender, RoutedEventArgs e) 
    {
        await RefreshPlayersAsync();
        LoadBannedPlayersFromFile();
    }

    private async void BanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerInfo selectedPlayer) return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.BanPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    private async void KickPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerDataGrid.SelectedItem is not PlayerInfo selectedPlayer) return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.KickPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        await RefreshPlayersAsync();
    }

    private async void UnBanPlayerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (BanListDataGrid.SelectedItem is not PlayerInfo selectedPlayer)
            return;

        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(_ssmPathManager.ServerSettings);
        await RCONClient.UnbanPlayerAsync("127.0.0.1", serverSettings.EchoPort, selectedPlayer.SteamId);
        LoadBannedPlayersFromFile();
        await RefreshPlayersAsync();
    }

    #endregion

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

    private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string target = Path.Combine(destDir, file.Name);
            file.CopyTo(target, true);
        }

        if (copySubDirs)
        {
            foreach (DirectoryInfo sub in dir.GetDirectories())
            {
                string targetSub = Path.Combine(destDir, sub.Name);
                DirectoryCopy(sub.FullName, targetSub, true);
            }
        }
    }

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
            }
            catch
            {
                BackgroundImage.Source = null;
            }
        }
        else
        {
            BackgroundImage.Source = null;
        }
    }

    /// <summary>
    /// 生成时间戳字符串
    /// </summary>
    /// <param name="format" 时间戳格式/>
    /// <returns>格式化后的时间戳字符串</returns>
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
    public void ShowLogMsg(string message, Brush color, LogType logType = LogType.MainConsole)
    {
        if (Dispatcher.CheckAccess())
            InternalShowLogMsg(logType, message, color);
        else
            Dispatcher.Invoke(() => InternalShowLogMsg(logType, message, color));
    }
}


