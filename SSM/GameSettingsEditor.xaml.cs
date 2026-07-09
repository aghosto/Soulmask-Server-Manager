using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SoulmaskServerManager
{
    public class GameSettingsEditorViewModel
    {
        public SoulmaskCoefficientSettings Settings { get; set; } = new();
    }

    public partial class GameSettingsEditor : Window
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly ObservableCollection<Server> _servers;
        private int _loadedServerIndex = -1;
        private SoulmaskCoefficientSettings? _originalSettings;
        private readonly DispatcherTimer _changesTimer;

        public GameSettingsEditor(ObservableCollection<Server> sentServers, bool autoLoad = false, int indexToLoad = -1)
        {
            _servers = sentServers;
            InitializeComponent();

            DataContext = new GameSettingsEditorViewModel();

            _changesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _changesTimer.Tick += (s, e) => RefreshUnappliedChanges();
            _changesTimer.Start();

            if (autoLoad && indexToLoad != -1 && sentServers.Count > 0)
            {
                _loadedServerIndex = indexToLoad;
                LoadFromServerPath(sentServers[indexToLoad]);
            }
        }

        private GameSettingsEditorViewModel VM => (GameSettingsEditorViewModel)DataContext;

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "JSON files|*.json",
                DefaultExt = "json",
                FileName = "GameXishu_Default.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                string fileName = Path.GetFileName(dialog.FileName);

                if (fileName.Equals("GameXishu_Default.json", StringComparison.OrdinalIgnoreCase))
                {
                    LoadFromJson(json);
                }
                else
                {
                    // 尝试读取旧格式中的 "1" 档位
                    var root = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(json);
                    if (root != null && root.TryGetValue("1", out JsonElement profile))
                    {
                        LoadFromJson(profile.GetRawText());
                    }
                    else
                    {
                        _ = new ModernWpf.Controls.ContentDialog
                        {
                            Title = "错误",
                            Content = "无法从该文件中读取系数配置，未找到 \"1\" 档位数据。",
                            CloseButtonText = "确定",
                            DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                        }.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = $"导入失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedServerIndex != -1 && _loadedServerIndex < _servers.Count)
            {
                try
                {
                    SaveToServerPath(_servers[_loadedServerIndex]);
                    var successDialog = new ModernWpf.Controls.ContentDialog
                    {
                        Title = "保存成功",
                        Content = $"系数配置已保存至服务器：\n{_servers[_loadedServerIndex].Path}",
                        CloseButtonText = "确定",
                        DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                    };
                    _ = successDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    _ = new ModernWpf.Controls.ContentDialog
                    {
                        Title = "错误",
                        Content = $"保存失败：{ex.Message}",
                        CloseButtonText = "确定",
                        DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                    }.ShowAsync();
                }
                return;
            }

            SaveFileDialog dialog = new()
            {
                Filter = "JSON files|*.json",
                DefaultExt = "json",
                FileName = "GameXishu_Default.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = SerializeCurrentState();

                if (File.Exists(dialog.FileName))
                    File.Copy(dialog.FileName, dialog.FileName + ".bak", true);

                File.WriteAllText(dialog.FileName, json);

                var successDialog = new ModernWpf.Controls.ContentDialog
                {
                    Title = "保存成功",
                    Content = $"文件已保存至：\n{dialog.FileName}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                };
                _ = successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = $"保存失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "JSON files|*.json",
                DefaultExt = "json",
                FileName = "GameXishu_Default.json",
                InitialDirectory = Directory.GetCurrentDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = SerializeCurrentState();
                File.WriteAllText(dialog.FileName, json);
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "导出成功",
                    Content = $"配置已导出至：\n{dialog.FileName}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = $"导出失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private async void ApplyConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedServerIndex == -1 || _loadedServerIndex >= _servers.Count)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "提示",
                    Content = "未加载服务器，无法应用配置。",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            var server = _servers[_loadedServerIndex];
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            if (!File.Exists(paths.ServerSettings))
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = "未找到服务器设置文件，无法获取 EchoPort。",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            var serverSettings = ServerSettingsEditor.LoadServerSettings(paths.ServerSettings);
            int echoPort = serverSettings.EchoPort;

            if (_originalSettings == null)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "提示",
                    Content = "未加载原始配置，无法检测改动。",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            var changed = GetChangedProperties(_originalSettings, VM.Settings);
            if (changed.Count == 0)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "提示",
                    Content = "没有检测到任何改动。",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            bool anyFailed = false;
            int appliedCount = 0;
            var rcon = new RCON.RemoteConClient();
            foreach (var kv in changed)
            {
                string cmd = $"sc {kv.Key} {kv.Value}";
                var result = await rcon.ExecuteAsync("127.0.0.1", echoPort, cmd);
                if (result != null)
                {
                    appliedCount++;
                }
                else
                {
                    anyFailed = true;
                }
            }

            try
            {
                SaveToServerPath(server);
            }
            catch (Exception ex)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = $"指令已发送，但保存到文件时失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            _originalSettings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(SerializeCurrentState(), JsonOptions);
            RefreshUnappliedChanges();

            string msg = anyFailed
                ? $"已应用 {appliedCount}/{changed.Count} 项系数到服务器，同时已保存到 GameXishu_Default.json 和 GameXishu.json，部分指令发送失败。"
                : $"已成功应用 {appliedCount} 项系数到服务器，并保存到 GameXishu_Default.json 和 GameXishu.json。";
            _ = new ModernWpf.Controls.ContentDialog
            {
                Title = "应用结果",
                Content = msg,
                CloseButtonText = "确定",
                DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
            }.ShowAsync();
        }

        private void RefreshUnappliedChanges()
        {
            // 只有当前加载的服务器正在运行中才显示提示
            bool serverRunning = _loadedServerIndex != -1
                && _loadedServerIndex < _servers.Count
                && _servers[_loadedServerIndex].Runtime.State == ServerRuntime.ServerState.运行中;

            if (!serverRunning || _originalSettings == null)
            {
                UnappliedChangesText.Visibility = Visibility.Collapsed;
                return;
            }

            var changed = GetChangedProperties(_originalSettings, VM.Settings);
            if (changed.Count > 0)
            {
                UnappliedChangesText.Text = $"有 {changed.Count} 项系数未实时应用到服务器中";
                UnappliedChangesText.Visibility = Visibility.Visible;
            }
            else
            {
                UnappliedChangesText.Visibility = Visibility.Collapsed;
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> GetChangedProperties(SoulmaskCoefficientSettings original, SoulmaskCoefficientSettings current)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>();
            var flatOptions = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            string originalJson = JsonSerializer.Serialize(original, flatOptions);
            string currentJson = JsonSerializer.Serialize(current, flatOptions);

            var originalDict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(originalJson);
            var currentDict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(currentJson);

            if (originalDict == null || currentDict == null) return result;

            foreach (var kv in currentDict)
            {
                if (originalDict.TryGetValue(kv.Key, out var originalValue))
                {
                    if (originalValue.GetRawText() != kv.Value.GetRawText())
                    {
                        result[kv.Key] = FormatJsonValue(kv.Value);
                    }
                }
            }

            return result;
        }

        private static string FormatJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => element.GetRawText()
            };
        }

        private void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            DataContext = new GameSettingsEditorViewModel();
            RefreshUnappliedChanges();
        }

        private void ExitConfig_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public string SerializeCurrentState()
        {
            return JsonSerializer.Serialize(VM.Settings, JsonOptions);
        }

        public void LoadFromJson(string json)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(json, JsonOptions);
                if (settings != null)
                {
                    DataContext = new GameSettingsEditorViewModel { Settings = settings };
                    _originalSettings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                _ = new ModernWpf.Controls.ContentDialog
                {
                    Title = "错误",
                    Content = $"JSON 解析失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ModernWpf.Controls.ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private void LoadFromServerPath(Server server)
        {
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            if (File.Exists(paths.GameXishuDefaultPath))
            {
                try
                {
                    string json = File.ReadAllText(paths.GameXishuDefaultPath);
                    LoadFromJson(json);
                }
                catch { }
            }
        }

        private void SaveToServerPath(Server server)
        {
            BuildAndSaveGameXishuDefault(server);
            MergeIntoGameXishuJson(server);
        }

        private void BuildAndSaveGameXishuDefault(Server server)
        {
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            string json = SerializeCurrentState();
            if (!Directory.Exists(paths.SaveDataSettingsDir))
                Directory.CreateDirectory(paths.SaveDataSettingsDir);
            File.WriteAllText(paths.GameXishuDefaultPath, json);
        }

        private void MergeIntoGameXishuJson(Server server)
        {
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            string gameplayDir = Path.GetDirectoryName(paths.GameplaySettingsPath);
            if (!File.Exists(paths.GameXishuDefaultPath))
                return;

            try
            {
                if (!Directory.Exists(gameplayDir))
                    Directory.CreateDirectory(gameplayDir);

                // 直接覆盖写入扁平配置（不再使用 0/1/2 三档位格式）
                File.Copy(paths.GameXishuDefaultPath, paths.GameplaySettingsPath, overwrite: true);
            }
            catch { /* Silently handle write errors */ }
        }


    }
}
