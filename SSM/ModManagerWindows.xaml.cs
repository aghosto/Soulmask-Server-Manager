using ModernWpf.Controls;
using SoulmaskServerManager;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;

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
        private int? _totalModCount;

        private string _currentSearchText = "";
        private bool _isSearchMode = false;

        MainSettings SsmSettings = new();
        SteamModsList SteamMods = new();

        // 拖动排序相关字段
        private InstalledModViewModel _draggedMod;
        private Point _dragStartPoint;
        private bool _isDragging = false;
        private bool _dropCompleted = false;
        private int _originalIndex = -1;
        private int _lastPlaceholderIndex = -1;
        private int _finalInsertIndex = -1;
        private InstalledModViewModel _placeholderMod;
        private readonly DispatcherTimer _dragAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        private int _dragAutoScrollDirection;
        private Point _lastDragPosition;

        private Dictionary<string, string> _modNameCache = new Dictionary<string, string>();
        private CancellationTokenSource _detailCts;
        private CancellationTokenSource _pageLoadCts;
        private int _pageLoadRequestId;

        public ModManagerWindows(MainSettings mainSettings, Server? selectedServer = null)
        {
            SsmSettings = mainSettings;
            InitializeComponent();
            _dragAutoScrollTimer.Tick += InstalledMods_DragAutoScrollTimer_Tick;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            SteamServerComboBox.DataContext = SsmSettings;

            if (SsmSettings.Servers.Count > 0)
            {
                SelectServer(selectedServer);
            }
            Closed += (_, _) =>
            {
                _pageLoadCts?.Cancel();
                _detailCts?.Cancel();
            };
            Loaded += async (s, e) => await LoadPageAsync(1);
        }

        /// <summary>
        /// 选择要管理 Mod 的服务器；未指定或未包含在服务器列表中时选择第一台服务器。
        /// </summary>
        /// <param name="server">希望选中的服务器。</param>
        public void SelectServer(Server? server)
        {
            if (SsmSettings.Servers.Count == 0)
                return;

            SteamServerComboBox.SelectedItem = server != null && SsmSettings.Servers.Contains(server)
                ? server
                : SsmSettings.Servers[0];
        }

        private async void DgMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgMods.SelectedItem is not SteamMod mod)
            {
                _detailCts?.Cancel();
                return;
            }

            // Cancel any previous detail fetch in progress
            _detailCts?.Cancel();
            _detailCts = new CancellationTokenSource();
            var ct = _detailCts.Token;

            ShowModDetails(mod);

            // Fetch full details from Steam API
            await FetchModDetailAsync(mod.Id, ct);
        }

        private void ShowModDetails(SteamMod mod)
        {
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
        }

        private void SelectFirstMod()
        {
            dgMods.SelectedIndex = -1;
            if (dgMods.Items.Count > 0)
                dgMods.SelectedIndex = 0;
            else
            {
                _detailCts?.Cancel();
                ModIdText.Text = "";
                ModTitleText.Text = "";
                ModAuthorText.Text = "";
                ModAuthorHyperlink.NavigateUri = null;
                ModImage.Source = null;
                ModSubsText.Text = "";
                BuildDescriptionText("");
            }
        }

        private void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SteamServerComboBox.SelectedItem is not Server server)
                return;

            AutoCheckSubscribedMods(_allMods);
            RefreshInstalledModGridAsync();
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

        private sealed class WorkshopSsrPage
        {
            public int CurrentPage { get; init; }
            public int TotalPages { get; init; }
            public int? TotalCount { get; init; }
            public List<SteamMod> Mods { get; init; } = new();
        }

        /// <summary>
        /// 从页面序列化的 SSR 数据读取稳定字段，避免依赖动态生成的 CSS 类名。
        /// </summary>
        private static WorkshopSsrPage ParseWorkshopSsrPage(string html, int currentPageFallback)
        {
            static Match MatchField(string source, string name) => Regex.Match(
                source,
                $@"\\*""{Regex.Escape(name)}\\*""\s*:\s*(?:\\*"")?(?<value>\d+)(?:\\*"")?",
                RegexOptions.Singleline);

            int ReadInt(string name, int fallback)
            {
                var match = MatchField(html, name);
                return match.Success && int.TryParse(match.Groups["value"].Value, out int value)
                    ? value
                    : fallback;
            }

            string ReadString(string source, string name)
            {
                var match = Regex.Match(
                    source,
                    $@"\\*""{Regex.Escape(name)}\\*""\s*:\s*\\*""(?<value>.*?)\\*""",
                    RegexOptions.Singleline);
                return match.Success
                    ? HttpUtility.HtmlDecode(Regex.Unescape(match.Groups["value"].Value))
                    : "";
            }

            var idMatches = Regex.Matches(
                html,
                @"\\*""publishedfileid\\*""\s*:\s*\\*""(?<id>\d+)\\*""",
                RegexOptions.Singleline);
            var mods = new List<SteamMod>();
            for (int i = 0; i < idMatches.Count; i++)
            {
                var idMatch = idMatches[i];
                int recordEnd = i + 1 < idMatches.Count ? idMatches[i + 1].Index : html.Length;
                string record = html.Substring(idMatch.Index, recordEnd - idMatch.Index);
                string creator = ReadString(record, "creator");
                mods.Add(new SteamMod
                {
                    Id = idMatch.Groups["id"].Value,
                    Title = ReadString(record, "title"),
                    Author = creator,
                    AuthorUrl = string.IsNullOrWhiteSpace(creator) ? "" : $"https://steamcommunity.com/profiles/{creator}",
                    PreviewImage = ReadString(record, "preview_url")
                });
            }

            var totalCountMatch = MatchField(html, "total_count");
            int? totalCount = totalCountMatch.Success &&
                              int.TryParse(totalCountMatch.Groups["value"].Value, out int parsedCount) &&
                              parsedCount >= 0
                ? parsedCount
                : null;

            return new WorkshopSsrPage
            {
                CurrentPage = ReadInt("current_page", currentPageFallback),
                TotalPages = ReadInt("total_pages", 1),
                TotalCount = totalCount,
                Mods = mods.GroupBy(mod => mod.Id).Select(group => group.First()).ToList()
            };
        }
        public static List<SteamMod> ParseModsFromHtml(string html)
        {
            var linkMatches = Regex.Matches(
                html,
                @"<a\b[^>]*href=""[^""]*[?&]id=(?<id>\d+)[^""]*""[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Each card may contain multiple links to the same workshop item. Keep the first
            // link for each consecutive item and parse only up to the next item's link.
            var cardStarts = new List<(string Id, int Index)>();
            string previousId = null;
            foreach (Match link in linkMatches)
            {
                string id = link.Groups["id"].Value;
                if (id == previousId) continue;
                cardStarts.Add((id, link.Index));
                previousId = id;
            }

            var mods = new List<SteamMod>();
            for (int i = 0; i < cardStarts.Count; i++)
            {
                int start = cardStarts[i].Index;
                int end = i + 1 < cardStarts.Count ? cardStarts[i + 1].Index : html.Length;
                string card = html.Substring(start, end - start);

                var authorMatch = Regex.Match(
                    card,
                    @"<a\s+[^>]*href=""(?<url>[^""]*)""[^>]*>\s*By\s+(?<name>.*?)</a>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                string author = authorMatch.Success
                    ? HttpUtility.HtmlDecode(Regex.Replace(authorMatch.Groups["name"].Value, "<[^>]+>", "").Trim())
                    : "";
                string authorUrl = authorMatch.Success
                    ? HttpUtility.HtmlDecode(authorMatch.Groups["url"].Value.Trim())
                    : "";

                string title = "";
                foreach (Match titleLink in Regex.Matches(
                             card,
                             @"<a\b[^>]*href=""[^""]*[?&]id=" + Regex.Escape(cardStarts[i].Id) + @"[^""]*""[^>]*>(?<content>.*?)</a>",
                             RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string candidate = HttpUtility.HtmlDecode(Regex.Replace(titleLink.Groups["content"].Value, "<[^>]+>", "").Trim());
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        title = candidate;
                        break;
                    }
                }

                var imageMatch = Regex.Match(card, @"<img\s+[^>]*src=""(?<src>[^""]*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                mods.Add(new SteamMod
                {
                    Id = cardStarts[i].Id,
                    Title = title,
                    Author = author,
                    AuthorUrl = authorUrl,
                    PreviewImage = imageMatch.Success ? HttpUtility.HtmlDecode(imageMatch.Groups["src"].Value.Trim()) : ""
                });
            }

            return mods.GroupBy(mod => mod.Id).Select(group => group.First()).ToList();
        }
        private static bool IsSteamId(string value) => Regex.IsMatch(value ?? "", @"^\d{17}$");

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

                if (detail.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    modInList.Title = title.GetString() ?? modInList.Title;
                if (detail.TryGetProperty("preview_url", out var preview) && preview.ValueKind == JsonValueKind.String)
                    modInList.PreviewImage = preview.GetString() ?? modInList.PreviewImage;
                if (detail.TryGetProperty("creator", out var creator) && creator.ValueKind == JsonValueKind.String &&
                    string.IsNullOrWhiteSpace(modInList.AuthorUrl))
                    modInList.AuthorUrl = $"https://steamcommunity.com/profiles/{creator.GetString()}";
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

                // Refresh the selected mod's complete detail panel with the API response.
                Dispatcher.Invoke(() =>
                {
                    if (dgMods.SelectedItem is SteamMod selected && selected.Id == modInList.Id)
                        ShowModDetails(modInList);
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
            _pageLoadCts?.Cancel();
            _pageLoadCts?.Dispose();
            var requestCts = new CancellationTokenSource();
            _pageLoadCts = requestCts;
            int requestId = Interlocked.Increment(ref _pageLoadRequestId);
            CancellationToken cancellationToken = requestCts.Token;

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

                string html = await GetWorkshopHtmlAsync(url, cancellationToken);
#if DEBUG
                ExportDebugHtml(html, page);
#endif
                ValidateWorkshopHtml(html);
                cancellationToken.ThrowIfCancellationRequested();
                if (requestId != Volatile.Read(ref _pageLoadRequestId)) return;

                var ssrPage = ParseWorkshopSsrPage(html, page);
                var parsedCards = ParseModsFromHtml(html)
                    .GroupBy(mod => mod.Id)
                    .ToDictionary(group => group.Key, group => group.First());

                // SSR 结果对象是主数据源；页面卡片仅在 SSR 字段缺失时补充信息。
                var mods = ssrPage.Mods.Count > 0
                    ? ssrPage.Mods.Select(mod =>
                    {
                        if (parsedCards.TryGetValue(mod.Id, out var card))
                        {
                            if (string.IsNullOrWhiteSpace(mod.Title)) mod.Title = card.Title;
                            if (string.IsNullOrWhiteSpace(mod.Author) || IsSteamId(mod.Author))
                            {
                                if (!string.IsNullOrWhiteSpace(card.Author) && !IsSteamId(card.Author))
                                    mod.Author = card.Author;
                            }
                            if (string.IsNullOrWhiteSpace(mod.AuthorUrl)) mod.AuthorUrl = card.AuthorUrl;
                            if (string.IsNullOrWhiteSpace(mod.PreviewImage)) mod.PreviewImage = card.PreviewImage;
                        }
                        return mod;
                    }).ToList()
                    : parsedCards.Values.ToList();
                _allMods = mods;
                dgMods.ItemsSource = mods;
                SelectFirstMod();

                _totalPage = ssrPage.TotalPages > 1 ? ssrPage.TotalPages : ParseTotalPageCount(html);
                _currentPage = ssrPage.CurrentPage > 0 ? ssrPage.CurrentPage : page;
                _totalModCount = ssrPage.TotalCount;

                string totalText = _totalModCount.HasValue
                    ? $"共 {_totalModCount.Value} 个Mod"
                    : $"本页 {mods.Count} 个Mod，总数暂不可用";
                txtPageInfo.Text = $"第 {_currentPage} 页 / 共 {_totalPage} 页（{totalText}）";
                txtStatus.Text = $"加载完成：第 {_currentPage} 页，共 {mods.Count} 个Mod";

                AutoCheckSubscribedMods(mods);
                RefreshInstalledModGridAsync();
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException || requestId != Volatile.Read(ref _pageLoadRequestId))
                    return;

                txtStatus.Text = "加载失败";
                _ = new ContentDialog
                {
                    Title = "加载失败",
                    Content = $"加载第 {page} 页失败：{ex.Message}",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
            }
            finally
            {
                if (requestId == Volatile.Read(ref _pageLoadRequestId))
                {
                    btnPrev.IsEnabled = _currentPage > 1;
                    btnNext.IsEnabled = _currentPage < _totalPage;
                    _pageLoadCts = null;
                    requestCts.Dispose();
                }
            }
        }

        private static async Task<string> GetWorkshopHtmlAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Steam Workshop 返回 HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。", null, response.StatusCode);

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
                throw new InvalidDataException("Steam Workshop 返回了空页面。");

            return html;
        }

        private static void ValidateWorkshopHtml(string html)
        {
            string normalized = html.ToLowerInvariant();
            string[] errorMarkers =
            {
                "<title>steam community :: error</title>",
                "<title>sign in</title>",
                "verify you are human",
                "unusual traffic",
                "<title>access denied",
                "id=\"captcha\"",
                "class=\"g-recaptcha\""
            };
            string matchedMarker = errorMarkers.FirstOrDefault(normalized.Contains);
            if (matchedMarker != null)
                throw new InvalidDataException($"Steam Workshop 返回了验证、登录或错误页面（检测到：{matchedMarker}）。");

            var page = ParseWorkshopSsrPage(html, 1);
            if (page.Mods.Count == 0 && !Regex.IsMatch(html, @"<a\b[^>]*href=""[^""]*[?&]id=\d+", RegexOptions.IgnoreCase))
                throw new InvalidDataException("返回内容中没有找到 Workshop 列表数据，可能是作者隐私私密状态、登录页、验证页或页面结构已变化。");
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
            int ssrPages = ParseWorkshopSsrPage(html, 1).TotalPages;
            if (ssrPages > 1) return ssrPages;

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

#if DEBUG
        private static void ExportDebugHtml(string html, int page)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string prefix = $"workshop_page_{stamp}_p{page}";
                File.WriteAllText(Path.Combine(dir, $"{prefix}_raw.html"), html, Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, $"{prefix}_backslashes_removed.html"), html.Replace("\\", ""), Encoding.UTF8);
            }
            catch (Exception ex) { Debug.WriteLine($"导出 Workshop 调试页面失败：{ex}"); }
        }
