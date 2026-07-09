using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModernWpf;
using ModernWpf.Controls;
using System.Text.Encodings.Web;
using System.Windows.Media;

namespace SoulmaskServerManager;
public class MainSettings : PropertyChangedBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private ObservableCollection<Server> _servers = new();
    public ObservableCollection<Server> Servers
    {
        get => _servers;
        set => SetField(ref _servers, value);
    }
    public AppSettings AppSettings { get; set; } = new AppSettings();
    public Webhook WebhookSettings { get; set; } = new Webhook();
    public List<Mod> DownloadedMods { get; set; } = new List<Mod>();
    public List<string> MainServers { get; set; } = new List<string>();

    /// <summary>
    /// Saves the specified <see cref="MainSettings"/> object.
    /// </summary>
    /// <param name="settings">The <see cref="MainSettings"/> object to save.</param>
    public static void Save(MainSettings settings)
    {
        string dir = Directory.GetCurrentDirectory() + @"\SSMSettings.json";
        string SettingsJSON = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(dir, SettingsJSON);
    }

    /// <summary>
    /// Loads a <see cref="MainSettings"/> object from rootdirectory and returns it.
    /// </summary>
    /// <returns>The loaded <see cref="MainSettings"/> object.</returns>
    public static MainSettings LoadManagerSettings()
    {
        string dir = Directory.GetCurrentDirectory() + @"\SSMSettings.json";
        if (File.Exists(dir))
        {
            using (StreamReader sr = new(dir, false))
            {
                string SettingsJSON = sr.ReadToEnd();
                MainSettings LoadedSettings = JsonSerializer.Deserialize<MainSettings>(SettingsJSON);
                return LoadedSettings;
            }
        }
        else
        {
            ContentDialog yesDialog = new()
            {
                Content = $"未找到管理器配置文件(SSMSettings.json)，设置未能导入。",
                PrimaryButtonText = "是",
            };
            yesDialog.ShowAsync();
            MainSettings DefaultSettings = new MainSettings();
            return DefaultSettings;
        }

    }
}

/// <summary>
/// Object containing information about a <see cref="Server"/>.
/// </summary>
public class Server : PropertyChangedBase
{
    private string _ssmServerName = "灵魂面甲服务器";
    public string ssmServerName
    {
        get => _ssmServerName;
        set => SetField(ref _ssmServerName, value);
    }

    private string _path = Directory.GetCurrentDirectory() + @"\Server";
    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }
    private bool _firstStart = true;
    public bool FirstStart
    {
        get => _firstStart;
        set => SetField(ref _firstStart, value);
    }
    public LaunchSettings LaunchSettings { get; set; } = new LaunchSettings();
    public RCONServerSettings RconServerSettings { get; set; } = new RCONServerSettings();
    private bool _autoRestart = false;
    public bool AutoRestart
    {
        get => _autoRestart;
        set => SetField(ref _autoRestart, value);
    }
    private bool _autoStart = false;
    public bool AutoStart
    {
        get => _autoStart;
        set => SetField(ref _autoStart, value);
    }
    private ServerWebhook _webhookMessages = new();
    public ServerWebhook WebhookMessages
    {
        get => _webhookMessages;
        set => SetField(ref _webhookMessages, value);
    }
    [JsonIgnore]
    public ServerRuntime Runtime { get; set; } = new ServerRuntime();
    private List<string> _installedMods = new();
    public List<string> SubscribedMods
    {
        get => _installedMods;
        set => SetField(ref _installedMods, value);
    }
    private bool _logFileExists = false;
    public bool LogFileExists
    {
        get => _logFileExists;
        set => SetField(ref _logFileExists, value);
    }
    private bool _runWithoutWindow = false;
    public bool RunWithoutWindow
    {
        get => _runWithoutWindow;
        set => SetField(ref _runWithoutWindow, value);
    }
    public string UniqueId { get; set; }
}

