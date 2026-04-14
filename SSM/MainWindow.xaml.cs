using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.ComponentModel;
using System.Text.Json.Nodes;
using SoulMaskServerManager.RCON;
using ModernWpf.Controls;
using ModernWpf;
using Newtonsoft.Json;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using static SoulMaskServerManager.LogManager;
using static SoulMaskServerManager.PlayerDataManager;
using System.Net;
using System.Windows.Controls.Ribbon;
using System.Collections.ObjectModel;
using System.Windows.Data;
using SoulMaskServerManager;
using SoulmaskServerManager;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Runtime;

namespace SoulMaskServerManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainSettings SsmSettings = new();
    private static dWebhook DiscordSender = new();
    private static HttpClient HttpClient = new();
    private PeriodicTimer? AutoUpdateTimer;
    private PeriodicTimer? AutoRestartTimer;
    private PeriodicTimer? AutoDelectSaveTimer;
    private RemoteConClient RCONClient;
    //private ChangeSaveFileEditor changeSaveFileEditor = new();

    private static MainWindow? _instance;
    public static MainWindow Instance => _instance ?? throw new InvalidOperationException("MainWindow未初始化");

    // 自动清理存档计时器
    private System.Timers.Timer _backupCleanTimer;
    // 存档路径
    //private string _backupFolderPath => Path.Combine(_currentServer.Path, "WS", "Saved", "World", "Dedicated", $"{(_currentServer.ServerMap == 0 ? "Level01_Main" : "DLC_Level01_Main")}");

    // 日志文件相对路径（基于当前服务器路径）
    private readonly Dictionary<LogType, string> _logTypeToTag = new Dictionary<LogType, string>
    {
        { LogType.WSServer, @"WS\Saved\Logs\WS.log" },
    };

    private Dictionary<string, LogType> _logTagToType;
    private Dictionary<LogType, RichTextBox> _logTypeToTexbox;
    private Dictionary<LogType, CheckBox> _logTypeToCheckbox;
    private string _activeLogType;

    // 日志监控器
    private Dictionary<LogType, FileSystemWatcher> _logWatchers = new Dictionary<LogType, FileSystemWatcher>();

    // 最大加载行数
    private const int MAX_LOG_LINES = 600;

    private LogManager LogManager;

    // 定时器（用于定时检查日志更新）
    private DispatcherTimer _logUpdateTimer;
    public System.Timers.Timer playerUpdateTimer;

    // 记录上次检查时的文件大小
    private Dictionary<LogType, long> _lastFileSizes = new Dictionary<LogType, long>();

    // 管理员列表文件路径
    private string AdminListPath => Path.Combine(_currentServer.Path, @"SaveData\Settings\adminlist.txt");

    // 服务器到玩家数据的映射
    private Dictionary<Server, ObservableCollection<VRisingPlayerInfo>> _serverPlayers = new();

    // 当前选中的服务器
    private Server _currentServer;

    private int serveridIndex = 0;

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
            //ServerIdMapping.Load();
            //ServerIdMapping.EnsureAllServersHaveIds();
            SsmSettings = MainSettings.LoadManagerSettings();
        }
        DataContext = SsmSettings;

        if (SsmSettings.AppSettings.DarkMode == true)
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        else
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;


        InitializeComponent();

        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;

        // 初始化定时器（每1秒检查一次）
        if (SsmSettings.Servers.Count != 0)
        {
            _logUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();
        }

        
        // 初始化日志控件映射
        _logTypeToTexbox = new Dictionary<LogType, RichTextBox>
        {
            { LogType.WSServer, SoulmaskLogTextBox },
            { LogType.MainConsole, MainMenuConsoleTextBox },
        };

        // 初始化自动滚动控件映射
        _logTypeToCheckbox = new Dictionary<LogType, CheckBox>
        {
            { LogType.WSServer, AutoScrollSoulmaskLog },
            { LogType.MainConsole, AutoScrollMainConsole },
        };
        
        _logTagToType = new Dictionary<string, LogType>
        {
            { "WSServer", LogType.WSServer },
            { "MainConsole", LogType.MainConsole },
        };

        // 绑定自动滚动复选框事件
        AutoScrollSoulmaskLog.Checked += AutoScrollCheckBox_CheckedChanged;
        AutoScrollSoulmaskLog.Unchecked += AutoScrollCheckBox_CheckedChanged;

        // 监听服务器选择变化
        ServerTabControl.SelectionChanged += async (s, e) =>
        {
            if (SsmSettings.Servers.Count == 0)
                return;
            if (ServerTabControl.SelectedItem is Server selectedServer)
            {
                _currentServer = selectedServer;
                if (_currentServer.FirstStart)
                    return;
                if (!string.IsNullOrEmpty(_activeLogType))
                {
                    LoadLogByType(_logTagToType[_activeLogType], true);
                }
            }
        };

        SsmSettings.AppSettings.PropertyChanged += AppSettings_PropertyChanged;
        SsmSettings.Servers.CollectionChanged += Servers_CollectionChanged; // MVVM method not working
        SsmSettings.AppSettings.Version = new AppSettings().Version;

        // 初始化日志
        ShowLogMsg($"灵魂面甲服务端管理器(SSM)启动成功。", Brushes.Lime);
        ShowLogMsg(((SsmSettings.Servers.Count > 0) ? $"{SsmSettings.Servers.Count} 个服务器从设置中加载成功。" : $"未找到服务器，请点击“添加服务器”以开始使用。"), SsmSettings.Servers.Count > 0 ? Brushes.Lime : Brushes.Yellow);

        //ScanForServers();
        SetupTimer();
        SetAutoRestartTimer();

        if (File.Exists("VSMUpdater.exe") && File.Exists("VSMUpdater.deps.json") && File.Exists("VSMUpdater.dll") && File.Exists("VSMUpdater.runtimeconfig.json"))
        {
            File.Delete("VSMUpdater.exe");
            File.Delete("VSMUpdater.dll");
            File.Delete("VSMUpdater.deps.json");
            File.Delete("VSMUpdater.runtimeconfig.json");
            ShowLogMsg($"旧版更新程序清理完成。", Brushes.Gray);
        }

        if (SsmSettings.AppSettings.AutoUpdateApp == true)
            LookForUpdate();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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

    // 最小化到托盘
    private void MinimizeToTray()
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
        TrayIcon.ShowBalloonTip("已最小化", "程序在托盘运行中", BalloonIcon.Info);
    }

    // 托盘菜单：显示窗口
    private void TrayIcon_ShowWindow(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        TrayIcon.Visibility = Visibility.Visible;
        Activate();
    }

    // 托盘菜单：退出程序
    private void TrayIcon_Exit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Visibility = Visibility.Collapsed;
        TrayIcon.Dispose();

        Closing -= MainWindow_Closing;
        Close();
    }

    // 窗口关闭后释放资源
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

    // 点击托盘显示或隐藏
    private void TrayIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    // 自动滚动复选框状态变化处理
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

    private void LogUpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // 获取当前日志类型对应的LogType
            if (!_logTagToType.TryGetValue(_activeLogType, out LogType logType))
                return;

            if (logType == LogType.MainConsole)
                return;

            if (!_logTypeToTag.TryGetValue(logType, out string relativePath))
                return;

            if (_currentServer == null || _currentServer.Runtime?.State != ServerRuntime.ServerState.运行中)
                return;

            relativePath = Path.Combine(_currentServer.Path, relativePath);

            // 检查文件是否存在
            if (File.Exists(relativePath))
            {
                long currentSize = new FileInfo(relativePath).Length;
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
            ShowLogMsg($"定时器检查日志更新失败：{ex.Message}", Brushes.Red);
        }
    }

    // 初始化日志监控器
    private void InitializeLogWatchers()
    {
        // 清除现有监控器
        foreach (var watcher in _logWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _logWatchers.Clear();

        if (_currentServer == null || !Directory.Exists(_currentServer.Path))
            return;

        foreach (var logType in _logTypeToTag.Keys)
        {
            string fullPath = Path.Combine(_currentServer.Path, _logTypeToTag[logType]);
            string dir = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            if (Directory.Exists(dir))
            {
                var watcher = new FileSystemWatcher(dir, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = false
                };
                watcher.Changed += (s, e) => OnLogFileChanged(logType);
                watcher.Renamed += (s, e) => OnLogFileChanged(logType);
                _logWatchers[logType] = watcher;
            }
        }
    }

    // 日志标签页切换事件（强制更新）
    private void LogTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentServer == null) return;

        if (LogTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is string logType)
        {
            // 先暂停其他的所有标签页
            foreach (var watcher in _logWatchers.Values)
            {
                watcher.EnableRaisingEvents = false;
            }
            _activeLogType = logType;
            LoadLogByType(_logTagToType[logType], forceRefresh: true);
            if (_currentServer.Runtime.State == ServerRuntime.ServerState.运行中)
            {
                StartActiveLogWatcher();
            }
        }
    }

    // 根据窗口类型加载日志文件
    private void LoadLogByType(LogType logType, bool forceRefresh = false)
    {
        if (_currentServer == null || !_logTypeToTexbox.ContainsKey(logType))
            return;

        if (logType == LogType.MainConsole)
        {
            RichTextBox mainLogTextBox = _logTypeToTexbox[logType];
            if (_logTypeToCheckbox[logType].IsChecked == true)
            {
                mainLogTextBox.ScrollToEnd();
            }
            return;
        }

        RichTextBox logBox = _logTypeToTexbox[logType];
        string fullPath = Path.Combine(_currentServer.Path, _logTypeToTag[logType]);

        try
        {
            // 仅在强制刷新或文件大小变化时重新加载
            if (forceRefresh || !_lastFileSizes.TryGetValue(logType, out long lastSize) ||
                new FileInfo(fullPath).Length != lastSize)
            {
                logBox.Document.Blocks.Clear();

                if (!File.Exists(fullPath))
                {
                    ShowLogMsg($"日志文件不存在：{fullPath}", Brushes.Yellow, logType);
                    ShowLogMsg($"请确保服务器有正常启动过至少一次", Brushes.Yellow, logType);
                    //if (logBox != SoulmaskLogTextBox)
                    //{
                    //    ShowLogMsg($"或当前服务器并不是Mod服务器", Brushes.Yellow, logType);
                    //}
                    return;
                }

                string[] lines = ReadLastNLines(fullPath, MAX_LOG_LINES);
                foreach (string line in lines)
                {
                    AppendLogLine(logType, line);
                }

                _lastFileSizes[logType] = new FileInfo(fullPath).Length;

                ShowLogMsg($"已加载最近 {lines.Length} 行日志", Brushes.Gray, logType);
            }

            if (_logTypeToCheckbox[logType].IsChecked == true)
            {
                logBox.ScrollToEnd();
            }
        }
        catch (Exception ex)
        {
            ShowLogMsg($"加载失败：{ex.Message}", Brushes.Red, logType);
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
                    if (line != null) // 避免空行干扰
                    {
                        buffer[bufferIndex] = line;
                        bufferIndex = (bufferIndex + 1) % lineCount;
                        totalLines++;
                    }
                }

                // 提取有效行（处理文件截断场景）
                int startIndex = totalLines > lineCount ? bufferIndex : 0;
                int count = Math.Min(lineCount, totalLines);

                for (int i = 0; i < count; i++)
                {
                    string line = buffer[(startIndex + i) % lineCount];
                    if (!string.IsNullOrEmpty(line)) // 过滤空行
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

        string logPath = Path.Combine(_currentServer.Path, _logTypeToTag[logType]);
        if (!File.Exists(logPath)) return;

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
            ShowLogMsg($"更新 {logType} 日志失败: {ex.Message}", Brushes.Red);
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

                        // 重新开启监控
                        MonitorSingleProcess(pid, (isRunning) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                server.Runtime.State = isRunning
                                    ? ServerRuntime.ServerState.运行中
                                    : ServerRuntime.ServerState.已停止;
                            });
                        });

                        StartBackupCleanTimer(server, serverSettings.AutoCleanInterval, serverSettings.AutoSaveCount);
                        ShowLogMsg($"[{server.ssmServerName}] 检测到正在运行，已自动关联进程", Brushes.Cyan);
                    }
                }
                else
                {
                    server.Runtime.State = ServerRuntime.ServerState.已停止;
                }
            }
            catch { }
        }

        await Task.CompletedTask;
    }

    // 更新服务器状态面板数据
    private void UpdateServerStatusUI()
    {
        if (_currentServer == null)
            return;

        if (ServerNameText != null)
            ServerNameText.Text = _currentServer.ssmServerName;

        var serverState = _currentServer.Runtime?.State ?? ServerRuntime.ServerState.已停止;
        string statusText = serverState == ServerRuntime.ServerState.运行中 ? "运行中" : "已停止";
        Brush statusBrush = serverState == ServerRuntime.ServerState.运行中 ? Brushes.Green : Brushes.Red;

        if (ServerStatusText != null)
        {
            ServerStatusText.Text = statusText;
            ServerStatusText.Foreground = statusBrush;
        }

        if (LastUpdatedText != null)
            LastUpdatedText.Text = $"最后更新: {DateTime.Now:HH:mm:ss}";
    }


    // 服务器状态变化时的处理
    private void OnServerStateChanged()
    {
        if (_currentServer == null || _currentServer.Runtime == null)
            return;

        ServerRuntime.ServerState currentState = _currentServer.Runtime.State;

        Dispatcher.Invoke(() =>
        {
            if (currentState == ServerRuntime.ServerState.运行中)
            {
                UpdateServerStatusUI();
                if (!string.IsNullOrEmpty(_activeLogType) && _logWatchers.TryGetValue(_logTagToType[_activeLogType], out var watcher))
                {
                    watcher.EnableRaisingEvents = true;
                    //ShowLogMsg("服务器正在运行，日志将实时更新", Brushes.Lime);
                }
            }
            else
            {
                foreach (var watcher in _logWatchers.Values)
                {
                    watcher.EnableRaisingEvents = false;
                }
                UpdateServerStatusUI();

                if (!string.IsNullOrEmpty(_activeLogType))
                {
                    //ShowLogMsg($"服务器状态：{currentState}，日志已停止更新", Brushes.Gray);
                }
            }
        });
    }

    // 初始化服务器状态监听
    private void InitializeServerStateListener()
    {
        if (_currentServer != null && _currentServer.Runtime != null)
        {
            _currentServer.Runtime.PropertyChanged -= OnRuntimePropertyChanged;
            _currentServer.Runtime.PropertyChanged += OnRuntimePropertyChanged;

            OnServerStateChanged();
        }
    }

    // 服务器运行时属性变化处理
    private void OnRuntimePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerRuntime.State))
        {
            OnServerStateChanged();
        }
    }

    // 刷新日志按钮点击（强制更新）
    private void RefreshLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType)
        {
            LoadLogByType(_logTagToType[logType], true);
        }
    }

    // 清空日志按钮点击
    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType && _logTagToType.ContainsKey(logType))
        {
            _logTypeToTexbox[_logTagToType[logType]].Document.Blocks.Clear();
            ShowLogMsg("日志已清空", Brushes.Gray, _logTagToType[logType]);
        }
    }

    // 保存日志按钮点击
    private async void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string logType && _logTagToType.ContainsKey(logType))
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = Path.GetFileName(_logTypeToTag[_logTagToType[logType]])
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(saveDialog.FileName, false, Encoding.UTF8))
                    {
                        var textRange = new TextRange(
                            _logTypeToTexbox[_logTagToType[logType]].Document.ContentStart,
                            _logTypeToTexbox[_logTagToType[logType]].Document.ContentEnd);
                        writer.Write(textRange.Text);
                    }
                    await ShowErrorDialog($"日志已保存至：{saveDialog.FileName}");
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog($"保存失败：{ex.Message}");
                }
            }
        }
    }

    // 打开日志按钮点击事件
    private async void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        //Server server = ((Button)sender).DataContext as Server;
        if (_currentServer == null)
        {
            await ShowErrorDialog($"未找到对应的服务器实例");
            return;
        }

        if (sender is not Button btn || btn.Tag is not string logType || !_logTagToType.ContainsKey(logType))
        {
            ShowLogMsg($"日志类型配置错误", Brushes.Red);
            return;
        }

        try
        {
            string logPath = string.Empty;
            switch (_logTagToType[logType])
            {
                case LogType.WSServer:
                    logPath = Path.Combine(_currentServer.Path, "WS", "Saved", "Logs", "WS.log");
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

            //ShowLogMsg($"已用默认程序打开日志：{logPath}", Brushes.Green);
        }
        catch (Exception ex)
        {
            // 捕获所有异常（如权限不足、无默认程序关联等）
            await ShowErrorDialog($"打开日志失败：{ex.Message}");
        }
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
    public void ShowLogMsg(string message, Brush color, LogType logType = LogType.MainConsole)
    {
        if (Dispatcher.CheckAccess())
        {
            InternalShowLogMsg(logType, message, color);
        }
        else
        {
            Dispatcher.Invoke(() => InternalShowLogMsg(logType, message, color));
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
        //if (lowerLine.Contains("[info"))
        //    return Brushes.LimeGreen;
        return Brushes.White;
    }

    private void StartActiveLogWatcher()
    {
        if (_logWatchers.TryGetValue(_logTagToType[_activeLogType], out var watcher))
        {
            watcher.EnableRaisingEvents = true;
        }
    }

    private async void LookForUpdate()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;
        int attempt = 0;

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "VSM-Client/1.0");

        try
        {
            string latestVersion = null;

            try
            {
                var response = await httpClient.GetAsync("https://gitee.com/aGHOSToZero/Soulmask-Server-Manager/raw/master/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                var response = await httpClient.GetAsync("https://raw.githubusercontent.com/aghosto/Soulmask-Server-Manager/refs/heads/master/VERSION");
                response.EnsureSuccessStatusCode();
                latestVersion = await response.Content.ReadAsStringAsync();
            }

            latestVersion = latestVersion.Trim();

            if (latestVersion != SsmSettings.AppSettings.Version)
            {
                SsmSettings.AppSettings.HasNewVersion = true;
                SsmSettings.AppSettings.NewVersion = latestVersion;
                ShowLogMsg($"发现新版本：{latestVersion}，可点击左下角更新", Brushes.Yellow);
            }
            else
            {
                SsmSettings.AppSettings.HasNewVersion = false;
                ShowLogMsg($"当前已是最新版本：{latestVersion}", Brushes.Lime);
            }
        }
        catch (HttpRequestException ex)
        {
            string errorMessage = "检查更新失败：网络异常";

            if (ex.InnerException != null)
            {
                string inner = ex.InnerException.Message.ToLower();
                if (inner.Contains("eof") || inner.Contains("closed")) errorMessage = "服务器连接关闭";
                else if (inner.Contains("timeout")) errorMessage = "连接超时";
                else if (inner.Contains("host") || inner.Contains("resolve")) errorMessage = "无法连接服务器";
                else if (ex.InnerException is System.Security.Authentication.AuthenticationException) errorMessage = "SSL安全认证失败";
            }
            else if (ex.StatusCode.HasValue)
            {
                errorMessage = $"服务器错误：{ex.StatusCode}";
            }

            ShowLogMsg(errorMessage, Brushes.Red);
            ShowLogMsg("无法检查更新，请检查网络后重试", Brushes.Red);
        }
        catch (Exception ex)
        {
            ShowLogMsg($"检查更新出错：{ex.Message}", Brushes.Red);
        }
    }

    /// <summary>
    /// Sets up the timer for AutoUpdates
    /// </summary>
    public void SetupTimer()
    {
        if (SsmSettings.AppSettings.AutoUpdate == true)
        {
#if DEBUG
            AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
#else
            AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
            AutoUpdateLoop();
        }
    }

    public void SetAutoRestartTimer()
    {
        if (SsmSettings.AppSettings.EnableAutoRestart == true)
        {
            ShowLogMsg($"自动重启已启动，重启时间为每日的 {SsmSettings.AppSettings.AutoRestartHour} 时 {SsmSettings.AppSettings.AutoRestartMin} 分 {SsmSettings.AppSettings.AutoRestartSec} 秒。", Brushes.Yellow);
            AutoRestartTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            AutoRestartLoop();
        }
    }

    public static void StartBackupCleanTimer(Server server, int deleteInterval, int backupAmount)
    {
        try
        {
            // 停止这个服务器自己的旧计时器
            if (server.Runtime.BackupCleanTimer != null)
            {
                server.Runtime.BackupCleanTimer.Stop();
                server.Runtime.BackupCleanTimer.Dispose();
            }

            double checkMinutes = Convert.ToDouble(deleteInterval);
            int keepCount = Convert.ToInt32(backupAmount);

            if (checkMinutes <= 0) checkMinutes = 10;
            if (keepCount <= 0) keepCount = 5;

            // 每个服务器创建自己独立的计时器
            server.Runtime.BackupCleanTimer = new System.Timers.Timer();
            server.Runtime.BackupCleanTimer.Interval = checkMinutes * 60 * 1000;
            server.Runtime.BackupCleanTimer.AutoReset = true;

            server.Runtime.BackupCleanTimer.Elapsed += (s, ev) => CleanOldBackups(server, keepCount);
            server.Runtime.BackupCleanTimer.Start();

            //ShowLogMsg($"✅ 自动存档清理已启动：{server.ssmServerName} | 每 {checkMinutes} 分钟 | 保留 {keepCount} 个", Brushes.Orange);
        }
        catch
        {
            //ShowLogMsg($"❌ {server.ssmServerName} 自动存档清理启动失败", Brushes.Red);
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

            // 执行删除
            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                    //ShowLogMsg($"🗑️ 已删除旧存档：{file.Name}", Brushes.Orange);
                }
                catch { }
            }

            //ShowLogMsg($"📁 存档清理完成：当前 {backupFiles.Count - filesToDelete.Count} 个（保留 {keepCount} 个）", Brushes.LightGreen);
        }
        catch
        {
            //ShowLogMsg("❌ 存档清理时出错", Brushes.Red);
        }
    }

    private async void AutoRestartLoop()
    {
        while (await AutoRestartTimer.WaitForNextTickAsync())
        {
            if (SsmSettings.AppSettings.ManagerSettingsClose)
            {
                if (SsmSettings.AppSettings.EnableAutoRestart)
                {
                    ShowLogMsg($"重载自动重启时间，重启时间为每日的 {SsmSettings.AppSettings.AutoRestartHour} 时 {SsmSettings.AppSettings.AutoRestartMin} 分 {SsmSettings.AppSettings.AutoRestartSec} 秒。", Brushes.Yellow);
                    SsmSettings.AppSettings.ManagerSettingsClose = false;
                }
            }
            bool timetoRestart = await CheckForRestart();
            if (timetoRestart == true && SsmSettings.Servers.Count > 0)
                AutoRestart();
        }
    }

    private async void AutoUpdateLoop()
    {
        while (await AutoUpdateTimer.WaitForNextTickAsync())
        {
            bool foundUpdate = await CheckForUpdate();
            if (foundUpdate == true && SsmSettings.Servers.Count > 0)
                AutoUpdate();
        }
    }

    private async Task<bool> CheckForRestart()
    {
        bool timetoRestart = false;
        if (DateTime.Now.Hour == SsmSettings.AppSettings.AutoRestartHour &&
                DateTime.Now.Minute == SsmSettings.AppSettings.AutoRestartMin &&
                    DateTime.Now.Second == SsmSettings.AppSettings.AutoRestartSec)
        {
            AutoRestart();
            //ShowLogMsg("自动重启中", Brushes.Yellow);
        }
        return timetoRestart;
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
            ShowLogMsg($"当前无正在运行的服务器，自动重启未生效。", Brushes.Yellow);
            return;
        }

        ShowLogMsg($"正在自动重启 {runningServers.Count} 个服务器" + ((runningServers.Count > 0) ? $" ,在此之前即将关闭 {runningServers.Count} 个服务器" : ""), Brushes.Yellow);
        foreach (Server server in runningServers)
        {
            await RestartServer(server);
        }
        ShowLogMsg($"自动重启完成。", Brushes.Lime);

    }

    private void SendDiscordMessage(string message)
    {
        if (SsmSettings.WebhookSettings.Enabled == false || message == "")
            return;

        if (SsmSettings.WebhookSettings.URL == "")
        {
            //ShowLogMsg("Discord webhook尝试发送消息，但URL未定义。", Brushes.Yellow);
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
        ShowLogMsg("未找到SteamCMD，正在下载...", Brushes.Yellow);
        byte[] fileBytes = await HttpClient.GetByteArrayAsync(@"https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip");
        await File.WriteAllBytesAsync(workingDir + @"\steamcmd.zip", fileBytes);
        if (File.Exists(workingDir + @"\SteamCMD\steamcmd.exe") == true)
        {
            File.Delete(workingDir + @"\SteamCMD\steamcmd.exe");
        }
        ShowLogMsg("解压中...", Brushes.Yellow);
        ZipFile.ExtractToDirectory(workingDir + @"\steamcmd.zip", workingDir + @"\SteamCMD");
        if (File.Exists(workingDir + @"\steamcmd.zip"))
        {
            File.Delete(workingDir + @"\steamcmd.zip");
        }

        ShowLogMsg("正在获取Soulmask Dedicated Server应用信息。", Brushes.Lime);
        await CheckForUpdate();

        return true;
    }

    private async Task<bool> UpdateGame(Server server)
    {
        if (server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogMsg($"服务器 {server.ssmServerName} 正在更新中，尝试终止现有SteamCMD进程...", Brushes.Yellow);
            KillAllSteamcmdProcesses();
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }
        if (server.Runtime.State != ServerRuntime.ServerState.已停止)
        {
            ShowLogMsg($"服务器 {server.ssmServerName} 状态为 {server.Runtime.State}，无法更新（仅允许已停止状态）", Brushes.Red);
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
            ShowLogMsg($"服务器目录不存在，正在创建: {server.Path}", Brushes.Lime);
            Directory.CreateDirectory(server.Path);
        }

        if (server.Runtime.Process != null && !server.Runtime.Process.HasExited)
        {
            ShowLogMsg($"服务器 {server.ssmServerName} 仍在运行中，无法更新", Brushes.Yellow);
            server.Runtime.State = ServerRuntime.ServerState.已停止;
            return false;
        }

        string steamCmdDir = Path.Combine(Directory.GetCurrentDirectory(), @"SteamCMD");
        string steamCmdPath = Path.Combine(steamCmdDir, "steamcmd.exe");

        if (!File.Exists(steamCmdPath))
        {
            ShowLogMsg("未找到SteamCMD，正在下载...", Brushes.Lime);

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
                ShowLogMsg("SteamCMD下载并安装成功", Brushes.Lime);
            }
            catch (Exception ex)
            {
                ShowLogMsg($"SteamCMD下载失败：{ex.Message}", Brushes.Red);
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
        }

        bool isNewInstall = !Directory.EnumerateFiles(server.Path).Any();
        string action = isNewInstall ? "下载" : "更新";

        ShowLogMsg($"正在{action}游戏服务器：{server.ssmServerName}，请等待...", Brushes.Lime);
        //ShowLogMsg($"若{action}成功但启动失败，请到设置中开启“显示SteamCMD窗口”", Brushes.Gray);

        if (SsmSettings.AppSettings == null)
        {
            ShowLogMsg("警告：应用设置未初始化，使用默认值", Brushes.Yellow);
            SsmSettings.AppSettings = new AppSettings();
        }

        string[] installScript = {
            $"force_install_dir \"{server.Path}\"",
            "login anonymous",
            "app_update 3017310 validate",
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
            steamcmd = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = steamCmdPath,
                    Arguments = parameters,
                    CreateNoWindow = !SsmSettings.AppSettings.ShowSteamWindow,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = server.Path,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            steamcmd.Start();
            steamcmd.OutputDataReceived += (sender, e) =>
            {
                if (hasError)
                    return;

                if (!string.IsNullOrEmpty(e.Data))
                {
                    // 检测到中文路径错误
                    if (e.Data.Contains("默认文件夹"))
                    {
                        ShowLogMsg("错误：路径包含中文，请不要在带有中文的目录中使用！", Brushes.Red);
                        hasError = true;
                        KillAllSteamcmdProcesses();
                        return;
                    }

                    if (e.Data.Contains("FAILED (No Connection)"))
                    {
                        ShowLogMsg("错误：服务器更新失败，请检查你的网络连接！", Brushes.Red);
                        hasError = true;
                        KillAllSteamcmdProcesses();
                        return;
                    }
                }
            };

            // 启动进程并异步读取输出
            steamcmd.BeginOutputReadLine();
            steamcmd.BeginErrorReadLine();
            await steamcmd.WaitForExitAsync();

            if (hasError)
            {
                ShowLogMsg($"{action}被强制终止（检测到错误）", Brushes.Red);
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }

            if (steamcmd.ExitCode == 0 && !hasError)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowLogMsg($"{action}成功：{server.ssmServerName}", Brushes.Lime);
                    LogTextBox.Text = $"{action}完成！";
                });
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return true;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    ShowLogMsg($"{action}失败，退出代码：{steamcmd.ExitCode}", Brushes.Red);
                    LogTextBox.Text = $"{action}失败";
                });
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                return false;
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                ShowLogMsg($"{action}出错：{ex.Message}", Brushes.Red);
                LogTextBox.Text = "操作出错";
            });
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
            if (File.Exists(scriptPath))
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch
                {

                }
            }
        }
    }
    private void KillAllSteamcmdProcesses()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("steamcmd"))
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                    ShowLogMsg($"终止SteamCMD进程（PID: {process.Id}）", Brushes.Yellow);
                }
            }
        }
        catch (Exception ex)
        {
            ShowLogMsg($"终止SteamCMD进程失败：{ex.Message}", Brushes.Red);
        }
    }

    private async Task<bool> StartServer(Server server)
    {
        if (server.Runtime.Process != null)
        {
            ShowLogMsg($"错误：{server.ssmServerName} 已在运行中", Brushes.Red);
            return false;
        }

        try
        {
            //SsmSettings = MainSettings.LoadManagerSettings();
            string serverSettingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
            string jsonString = File.ReadAllText(serverSettingsPath);
            ServerSettings jsonObject = JsonConvert.DeserializeObject<ServerSettings>(jsonString);

            // 找到当前启动的服务器的最新配置
            server = SsmSettings.Servers.FirstOrDefault(s => s.ssmServerName == server.ssmServerName) ?? server;

            ShowLogMsg($"启动服务器：{server.ssmServerName}{(server.Runtime.RestartAttempts > 0 ? $" 尝试 {server.Runtime.RestartAttempts}/3" : "")}", Brushes.Lime);

            string serverExePath = Path.Combine(server.Path, "StartServer.bat");
            string crossServerMode = "";
            string plusContent = "";
            string pvpMode = $"{(jsonObject.PVP == true ? " -pvp" : " -pve")}";
            string psw = "";
            string adminpsw = "";
            string reconSettings = "";

            if (!File.Exists(serverExePath))
            {

                ShowLogMsg("错误：StartServer.bat", Brushes.Red);
                return false;
            }

            if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StartServer) && server.WebhookMessages.Enabled)
            {
                SendDiscordMessage(server.WebhookMessages.StartServer);
            }
            
            if(jsonObject.ClusterMode != 0)
            {
                if (jsonObject.ClusterMode == 1)
                    crossServerMode = $" -mainserverport={jsonObject.Port}";
                else if (jsonObject.ClusterMode == 2)
                {
                    
                    crossServerMode = $" -clientserverconnect={jsonObject.PublicIP}:{jsonObject.MainPort}";
                }
                plusContent = $" -serverid={++serveridIndex}{crossServerMode}";
            }
            if (jsonObject.Password != "")
                psw = $" -PSW={jsonObject.Password}";

            if (jsonObject.GMPassword != "")
                adminpsw = $" -adminpsw={jsonObject.GMPassword}";

            //if (!server.FirstStart)
            //{
                try
                {
                    string batContent = 
$@"@echo off
pushd ""%~dp0""
start WSServer.exe {jsonObject.Map} -server %* -log -UTF8Output -MULTIHOME=0.0.0.0 -EchoPort=18888 -forcepassthrough{plusContent}{pvpMode} -PORT={jsonObject.Port} -QueryPort={jsonObject.QueryPort} -MaxPlayers={jsonObject.MaxPlayers} -saving={jsonObject.Saving} -backup={jsonObject.Backup} -backupinterval={jsonObject.AutoSaveInterval}{reconSettings}{psw}{adminpsw} -mod=""{jsonObject.Mods}""
popd
exit /B";
                    File.WriteAllText(serverExePath, batContent, System.Text.Encoding.UTF8);
                    await Task.Delay(1000);

                    //ShowLogMsg("StartServer.bat 已生成！\n路径：" + path, Brushes.Yellow);
                }
                catch (Exception ex)
                {
                    //MessageBox.Show("生成失败：" + ex.Message);
                }
            //}

            Process.Start(new ProcessStartInfo
            {
                FileName = serverExePath,
                UseShellExecute = true
            });
            ShowLogMsg("等待服务器窗口启动...", Brushes.Gray);

            // 等待真正的服务器窗口出现（最多等15秒）
            int realPid = -1;
            int retry = 0;
            string realWindowTitle = $@"{server.Path}\WS\Binaries\Win64\WSServer-Win64-Shipping.exe";

            while (retry < 30 && realPid == -1)
            {
                await Task.Delay(500);
                realPid = GetProcessIdByWindowTitle(realWindowTitle);
                retry++;
            }

            if (realPid == -1)
            {
                ShowLogMsg("错误：未找到服务器窗口", Brushes.Red);
                return false;
            }

            Process realServerProcess = Process.GetProcessById(realPid);
            realServerProcess.EnableRaisingEvents = true;
            realServerProcess.Exited += (sender, e) => ServerProcessExited(sender, e, server);

            server.Runtime.Process = realServerProcess;
            server.Runtime.Pid = realPid;
            server.Runtime.State = ServerRuntime.ServerState.运行中;
            server.Runtime.UserStopped = false;
            //server.FirstStart = false;

            //ShowLogMsg($"启动成功！服务器PID：{realPid}", Brushes.AliceBlue);
            ShowLogMsg($"启动完成：{server.ssmServerName} | {(jsonObject.Map == "Level01_Main" ? "云雾之森" : "金色浮沙")}", Brushes.Lime);

            StartBackupCleanTimer(server, jsonObject.AutoCleanInterval, jsonObject.AutoSaveCount);

            if (server.RunWithoutWindow)
            {
                HideWindow(realServerProcess.MainWindowHandle);
            }
            //server.FirstStart = false;
            MainSettings.Save(SsmSettings);
            return true;
        }
        catch (Exception ex)
        {
            ShowLogMsg($"启动服务器失败：{ex.Message}", Brushes.Red);
            return false;
        }
    }

    // 系统API
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // 隐藏窗口
    private void HideWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            ShowWindow(hwnd, 0); // 0 = 隐藏
    }

    private int GetProcessIdByWindowTitle(string windowTitle)
    {
        try
        {
            IntPtr hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero) return -1;
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }
        catch { return -1; }
    }


    private async void MonitorSingleProcess(int pid, Action<bool> onStateChanged)
    {
        await Task.Run(async () =>
        {
            while (true)
            {
                bool isRunning = IsProcessRunning(pid);
                onStateChanged?.Invoke(isRunning);

                if (!isRunning) 
                    break;
                await Task.Delay(1000);
            }
        });
    }

    // 判断单个进程是否存在
    private bool IsProcessRunning(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    private async Task SendRconRestartMessage(Server server)
    {
        ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(server.Path);
        RCONClient = new()
        {
            UseUtf8 = true
        };

        RCONClient.OnLog += async message =>
        {
            if (message == "Authentication success.")
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                RCONClient.SendCommand("announcerestart 5", result =>
                {
                    //Do nothing
                });
            }

        };

        RCONClient.OnConnectionStateChange += state =>
        {
            if (state == RemoteConClient.ConnectionStateChange.Connected)
            {
                RCONClient.Authenticate(serverSettings.Rcon.Password);
            }
        };

        RCONClient.Connect(serverSettings.PublicIP, serverSettings.Rcon.Port);
        await Task.Delay(TimeSpan.FromSeconds(3));
        RCONClient.Disconnect();
    }

    private async void ScanForServers()
    {
        int foundServers = 0;

        Process[] serverProcesses = Process.GetProcessesByName("WSServer.exe");
        foreach (Process process in serverProcesses)
        {
            foreach (Server server in SsmSettings.Servers)
            {
                if (process.MainModule.FileName == server.Path + @"\WS\Binaries\Win64\WSServer-Win64-Shipping.exe")
                {
                    server.Runtime.State = ServerRuntime.ServerState.运行中;
                    process.EnableRaisingEvents = true;
                    process.Exited += new EventHandler((sender, e) => ServerProcessExited(sender, e, server));
                    server.Runtime.Process = process;
                    foundServers++;
                }
            }
        }

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.AutoStart == true && server.Runtime.State == ServerRuntime.ServerState.已停止)
            {
                await StartServer(server);
            }
        }

        if (foundServers > 0)
        {
            ShowLogMsg($"已找到 {foundServers} 个服务器正在运行。", Brushes.Lime);
        }
    }

    private async void AutoUpdate()
    {
        SendDiscordMessage(SsmSettings.WebhookSettings.UpdateFound);

        if (!File.Exists(Directory.GetCurrentDirectory() + @"\SteamCMD\steamcmd.exe"))
        {
            await UpdateSteamCMD();
        }

        List<Task> serverTasks = new List<Task>();
        List<Server> runningServers = new List<Server>();

        foreach (Server server in SsmSettings.Servers)
        {
            if (server.Runtime.State == ServerRuntime.ServerState.运行中)
            {
                runningServers.Add(server);
            }
        }

        foreach (Server server in runningServers)
        {
            ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(server.Path);
            if (serverSettings.Rcon.Enabled == true)
            {
                await SendRconRestartMessage(server);
            }
        }

        if (SsmSettings.WebhookSettings.Enabled == true && SsmSettings.WebhookSettings.URL != "" && runningServers.Count > 0)
        {
            SendDiscordMessage(SsmSettings.WebhookSettings.UpdateWait);
#if DEBUG
            await Task.Delay(TimeSpan.FromSeconds(10));
#else
            await Task.Delay(TimeSpan.FromMinutes(5));
#endif
        }

        foreach (Server server in runningServers)
        {
            serverTasks.Add(StopServer(server));
        }

        ShowLogMsg($"正在自动更新 {SsmSettings.Servers.Count} 个服务器。" + ((runningServers.Count > 0) ? $"在此之前即将关闭 {runningServers.Count} 个服务器。" : ""), Brushes.Yellow);

        await Task.WhenAll(serverTasks.ToArray());
        serverTasks.Clear();

        foreach (Server server in SsmSettings.Servers)
        {
            await UpdateGame(server);
        }

        foreach (Server server in runningServers)
        {
            serverTasks.Add(StartServer(server));
        }

        await Task.WhenAll(serverTasks.ToArray());
        ShowLogMsg($"自动更新完成。", Brushes.Lime);
    }

    private async Task<bool> StopServer(Server server)
    {
        if (server.Runtime.Process == null || server.Runtime.Process.HasExited)
        {
            ShowLogMsg($"服务器 {server.ssmServerName} 未运行或已退出", Brushes.Yellow);
            server.Runtime.Process = null;
            return true;
        }

        // 发送关闭通知
        if (SsmSettings.WebhookSettings.Enabled && !string.IsNullOrEmpty(server.WebhookMessages.StopServer) && server.WebhookMessages.Enabled)
        {
            SendDiscordMessage(server.WebhookMessages.StopServer);
        }

        server.Runtime.UserStopped = true;

        try
        {
            int processId = server.Runtime.Pid;
            Process process = Process.GetProcessById(processId);
            if (process != null && !process.HasExited)
            {
                bool gracefulShutdown = await TryGracefulShutdownAsync(process, timeoutSeconds: 90);
                if (!gracefulShutdown)
                {
                    //ShowLogMsg($"服务器 {server.ssmServerName} 未能在30秒内响应，尝试强制关闭...", Brushes.Yellow);
                    //process.Kill();
                    //using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    //await process.WaitForExitAsync(killCts.Token);
                }
                server.Runtime.State = ServerRuntime.ServerState.已停止;
                server.Runtime.Process = null;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试优雅地关闭进程
    /// </summary>
    private async Task<bool> TryGracefulShutdownAsync(Process process, int timeoutSeconds)
    {
        if (process == null || process.HasExited)
            return true;

        process.EnableRaisingEvents = true;
        var tcs = new TaskCompletionSource<bool>();
        process.Exited += (s, e) =>
        {
            tcs.TrySetResult(true);
        };

        if (process.HasExited)
            return true;

        FocusWindowAndSendCtrlC(process);

        //SendCtrlC(process);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await tcs.Task.WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(uint ctrlType);

    [DllImport("kernel32.dll")]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

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
                Content = $@"是否为该服务器数据创建备份？{Environment.NewLine}备份将保存于：{workingDir}\Backups\{serverName}_Bak.zip",
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
        ShowLogMsg($"正在查询服务器更新...", Brushes.Yellow);
        string json = await HttpClient.GetStringAsync("https://api.steamcmd.net/v1/info/3017310");
        JsonNode jsonNode = JsonNode.Parse(json);

        var version = jsonNode!["data"]["3017310"]["depots"]["branches"]["public"]["timeupdated"]!.ToString();

        if (version == SsmSettings.AppSettings.LastUpdateTimeUNIX)
        {
            SsmSettings.AppSettings.LastUpdateTimeUNIX = version;
            foundUpdate = false;
            if (SsmSettings.AppSettings.LastUpdateTimeUNIX != "")
                SsmSettings.AppSettings.LastUpdateTime = "服务器最近更新的时间：" + DateTimeOffset.FromUnixTimeSeconds(long.Parse(SsmSettings.AppSettings.LastUpdateTimeUNIX)).DateTime.ToString();

            MainSettings.Save(SsmSettings);
            ShowLogMsg($"当前游戏服务器已是最新版本。", Brushes.Lime);
            return foundUpdate;
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
        if (server == null)
        {
            ShowLogMsg($"传入的服务器为空！", Brushes.Red);
            return;
        }
        string logPath = Path.Combine(server.Path, "WS", "Saved", "Logs", "WS.log");
        
        try
        {
            // 首次启动时检查文件是否存在
            if (!server.LogFileExists)
            {
                if (!File.Exists(logPath))
                {
                    ShowLogMsg($"[{server.ssmServerName}] 日志文件不存在，请确保服务器已成功启动过一次", Brushes.Yellow);
                    await Task.Delay(5000);
                    if (!File.Exists(logPath))
                    {
                        ShowLogMsg($"[{server.ssmServerName}] 日志文件仍不存在，请手动启动服务器一次", Brushes.Red);
                        return;
                    }
                }

                //文件存在，设置标志位
                server.LogFileExists = true;
                ShowLogMsg($"[{server.ssmServerName}] 已检测到日志文件：{logPath}", Brushes.Green);
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
                        //ShowLogMsg("首次启动服务器，正在关闭以进行配置", Brushes.Yellow);
                        server.FirstStart = false;

                        //await StopServer(server);
                    }
                }
                else
                {
                    // 无新内容时短暂等待
                    await Task.Delay(100);
                    //ShowLogMsg($"等待日志更新", Brushes.Green);
                }
            }

            MainSettings.Save(SsmSettings);
            // 移动到文件末尾，只读取新内容
            fs.Seek(0, SeekOrigin.End);
            long initialPosition = fs.Position;
        }
        catch (FileNotFoundException ex)
        {
            server.LogFileExists = false;
            ShowLogMsg($"[{server.ssmServerName}] 日志文件已被删除，请重启服务器: {ex.Message}", Brushes.Red);
        }
        catch (Exception ex)
        {
            ShowLogMsg($"[{server.ssmServerName}] 日志处理错误：{ex.Message}", Brushes.Red);
        }

    }

    // 从日志行中提取特定值
    private string ExtractValue(string line, string startMarker, string endMarker)
    {
        int startIndex = line.IndexOf(startMarker);
        if (startIndex == -1)
            return null;

        startIndex += startMarker.Length;
        if (startIndex >= line.Length)
            return null;

        int endIndex = line.IndexOf(endMarker, startIndex);
        if (endIndex == -1)
            return null;

        return line.Substring(startIndex, endIndex - startIndex);
    }

    #region Events
    private async void ServerProcessExited(object sender, EventArgs e, Server server)
    {
        if (server == null)
        {
            ShowLogMsg("错误：服务器实例为空，无法处理进程退出事件", Brushes.Red);
            return;
        }

        if (server.Runtime == null)
        {
            ShowLogMsg($"错误：[{server.ssmServerName}] 运行时对象未初始化", Brushes.Red);
            return;
        }

        int exitCode = -1;
        Process exitedProcess = sender as Process;
        if (exitedProcess != null && !exitedProcess.HasExited)
        {
            try
            {
                exitCode = exitedProcess.ExitCode; // 仅在进程未释放时获取
            }
            catch (InvalidOperationException)
            {
                // 进程已释放时忽略
                exitCode = -1;
            }
        }

        server.Runtime.State = ServerRuntime.ServerState.已停止;
        server.Runtime.Process = null;

        StopBackupCleanTimer(server);
        ShowLogMsg($"{server.ssmServerName} 存档清理计时器已停止", Brushes.Gray);

        try
        {
            switch (exitCode)
            {
                case 1:
                    ShowLogMsg($"{server.ssmServerName} 崩溃了。", Brushes.Red);
                    break;
                case -2147483645:
                    ShowLogMsg($"{server.ssmServerName} 已中断（代码：-2147483645），可能是端口被占用。", Brushes.Red);
                    break;
                default:
                    //ShowLogMsg($"{server.ssmServerName} 已停止（退出码：{exitCode}）", Brushes.Yellow);
                    break;
            }

            if (server.Runtime.RestartAttempts >= 3)
            {
                ShowLogMsg($"服务器 '{server.ssmServerName}' 已尝试重启3次失败，禁用自动重启。", Brushes.Red);

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.AttemptStart3) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.AttemptStart3);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (LogManager.WriteServerCrashLog(server))
                    {
                        ShowLogMsg($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}", Brushes.Yellow);
                    }
                }

                ShowLogMsg("尝试最后一次重启服务器...", Brushes.Lime);
                await Task.Delay(5000); // 延长延迟，避免频繁重启

                bool restartSuccess = await StartServer(server);
                if (restartSuccess)
                {
                    ShowLogMsg($"{server.ssmServerName} 重启成功，重新启用自动重启。", Brushes.Green);
                    server.AutoRestart = true;
                    server.Runtime.RestartAttempts = 0;
                }
                else
                {
                    ShowLogMsg($"{server.ssmServerName} 最后一次重启失败，请手动检查。", Brushes.Red);
                }
                return;
            }

            if (server.AutoRestart && !server.Runtime.UserStopped)
            {
                server.Runtime.RestartAttempts++;
                ShowLogMsg($"{server.ssmServerName} 将自动重启（尝试 {server.Runtime.RestartAttempts}/3）", Brushes.Lime);

                if (SsmSettings.WebhookSettings.Enabled &&
                    !string.IsNullOrEmpty(server.WebhookMessages.ServerCrash) &&
                    server.WebhookMessages.Enabled)
                {
                    SendDiscordMessage(server.WebhookMessages.ServerCrash);
                }

                if (SsmSettings.AppSettings.SaveLogWhenCrash)
                {
                    if (LogManager.WriteServerCrashLog(server))
                    {
                        ShowLogMsg($"已创建崩溃日志：{Path.Combine(server.Path, "CrashLog")}", Brushes.Yellow);
                    }
                }

                await Task.Delay(3000);
                await StartServer(server);
            }
        }
        catch (Exception ex)
        {
            ShowLogMsg($"[{server.ssmServerName}] 处理进程退出时出错：{ex.Message}", Brushes.Red);
        }
    }

    private void AppSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {

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
                    LookForUpdate();
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
//                        AutoUpdateTimer = new PeriodicTimer(TimeSpan.FromMinutes(SsmSettings.AppSettings.AutoUpdateInterval));
#endif
                    AutoUpdateLoop();
                }
                break;
            case "DarkMode":
                if (SsmSettings.AppSettings.DarkMode == true)
                {
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                }
                else
                {
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                }
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

    // 服务器选择变化事件
    private void ServerTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerTabControl.SelectedItem is Server selectedServer)
        {
            _currentServer = selectedServer;
            InitializeLogWatchers();
            InitializeServerStateListener();

            // 加载当前日志并更新玩家状态
            if (_currentServer.Runtime?.State == ServerRuntime.ServerState.运行中)
            {
                string logPath = Path.Combine(_currentServer.Path, _logTypeToTag[LogType.WSServer]);
                if (File.Exists(logPath))
                {
                    UpdateServerStatusUI();    // 更新面板
                }
            }
        }
    }

    #endregion


    #region Buttons
    private async void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Server server)
        {
            ShowLogMsg("启动服务器失败：无效的按钮或服务器实例", Brushes.Red);
            return;
        }

        // 点击后马上禁用按钮，防止双击
        button.IsEnabled = false;
        try
        {
            //MainSettings.Save(SsmSettings);
            string batPath = Path.Combine(server.Path, "StartServer.bat");
            if (!File.Exists(batPath))
            {
                ShowLogMsg($"{server.ssmServerName} 启动失败：未找到启动文件（{batPath}）", Brushes.Red);
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
            ShowLogMsg($"{server.ssmServerName} 启动异常：{ex.Message}", Brushes.Red);
        }
        finally
        {
            // 走完流程后重新启用按钮
            button.IsEnabled = true;
        }
    }

    private async void UpdateServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogMsg($"错误：未找到服务器信息", Brushes.Red);
            return;
        }

        TextBlock buttonText = FindVisualChild<TextBlock>(button, "ButtonText");
        if (buttonText == null)
        {
            ShowLogMsg($"警告：未找到按钮文本元素", Brushes.Yellow);
            return;
        }

        try
        {
            // 禁用按钮防止重复点击
            button.IsEnabled = false;
            buttonText.Text = "取消更新";

            // 重新启用按钮，允许取消更新
            button.IsEnabled = true;

            bool success = await UpdateGame(server);

            if (success)
            {
                ShowLogMsg($"服务器 {server.ssmServerName} 更新成功！", Brushes.Lime);
            }
            else
            {
                ShowLogMsg($"服务器 {server.ssmServerName} 更新失败！", Brushes.Red);
            }
        }
        catch (Exception ex)
        {
            ShowLogMsg($"更新过程中发生错误：{ex.Message}", Brushes.Red);
        }
        finally
        {
            buttonText.Text = "更新服务器";
            button.IsEnabled = true;
        }
    }

    // 查找指定名称的子元素
    private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && typedChild.Name == name)
            {
                return typedChild;
            }

            T result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    private async void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Button button = (Button)sender;
            Server server = button.DataContext as Server;

            if (server == null)
            {
                ShowLogMsg($"未找到服务器信息，请确认服务器有正常运行过至少一次", Brushes.Red);
                return;
            }

            ShowLogMsg($"正在停止服务器：{server.ssmServerName}", Brushes.Yellow);
            bool wasRunning = server.Runtime?.State == ServerRuntime.ServerState.运行中;
            bool success = await StopServer(server);

            if (success)
            {
                ShowLogMsg($"已成功停止服务器：{server.ssmServerName}", Brushes.Lime);
            }
            else
            {
                if (wasRunning)
                {
                    LogManager.WriteServerCrashLog(server);
                }
                ShowLogMsg($"停止服务器失败：{server.ssmServerName}", Brushes.Red);
            }
        }
        catch (Exception ex)
        {
            ShowLogMsg($"停止服务器时出错：{ex.Message}", Brushes.Red);
            if (sender is Button button && button.DataContext is Server server)
            {
                if (server.Runtime?.State == ServerRuntime.ServerState.运行中)
                {
                    LogManager.WriteServerCrashLog(server);
                }
            }
        }
    }

    private async void RestartServerButton_Click(object sender, RoutedEventArgs e)
    {
        Button button = (Button)sender;
        Server server = button.DataContext as Server;

        if (server == null)
        {
            ShowLogMsg($"未找到服务器信息，请确认服务器有正常运行过至少一次", Brushes.Red);
            return;
        }
        bool restartSuccess = await RestartServer(server);
        if (restartSuccess == true)
        {
            ReadLog(server);
        }
    }

    private async Task<bool> RestartServer(Server server)
    {
        LogManager = new(this);
        ShowLogMsg($"正在重启服务器：" + server.ssmServerName, Brushes.Yellow);
        try
        {
            bool success = await StopServer(server);
            if (success)
            {
                if (!LogManager.WriteServerCrashLog(server))
                    ShowLogMsg($"备份 {server.ssmServerName} 服务器日志失败", Brushes.Red);
                else
                    ShowLogMsg($"已备份 {server.ssmServerName} 服务器日志", Brushes.Lime);

                ShowLogMsg($"正在启动服务器：{server.ssmServerName}", Brushes.Yellow);

                MainSettings.Save(SsmSettings);
                if (File.Exists(server.Path + @"\StartServer.bat"))
                {
                    success = await StartServer(server);
                }
                else
                {
                    success = false;
                    ShowLogMsg($"未找到服务器启动脚本，请检查服务器安装是否有误", Brushes.Red);
                    return success;
                }
                return true;
            }
            else
            {
                ShowLogMsg($"无法停止服务器：{server.ssmServerName}", Brushes.Red);
                LogManager.WriteServerCrashLog(server);
                return false;
            }

        }
        catch (Exception ex)
        {
            ShowLogMsg($"重启服务器发生错误：{ex.Message}", Brushes.Red);
            return false;
        }
    }

    private void ThemeSelect_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        else
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
    }

    private async void RemoveServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (server == null)
        {
            ShowLogMsg($"错误：找不到要删除的选定服务器", Brushes.Red);
            return;
        }
        if (server.Runtime.State == ServerRuntime.ServerState.运行中 || server.Runtime.State == ServerRuntime.ServerState.更新中)
        {
            ShowLogMsg($"错误：服务器正在运行或者更新中，请先停止服务器！", Brushes.Red);
            return;
        }
        bool success = await RemoveServer(server);
        if (!success)
            ShowLogMsg($"删除服务器时出错，或操作已中止。", Brushes.Red);
        else
            MainSettings.Save(SsmSettings);
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
                    return;
                    ShowLogMsg($"错误：{ex}", Brushes.Red);
                }
            }
        }
        catch (Exception ex)
        {
            return;
            ShowLogMsg($"错误：{ex}", Brushes.Red);
        }
    }

    private void GameSettingsEditor_Click(object sender, RoutedEventArgs e)
    {
        var gameSettingsEditor = Application.Current.Windows.OfType<GameSettingsEditor>().FirstOrDefault();
        if (gameSettingsEditor != null)
        {
            gameSettingsEditor.Activate();
            gameSettingsEditor.Topmost = true;
            gameSettingsEditor.Topmost = false;
        }
        else
        {
            if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
            {
                GameSettingsEditor gSettingsEditor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
                gSettingsEditor.Show();
            }
            else
            {
                GameSettingsEditor gSettingsEditor = new(SsmSettings.Servers);
                gSettingsEditor.Show();
            }
        }
    }

    private async void ManageAdminsButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (server == null)
        {
            ShowLogMsg($"未找到服务器信息，请确认服务器有正常运行过至少一次", Brushes.Red);
            return;
        }

        var aManager = Application.Current.Windows.OfType<AdminManager>().FirstOrDefault();
        if (aManager != null)
        {
            aManager.Activate();
            aManager.Topmost = true;
            aManager.Topmost = false;
        }
        else
        {
            if (!File.Exists(server.Path + @"\SaveData\Settings\adminlist.txt"))
            {
                ContentDialog closeFileDialog = new()
                {
                    Content = "找不到管理员文件(adminlist.txt)，请确保服务器安装正确。\n或尝试启动一次服务器",
                    PrimaryButtonText = "是",
                };
                await closeFileDialog.ShowAsync();
                return;
            }
            if (!Application.Current.Windows.OfType<AdminManager>().Any())
            {
                aManager = new AdminManager(server);
                aManager.Show();
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
            ShowLogMsg(ex.Message.ToString(), Brushes.Red);
        }

        if (Directory.Exists(path))
        {
            try
            {
                // 优化 Process.Start 参数，减少开销
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
                latestVersion = await HttpClient.GetStringAsync("https://gitee.com/aGHOSToZero/V-Rising-Server-Manager---Chinese/raw/master/VERSION");
            }

            latestVersion = latestVersion.Trim();

            if (latestVersion != SsmSettings.AppSettings.Version)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = $"软件有新版本可用于下载，需要关闭软件进行更新，是否更新？\r\r当前版本：{SsmSettings.AppSettings.Version}\r最新版本：{latestVersion}",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    Process.Start("SSMUpdater.exe");
                    Application.Current.MainWindow.Close();
                }
                else
                {
                    ShowLogMsg($"用户取消了本次软件更新。", Brushes.Yellow);
                }
            }
            else
            {
                ShowLogMsg($"当前已是最新版本：{latestVersion}", Brushes.Lime);
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("不知道这样的主机") || ex.Message.Contains("无法连接") || ex.Message.Contains("404"))
            {
                ShowLogMsg($"检查更新失败：网络异常或服务器不可用", Brushes.Red);
            }
            else
            {
                ShowLogMsg($"检查更新错误：{ex.Message}", Brushes.Red);
            }
        }
    }

    private void RconServerButton_Click(object sender, RoutedEventArgs e)
    {
        Server server = ((Button)sender).DataContext as Server;

        if (!Application.Current.Windows.OfType<RconConsole>().Any())
        {
            RconConsole rConsole = new(ServerTabControl.SelectedIndex);
            rConsole.Show();
        }
        //if (SsmSettings.AppSettings.AutoLoadEditor == true && !(ServerTabControl.SelectedIndex == -1))
        //{
        //    ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers, true, ServerTabControl.SelectedIndex);
        //    sSettingsEditor.Show();
        //}
        //else
        //{
        //    ServerSettingsEditor sSettingsEditor = new(SsmSettings.Servers);
        //    sSettingsEditor.Show();
        //}
    }

    // 修复工具
    private async void FixTools_Click(object sender, RoutedEventArgs e)
    {
        
    }

    // 右键菜单
    private async void RemoveAdminMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlayer = PlayerDataGrid.SelectedItem as VRisingPlayerInfo;
        if (selectedPlayer == null)
        {
            await ShowErrorDialog("请先选中一个玩家");
            return;
        }

        // 刷新列表显示
        PlayerDataGrid.Items.Refresh();
        ShowLogMsg($"已移除 {selectedPlayer.CharacterName} 的管理员权限", Brushes.Purple);
    }

    // 右键菜单：刷新玩家列表
    private void RefreshPlayerListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdminStatus();
    }

    // 刷新管理员状态
    private void RefreshAdminStatus()
    {

    }
    #endregion

    private async void RefreshServerStatus_Click(object sender, RoutedEventArgs e)
    {

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

    private void ChangeSaveFile_Click(object sender, RoutedEventArgs e)
    {

    }
}


