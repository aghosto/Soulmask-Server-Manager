using ModernWpf.Controls;
using SoulmaskServerManager;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace SoulmaskServerManager
{
    /// <summary>
    /// ModManagerWindows.xaml 的交互逻辑
    /// </summary>
    public partial class ModManagerWindows : Window
    {
        MainWindow mainWindow = Application.Current.MainWindow as MainWindow;

        private const int AppId = 2646460;
        private static readonly HttpClient _http = new HttpClient();
        private List<SteamMod> _allMods = new List<SteamMod>();

        private int _currentPage = 1;
        private int _pageSize = 30; 
        private int _totalPage = 1;
        private int _totalModCount = 0;

        private string _currentSearchText = "";
        private bool _isSearchMode = false;

        MainSettings SsmSettings = new();
        SteamModsList SteamMods = new();

        // 拖动排序相关字段
        private InstalledModViewModel _draggedMod;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private int _originalIndex = -1;
        private int _lastPlaceholderIndex = -1;
        private int _finalInsertIndex = -1;
        private InstalledModViewModel _placeholderMod;

        private Dictionary<string, string> _modNameCache = new Dictionary<string, string>();
        private CancellationTokenSource _detailCts;

        public ModManagerWindows(MainSettings mainSettings)
        {
            SsmSettings = mainSettings;
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            SteamServerComboBox.DataContext = SsmSettings;

            if (SsmSettings.Servers.Count > 0)
            {
                SteamServerComboBox.SelectedIndex = 0;
            }
            Loaded += async (s, e) => await LoadPageAsync(1);
        }

        private async void DgMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgMods.SelectedItem is not SteamMod mod) return;

            // Cancel any previous detail fetch in progress
            _detailCts?.Cancel();
            _detailCts = new CancellationTokenSource();
            var ct = _detailCts.Token;

            // Show basic info immediately (from HTML parsing)
            ModIdText.Text = mod.Id;
            ModTitleText.Text = mod.Title;
            ModAuthorText.Text = mod.Author;
            if (!string.IsNullOrEmpty(mod.AuthorUrl))
                ModAuthorHyperlink.NavigateUri = new Uri(mod.AuthorUrl);
            else
                ModAuthorHyperlink.NavigateUri = null;

            // If we already have details from a previous fetch, show them
            if (!string.IsNullOrEmpty(mod.PreviewImage))
            {
                try { ModImage.Source = new BitmapImage(new Uri(mod.PreviewImage)); }
                catch { ModImage.Source = null; }
            }
            else ModImage.Source = null;

            ModSubsText.Text = mod.Subscriptions > 0 ? mod.Subscriptions.ToString("N0") : "";
            BuildDescriptionText(mod.Description ?? "");

            // Fetch full details from Steam API
            await FetchModDetailAsync(mod.Id, ct);
        }

        private async void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Server server = (Server)SteamServerComboBox.SelectedItem;

            if (server.SubscribedMods.Count > 0)
            {
                for (int i = 0; i < server.SubscribedMods.Count; i++)
                {
                    foreach (SteamMod mod in SteamMods.ModList)
                    {
                        if (server.SubscribedMods.Contains(mod.Id))
                            mod.IsChecked = true;
                        else
                            mod.IsChecked = false;
                    }
                }
                RefreshInstalledModGridAsync();
            }
            else
            {
                foreach (SteamMod mod in SteamMods.ModList)
                    mod.IsChecked = false;
            }
            await LoadPageAsync(1);
        }

        private async void ModSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await SteamSearchAsync(ModSearchBox.Text);
            }
        }

        private async Task SteamSearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                _isSearchMode = false;
                _currentSearchText = "";
                await LoadPageAsync(1);
                return;
            }

            _isSearchMode = true;
            _currentSearchText = keyword.Trim();

            await LoadPageAsync(1);
        }

        private static string ExtractSsrDataObject(string html)
        {
            var m = Regex.Match(html, @"{\\""data\\"":{\\""eresult\\""", RegexOptions.Singleline);
            if (!m.Success) return null;

            int start = m.Index;
            int braceDepth = 0;
            int bracketDepth = 0;
            bool inStr = false;
            int end = start;
            for (; end < html.Length; end++)
            {
                char c = html[end];
                if (c == '\\') { end++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (!inStr)
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                    else if (c == '[') bracketDepth++;
                    else if (c == ']') bracketDepth--;
                }
                if (braceDepth == 0 && bracketDepth == 0 && end > start)
                    break;
            }
            return (end < html.Length) ? html.Substring(start, end - start + 1) : null;
        }

        private static (int currentPage, int totalPages, int totalCount) ParseSsrPagination(string html, int currentPageFallback)
        {
            string data = ExtractSsrDataObject(html);
            if (data == null) return (currentPageFallback, 1, 0);
            data = data.Replace("\\\"", "\"");
            try
            {
                using var doc = JsonDocument.Parse(data);
                int cp = currentPageFallback, tp = 1, tc = 0;
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var dataObj))
                {
                    if (dataObj.TryGetProperty("current_page", out var c)) cp = c.GetInt32();
                    if (dataObj.TryGetProperty("total_pages", out var t)) tp = t.GetInt32();
                    if (dataObj.TryGetProperty("total_count", out var tcProp)) tc = tcProp.GetInt32();
                }
                return (cp, tp, tc);
            }
            catch { return (currentPageFallback, 1, 0); }
        }

        public static List<SteamMod> ParseModsFromHtml(string html)
        {
            var mods = new List<SteamMod>();
            string panelStartTag = "<div class=\"tmIrUKf-Mh8- Panel\">";
            int searchFrom = 0;

            while (true)
            {
                int start = html.IndexOf(panelStartTag, searchFrom, StringComparison.Ordinal);
                if (start < 0) break;

                // Count balanced <div> and </div> to find the matching closing tag
                int depth = 1;
                int end = start + panelStartTag.Length;

                for (; end < html.Length && depth > 0; end++)
                {
                    if (html[end] == '<')
                    {
                        if (end + 6 <= html.Length &&
                            html[end + 1] == '/' &&
                            char.ToLowerInvariant(html[end + 2]) == 'd' &&
                            char.ToLowerInvariant(html[end + 3]) == 'i' &&
                            char.ToLowerInvariant(html[end + 4]) == 'v' &&
                            html[end + 5] == '>')
                        {
                            depth--;
                            end += 5;
                        }
                        else
                        {
                            int gtPos = html.IndexOf('>', end + 1);
                            if (gtPos > end)
                            {
                                // Extract tag name
                                int tagNameEnd = end + 1;
                                while (tagNameEnd < gtPos &&
                                       !char.IsWhiteSpace(html[tagNameEnd]) &&
                                       html[tagNameEnd] != '>')
                                    tagNameEnd++;
                                string tagName = html.Substring(end + 1, tagNameEnd - end - 1);

                                if (string.Equals(tagName, "div", StringComparison.OrdinalIgnoreCase) &&
                                    gtPos > 0 && html[gtPos - 1] != '/')
                                {
                                    depth++;
                                }
                                end = gtPos;
                            }
                        }
                    }
                }
                if (depth != 0) break;

                string panelContent = html.Substring(start, end - start);
                var mod = ParseModFromPanel(panelContent);
                if (mod != null) mods.Add(mod);

                searchFrom = end;
            }
            return mods;
        }

        private static SteamMod ParseModFromPanel(string panelContent)
        {
            // Extract mod ID from URL ?id=XXXX
            var idMatch = Regex.Match(panelContent, @"\?id=(\d+)");
            if (!idMatch.Success) return null;
            string modId = idMatch.Groups[1].Value;

            // Extract title from _3rvey4VpXts- div
            var titleMatch = Regex.Match(panelContent,
                @"<div\s+class=""_3rvey4VpXts-"">\s*<a\s+href=""[^""]*"">(.*?)</a>",
                RegexOptions.Singleline);
            string title = titleMatch.Success
                ? HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
                : "";

            // Extract author name + author URL from CmHGWYJjMk0- div
            var authorMatch = Regex.Match(panelContent,
                @"<div\s+class=""CmHGWYJjMk0-"">\s*<a\s+href=""([^""]*)"">By\s+(.*?)</a>",
                RegexOptions.Singleline);
            string author = authorMatch.Success
                ? HttpUtility.HtmlDecode(authorMatch.Groups[2].Value.Trim())
                : "";
            string authorUrl = authorMatch.Success ? authorMatch.Groups[1].Value.Trim() : "";

            // Extract preview image URL from <img src="...">
            var imgMatch = Regex.Match(panelContent,
                @"<img\s+[^>]*src=""([^""]*)""",
                RegexOptions.Singleline);
            string previewImage = imgMatch.Success
                ? HttpUtility.HtmlDecode(imgMatch.Groups[1].Value.Trim())
                : "";

            return new SteamMod
            {
                Id = modId,
                Title = title,
                Author = author,
                AuthorUrl = authorUrl,
                PreviewImage = previewImage
            };
        }

        public static List<SteamMod> ParseAuthorMods(string html)
        {
            var mods = ParseModsFromHtml(html);

            var authorMatch = Regex.Match(
                html,
                @"<span id=""HeaderUserInfoName"">.*?<a[^>]*>(.*?)</a>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            string authorName = authorMatch.Success ? authorMatch.Groups[1].Value.Trim() : "未知作者";
            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.Author))
                    mod.Author = authorName;
            }

            return mods;
        }

        private async Task FetchModDetailAsync(string modId, CancellationToken ct)
        {
            try
            {
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("itemcount", "1"),
                    new KeyValuePair<string, string>($"publishedfileids[0]", modId)
                };

                var response = await _http.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    new FormUrlEncodedContent(formData), ct);

                if (!response.IsSuccessStatusCode) return;
                ct.ThrowIfCancellationRequested();

                string json = await response.Content.ReadAsStringAsync(ct);
                ct.ThrowIfCancellationRequested();

                // Export raw JSON to debug file
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                    Directory.CreateDirectory(dir);
                    string jsonPath = Path.Combine(dir, $"mod_detail_{modId}.json");
                    // Pretty-print the JSON
                    using var doc = JsonDocument.Parse(json);
                    var formatted = System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    File.WriteAllText(jsonPath, formatted, Encoding.UTF8);
                }
                catch { }

                using var doc2 = JsonDocument.Parse(json);
                var detail = doc2.RootElement
                    .GetProperty("response")
                    .GetProperty("publishedfiledetails")
                    .EnumerateArray()
                    .First();

                ct.ThrowIfCancellationRequested();

                // Update the SteamMod in the list with fetched details
                var modInList = _allMods.FirstOrDefault(m => m.Id == modId);
                if (modInList == null) return;

                if (detail.TryGetProperty("subscriptions", out var subs))
                {
                    modInList.Subscriptions = subs.GetInt32();
                }
                if (detail.TryGetProperty("description", out var desc))
                {
                    string d = desc.GetString() ?? "";
                    d = Regex.Unescape(d);
                    d = HttpUtility.HtmlDecode(d);
                    modInList.Description = d;
                }

                ct.ThrowIfCancellationRequested();

                // Update detail panel on UI thread (preview image not changed - keep from HTML parsing)
                Dispatcher.Invoke(() =>
                {
                    ModSubsText.Text = modInList.Subscriptions > 0
                        ? modInList.Subscriptions.ToString("N0")
                        : "";

                    BuildDescriptionText(modInList.Description ?? "");

                    dgMods.Items.Refresh();
                });
            }
            catch (OperationCanceledException)
            {
                // User clicked another mod - silently ignore
            }
            catch
            {
                // Cannot reach Steam API - just keep basic info shown
            }
        }

        /// <summary>
        /// Parse description with [url=URL]text[/url] into clickable Hyperlinks,
        /// strip other pseudo-tags like [h2], [/h3], [b], [/b] etc.
        /// </summary>
        private void BuildDescriptionText(string description)
        {
            ModDescText.Inlines.Clear();

            if (string.IsNullOrEmpty(description))
                return;

            // Process [url=URL]text[/url] segments
            int lastEnd = 0;
            var urlRegex = new Regex(@"\[url=([^\]]+)\](.*?)\[/url\]", RegexOptions.IgnoreCase);

            foreach (Match match in urlRegex.Matches(description))
            {
                // Add plain text before this url segment (strip remaining tags)
                if (match.Index > lastEnd)
                {
                    string plain = description.Substring(lastEnd, match.Index - lastEnd);
                    plain = StripPseudoTags(plain);
                    if (!string.IsNullOrEmpty(plain))
                        ModDescText.Inlines.Add(new Run(plain));
                }

                // Extract mod ID from URL
                string url = match.Groups[1].Value;
                string text = match.Groups[2].Value;
                var idMatch = Regex.Match(url, @"id=(\d+)");
                string modId = idMatch.Success ? idMatch.Groups[1].Value : null;

                if (modId != null)
                {
                    // Create clickable Hyperlink
                    var link = new Hyperlink();
                    link.Inlines.Add(new Run(text));
                    link.Click += (s, e) => _ = ModIdSearchAsync(modId);
                    ModDescText.Inlines.Add(link);
                }
                else
                {
                    // No mod ID found, show as plain text
                    ModDescText.Inlines.Add(new Run(text));
                }

                lastEnd = match.Index + match.Length;
            }

            // Add remaining text after last url segment
            if (lastEnd < description.Length)
            {
                string remaining = description.Substring(lastEnd);
                remaining = StripPseudoTags(remaining);
                if (!string.IsNullOrEmpty(remaining))
                    ModDescText.Inlines.Add(new Run(remaining));
            }
        }

        /// <summary>
        /// Strip [tagname] and [/tagname] but NOT [url=...]
        /// </summary>
        private static string StripPseudoTags(string text)
        {
            return Regex.Replace(text, @"\[/?\w+\]", "", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Search by mod ID: switch to search mode and load results for this mod ID
        /// </summary>
        private async Task ModIdSearchAsync(string modId)
        {
            _isSearchMode = true;
            _currentSearchText = modId;
            ModSearchBox.Text = modId;
            await LoadPageAsync(1);
        }

        private async Task LoadPageAsync(int page)
        {
            btnPrev.IsEnabled = false;
            btnNext.IsEnabled = false;
            txtStatus.Text = $"正在加载第 {page} 页...";

            try
            {
                string url;
                if (_isSearchMode && !string.IsNullOrWhiteSpace(_currentSearchText))
                {
                    string searchText = Uri.EscapeDataString(_currentSearchText);
                    url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort=textsearch&section=readytouseitems&p={page}&num_per_page=30&searchtext={searchText}";
                }
                else
                    url = $"https://steamcommunity.com/workshop/browse/?appid={AppId}&browsesort=toprated&section=readytouseitems&p={page}&num_per_page=30";

                string html = await _http.GetStringAsync(url);
                var mods = ParseModsFromHtml(html);
                _allMods = mods;
                dgMods.ItemsSource = mods;

                var (cp, tp, tc) = ParseSsrPagination(html, page);
                _totalPage = tp > 1 ? tp : ParseTotalPageCount(html);
                _currentPage = cp > 0 ? cp : page;
                _totalModCount = tc;

                txtPageInfo.Text = $"第 {_currentPage} 页 / 共 {_totalPage} 页（共 {_totalModCount} 个Mod）";
                txtStatus.Text = $"加载完成：第 {_currentPage} 页，共 {mods.Count} 个Mod";

                AutoCheckSubscribedMods(mods);
                RefreshInstalledModGridAsync();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "加载失败";
                _ = new ContentDialog
                {
                    Title = "加载失败",
                    Content = $"加载第 {page} 页失败：{ex.Message}\n\n提示：Steam Workshop 需要网络加速器（如 Watt Toolkit）才能正常访问。",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
            finally
            {
                btnPrev.IsEnabled = _currentPage > 1;
                btnNext.IsEnabled = _currentPage < _totalPage;
            }
        }

        private void AutoCheckSubscribedMods(IEnumerable<SteamMod> currentMods)
        {
            Server server = SteamServerComboBox.SelectedItem as Server;
            if (server == null || server.SubscribedMods == null) return;

            ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            HashSet<string> installed = new HashSet<string>(server.SubscribedMods);
            if (!string.IsNullOrWhiteSpace(serverSettings.Mods))
            {
                var modsFromSettings = serverSettings.Mods.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var modId in modsFromSettings)
                {
                    string id = modId.Trim();

                    if (!string.IsNullOrEmpty(id) && !server.SubscribedMods.Contains(id))
                    {
                        server.SubscribedMods.Add(id);
                    }
                }
            }

            foreach (var mod in currentMods)
            {
                mod.IsChecked = installed.Contains(mod.Id);
            }
        }

        private int ParseTotalPageCount(string html)
        {
            var (_, tp, _) = ParseSsrPagination(html, 1);
            if (tp > 1) return tp;

            try
            {
                var match = Regex.Match(html, @"total_pages[^0-9]*?(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int pages))
                    return pages;
                return 1;
            }
            catch
            {
                return 1;
            }
        }

        private static void ExportDebugHtml(string html)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                Directory.CreateDirectory(dir);

                string formatted = html.Replace("</", "\n</").Replace("><", ">\n<");
                File.WriteAllText(Path.Combine(dir, "workshop_debug.html"), formatted, Encoding.UTF8);

                string raw = ExtractSsrDataObject(html);
                if (raw != null)
                {
                    raw = raw.Replace("\\", "");
                    raw = raw.Replace("{", "\n{\n");
                    raw = raw.Replace("}", "\n}\n");
                    raw = raw.Replace("[", "\n[\n");
                    raw = raw.Replace("]", "\n]\n");
                    raw = raw.Replace(",", ",\n");
                    var lines = raw.Split('\n');
                    var sb = new StringBuilder();
                    int indent = 0;
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        if (trimmed.StartsWith("}") || trimmed.StartsWith("]"))
                            indent = Math.Max(0, indent - 1);
                        sb.Append(new string(' ', indent * 4));
                        sb.AppendLine(trimmed);
                        if (trimmed.EndsWith("{") || trimmed.EndsWith("["))
                            indent++;
                    }
                    File.WriteAllText(Path.Combine(dir, "workshop_ssr.json"), sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private void SafeCopyToClipboard(string text)
        {
            try
            {
                System.Windows.IDataObject data = new DataObject(DataFormats.UnicodeText, text);
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == -2147221040) 
            {
                //System.Threading.Thread.Sleep(50);
            }
        }

        private async void RefreshInstalledModGridAsync()
        {
            Server server = SteamServerComboBox.SelectedItem as Server;
            if (server == null || server.SubscribedMods == null)
            {
                installedMods.ItemsSource = new ObservableCollection<InstalledModViewModel>();
                return;
            }

            var modIds = server.SubscribedMods.ToList();
            await FetchModNamesAsync(modIds);

            var observableList = new ObservableCollection<InstalledModViewModel>();

            foreach (var id in modIds)
            {
                observableList.Add(new InstalledModViewModel
                {
                    Id = id,
                    Title = _modNameCache.TryGetValue(id, out var name) ? name : $"MOD {id}"
                });
            }

            InstallModsText.Text = $"服务器已订阅Mods：{modIds.Count.ToString()} 个";
            installedMods.ItemsSource = observableList;
        }

        private async Task FetchModNamesAsync(List<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return;

            var needFetch = ids.Where(id => !_modNameCache.ContainsKey(id)).ToList();
            if (needFetch.Count == 0)
                return;

            try
            {
                var formData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("itemcount", needFetch.Count.ToString())
                };
                for (int i = 0; i < needFetch.Count; i++)
                {
                    formData.Add(new KeyValuePair<string, string>($"publishedfileids[{i}]", needFetch[i]));
                }

                var response = await _http.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    new FormUrlEncodedContent(formData));

                if (!response.IsSuccessStatusCode)
                    return;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var details = doc.RootElement
                    .GetProperty("response")
                    .GetProperty("publishedfiledetails");

                foreach (var d in details.EnumerateArray())
                {
                    if (!d.TryGetProperty("publishedfileid", out var idProp)) 
                        continue;
                    string id = idProp.GetString() ?? "";
                    if (string.IsNullOrEmpty(id)) 
                        continue;

                    if (d.TryGetProperty("title", out var titleProp))
                        _modNameCache[id] = titleProp.GetString() ?? "Unknown";
                    else
                        _modNameCache[id] = "Unknown";

                    //if (d.TryGetProperty("time_updated", out var timeProp))
                    //    _modSteamTimestamp[id] = timeProp.GetInt64();
                }
                return;
            }
            catch
            {
                foreach (var id in needFetch)
                {
                    if (!_modNameCache.ContainsKey(id))
                        _modNameCache[id] = "Unknown";
                }
            }
        }

        private void InstalledMods_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedMod = GetDataGridItemFromPoint(e.GetPosition(installedMods)) as InstalledModViewModel;

            if (_draggedMod != null && _draggedMod.IsPlaceholder)
                _draggedMod = null;
        }

        private void InstalledMods_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                _draggedMod != null &&
                !_isDragging &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    _originalIndex = mods.IndexOf(_draggedMod);

                    if (_originalIndex != -1)
                    {
                        mods.RemoveAt(_originalIndex);
                        DragDrop.DoDragDrop(installedMods, _draggedMod, DragDropEffects.Move);
                    }
                }
            }
        }

        private void InstalledMods_DragOver(object sender, DragEventArgs e)
        {
            if (_isDragging &&
                e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
            {
                Point point = e.GetPosition(installedMods);
                InstalledModViewModel targetMod = GetDataGridItemFromPoint(point) as InstalledModViewModel;

                int insertIndex = 0;

                if (targetMod != null)
                {
                    insertIndex = mods.IndexOf(targetMod);
                    DataGridRow row = GetDataGridRowFromPoint(point);
                    if (row != null)
                    {
                        Point rowPoint = e.GetPosition(row);
                        if (rowPoint.Y > row.ActualHeight)
                        {
                            insertIndex++;
                        }
                    }
                }
                else if(point.Y < 5)
                {
                    //insertIndex = 0;
                }
                else
                {
                    //insertIndex = mods.Count;
                }

                _finalInsertIndex = insertIndex;

                UpdatePlaceholder(insertIndex, mods);
            }
        }

        private void InstalledMods_Drop(object sender, DragEventArgs e)
        {
            if (!_isDragging) return;

            try
            {
                if (e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                    installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
                {
                    RemovePlaceholder(mods);

                    InstalledModViewModel droppedMod = e.Data.GetData(typeof(InstalledModViewModel)) as InstalledModViewModel;

                    if (droppedMod != null && droppedMod == _draggedMod)
                    {
                        int newIndex = _finalInsertIndex;

                        if (newIndex < 0 || newIndex > mods.Count)
                        {
                            newIndex = _originalIndex;
                        }

                        newIndex = Math.Max(0, Math.Min(newIndex, mods.Count));
                        mods.Insert(newIndex, droppedMod);
                    }
                }
            }
            finally
            {
                // 重置所有
                _isDragging = false;
                _draggedMod = null;
                _originalIndex = -1;
                _finalInsertIndex = -1;
            }
        }

        private void UpdatePlaceholder(int insertIndex, ObservableCollection<InstalledModViewModel> mods)
        {
            RemovePlaceholder(mods);

            if (_draggedMod == null) return;

            _placeholderMod = new InstalledModViewModel
            {
                Id = _draggedMod.Id,
                Title = _draggedMod.Title,
                IsPlaceholder = true
            };

            insertIndex = Math.Max(0, Math.Min(insertIndex, mods.Count));
            mods.Insert(insertIndex, _placeholderMod);
            _lastPlaceholderIndex = insertIndex;
        }

        private void RemovePlaceholder(ObservableCollection<InstalledModViewModel> mods)
        {
            if (_placeholderMod != null && mods.Contains(_placeholderMod))
            {
                mods.Remove(_placeholderMod);
            }
            _placeholderMod = null;
            _lastPlaceholderIndex = -1;
        }

        private object GetDataGridItemFromPoint(Point point)
        {
            HitTestResult hit = VisualTreeHelper.HitTest(installedMods, point);
            if (hit == null) return null;

            DependencyObject obj = hit.VisualHit;
            while (obj != null && !(obj is DataGridRow))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }

            return (obj as DataGridRow)?.Item;
        }

        private DataGridRow GetDataGridRowFromPoint(Point point)
        {
            HitTestResult hit = VisualTreeHelper.HitTest(installedMods, point);
            if (hit == null) return null;

            DependencyObject obj = hit.VisualHit;
            while (obj != null && !(obj is DataGridRow))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }

            return obj as DataGridRow;
        }

        private void SaveModsList(Server server)
        {
            ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            var currentPageMods = dgMods.ItemsSource as IEnumerable<SteamMod>;
            if (currentPageMods == null)
            {
                _ = new ContentDialog
                {
                    Title = "提示",
                    Content = "没有可保存的MOD",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            var currentInstalledMods = installedMods.ItemsSource as ObservableCollection<InstalledModViewModel>
                                        ?? new ObservableCollection<InstalledModViewModel>();

            Dictionary<string, SteamMod> currentPageModDict = currentPageMods.ToDictionary(m => m.Id);
            List<string> finalOrderedIds = new List<string>();

            foreach (var installedMod in currentInstalledMods)
            {
                if (installedMod.IsMarkedForDelete) continue;

                if (currentPageModDict.TryGetValue(installedMod.Id, out var correspondingMod))
                {
                    if (correspondingMod.IsChecked)
                        finalOrderedIds.Add(installedMod.Id);
                }
                else
                    finalOrderedIds.Add(installedMod.Id);
            }

            //foreach (var installedMod in currentInstalledMods)
            //{
            //    if (currentPageModDict.TryGetValue(installedMod.Id, out var correspondingMod))
            //    {
            //        if (correspondingMod.IsChecked)
            //        {
            //            finalOrderedIds.Add(installedMod.Id);
            //        }
            //    }
            //    else
            //    {
            //        finalOrderedIds.Add(installedMod.Id);
            //    }
            //}

            HashSet<string> alreadyAdded = new HashSet<string>(finalOrderedIds);
            foreach (var mod in currentPageMods)
            {
                if (mod.IsChecked && !alreadyAdded.Contains(mod.Id))
                    finalOrderedIds.Add(mod.Id);
            }

            server.SubscribedMods.Clear();
            server.SubscribedMods.AddRange(finalOrderedIds);
            serverSettings.Mods = string.Join(",", server.SubscribedMods);
            ServerSettingsEditor.SaveServerSettings(server, serverSettings);
            MainSettings.Save(SsmSettings);
        }

        private async void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            _isSearchMode = false;
            _currentSearchText = "";
            ModSearchBox.Text = ""; 
            await LoadPageAsync(1);
        }
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            Server server = (Server)SteamServerComboBox.SelectedItem;
            SaveModsList(server);
            RefreshInstalledModGridAsync();
        }

        private async void UnInstallAllMod_Click(object sender, RoutedEventArgs e)
        {
            Server server = (Server)SteamServerComboBox.SelectedItem;
            ServerSettings serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            if (server.SubscribedMods.Count != 0)
            {
                var contentDialog = new ContentDialog()
                {
                    Title = "警告",
                    Content = "确定取消订阅所有MOD？此操作可能导致服务器有关Mod的物品出错，请谨慎选择！",
                    PrimaryButtonText = "确定",
                    SecondaryButtonText = "取消"
                };

                if (await contentDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    server.SubscribedMods.Clear();
                    serverSettings.Mods = "";

                    if (dgMods.ItemsSource is IEnumerable<SteamMod> currentMods)
                    {
                        foreach (var mod in currentMods)
                        {
                            mod.IsChecked = false;
                        }
                    }
                    ServerSettingsEditor.SaveServerSettings(server, serverSettings);
                    MainSettings.Save(SsmSettings);
                    RefreshInstalledModGridAsync();

                }
                return;
            }
            else
            {
                var contentDialog = new ContentDialog()
                {
                    Title = "提示",
                    Content = "没有可取消订阅的Mod！",
                    PrimaryButtonText = "确定",
                    SecondaryButtonText = "取消"
                }.ShowAsync();
            }
        }

        private void BtnAddManualModId_Click(object sender, RoutedEventArgs e)
        {
            string modId = TxtManualModId.Text.Trim();
            if (string.IsNullOrEmpty(modId) || !long.TryParse(modId, out _)) return;

            Server server = SteamServerComboBox.SelectedItem as Server;
            if (server == null) return;

            if (server.SubscribedMods.Contains(modId)) return;

            if (dgMods.ItemsSource is IEnumerable<SteamMod> modList)
            {
                var findMod = modList.FirstOrDefault(m => m.Id == modId);
                if (findMod != null)
                {
                    findMod.IsChecked = true;
                    dgMods.Items.Refresh();
                }
            }

            server.SubscribedMods.Add(modId);
            TxtManualModId.Clear();
            RefreshInstalledModGridAsync();
        }

        private async void SearchIcon_Click(object sender, RoutedEventArgs e)
        {
            await SteamSearchAsync(ModSearchBox.Text);
        }

        private void Hyperlink_RequestNavigate_Click(object sender, RequestNavigateEventArgs e)
        {
            Hyperlink hyperlink = (Hyperlink)sender;
            Process.Start(new ProcessStartInfo { FileName = hyperlink.NavigateUri.ToString(), UseShellExecute = true });
        }

        private void OpenWorkshopPage_Click(object sender, RoutedEventArgs e)
        {
            if (dgMods.SelectedItem is SteamMod mod)
            {
                Process.Start(new ProcessStartInfo { FileName = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + mod.Id, UseShellExecute = true });
            }
        }
        
        // 使用反射获取名字和id，tag和item的对应属性要一样
        private void CopyCommon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            string propName = menuItem.Tag as string;

            if (menuItem.Parent is not ContextMenu ctx || ctx.PlacementTarget is not DataGrid dg)
                return;

            var item = dg.SelectedItem;
            if (item == null || string.IsNullOrEmpty(propName)) return;

            var prop = item.GetType().GetProperty(propName);
            if (prop == null) return;

            string text = prop.GetValue(item)?.ToString() ?? "";

            if (!string.IsNullOrWhiteSpace(text))
                SafeCopyToClipboard(text);
        }

        private void DeleteCommon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.Parent is not ContextMenu ctx || ctx.PlacementTarget is not DataGrid dg)
                return;

            var selectedMod = dg.SelectedItem as InstalledModViewModel;
            if (selectedMod == null) return;

            selectedMod.IsMarkedForDelete = true;
            dg.Items.Refresh();

            if (dgMods.ItemsSource is IEnumerable<SteamMod> allPageMods)
            {
                var modInList = allPageMods.FirstOrDefault(m => m.Id == selectedMod.Id);
                if (modInList != null)
                {
                    modInList.IsChecked = false;
                    dgMods.Items.Refresh();
                }
            }

        }

        private async void ShowOnlyThisAuthor_Click(object sender, RoutedEventArgs e)
        {
            if (dgMods.SelectedItem is not SteamMod mod) return;
            if (string.IsNullOrWhiteSpace(mod.AuthorUrl)) return;

            string authorUrl = $"{mod.AuthorUrl}&p=1&numperpage=30";

            txtStatus.Text = $"正在加载 {mod.Author} 的所有MOD...";

            try
            {
                string html = await _http.GetStringAsync(authorUrl);

                var mods = ParseAuthorMods(html);
                _allMods = mods;
                dgMods.ItemsSource = mods;

                AutoCheckSubscribedMods(mods);

                txtStatus.Text = $"已加载 {mod.Author} 的 {mods.Count} 个MOD";
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "加载失败",
                    Content = $"加载失败：{ex.Message}\n\n提示：Steam Workshop 需要网络加速器（如 Watt Toolkit）才能正常访问。",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
            finally
            {
            }
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                await LoadPageAsync(_currentPage - 1);
            }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPage)
            {
                await LoadPageAsync(_currentPage + 1);
            }
        }
    }
}