public class ServerWebhook : PropertyChangedBase
{
    private bool _enabled = false;
    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }
    private string _startServer = "正在启动服务器。";
    public string StartServer
    {
        get => _startServer;
        set => SetField(ref _startServer, value);
    }
    private string _stopServer = "正在关闭服务器。";
    public string StopServer
    {
        get => _stopServer;
        set => SetField(ref _stopServer, value);
    }
    private string _serverReady = "服务器启动成功。";
    public string ServerReady
    {
        get => _serverReady;
        set => SetField(ref _serverReady, value);
    }
    private string _attemptStart3 = "服务器尝试重新启动3次未成功，正在禁用自动重新启动。";
    public string AttemptStart3
    {
        get => _attemptStart3;
        set => SetField(ref _attemptStart3, value);
    }
    private string _serverCrash = "服务器意外停止，正在重新启动。";
    public string ServerCrash
    {
        get => _serverCrash;
        set => SetField(ref _serverCrash, value);
    }
    private bool _broadcastIP = false;
    public bool BroadcastIP
    {
        get => _broadcastIP;
        set => SetField(ref _broadcastIP, value);
    }
    private bool _broadcastSteamID = false;
    public bool BroadcastSteamID
    {
        get => _broadcastSteamID;
        set => SetField(ref _broadcastSteamID, value);
    }
}

/// <summary>
/// Property of <see cref="Server"/> used to track runtime.
/// </summary>
public class ServerRuntime : PropertyChangedBase
{
    public Process? Process { get; set; }
    public bool UserStopped { get; set; } = false;
    public int RestartAttempts { get; set; } = 0;
    public System.Timers.Timer BackupCleanTimer { get; set; }
    public enum ServerState
    {
        已停止,
        运行中,
        更新中
    }
    private ServerState _state = ServerState.已停止;
    public ServerState State
    {
        get => _state;
        set => SetField(ref _state, value);
    }
    public int Pid { get; set; } = 0;
}

/// <summary>
/// Property of <see cref="Server"/> used to fetch RCON Settings.
/// </summary>
public class RCONServerSettings : PropertyChangedBase
{
    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }
    private string _ipAddress = "127.0.0.1";
    public string IPAddress
    {
        get => _ipAddress;
        set => SetField(ref _ipAddress, value);
    }
    private int _port = 19000;
    public int Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }
    private int _echoPort = 18888;
    public int EchoPort
    {
        get => _echoPort;
        set => SetField(ref _echoPort, value);
    }
    private string _password = "";
    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

}

/// <summary>
/// Property of <see cref="Server"/> used to fetch Launch Settings.
/// </summary>
public class LaunchSettings
{
    public string DisplayName { get; set; } = "可在此处填写展示名称";
}

