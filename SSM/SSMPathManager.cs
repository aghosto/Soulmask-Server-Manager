using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SoulmaskServerManager
{
    public class SSMPathManager
    {
        private readonly string _rootDir;
        private readonly string _serverPath;

        public string SettingsFile => Path.Combine(_rootDir, "SSMSettings.json");
        public string ServersDir => Path.Combine(_rootDir, "ServersDir");
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
        public string GameplaySettingsPath => Path.Combine(SavedDir, "GameplaySettings", "GameXishu.json");     // 服务器自己创建的系数配置文件
        public string SaveDataSettingsDir => Path.Combine(ServerFilesDir, "SaveData", "Settings");
        public string GameXishuDefaultPath => Path.Combine(SaveDataSettingsDir, "GameXishu_Custom.json");
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
            _rootDir = Path.GetFullPath(rootDir);
            _serverPath = Path.GetFullPath(server.Path);
        }

        /// <summary>
        /// 将程序根目录中的默认服务器目录迁移到 ServersDir。
        /// </summary>
        /// <param name="servers">管理器配置中的服务器。</param>
        /// <param name="rootDir">管理器程序目录。</param>
        /// <returns>是否成功迁移了至少一个目录。</returns>
        public static bool MigrateLegacyServerDirectories(IEnumerable<Server> servers, string rootDir)
        {
            string fullRoot = Path.GetFullPath(rootDir);
            string serversDir = Path.Combine(fullRoot, "ServersDir");
            Directory.CreateDirectory(serversDir);

            bool migrated = false;
            foreach (Server server in servers)
            {
                if (string.IsNullOrWhiteSpace(server.Path))
                    continue;

                string source = Path.GetFullPath(server.Path);
                if (!string.Equals(Path.GetDirectoryName(source), fullRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                string folderName = Path.GetFileName(source);
                if (!string.Equals(folderName, "Server", StringComparison.OrdinalIgnoreCase)
                    && !Regex.IsMatch(folderName, "^Server\\d+$", RegexOptions.IgnoreCase))
                    continue;

                string destination = Path.Combine(serversDir, folderName);
                bool sourceExists = Directory.Exists(source);
                bool destinationExists = Directory.Exists(destination);

                if (destinationExists)
                {
                    if (sourceExists && IsServerRunningFrom(source))
                        continue;

                    if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                    {
                        server.Path = destination;
                        migrated = true;
                    }
                    continue;
                }

                if (!sourceExists || IsServerRunningFrom(source))
                    continue;

                Directory.Move(source, destination);
                server.Path = destination;
                migrated = true;
            }

            return migrated;
        }

        private static bool IsServerRunningFrom(string serverDirectory)
        {
            foreach (string processName in new[] { "WSServer-Win64-Shipping", "WSServer" })
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string? executablePath = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(executablePath))
                            return true;

                        string prefix = serverDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        if (Path.GetFullPath(executablePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                        // 无法确认服务器进程路径时，保守地不移动目录。
                        return true;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }
    }
}
