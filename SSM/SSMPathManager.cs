using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoulmaskServerManager
{
    public class SSMPathManager
    {
        private readonly string _rootDir;
        private readonly string _serverPath;

        public string SettingsFile => Path.Combine(_rootDir, "SSMSettings.json");
        public string ServerFilesDir => _serverPath;
        public string SavedDir => Path.Combine(ServerFilesDir, "WS", "Saved");
        public string ModDir => Path.Combine(ServerFilesDir, "WS", "Mods");
        public string PluginDir => Path.Combine(ServerFilesDir, "WS", "Plugins");
        public string ServerSettings => Path.Combine(ServerFilesDir, "SaveData", "Settings", "ServerSettings.json");
        public string DedicatedPath => Path.Combine(SavedDir, "Worlds", "Dedicated");
        public string LogsDir => Path.Combine(SavedDir, "Logs");
        public string LogsPath => Path.Combine(LogsDir, "WS.log");
        public string ConfigDir => Path.Combine(SavedDir, "Config", "WindowsServer");
        public string GameIniPath => Path.Combine(ConfigDir, "Game.ini");
        public string EngineIniPath => Path.Combine(ConfigDir, "Engine.ini");
        public string GameplaySettingsPath => Path.Combine(SavedDir, "GameplaySettings", "GameXishu.json");
        public string GameplayDefaultsPath => Path.Combine(SavedDir, "GameplaySettings", "GameXishu_default.json");
        public string SaveDataSettingsDir => Path.Combine(ServerFilesDir, "SaveData", "Settings");
        public string GameXishuDefaultPath => Path.Combine(SaveDataSettingsDir, "GameXishu_Default.json");
        public string ServerExePath => Path.Combine(ServerFilesDir, "WS", "Binaries", "Win64", "WSServer-Win64-Shipping.exe");
        public string BanListPath => Path.Combine(SavedDir, "BlackAccountList.txt");
        public string MuteListPath => Path.Combine(SavedDir, "BanSpeek.txt");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public SSMPathManager(string rootDir, Server server)
        {
            _rootDir = rootDir;
            _serverPath = server.Path;
        }
    }
}