/// <summary>
/// Property of <see cref="MainSettings"/> used to for application settings.
/// </summary>
public class AppSettings : PropertyChangedBase
{
    private bool _verifyUpdates = true;
    public bool VerifyUpdates
    {
        get => _verifyUpdates;
        set => SetField(ref _verifyUpdates, value);
    }
    private bool _autoUpdate = false;
    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetField(ref _autoUpdate, value);
    }
    private bool _autoUpdateApp = true;
    public bool AutoUpdateApp
    {
        get => _autoUpdateApp;
        set => SetField(ref _autoUpdateApp, value);
    }
    private bool _showSteamWindow = true;
    public bool ShowSteamWindow
    {
        get => _showSteamWindow;
        set => SetField(ref _showSteamWindow, value);
    }
    private int _autoUpdateInterval = 60;
    public int AutoUpdateInterval
    {
        get => _autoUpdateInterval;
        set => SetField(ref _autoUpdateInterval, value);
    }
    private string _lastUpdateTimeUNIX = "";
    public string LastUpdateTimeUNIX
    {
        get => _lastUpdateTimeUNIX;
        set => SetField(ref _lastUpdateTimeUNIX, value);
    }
    private string _lastUpdateTime = "服务端上次更新：未知";
    public string LastUpdateTime
    {
        get => _lastUpdateTime;
        set => SetField(ref _lastUpdateTime, value);
    }

    private string _newversion = "";
    public string NewVersion
    {
        get => _newversion;
        set => SetField(ref _newversion, value);
    }
    private bool _darkMode = false;
    public bool DarkMode
    {
        get => _darkMode;
        set => SetField(ref _darkMode, value);
    }
    private bool _autoLoadEditor = true;
    public bool AutoLoadEditor
    {
        get => _autoLoadEditor;
        set => SetField(ref _autoLoadEditor, value);
    }
    private bool _saveLogWhenCrash = false;
    public bool SaveLogWhenCrash
    {
        get => _saveLogWhenCrash;
        set => SetField(ref _saveLogWhenCrash, value);
    }
    private bool _enableAutoRestart = false;
    public bool EnableAutoRestart
    {
        get => _enableAutoRestart;
        set => SetField(ref _enableAutoRestart, value);
    }
    private int _autoRestartHour = 00;
    public int AutoRestartHour
    {
        get => _autoRestartHour;
        set => SetField(ref _autoRestartHour, value);
    }
    private int _autoRestartMin = 00;
    public int AutoRestartMin
    {
        get => _autoRestartMin;
        set => SetField(ref _autoRestartMin, value);
    }
    private int _autoRestartSec = 00;
    public int AutoRestartSec
    {
        get => _autoRestartSec;
        set => SetField(ref _autoRestartSec, value);
    }
    private int _closeExecuteSelect = 0;
    public int CloseExecuteSelect
    {
        get => _closeExecuteSelect;
        set => SetField(ref _closeExecuteSelect, value);
    }
    private bool _hasNewVersion;
    public bool HasNewVersion
    {
        get => _hasNewVersion;
        set
        {
            _hasNewVersion = value;
            OnPropertyChanged(nameof(HasNewVersion));
        }
    }
    private string _wallpaperPath = "";
    public string WallpaperPath
    {
        get => _wallpaperPath;
        set => SetField(ref _wallpaperPath, value);
    }
    private double _wallpaperOpacity = 0.3;
    public double WallpaperOpacity
    {
        get => _wallpaperOpacity;
        set => SetField(ref _wallpaperOpacity, value);
    }
    private bool _wallpaperEnabled = false;
    public bool WallpaperEnabled
    {
        get => _wallpaperEnabled;
        set => SetField(ref _wallpaperEnabled, value);
    }
}

/// <summary>
/// Property of <see cref="MainSettings"/> used for webhook settings.
/// </summary>
public class Webhook : PropertyChangedBase
{
    private bool _enabled = false;
    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }
    public string URL { get; set; } = "";
    private string _updateFound = "该游戏有更新，正在开始自动更新。";
    public string UpdateFound
    {
        get => _updateFound;
        set => SetField(ref _updateFound, value);
    }
    private string _updateWait = "5分钟后服务器关闭(用于更新)。";
    public string UpdateWait
    {
        get => _updateWait;
        set => SetField(ref _updateWait, value);
    }
}
public class Mod : PropertyChangedBase
{
    private bool _downloaded = false;
    public bool Downloaded
    {
        get => _downloaded;
        set => SetField(ref _downloaded, value);
    }
    private string _modName = "";
    public string ModName
    {
        get => _modName;
        set => SetField(ref _modName, value);
    }
    private string _localVersion = "1.3.1";
    public string LocalVersion
    {
        get => _localVersion;
        set => SetField(ref _localVersion, value);
    }
    private string _newVersion = "";
    public string NewVersion
    {
        get => _newVersion;
        set => SetField(ref _newVersion, value);
    }

}


/// <summary>
/// Class to implement INotifyPropertyChanged easily
/// </summary>
public class PropertyChangedBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value,
    [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}