#endif

        private void SafeCopyToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Clipboard calls can block when another process owns the clipboard.
            // Use the native Win32 clipboard API on an STA worker so the UI stays responsive.
            IntPtr ownerHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var clipboardThread = new Thread(() =>
            {
                const int maxOpenAttempts = 10;
                for (int attempt = 0; attempt < maxOpenAttempts; attempt++)
                {
                    if (!OpenClipboard(ownerHandle))
                    {
                        Thread.Sleep(40 + attempt * 30);
                        continue;
                    }

                    IntPtr globalMemory = IntPtr.Zero;
                    try
                    {
                        if (!EmptyClipboard())
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                        byte[] unicodeText = Encoding.Unicode.GetBytes(text + '\0');
                        globalMemory = GlobalAlloc(GmemMoveable, (UIntPtr)unicodeText.Length);
                        if (globalMemory == IntPtr.Zero)
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                        IntPtr memoryPointer = GlobalLock(globalMemory);
                        if (memoryPointer == IntPtr.Zero)
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                        try
                        {
                            Marshal.Copy(unicodeText, 0, memoryPointer, unicodeText.Length);
                        }
                        finally
                        {
                            GlobalUnlock(globalMemory);
                        }

                        if (SetClipboardData(CfUnicodeText, globalMemory) == IntPtr.Zero)
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                        // Windows now owns the global memory block.
                        globalMemory = IntPtr.Zero;
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"复制文本到剪贴板失败：{ex}");
                        return;
                    }
                    finally
                    {
                        if (globalMemory != IntPtr.Zero)
                            GlobalFree(globalMemory);
                        CloseClipboard();
                    }
                }

                Debug.WriteLine("复制文本到剪贴板失败：剪贴板持续被其他进程占用。");
            })
            {
                IsBackground = true,
                Name = "ModManagerClipboardWriter"
            };
            clipboardThread.SetApartmentState(ApartmentState.STA);
            clipboardThread.Start();
        }

        private const uint GmemMoveable = 0x0002;
        private const uint CfUnicodeText = 13;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr newOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr memory);

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
                    _originalIndex = mods.IndexOf(_draggedMod);
                    if (_originalIndex < 0)
                    {
                        ResetInstalledModDragState();
                        return;
                    }

                    _isDragging = true;
                    _dropCompleted = false;
                    mods.RemoveAt(_originalIndex);
                    try
                    {
                        DragDrop.DoDragDrop(installedMods, _draggedMod, DragDropEffects.Move);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Mod 排序拖放失败：{ex}");
                    }
                    finally
                    {
                        CompleteInstalledModDrag(mods);
                    }
                }
            }
        }

        private void InstalledMods_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
                CompleteInstalledModDrag(mods);
        }

        private void InstalledMods_DragOver(object sender, DragEventArgs e)
        {
            if (_isDragging &&
                e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods)
            {
                Point point = e.GetPosition(installedMods);
                _lastDragPosition = point;
                UpdateInstalledModDropTarget(point, mods);
                UpdateInstalledModAutoScroll(point);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void UpdateInstalledModDropTarget(Point point, ObservableCollection<InstalledModViewModel> mods)
        {
            DataGridRow targetRow = GetDataGridRowFromPoint(point);
            InstalledModViewModel targetMod = targetRow?.Item as InstalledModViewModel;
            int placeholderIndex = _placeholderMod == null ? -1 : mods.IndexOf(_placeholderMod);
            int insertIndex;

            if (targetMod == _placeholderMod && placeholderIndex >= 0)
            {
                insertIndex = placeholderIndex;
            }
            else if (targetMod != null && targetRow != null)
            {
                insertIndex = mods.IndexOf(targetMod);
                if (placeholderIndex >= 0 && placeholderIndex < insertIndex)
                    insertIndex--;

                Point rowPoint = Mouse.GetPosition(targetRow);
                if (rowPoint.Y >= targetRow.ActualHeight / 2)
                    insertIndex++;
            }
            else
            {
                var rows = FindVisualChildren<DataGridRow>(installedMods)
                    .Where(row => row.Item is InstalledModViewModel item && !item.IsPlaceholder)
                    .ToList();
                double lastRowBottom = rows.Count == 0
                    ? 0
                    : rows.Max(row => row.TransformToAncestor(installedMods).Transform(new Point(0, 0)).Y + row.ActualHeight);
                insertIndex = point.Y >= lastRowBottom
                    ? mods.Count - (placeholderIndex >= 0 ? 1 : 0)
                    : 0;
            }

            _finalInsertIndex = insertIndex;
            UpdatePlaceholder(insertIndex, mods);
        }

        private void UpdateInstalledModAutoScroll(Point point)
        {
            const double edgeSize = 44;
            _dragAutoScrollDirection = point.Y < edgeSize
                ? -1
                : point.Y > installedMods.ActualHeight - edgeSize ? 1 : 0;

            if (_dragAutoScrollDirection == 0)
                _dragAutoScrollTimer.Stop();
            else if (!_dragAutoScrollTimer.IsEnabled)
                _dragAutoScrollTimer.Start();
        }

        private void InstalledMods_DragAutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isDragging || _dragAutoScrollDirection == 0 ||
                installedMods.ItemsSource is not ObservableCollection<InstalledModViewModel> mods)
            {
                _dragAutoScrollTimer.Stop();
                return;
            }

            ScrollViewer scrollViewer = FindVisualChildren<ScrollViewer>(installedMods).FirstOrDefault();
            if (scrollViewer == null)
            {
                _dragAutoScrollTimer.Stop();
                return;
            }

            double newOffset = Math.Max(0, Math.Min(
                scrollViewer.ScrollableHeight,
                scrollViewer.VerticalOffset + _dragAutoScrollDirection));
            if (Math.Abs(newOffset - scrollViewer.VerticalOffset) < 0.1)
            {
                _dragAutoScrollTimer.Stop();
                return;
            }

            scrollViewer.ScrollToVerticalOffset(newOffset);
            UpdateInstalledModDropTarget(_lastDragPosition, mods);
        }

        private void InstalledMods_Drop(object sender, DragEventArgs e)
        {
            if (!_isDragging) return;

            if (e.Data.GetDataPresent(typeof(InstalledModViewModel)) &&
                installedMods.ItemsSource is ObservableCollection<InstalledModViewModel> mods &&
                e.Data.GetData(typeof(InstalledModViewModel)) is InstalledModViewModel droppedMod &&
                droppedMod == _draggedMod)
            {
                RemovePlaceholder(mods);
                int newIndex = _finalInsertIndex;
                if (newIndex < 0 || newIndex > mods.Count)
                    newIndex = _originalIndex;

                newIndex = Math.Max(0, Math.Min(newIndex, mods.Count));
                mods.Insert(newIndex, droppedMod);
                _dropCompleted = true;
                e.Effects = DragDropEffects.Move;
            }
            else
                e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void CompleteInstalledModDrag(ObservableCollection<InstalledModViewModel> mods)
        {
            RemovePlaceholder(mods);
            if (!_dropCompleted && _draggedMod != null && !mods.Contains(_draggedMod))
            {
                int restoreIndex = Math.Max(0, Math.Min(_originalIndex, mods.Count));
                mods.Insert(restoreIndex, _draggedMod);
            }

            ResetInstalledModDragState();
        }

        private void ResetInstalledModDragState()
        {
            _dragAutoScrollTimer.Stop();
            _dragAutoScrollDirection = 0;
            _isDragging = false;
            _dropCompleted = false;
            _draggedMod = null;
            _originalIndex = -1;
            _finalInsertIndex = -1;
            _placeholderMod = null;
            _lastPlaceholderIndex = -1;
        }

        private void UpdatePlaceholder(int insertIndex, ObservableCollection<InstalledModViewModel> mods)
        {
            if (_draggedMod == null) return;

            insertIndex = Math.Max(0, Math.Min(insertIndex, Math.Max(0, mods.Count - (_placeholderMod == null ? 0 : 1))));
            if (_placeholderMod != null &&
                mods.Contains(_placeholderMod) &&
                insertIndex == _lastPlaceholderIndex)
                return;

            Dictionary<InstalledModViewModel, double> previousPositions = CaptureInstalledModRowPositions();
            RemovePlaceholder(mods);

            _placeholderMod = new InstalledModViewModel
            {
                Id = _draggedMod.Id,
                Title = _draggedMod.Title,
                IsPlaceholder = true
            };

            insertIndex = Math.Max(0, Math.Min(insertIndex, mods.Count));
            mods.Insert(insertIndex, _placeholderMod);
            _lastPlaceholderIndex = insertIndex;

            installedMods.UpdateLayout();
            AnimateInstalledModRows(previousPositions);
        }

        private Dictionary<InstalledModViewModel, double> CaptureInstalledModRowPositions()
        {
            var positions = new Dictionary<InstalledModViewModel, double>();
            foreach (DataGridRow row in FindVisualChildren<DataGridRow>(installedMods))
            {
                if (row.Item is not InstalledModViewModel item || item.IsPlaceholder)
                    continue;

                if (row.RenderTransform is TranslateTransform translate)
                {
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = 0;
                }

                try
                {
                    positions[item] = row.TransformToAncestor(installedMods).Transform(new Point(0, 0)).Y;
                }
                catch (InvalidOperationException)
                {
                    // Ignore a row that is being virtualized while the drag is moving.
                }
            }

            return positions;
        }

        private void AnimateInstalledModRows(Dictionary<InstalledModViewModel, double> previousPositions)
        {
            if (previousPositions.Count == 0)
                return;

            foreach (DataGridRow row in FindVisualChildren<DataGridRow>(installedMods))
            {
                if (row.Item is not InstalledModViewModel item ||
                    item.IsPlaceholder ||
                    !previousPositions.TryGetValue(item, out double previousY))
                    continue;

                double currentY;
                try
                {
                    currentY = row.TransformToAncestor(installedMods).Transform(new Point(0, 0)).Y;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                double offset = previousY - currentY;
                if (Math.Abs(offset) < 1)
                    continue;

                if (row.RenderTransform is not TranslateTransform translate)
                {
                    translate = new TranslateTransform();
                    row.RenderTransform = translate;
                }

                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
                var animation = new DoubleAnimation
                {
                    From = offset,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                translate.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target)
                    yield return target;

                foreach (T descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
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
            string authorUrl = BuildAuthorWorkshopUrl(mod);
            if (string.IsNullOrWhiteSpace(authorUrl))
            {
                _ = new ContentDialog
                {
                    Title = "无法打开作者页面",
                    Content = "当前 Mod 没有可用的作者 SteamID。",
                    CloseButtonText = "确定",
                    DefaultButton = ContentDialogButton.Close
                }.ShowAsync();
                return;
            }

            _pageLoadCts?.Cancel();
            _pageLoadCts?.Dispose();
            var requestCts = new CancellationTokenSource();
            _pageLoadCts = requestCts;
            int requestId = Interlocked.Increment(ref _pageLoadRequestId);
            CancellationToken cancellationToken = requestCts.Token;

            txtStatus.Text = $"正在加载 {mod.Author} 的所有MOD...";

            try
            {
                string html = await GetWorkshopHtmlAsync(authorUrl, cancellationToken);
#if DEBUG
                ExportDebugHtml(html, 1);
#endif
                ValidateWorkshopHtml(html);
                cancellationToken.ThrowIfCancellationRequested();
                if (requestId != Volatile.Read(ref _pageLoadRequestId)) return;

                var mods = ParseAuthorMods(html);
                _allMods = mods;
                dgMods.ItemsSource = mods;
                SelectFirstMod();

                AutoCheckSubscribedMods(mods);

                txtStatus.Text = $"已加载 {mod.Author} 的 {mods.Count} 个MOD";
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException || requestId != Volatile.Read(ref _pageLoadRequestId))
                    return;

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
                if (requestId == Volatile.Read(ref _pageLoadRequestId))
                {
                    _pageLoadCts = null;
                    requestCts.Dispose();
                }
            }
        }

        private static string BuildAuthorWorkshopUrl(SteamMod mod)
        {
            var match = Regex.Match(mod.AuthorUrl ?? "", @"/profiles/(?<steamId>\d{17})(?:/|$)", RegexOptions.IgnoreCase);
            string steamId = match.Success
                ? match.Groups["steamId"].Value
                : IsSteamId(mod.Author) ? mod.Author : "";

            if (string.IsNullOrWhiteSpace(steamId))
                return "";

            return $"https://steamcommunity.com/profiles/{steamId}/myworkshopfiles/?appid={AppId}&sort=score&browsefilter=myfiles&view=imagewall&p=1&numperpage=30";
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
