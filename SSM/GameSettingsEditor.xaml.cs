using Microsoft.Win32;
using ModernWpf.Controls;
using SoulmaskServerManager.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
                    LoadFromJson(json, updateOriginalSettings: false);
                }
                else
                {
                    // 尝试读取旧格式中的 "1" 档位
                    var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (root != null && root.TryGetValue("1", out JsonElement profile))
                    {
                        LoadFromJson(profile.GetRawText(), updateOriginalSettings: false);
                    }
                    else
                    {
                        _ = new ContentDialog
                        {
                            Title = "错误",
                            Content = "无法从该文件中读取系数配置，未找到 \"1\" 档位数据。",
                            CloseButtonText = "确定",
                            DefaultButton = ContentDialogButton.Close
                        }.ShowAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "错误",
                    Content = $"导入失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private async void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_servers.Count > 0)
            {
                ContentDialog savePrompt = new()
                {
                    Content = "是否保存到服务器？如果配置文件已存在，将创建备份。",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await savePrompt.ShowAsync() != ContentDialogResult.Primary)
                    return;

                Server? currentServer = _loadedServerIndex >= 0 && _loadedServerIndex < _servers.Count
                    ? _servers[_loadedServerIndex]
                    : null;
                EditorSaveDialog saveDialog = new(_servers, currentServer)
                {
                    PrimaryButtonText = "保存",
                    CloseButtonText = "取消"
                };

                if (await saveDialog.ShowAsync() != ContentDialogResult.Primary
                    || saveDialog.GetServer() is not Server targetServer)
                    return;

                if (targetServer.Runtime.State == ServerRuntime.ServerState.运行中)
                {
                    ContentDialog runningPrompt = new()
                    {
                        Title = "服务器正在运行",
                        Content = "服务器正在运行，修改系数需要实时应用到服务器，否则修改将不奏效。是否继续实时应用？",
                        PrimaryButtonText = "是",
                        SecondaryButtonText = "否",
                        DefaultButton = ContentDialogButton.Primary
                    };
                    if (await runningPrompt.ShowAsync() != ContentDialogResult.Primary)
                        return;

                    if (!await ConfirmServerReadyForApplyAsync())
                        return;

                    await ApplyChangesAndSaveAsync(targetServer);
                    return;
                }

                if (targetServer.Runtime.State != ServerRuntime.ServerState.已停止)
                {
                    await ShowEditorMessageAsync("服务器状态异常", $"服务器当前状态为“{targetServer.Runtime.State}”，请等待服务器停止或正常运行后再保存系数。");
                    return;
                }

                try
                {
                    SaveToServerPath(targetServer);
                    UpdateOriginalSettingsIfLoadedServer(targetServer);
                    await ShowEditorMessageAsync("保存成功", $"系数配置已保存到服务器：\n{targetServer.Path}");
                }
                catch (Exception ex)
                {
                    await ShowEditorMessageAsync("错误", $"保存失败：{ex.Message}");
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
                await ShowEditorMessageAsync("保存成功", $"文件已保存至：\n{dialog.FileName}");
            }
            catch (Exception ex)
            {
                await ShowEditorMessageAsync("错误", $"保存失败：{ex.Message}");
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

        private async void ApplyConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedServerIndex < 0 || _loadedServerIndex >= _servers.Count)
            {
                await ShowEditorMessageAsync("提示", "未加载服务器，无法应用配置。");
                return;
            }

            ContentDialog applyPrompt = new()
            {
                Title = "应用系数",
                Content = "是否将当前系数修改应用到当前服务器？",
                PrimaryButtonText = "是",
                SecondaryButtonText = "否",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await applyPrompt.ShowAsync() != ContentDialogResult.Primary)
                return;

            if (!await ConfirmServerReadyForApplyAsync())
                return;

            await ApplyChangesAndSaveAsync(_servers[_loadedServerIndex]);
        }

        private async Task<bool> ConfirmServerReadyForApplyAsync()
        {
            ContentDialog readyPrompt = new()
            {
                Title = "确认服务器状态",
                Content = "请确保当前服务器已正常运行，没有处于启动中或关闭中的状态。确认后将发送系数修改指令，并保存到两个配置文件。",
                PrimaryButtonText = "确认应用并保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            return await readyPrompt.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ApplyChangesAndSaveAsync(Server server)
        {
            if (!IsServerReadyForApply(server))
            {
                await ShowEditorMessageAsync("无法应用", "服务器当前未处于正常运行状态，已取消实时应用和保存。请确认服务器正常运行后重试。");
                return;
            }

            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            if (!File.Exists(paths.ServerSettings))
            {
                await ShowEditorMessageAsync("错误", "未找到服务器设置文件，无法获取监听端口，不能发送指令。");
                return;
            }

            if (_originalSettings == null)
            {
                await ShowEditorMessageAsync("提示", "未加载原始配置，无法检测改动。");
                return;
            }

            var changed = GetChangedProperties(_originalSettings, VM.Settings);
            bool anyFailed = false;
            int appliedCount = 0;
            if (changed.Count == 0)
            {
                try
                {
                    SaveToServerPath(server);
                    await ShowEditorMessageAsync("应用结果", "没有检测到系数改动，已保存到 GameXishu_Default.json 和 GameXishu.json。");
                }
                catch (Exception ex)
                {
                    await ShowEditorMessageAsync("错误", $"保存配置文件失败：{ex.Message}");
                }
                return;
            }

            int echoPort = ServerSettingsEditor.LoadServerSettings(paths.ServerSettings).EchoPort;
            StackPanel progressContent = new() { MinWidth = 320 };
            TextBlock progressText = new()
            {
                Text = $"正在应用第 1/{changed.Count} 项系数…",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.ProgressBar progressBar = new()
            {
                Minimum = 0,
                Maximum = changed.Count + 1,
                Value = 0,
                Height = 14
            };
            progressContent.Children.Add(progressText);
            progressContent.Children.Add(progressBar);

            ContentDialog progressDialog = new()
            {
                Title = "正在应用系数",
                Content = progressContent,
                PrimaryButtonText = "正在应用…",
                IsPrimaryButtonEnabled = false
            };
            Task<ContentDialogResult> progressDialogTask = progressDialog.ShowAsync();
            await Dispatcher.Yield(DispatcherPriority.Render);

            var rcon = new RCON.RemoteConClient();
            int currentIndex = 0;
            foreach (var kv in changed)
            {
                currentIndex++;
                progressText.Text = $"正在应用第 {currentIndex}/{changed.Count} 项系数：{kv.Key}";
                string cmd = $"sc {kv.Key} {kv.Value}";
                string? result = await rcon.ExecuteAsync("127.0.0.1", echoPort, cmd);
                if (result != null)
                    appliedCount++;
                else
                    anyFailed = true;
                progressBar.Value = currentIndex;
            }

            progressText.Text = $"系数指令处理完成（{appliedCount}/{changed.Count}），正在保存两个配置文件…";
            try
            {
                SaveToServerPath(server);
            }
            catch (Exception ex)
            {
                progressDialog.Title = "保存失败";
                progressText.Text = $"系数指令已处理，但保存配置文件失败：{ex.Message}";
                progressBar.Value = changed.Count + 1;
                progressDialog.PrimaryButtonText = "关闭";
                progressDialog.IsPrimaryButtonEnabled = true;
                await progressDialogTask;
                return;
            }

            progressBar.Value = changed.Count + 1;
            if (!anyFailed && IsLoadedServer(server))
                UpdateOriginalSettingsFromCurrent();
            RefreshUnappliedChanges();

            progressDialog.Title = anyFailed ? "应用未完全完成" : "应用完成";
            progressText.Text = anyFailed
                ? $"已应用 {appliedCount}/{changed.Count} 项系数，两个配置文件已保存。部分指令发送失败，未应用项仍会保留提示。"
                : $"已成功应用 {appliedCount} 项系数，两个配置文件已保存。";
            progressDialog.PrimaryButtonText = "确定";
            progressDialog.IsPrimaryButtonEnabled = true;
            await progressDialogTask;
        }

        private static bool IsServerReadyForApply(Server server)
        {
            if (server.Runtime.State != ServerRuntime.ServerState.运行中 || server.Runtime.Process == null)
                return false;

            try
            {
                return !server.Runtime.Process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private bool IsLoadedServer(Server server) =>
            _loadedServerIndex >= 0
            && _loadedServerIndex < _servers.Count
            && ReferenceEquals(_servers[_loadedServerIndex], server);

        private void UpdateOriginalSettingsIfLoadedServer(Server server)
        {
            if (IsLoadedServer(server))
                UpdateOriginalSettingsFromCurrent();
            RefreshUnappliedChanges();
        }

        private void UpdateOriginalSettingsFromCurrent()
        {
            _originalSettings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(SerializeCurrentState(), JsonOptions);
        }

        private static async Task ShowEditorMessageAsync(string title, string content)
        {
            await new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close
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

        private static Dictionary<string, string> GetChangedProperties(SoulmaskCoefficientSettings original, SoulmaskCoefficientSettings current)
        {
            var result = new Dictionary<string, string>();
            var flatOptions = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

            string originalJson = JsonSerializer.Serialize(original, flatOptions);
            string currentJson = JsonSerializer.Serialize(current, flatOptions);

            var originalDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(originalJson);
            var currentDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(currentJson);

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

        public void LoadFromJson(string json, bool updateOriginalSettings = true)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(json, JsonOptions);
                if (settings != null)
                {
                    DataContext = new GameSettingsEditorViewModel { Settings = settings };
                    if (updateOriginalSettings)
                        _originalSettings = JsonSerializer.Deserialize<SoulmaskCoefficientSettings>(json, JsonOptions);
                    RefreshUnappliedChanges();
                }
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "错误",
                    Content = $"JSON 解析失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private void LoadFromServerPath(Server server)
        {
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);

            bool loadGameplaySettings = File.Exists(paths.GameplaySettingsPath);
            string settingsPath = loadGameplaySettings
                ? paths.GameplaySettingsPath
                : paths.GameXishuDefaultPath;

            if (!File.Exists(settingsPath))
                return;

            try
            {
                string json = File.ReadAllText(settingsPath);
                if (loadGameplaySettings)
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !document.RootElement.TryGetProperty("1", out JsonElement activeProfile))
                    {
                        throw new JsonException("系数配置中未找到服务器使用的 \"1\" 档位。");
                    }

                    json = activeProfile.GetRawText();
                }

                LoadFromJson(json);
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "错误",
                    Content = $"读取系数配置失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
        }

        private void SaveToServerPath(Server server)
        {
            var paths = new SSMPathManager(Directory.GetCurrentDirectory(), server);
            string currentSettingsJson = SerializeCurrentState();

            SaveGameplaySettings(paths.GameplaySettingsPath, currentSettingsJson);
            SaveCustomSettings(paths.GameXishuDefaultPath, currentSettingsJson);
        }

        private static void SaveCustomSettings(string settingsPath, string currentSettingsJson)
        {
            EnsureSettingsDirectory(settingsPath);
            BackupIfExists(settingsPath);
            File.WriteAllText(settingsPath, currentSettingsJson);
        }

        private static void SaveGameplaySettings(string settingsPath, string currentSettingsJson)
        {
            EnsureSettingsDirectory(settingsPath);

            JsonObject root;
            if (File.Exists(settingsPath))
            {
                root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                    ?? throw new JsonException($"配置文件不是有效的 JSON 对象：{settingsPath}");
                BackupIfExists(settingsPath);
            }
            else
            {
                root = JsonNode.Parse(JsonSerializer.Serialize(new SoulmaskCoefficientConfigCollection(), JsonOptions)) as JsonObject
                    ?? throw new JsonException("无法创建默认系数配置。");
            }

            root["1"] = JsonNode.Parse(currentSettingsJson);
            File.WriteAllText(settingsPath, root.ToJsonString(JsonOptions));
        }

        private static void EnsureSettingsDirectory(string settingsPath)
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        private static void BackupIfExists(string settingsPath)
        {
            if (File.Exists(settingsPath))
                File.Copy(settingsPath, settingsPath + ".bak", overwrite: true);
        }

        #region Coefficient Search

        // 系数设置搜索匹配项
        private readonly List<SearchMatch> _searchMatches = new();
        private readonly List<FrameworkElement> _activeSearchElements = new();
        private int _currentMatchIndex = -1;
        private TabItem? _searchResultsTab;
        private TabItem? _activeSearchTab;
        private string _currentSearchKeyword = "";
        private readonly List<(AdornerLayer Layer, HighlightAdorner Adorner)> _searchAdorners = new();

        private sealed class SearchMatch
        {
            public string TabName { get; init; } = "";
            public string ParameterName { get; init; } = "";
            public FrameworkElement SourceElement { get; init; } = null!;
            public DependencyProperty SourceProperty { get; init; } = null!;
            public string BindingPath { get; init; } = "";
            public Border ResultCard { get; init; } = null!;
        }

        // 按 Enter 执行搜索，避免输入过程中反复扫描所有设置项。
        private void CoefficientSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            ClearOriginalHighlights();
            RemoveSearchResultsTab();
            ClearOriginalHighlights();
            _searchMatches.Clear();
            _activeSearchElements.Clear();
            _activeSearchTab = null;
            _currentMatchIndex = -1;

            string keyword = CoefficientSearchBox.Text?.Trim() ?? "";
            _currentSearchKeyword = keyword;
            if (string.IsNullOrEmpty(keyword))
            {
                UpdateSearchResultLabel();
                return;
            }

            string lowerKeyword = keyword.ToLowerInvariant();

            var seenBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 搜索原始设置页，忽略上一次动态创建的结果页。
            foreach (var item in SettingsTabControl.Items)
            {
                if (item is not TabItem tabItem) continue;
                foreach (var element in FindSearchableElements(tabItem))
                {
                    string text = element switch
                    {
                        TextBlock tb => tb.Text,
                        CheckBox cb => cb.Content as string,
                        _ => null
                    };
                    if (string.IsNullOrEmpty(text) || !text.ToLowerInvariant().Contains(lowerKeyword))
                        continue;

                    if (TryGetSearchParameter(element, out FrameworkElement? source, out DependencyProperty property, out string parameterName)
                        && source != null)
                    {
                        BindingBase? binding = BindingOperations.GetBindingBase(source, property);
                        string? path = (binding as Binding)?.Path?.Path;
                        if (string.IsNullOrWhiteSpace(path)) continue;

                        string uniqueKey = $"{tabItem.Header}|{path}";
                        if (!seenBindings.Add(uniqueKey)) continue;

                        Border card = CreateSearchResultCard(tabItem.Header?.ToString() ?? "", parameterName, source, property);
                        _searchMatches.Add(new SearchMatch
                        {
                            TabName = tabItem.Header?.ToString() ?? "",
                            ParameterName = parameterName,
                            SourceElement = source,
                            SourceProperty = property,
                            BindingPath = path,
                            ResultCard = card
                        });
                    }
                }
            }

            if (_searchMatches.Count > 0)
            {
                CreateSearchResultsTab(keyword);
                SettingsTabControl.SelectedItem = _searchResultsTab;
                NavigateToMatch(0);
            }
            else
            {
                UpdateActiveTabMatches();
                UpdateSearchResultLabel();
            }
        }

        private void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, SettingsTabControl)) return;

            if (ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab))
            {
                ClearOriginalHighlights();
                _activeSearchElements.Clear();
                _activeSearchTab = null;
                _currentMatchIndex = _searchMatches.Count > 0
                    ? Math.Clamp(_currentMatchIndex, 0, _searchMatches.Count - 1)
                    : -1;
                UpdateSearchResultLabel();
                return;
            }

            UpdateActiveTabMatches();
        }

        private void UpdateActiveTabMatches()
        {
            ClearOriginalHighlights();
            _activeSearchElements.Clear();
            _activeSearchTab = SettingsTabControl.SelectedItem as TabItem;

            string keyword = _currentSearchKeyword;
            if (_activeSearchTab == null || string.IsNullOrEmpty(keyword))
            {
                _activeSearchTab = null;
                _currentMatchIndex = -1;
                UpdateSearchResultLabel();
                return;
            }

            string lowerKeyword = keyword.ToLowerInvariant();
            HashSet<string> seenBindings = new(StringComparer.OrdinalIgnoreCase);
            foreach (FrameworkElement element in FindSearchableElements(_activeSearchTab))
            {
                string? text = element switch
                {
                    TextBlock textBlock => textBlock.Text,
                    CheckBox checkBox => checkBox.Content as string,
                    _ => null
                };
                if (string.IsNullOrEmpty(text) || !text.ToLowerInvariant().Contains(lowerKeyword))
                    continue;

                if (!TryGetSearchParameter(element, out FrameworkElement? source, out DependencyProperty property, out _)
                    || source == null)
                    continue;

                string? path = (BindingOperations.GetBindingBase(source, property) as Binding)?.Path?.Path;
                if (string.IsNullOrWhiteSpace(path) || !seenBindings.Add(path))
                    continue;

                // 仅高亮搜索命中的文字控件，不覆盖参数行或编辑控件。
                _activeSearchElements.Add(element);
            }

            _currentMatchIndex = _activeSearchElements.Count > 0 ? 0 : -1;
            ApplyOriginalHighlights();
            UpdateSearchResultLabel();
        }

        // 更新搜索结果标签 "X / Y"
        private void UpdateSearchResultLabel()
        {
            int total = ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab)
                ? _searchMatches.Count
                : _activeSearchTab != null ? _activeSearchElements.Count : 0;
            int current = total > 0 ? _currentMatchIndex + 1 : 0;
            SearchResultLabel.Text = $"{current} / {total}";
        }

        // 导航到指定索引的匹配项
        private void NavigateToMatch(int index)
        {
            if (ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab))
            {
                if (_searchMatches.Count == 0) return;

                index = ((index % _searchMatches.Count) + _searchMatches.Count) % _searchMatches.Count;
                _currentMatchIndex = index;
                Border resultCard = _searchMatches[index].ResultCard;
                SettingsTabControl.UpdateLayout();
                ScrollToElement(resultCard);
            }
            else
            {
                if (_activeSearchElements.Count == 0) return;
                index = ((index % _activeSearchElements.Count) + _activeSearchElements.Count) % _activeSearchElements.Count;
                _currentMatchIndex = index;
                ApplyOriginalHighlights();
                ScrollToElement(_activeSearchElements[index]);
            }

            UpdateSearchResultLabel();
        }

        private void ApplyOriginalHighlights()
        {
            ClearOriginalHighlights();
            if (_activeSearchElements.Count == 0 || _activeSearchTab == null) return;

            _activeSearchTab.UpdateLayout();
            for (int index = 0; index < _activeSearchElements.Count; index++)
            {
                FrameworkElement element = _activeSearchElements[index];
                AdornerLayer? layer = AdornerLayer.GetAdornerLayer(element);
                if (layer == null) continue;
                HighlightAdorner adorner = new(element, index == _currentMatchIndex);
                layer.Add(adorner);
                _searchAdorners.Add((layer, adorner));
            }
        }

        private void ClearOriginalHighlights()
        {
            foreach ((AdornerLayer layer, HighlightAdorner adorner) in _searchAdorners)
                layer.Remove(adorner);
            _searchAdorners.Clear();
        }

        private sealed class HighlightAdorner : Adorner
        {
            private readonly bool _isCurrent;

            public HighlightAdorner(UIElement adornedElement, bool isCurrent) : base(adornedElement)
            {
                _isCurrent = isCurrent;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                Rect bounds = new(AdornedElement.RenderSize);
                drawingContext.DrawRectangle(
                    new SolidColorBrush(_isCurrent ? Color.FromArgb(105, 255, 145, 0) : Color.FromArgb(65, 255, 235, 59)),
                    _isCurrent ? new Pen(Brushes.DarkOrange, 2) : null,
                    bounds);
            }
        }

        private void CreateSearchResultsTab(string keyword)
        {
            StackPanel results = new() { Margin = new Thickness(10) };
            foreach (IGrouping<string, SearchMatch> group in _searchMatches.GroupBy(match => match.TabName))
            {
                StackPanel tabGroup = new() { Margin = new Thickness(0, 0, 0, 14) };
                tabGroup.Children.Add(new TextBlock
                {
                    Text = group.Key,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                foreach (SearchMatch match in group)
                    tabGroup.Children.Add(match.ResultCard);
                results.Children.Add(tabGroup);
            }

            _searchResultsTab = new TabItem
            {
                Header = keyword,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = results
                }
            };
            SettingsTabControl.Items.Add(_searchResultsTab);
        }

        private void RemoveSearchResultsTab()
        {
            if (_searchResultsTab == null) return;
            if (ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab) && SettingsTabControl.Items.Count > 1)
                SettingsTabControl.SelectedIndex = 0;
            SettingsTabControl.Items.Remove(_searchResultsTab);
            _searchResultsTab = null;
        }

        private Border CreateSearchResultCard(string tabName, string parameterName, FrameworkElement source, DependencyProperty sourceProperty)
        {
            StackPanel content = new() { Margin = new Thickness(6, 3, 6, 3) };
            if (source is NumberBox numberBox)
            {
                StackPanel? originalRow = FindNumberParameterRow(numberBox);
                TextBlock? originalLabel = originalRow == null
                    ? null
                    : FindDescendants<TextBlock>(originalRow).FirstOrDefault(textBlock => textBlock.Text == parameterName);
                DockPanel? originalDock = originalRow == null ? null : FindDescendants<DockPanel>(originalRow).FirstOrDefault();
                Slider? originalSlider = originalDock == null ? null : FindDescendants<Slider>(originalDock).FirstOrDefault();

                TextBlock label = originalLabel == null
                    ? new TextBlock { Text = parameterName, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : CloneTextBlock(originalLabel);
                content.Margin = originalRow?.Margin ?? content.Margin;
                content.Children.Add(label);

                DockPanel editorPanel = new()
                {
                    LastChildFill = originalDock?.LastChildFill ?? true,
                    Margin = originalDock?.Margin ?? new Thickness()
                };
                NumberBox numberEditor = CloneNumberBox(numberBox);
                DockPanel.SetDock(numberEditor, DockPanel.GetDock(numberBox));
                editorPanel.Children.Add(numberEditor);
                if (originalSlider != null)
                {
                    Slider sliderEditor = CloneSlider(originalSlider);
                    DockPanel.SetDock(sliderEditor, DockPanel.GetDock(originalSlider));
                    editorPanel.Children.Add(sliderEditor);
                }
                content.Children.Add(editorPanel);
            }
            else if (source is CheckBox checkBox)
            {
                content = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
                CheckBox editor = new()
                {
                    Margin = checkBox.Margin,
                    HorizontalAlignment = checkBox.HorizontalAlignment,
                    VerticalAlignment = checkBox.VerticalAlignment,
                    HorizontalContentAlignment = checkBox.HorizontalContentAlignment,
                    VerticalContentAlignment = checkBox.VerticalContentAlignment,
                    ToolTip = checkBox.ToolTip,
                    IsThreeState = checkBox.IsThreeState,
                    Style = checkBox.Style,
                    Content = CloneCheckBoxContent(checkBox.Content, parameterName)
                };
                BindingBase? binding = BindingOperations.GetBindingBase(checkBox, ToggleButton.IsCheckedProperty);
                if (binding != null) BindingOperations.SetBinding(editor, ToggleButton.IsCheckedProperty, binding);
                content.Children.Add(editor);
            }

            return new Border
            {
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8),
                Child = content
            };
        }

        private static StackPanel? FindNumberParameterRow(NumberBox numberBox)
        {
            DependencyObject? current = numberBox;
            while (current != null)
            {
                if (current is StackPanel stackPanel
                    && FindDescendants<TextBlock>(stackPanel).Any()
                    && FindDescendants<NumberBox>(stackPanel).Any())
                    return stackPanel;
                current = LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        private static TextBlock CloneTextBlock(TextBlock source) => new()
        {
            Text = source.Text,
            TextWrapping = source.TextWrapping,
            TextAlignment = source.TextAlignment,
            HorizontalAlignment = source.HorizontalAlignment,
            VerticalAlignment = source.VerticalAlignment,
            Margin = source.Margin,
            Padding = source.Padding,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontStretch = source.FontStretch,
            FontStyle = source.FontStyle,
            FontWeight = source.FontWeight,
            Foreground = source.Foreground,
            ToolTip = source.ToolTip
        };

        private static object CloneCheckBoxContent(object? source, string fallbackText)
        {
            if (source is TextBlock textBlock) return CloneTextBlock(textBlock);
            return source is string text ? text : fallbackText;
        }

        private static NumberBox CloneNumberBox(NumberBox source)
        {
            NumberBox clone = new()
            {
                Minimum = source.Minimum,
                Maximum = source.Maximum,
                SmallChange = source.SmallChange,
                SpinButtonPlacementMode = source.SpinButtonPlacementMode,
                NumberFormatter = source.NumberFormatter,
                Width = source.Width,
                MinWidth = source.MinWidth,
                MaxWidth = source.MaxWidth,
                Margin = source.Margin,
                HorizontalAlignment = source.HorizontalAlignment,
                VerticalAlignment = source.VerticalAlignment,
                ToolTip = source.ToolTip
            };
            BindingBase? binding = BindingOperations.GetBindingBase(source, NumberBox.ValueProperty);
            if (binding != null) BindingOperations.SetBinding(clone, NumberBox.ValueProperty, binding);
            return clone;
        }

        private static Slider CloneSlider(Slider source)
        {
            Slider clone = new()
            {
                Minimum = source.Minimum,
                Maximum = source.Maximum,
                SmallChange = source.SmallChange,
                LargeChange = source.LargeChange,
                TickFrequency = source.TickFrequency,
                IsSnapToTickEnabled = source.IsSnapToTickEnabled,
                Orientation = source.Orientation,
                VerticalAlignment = source.VerticalAlignment,
                HorizontalAlignment = source.HorizontalAlignment,
                Margin = source.Margin,
                ToolTip = source.ToolTip
            };
            BindingBase? binding = BindingOperations.GetBindingBase(source, Slider.ValueProperty);
            if (binding != null) BindingOperations.SetBinding(clone, Slider.ValueProperty, binding);
            return clone;
        }

        private static bool TryGetSearchParameter(FrameworkElement match, out FrameworkElement? source, out DependencyProperty property, out string parameterName)
        {
            source = null;
            property = null!;
            parameterName = match is TextBlock textBlock ? textBlock.Text : "";

            DependencyObject? current = match;
            while (current != null)
            {
                if (current is CheckBox checkBox)
                {
                    source = checkBox;
                    property = ToggleButton.IsCheckedProperty;
                    if (string.IsNullOrWhiteSpace(parameterName)) parameterName = checkBox.Content as string ?? "";
                    return BindingOperations.GetBindingBase(source, property) != null;
                }

                if (current is StackPanel stackPanel)
                {
                    NumberBox? editor = FindDescendants<NumberBox>(stackPanel).FirstOrDefault(numberBox => BindingOperations.GetBindingBase(numberBox, NumberBox.ValueProperty) != null);
                    if (editor != null)
                    {
                        source = editor;
                        property = NumberBox.ValueProperty;
                        return true;
                    }
                }
                current = LogicalTreeHelper.GetParent(current);
            }
            return false;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is T match) yield return match;
                if (child is DependencyObject dependencyObject)
                    foreach (T descendant in FindDescendants<T>(dependencyObject))
                        yield return descendant;
            }
        }

        // 滚动使搜索结果可见
        private void ScrollToElement(FrameworkElement element)
        {
            if (element == null) return;

            // 先尝试找到元素所在的 ScrollViewer 并显式滚动
            ScrollViewer? scrollViewer = FindParentScrollViewer(element);
            if (scrollViewer != null)
            {
                scrollViewer.UpdateLayout();
                var transform = element.TransformToVisual(scrollViewer);
                var point = transform.Transform(new Point(0, 0));
                double targetOffset = Math.Max(0, point.Y - 50);
                scrollViewer.ScrollToVerticalOffset(targetOffset);
                return;
            }

            // 后备方案：使用 BringIntoView
            element.BringIntoView();
        }

        // 通过视觉树向上查找最近的 ScrollViewer
        private static ScrollViewer? FindParentScrollViewer(DependencyObject element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                if (current is ScrollViewer sv) return sv;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // 递归查找指定逻辑树下的所有 TextBlock 和 CheckBox
        private static IEnumerable<FrameworkElement> FindSearchableElements(DependencyObject root)
        {
            if (root == null) yield break;

            foreach (var current in LogicalTreeHelper.GetChildren(root))
            {
                if (current is TextBlock || current is CheckBox)
                    yield return (FrameworkElement)current;

                if (current is DependencyObject dep)
                {
                    foreach (var descendant in FindSearchableElements(dep))
                        yield return descendant;
                }
            }
        }

        // 上一个匹配项
        private void PrevMatch_Click(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab))
            {
                if (_searchMatches.Count == 0) return;
            }
            else if (_activeSearchElements.Count == 0) return;
            NavigateToMatch(_currentMatchIndex - 1);
        }

        // 下一个匹配项
        private void NextMatch_Click(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(SettingsTabControl.SelectedItem, _searchResultsTab))
            {
                if (_searchMatches.Count == 0) return;
            }
            else if (_activeSearchElements.Count == 0) return;
            NavigateToMatch(_currentMatchIndex + 1);
        }

        #endregion

    }
}
