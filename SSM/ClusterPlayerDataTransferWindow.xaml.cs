using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if DEBUG
using System.Text;
#endif
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoulmaskServerManager
{
    public partial class ClusterPlayerDataTransferWindow : Window
    {
        private static readonly Regex SteamIdPattern = new(@"^\d{17}$", RegexOptions.Compiled);
        private readonly Server _server;
        private readonly HashSet<string> _allSourceSteamIds = new(StringComparer.Ordinal);
        private readonly ObservableCollection<PlayerDatabaseEntry> _sourcePlayers = new();
        private readonly ObservableCollection<PlayerDatabaseEntry> _targetPlayers = new();
        private bool _updatingConflictOptions;
        private bool _isTransferring;
        private bool _isLoadingDatabase;
        private bool _hasShownHelpCompletionReminder;
        private int _guidedHelpStep;

        public ClusterPlayerDataTransferWindow(Server server)
        {
            InitializeComponent();
            _server = server;
            SourceSteamIdListBox.ItemsSource = _sourcePlayers;
            TargetSteamIdListBox.ItemsSource = _targetPlayers;
            TargetPathTextBox.Text = GetDefaultTargetPath();
            StatusTextBlock.Text = "请选择源数据库；目标默认为当前服务器的 WS\\Saved\\Accounts\\account.db。";
            Loaded += Window_Loaded;

            UseRecentConflictCheckBox.Checked += ConflictOption_Checked;
            UseRecentConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseSourceConflictCheckBox.Checked += ConflictOption_Checked;
            UseSourceConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseTargetConflictCheckBox.Checked += ConflictOption_Checked;
            UseTargetConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseRecentConflictCheckBox.IsChecked = true;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string defaultTarget = TargetPathTextBox.Text;
            if (File.Exists(defaultTarget))
                await LoadDatabaseAsync(defaultTarget, isSource: false);
        }

        private async void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择源数据库文件",
                Filter = "数据库文件 (*.db)|*.db",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
                await LoadDatabaseAsync(dialog.FileName, isSource: true);
        }

        private async void BrowseTargetButton_Click(object sender, RoutedEventArgs e)
        {
            string currentTarget = TargetPathTextBox.Text;
            string initialDirectory = Path.GetDirectoryName(currentTarget);
            if (string.IsNullOrWhiteSpace(initialDirectory) || !Directory.Exists(initialDirectory))
                initialDirectory = Directory.Exists(_server.Path) ? _server.Path : Environment.CurrentDirectory;

            var dialog = new SaveFileDialog
            {
                Title = "选择目标数据库文件",
                Filter = "数据库文件 (*.db)|*.db",
                DefaultExt = ".db",
                AddExtension = true,
                OverwritePrompt = false,
                InitialDirectory = initialDirectory,
                FileName = string.IsNullOrWhiteSpace(currentTarget) ? "account.db" : Path.GetFileName(currentTarget)
            };

            if (dialog.ShowDialog(this) == true)
                await LoadDatabaseAsync(dialog.FileName, isSource: false);
        }

        private string GetDefaultTargetPath() => Path.Combine(_server.Path, "WS", "Saved", "Accounts", "account.db");

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            SetGuidedHelpStep(1);
            HelpOverlay.Visibility = Visibility.Visible;
            UpdateGuidedHelpVisuals();
        }

        private void CloseHelpOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            _guidedHelpStep = 0;
            HelpOverlay.Visibility = Visibility.Collapsed;
        }

        private void GuideContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (_guidedHelpStep == 2)
                SetGuidedHelpStep(3);
            else if (_guidedHelpStep == 3)
                SetGuidedHelpStep(4);
            else if (_guidedHelpStep == 4 && HasSelectedPlayers())
                SetGuidedHelpStep(5);
        }

        private void SteamIdSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_guidedHelpStep == 4)
                GuideContinueButton.IsEnabled = HasSelectedPlayers();
        }

        private bool HasSelectedPlayers() => SourceSteamIdListBox.SelectedItems.Count > 0
            || TargetSteamIdListBox.SelectedItems.Count > 0;

        private void SetGuidedHelpStep(int step)
        {
            _guidedHelpStep = step;
            HelpOverlay.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
            GuideContinueButton.Visibility = step is 2 or 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
            GuideContinueButton.IsEnabled = step != 4 || HasSelectedPlayers();

            switch (step)
            {
                case 1:
                    HelpStepTitleTextBlock.Text = "第 1 步：选择源数据库";
                    HelpStepDescriptionTextBlock.Text = "在高亮区域点击“选择文件”，或将源 .db 文件拖入高亮区域。读取完成后会自动进入下一步。";
                    HelpCard.HorizontalAlignment = HorizontalAlignment.Right;
                    HelpCard.VerticalAlignment = VerticalAlignment.Center;
                    break;
                case 2:
                    HelpStepTitleTextBlock.Text = "第 2 步：确认目标数据库";
                    HelpStepDescriptionTextBlock.Text = "在高亮区域选择或拖入目标 .db 文件。若使用当前显示的目标路径，点击下方按钮继续。";
                    HelpCard.HorizontalAlignment = HorizontalAlignment.Left;
                    HelpCard.VerticalAlignment = VerticalAlignment.Center;
                    GuideContinueButton.Content = "使用当前目标继续";
                    break;
                case 3:
                    HelpStepTitleTextBlock.Text = "第 3 步：选择冲突存档模式";
                    HelpStepDescriptionTextBlock.Text = "选择角色数据发生冲突时采用哪一份存档。默认选中“最近的存档”；也可以选择源文件或目标文件存档。三种模式只能选一种。选择完成后点击“下一步”。";
                    HelpCard.HorizontalAlignment = HorizontalAlignment.Right;
                    HelpCard.VerticalAlignment = VerticalAlignment.Top;
                    GuideContinueButton.Content = "下一步";
                    break;
                case 4:
                    HelpStepTitleTextBlock.Text = "第 4 步：选择玩家";
                    HelpStepDescriptionTextBlock.Text = "在高亮列表中选择要转移的玩家。按住 Ctrl 或 Shift 可多选，也可点击“全选”。选择完成后点击“下一步”。";
                    HelpCard.HorizontalAlignment = HorizontalAlignment.Right;
                    HelpCard.VerticalAlignment = VerticalAlignment.Center;
                    GuideContinueButton.Content = "下一步";
                    break;
                case 5:
                    HelpStepTitleTextBlock.Text = "第 5 步：开始转移";
                    HelpStepDescriptionTextBlock.Text = "高亮区域是转移按钮。选中源数据库中的全部玩家会执行全量转移；只选部分玩家会仅转移所选 SteamID。点击高亮按钮继续。";
                    HelpCard.HorizontalAlignment = HorizontalAlignment.Center;
                    HelpCard.VerticalAlignment = VerticalAlignment.Top;
                    break;
            }

            UpdateGuidedHelpVisuals();
        }

        private void HelpOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGuidedHelpVisuals();

        private void UpdateGuidedHelpVisuals()
        {
            if (HelpOverlay.Visibility != Visibility.Visible || HelpOverlay.ActualWidth <= 0 || HelpOverlay.ActualHeight <= 0)
                return;

            FrameworkElement? target = _guidedHelpStep switch
            {
                1 => SourceDatabasePanel,
                2 => TargetDatabasePanel,
                3 => ConflictOptionsPanel,
                4 => SourcePlayerListPanel,
                5 => TransferButton,
                _ => null
            };
            if (target == null)
                return;

            double width = HelpOverlay.ActualWidth;
            double height = HelpOverlay.ActualHeight;
            HelpMask.Width = width;
            HelpMask.Height = height;
            HelpHighlightPath.Width = width;
            HelpHighlightPath.Height = height;

            var targetBounds = target.TransformToVisual(HelpOverlay).TransformBounds(
                new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            targetBounds.Inflate(5, 5);
            targetBounds.Intersect(new Rect(0, 0, width, height));

            var maskGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            maskGeometry.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
            maskGeometry.Children.Add(new RectangleGeometry(targetBounds, 7, 7));
            HelpMask.Data = maskGeometry;

            var highlightGeometry = new GeometryGroup();
            highlightGeometry.Children.Add(new RectangleGeometry(targetBounds, 7, 7));
            HelpHighlightPath.Data = highlightGeometry;

            HelpCard.Margin = _guidedHelpStep == 5
                ? new Thickness(12, 18, 12, 12)
                : new Thickness(12);
        }

        private void HelpMask_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private async Task LoadDatabaseAsync(string path, bool isSource)
        {
            if (_isLoadingDatabase)
                return;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
                if (!string.Equals(Path.GetExtension(fullPath), ".db", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowNoticeAsync("文件类型不正确", "数据库文件必须是 .db 文件。");
                    return;
                }

                if (isSource && !File.Exists(fullPath))
                {
                    await ShowNoticeAsync("文件不存在", $"找不到数据库文件：\n{fullPath}");
                    return;
                }
            }
            catch (Exception ex)
            {
                await ShowNoticeAsync("路径无效", ex.Message);
                return;
            }

            _isLoadingDatabase = true;
            SetActionButtonsEnabled(false);
            DataGrid list = isSource ? SourceSteamIdListBox : TargetSteamIdListBox;
            ObservableCollection<PlayerDatabaseEntry> players = isSource ? _sourcePlayers : _targetPlayers;
            TextBlock countText = isSource ? SourceCountTextBlock : TargetCountTextBlock;
            if (isSource)
            {
                SourcePathTextBox.Text = fullPath;
                _allSourceSteamIds.Clear();
            }
            else
                TargetPathTextBox.Text = fullPath;

            players.Clear();
            countText.Text = "正在读取玩家列表……";
            StatusTextBlock.Text = $"正在读取数据库：{fullPath}";

            try
            {
                List<PlayerDatabaseEntry> playersFromDatabase = File.Exists(fullPath)
                    ? await Task.Run(() => ReadPlayers(fullPath))
                    : new List<PlayerDatabaseEntry>();

                foreach (PlayerDatabaseEntry player in playersFromDatabase)
                    players.Add(player);

                if (isSource)
                {
                    _allSourceSteamIds.Clear();
                    foreach (PlayerDatabaseEntry player in playersFromDatabase)
                        _allSourceSteamIds.Add(player.SteamId);
                }

                countText.Text = $"玩家数：{playersFromDatabase.Count}";
                StatusTextBlock.Text = playersFromDatabase.Count > 0
                    ? $"已读取 {playersFromDatabase.Count} 个 SteamID：{fullPath}"
                    : File.Exists(fullPath)
                        ? $"数据库已读取，但未找到符合 17 位数字格式的 actor_name：{fullPath}"
                        : $"目标数据库尚不存在；转移时将由 CopyRoles.exe 创建：{fullPath}";

                if (HelpOverlay.Visibility == Visibility.Visible)
                {
                    if (isSource && _guidedHelpStep == 1)
                        SetGuidedHelpStep(2);
                    else if (!isSource && _guidedHelpStep == 2)
                        SetGuidedHelpStep(3);
                }
            }
            catch (Exception ex)
            {
                countText.Text = "读取失败";
                StatusTextBlock.Text = $"数据库读取失败：{ex.Message}";
                await ShowNoticeAsync("读取数据库失败", ex.Message);
            }
            finally
            {
                _isLoadingDatabase = false;
                SetActionButtonsEnabled(!_isTransferring);
            }
        }

        private static List<PlayerDatabaseEntry> ReadPlayers(string databasePath)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT actor_name, MAX(actor_time) FROM actor_table WHERE actor_name IS NOT NULL GROUP BY actor_name";
            using var reader = command.ExecuteReader();

            var players = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                string actorName = reader.GetString(0).Trim();
                if (SteamIdPattern.IsMatch(actorName))
                    players[actorName] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            }

            return players
                .OrderBy(player => player.Key, StringComparer.Ordinal)
                .Select(player => new PlayerDatabaseEntry(player.Key, string.IsNullOrWhiteSpace(player.Value) ? "未记录" : player.Value))
                .ToList();
        }

        private void SourceDrop_DragEnter(object sender, DragEventArgs e) => SetDatabaseDropState(sender, e);
        private void TargetDrop_DragEnter(object sender, DragEventArgs e) => SetDatabaseDropState(sender, e);

        private void SetDatabaseDropState(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (sender is Border border)
                border.Background = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        }

        private void DatabaseDrop_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DatabaseDrop_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
                border.Background = Brushes.White;
        }

        private void SourceDrop_Drop(object sender, DragEventArgs e) => HandleDatabaseDrop(sender, e, isSource: true);
        private void TargetDrop_Drop(object sender, DragEventArgs e) => HandleDatabaseDrop(sender, e, isSource: false);

        private void HandleDatabaseDrop(object sender, DragEventArgs e, bool isSource)
        {
            e.Handled = true;
            if (sender is Border border)
                border.Background = Brushes.White;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
                return;

            Dispatcher.BeginInvoke(new Action(async () => await LoadDatabaseAsync(paths[0], isSource)));
        }

        private void SteamIdList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid list)
                return;

            list.Tag = null;
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && current is not DataGridRow)
                current = VisualTreeHelper.GetParent(current);

            if (current is DataGridRow row && row.Item is PlayerDatabaseEntry player)
            {
                if (!row.IsSelected)
                {
                    list.SelectedItems.Clear();
                    list.SelectedItem = player;
                }
                list.Tag = player;
            }
        }

        private async void RemoveSteamId_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Parent is not ContextMenu contextMenu
                || contextMenu.PlacementTarget is not DataGrid list || list.Tag is not PlayerDatabaseEntry player)
                return;

            if (_isLoadingDatabase || _isTransferring)
            {
                await ShowNoticeAsync("操作进行中", "请等待当前数据库操作完成后再删除玩家数据。");
                return;
            }

            bool isSource = list == SourceSteamIdListBox;
            string databasePath = isSource ? SourcePathTextBox.Text : TargetPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            {
                await ShowNoticeAsync("数据库不可用", "找不到该玩家所在的数据库文件。");
                return;
            }

            var confirmation = new ContentDialog
            {
                Owner = this,
                Title = "确认删除玩家数据",
                Content = $"将从以下数据库中永久删除该玩家的记录：\n{databasePath}\n\nSteamID：{player.SteamId}\n\n此操作会修改数据库文件，确认继续吗？",
                PrimaryButtonText = "删除玩家数据",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            _isLoadingDatabase = true;
            SetActionButtonsEnabled(false);
            try
            {
                int deletedRows = await Task.Run(() => DeletePlayerRecords(databasePath, player.SteamId));
                _isLoadingDatabase = false;
                await LoadDatabaseAsync(databasePath, isSource);
                StatusTextBlock.Text = $"已从数据库删除 SteamID {player.SteamId}，并刷新玩家列表。";
                await ShowNoticeAsync("删除完成", deletedRows > 0
                    ? $"已删除 {deletedRows} 条玩家记录，并刷新数据库列表。"
                    : "数据库中没有找到该 SteamID 的记录，已刷新数据库列表。");
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"删除玩家数据失败：{ex.Message}";
                await ShowNoticeAsync("删除失败", ex.Message);
            }
            finally
            {
                _isLoadingDatabase = false;
                SetActionButtonsEnabled(!_isTransferring);
                list.Tag = null;
            }
        }

        private static int DeletePlayerRecords(string databasePath, string steamId)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM actor_table WHERE actor_name = $steamId";
            command.Parameters.AddWithValue("$steamId", steamId);
            int deletedRows = command.ExecuteNonQuery();
            transaction.Commit();
            return deletedRows;
        }

        private void SelectAllSourceButton_Click(object sender, RoutedEventArgs e) => SourceSteamIdListBox.SelectAll();

        private void SelectAllTargetButton_Click(object sender, RoutedEventArgs e) => TargetSteamIdListBox.SelectAll();

        private void UpdateVisibleCount(DataGrid list)
        {
            if (list == SourceSteamIdListBox)
                SourceCountTextBlock.Text = $"玩家数：{_sourcePlayers.Count}";
            else
                TargetCountTextBlock.Text = $"玩家数：{_targetPlayers.Count}";
        }

        private void ConflictOption_Checked(object sender, RoutedEventArgs e)
        {
            if (_updatingConflictOptions || sender is not CheckBox selectedOption)
                return;

            _updatingConflictOptions = true;
            if (selectedOption != UseRecentConflictCheckBox)
                UseRecentConflictCheckBox.IsChecked = false;
            if (selectedOption != UseSourceConflictCheckBox)
                UseSourceConflictCheckBox.IsChecked = false;
            if (selectedOption != UseTargetConflictCheckBox)
                UseTargetConflictCheckBox.IsChecked = false;
            _updatingConflictOptions = false;
        }

        private void ConflictOption_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_updatingConflictOptions)
                return;

            if (UseRecentConflictCheckBox.IsChecked != true
                && UseSourceConflictCheckBox.IsChecked != true
                && UseTargetConflictCheckBox.IsChecked != true
                && sender is CheckBox uncheckedOption)
            {
                _updatingConflictOptions = true;
                uncheckedOption.IsChecked = true;
                _updatingConflictOptions = false;
            }
        }

        private async void TransferButton_Click(object sender, RoutedEventArgs e)
        {
            if (_guidedHelpStep == 5)
                CloseHelpOverlayButton_Click(sender, e);

            var selectedSteamIds = SourceSteamIdListBox.SelectedItems.Cast<PlayerDatabaseEntry>().Select(player => player.SteamId)
                .Concat(TargetSteamIdListBox.SelectedItems.Cast<PlayerDatabaseEntry>().Select(player => player.SteamId))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            if (selectedSteamIds.Count == 0)
            {
                await ShowNoticeAsync("未选择玩家", "请先选择一个或多个 SteamID；选中源数据库中的全部 SteamID 时将执行全量转移。");
                return;
            }

            string[] missingFromSource = selectedSteamIds.Where(id => !_allSourceSteamIds.Contains(id)).ToArray();
            if (missingFromSource.Length > 0)
            {
                await ShowNoticeAsync("源数据库缺少玩家", $"以下 {missingFromSource.Length} 个 SteamID 不存在于源数据库，无法转移。请从目标列表中取消选择这些玩家，或重新载入源数据库。\n\n{string.Join("\n", missingFromSource.Take(10))}");
                return;
            }

            bool allSourcePlayersSelected = _allSourceSteamIds.Count > 0
                && selectedSteamIds.SetEquals(_allSourceSteamIds);
            await ExecuteTransferAsync(allSourcePlayersSelected ? null : selectedSteamIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }

        private async Task ExecuteTransferAsync(IReadOnlyCollection<string>? userIds)
        {
            if (_isTransferring || _isLoadingDatabase)
                return;

            string sourcePath = SourcePathTextBox.Text;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)
                || !string.Equals(Path.GetExtension(sourcePath), ".db", StringComparison.OrdinalIgnoreCase))
            {
                await ShowNoticeAsync("缺少源文件", "请先选择或拖入一个存在的 .db 源文件。");
                return;
            }

            string targetPath = TargetPathTextBox.Text;
            if (string.IsNullOrWhiteSpace(targetPath)
                || !string.Equals(Path.GetExtension(targetPath), ".db", StringComparison.OrdinalIgnoreCase))
            {
                await ShowNoticeAsync("缺少目标文件", "请选择一个 .db 目标文件路径。");
                return;
            }

            sourcePath = Path.GetFullPath(sourcePath);
            targetPath = Path.GetFullPath(targetPath);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                await ShowNoticeAsync("路径冲突", "源文件与目标文件相同，无法进行转移。");
                return;
            }

            string copyRolesPath = Path.Combine(_server.Path, "WS", "Plugins", "DBAgent", "ThirdParty", "Binaries", "CopyRoles.exe");
            if (!File.Exists(copyRolesPath))
            {
                await ShowNoticeAsync("缺少转移工具", $"未找到 CopyRoles.exe：\n{copyRolesPath}");
                return;
            }

            string conflictDescription = UseSourceConflictCheckBox.IsChecked == true
                ? "冲突时采用源文件存档"
                : UseTargetConflictCheckBox.IsChecked == true
                    ? "冲突时采用目标文件存档"
                    : "冲突时采用最近修改的存档";
            string operationDescription = userIds == null
                ? "全量转移所有角色"
                : $"转移 {userIds.Count} 个指定玩家：\n{string.Join("、", userIds.Take(8))}{(userIds.Count > 8 ? "……" : string.Empty)}";
            var confirmation = new ContentDialog
            {
                Owner = this,
                Title = "确认转移玩家数据",
                Content = $"源文件：\n{sourcePath}\n\n目标文件：\n{targetPath}\n\n{operationDescription}\n{conflictDescription}\n\n请确保目标服务器已停止运行。",
                PrimaryButtonText = "开始转移",
                SecondaryButtonText = "取消"
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            _isTransferring = true;
            SetActionButtonsEnabled(false);
            StatusTextBlock.Text = userIds == null
                ? "正在执行全量玩家数据转移，请勿关闭窗口……"
                : $"正在转移 {userIds.Count} 个玩家的数据，请勿关闭窗口……";

#if DEBUG
            string logPath = string.Empty;
#endif
            bool transferAttempted = false;
            try
            {
#if DEBUG
                logPath = CreateTransferLogPath();
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                var logHeader = new StringBuilder()
                    .AppendLine("CopyRoles 玩家数据转移日志")
                    .AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}")
                    .AppendLine($"源文件：{sourcePath}")
                    .AppendLine($"目标文件：{targetPath}")
                    .AppendLine($"操作：{operationDescription.Replace(Environment.NewLine, " ")}")
                    .AppendLine($"冲突模式：{conflictDescription}")
                    .AppendLine(new string('-', 72))
                    .ToString();
                File.WriteAllText(logPath, logHeader, Encoding.UTF8);
#endif

                string targetDirectory = Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidOperationException("无法识别目标文件所在目录。");
                Directory.CreateDirectory(targetDirectory);

                string?[] idsToTransfer = userIds?.Cast<string?>().ToArray() ?? new string?[] { null };
                var outputParts = new List<string>();
                for (int index = 0; index < idsToTransfer.Length; index++)
                {
                    StatusTextBlock.Text = userIds == null
                        ? "正在执行全量玩家数据转移，请勿关闭窗口……"
                        : $"正在转移玩家 {index + 1}/{idsToTransfer.Length}：{idsToTransfer[index]}";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = copyRolesPath,
                        WorkingDirectory = Path.GetDirectoryName(copyRolesPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    startInfo.ArgumentList.Add($"-src={sourcePath}");
                    startInfo.ArgumentList.Add($"-dst={targetPath}");
                    if (idsToTransfer[index] != null)
                        startInfo.ArgumentList.Add($"-userid={idsToTransfer[index]}");
                    if (UseSourceConflictCheckBox.IsChecked == true)
                        startInfo.ArgumentList.Add("-type=1");
                    else if (UseTargetConflictCheckBox.IsChecked == true)
                        startInfo.ArgumentList.Add("-type=2");

                    transferAttempted = true;
                    using var process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("无法启动 CopyRoles.exe。");
                    Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    string standardOutput = await standardOutputTask;
                    string standardError = await standardErrorTask;
#if DEBUG
                    AppendInvocationLog(logPath, index, idsToTransfer[index], startInfo.ArgumentList, process.ExitCode, standardOutput, standardError);
#endif

                    if (process.ExitCode != 0)
                    {
                        throw new CopyRolesException(process.ExitCode, idsToTransfer[index], standardError);
                    }

                    outputParts.Add(standardOutput);
                }

                await RefreshDatabasesAfterTransferAsync(sourcePath, targetPath);
                TransferResultSummary summary = SummarizeTransferResult(string.Join(Environment.NewLine, outputParts));
                StatusTextBlock.Text = summary.UpdatedPlayerCount > 0 ? "玩家数据转移完成。" : "CopyRoles 已运行，但没有玩家数据被更新。";
                string resultTitle = summary.UpdatedPlayerCount == 0 && summary.NotUpdatedPlayerCount > 0
                    ? "玩家数据未更新"
                    : summary.NotUpdatedPlayerCount > 0
                        ? "转移结果（部分玩家未更新）"
                        : "转移成功";
                string resultMessage = summary.Message;
#if DEBUG
                resultMessage += $"\n\n完整 standardOutput 已导出到：\n{logPath}";
#endif
                await ShowNoticeAsync(resultTitle, resultMessage);
                if (!_hasShownHelpCompletionReminder)
                {
                    _hasShownHelpCompletionReminder = true;
                    await ShowNoticeAsync("使用提示", "下次忘记操作步骤时，可以点击窗口顶部的“使用帮助”按钮，查看分步说明。");
                }
            }
            catch (CopyRolesException ex)
            {
                if (transferAttempted)
                    await RefreshDatabasesAfterTransferAsync(sourcePath, targetPath);
                StatusTextBlock.Text = "玩家数据转移失败。";
                string userLabel = ex.UserId == null ? string.Empty : $"\n失败的 SteamID：{ex.UserId}";
                string details = string.IsNullOrWhiteSpace(ex.Details) ? string.Empty : $"\n\n{ex.Details}";
#if DEBUG
                string logLocation = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $"\n\n完整日志：\n{logPath}";
#else
                string logLocation = string.Empty;
#endif
                await ShowNoticeAsync("转移失败", $"CopyRoles.exe 退出代码：{ex.ExitCode}{userLabel}{details}{logLocation}");
            }
            catch (Exception ex)
            {
                if (transferAttempted)
                    await RefreshDatabasesAfterTransferAsync(sourcePath, targetPath);
                StatusTextBlock.Text = $"转移失败：{ex.Message}";
#if DEBUG
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    try
                    {
                        File.AppendAllText(logPath, $"\r\n转移异常：{ex}\r\n", Encoding.UTF8);
                    }
                    catch
                    {
                        // Preserve the original transfer error if writing the diagnostic log also fails.
                    }
                }
                string logLocation = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $"\n\n日志：\n{logPath}";
#else
                string logLocation = string.Empty;
#endif
                await ShowNoticeAsync("转移失败", $"{ex.Message}{logLocation}");
            }
            finally
            {
                _isTransferring = false;
                SetActionButtonsEnabled(true);
            }
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            TransferButton.IsEnabled = enabled;
            SourceSteamIdListBox.IsEnabled = enabled;
            TargetSteamIdListBox.IsEnabled = enabled;
        }

        private async Task RefreshDatabasesAfterTransferAsync(string sourcePath, string targetPath)
        {
            await LoadDatabaseAsync(sourcePath, isSource: true);
            await LoadDatabaseAsync(targetPath, isSource: false);
            StatusTextBlock.Text = "已重新读取源和目标数据库中的玩家列表。";
        }

        private async Task ShowNoticeAsync(string title, string message)
        {
            await new ContentDialog
            {
                Owner = this,
                Title = title,
                Content = message,
                PrimaryButtonText = "确定"
            }.ShowAsync();
        }

#if DEBUG
        private static string CreateTransferLogPath()
        {
            string localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDirectory = Path.Combine(localDataPath, "SoulmaskServerManager", "Logs", "CopyRoles");
            return Path.Combine(logDirectory, $"CopyRoles_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log");
        }

        private static void AppendInvocationLog(string logPath, int index, string? userId,
            System.Collections.ObjectModel.Collection<string> arguments, int exitCode, string standardOutput, string standardError)
        {
            var logEntry = new StringBuilder()
                .AppendLine($"调用序号：{index + 1}")
                .AppendLine($"SteamID：{userId ?? "（全量转移）"}")
                .AppendLine($"参数：CopyRoles.exe {string.Join(" ", arguments)}")
                .AppendLine($"退出代码：{exitCode}")
                .AppendLine("--- standardOutput ---")
                .AppendLine(string.IsNullOrEmpty(standardOutput) ? "（空）" : standardOutput)
                .AppendLine("--- standardError ---")
                .AppendLine(string.IsNullOrEmpty(standardError) ? "（空）" : standardError)
                .AppendLine(new string('-', 72))
                .ToString();
            File.AppendAllText(logPath, logEntry, Encoding.UTF8);
        }
#endif

        private static TransferResultSummary SummarizeTransferResult(string standardOutput)
        {
            string output = standardOutput ?? string.Empty;
            MatchCollection updatedMatches = Regex.Matches(
                output,
                @"\(CopyRoles\)(?:update|new):\s*Name:(?<steamId>\d{17})(?!\d)",
                RegexOptions.IgnoreCase);
            var updatedSteamIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in updatedMatches)
                updatedSteamIds.Add(match.Groups["steamId"].Value);

            MatchCollection noUpdateMatches = Regex.Matches(
                output,
                @"\(CopyRoles\)no update:\s*Name:(?<steamId>\d{17})\s*,\s*time:(?<time>[^,\r\n]+),\s*oldtime:(?<oldtime>[^\r\n]+)",
                RegexOptions.IgnoreCase);
            var sameTimeSteamIds = new HashSet<string>(StringComparer.Ordinal);
            var otherNoUpdateSteamIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in noUpdateMatches)
            {
                string steamId = match.Groups["steamId"].Value;
                string currentTime = match.Groups["time"].Value.Trim().TrimEnd('.');
                string oldTime = match.Groups["oldtime"].Value.Trim().TrimEnd('.');
                if (string.Equals(currentTime, oldTime, StringComparison.OrdinalIgnoreCase))
                    sameTimeSteamIds.Add(steamId);
                else
                    otherNoUpdateSteamIds.Add(steamId);
            }

            var messageParts = new List<string>();
            if (updatedSteamIds.Count > 0)
                messageParts.Add($"已新增或更新 {updatedSteamIds.Count} 名玩家。");
            if (sameTimeSteamIds.Count > 0)
                messageParts.Add($"{sameTimeSteamIds.Count} 名玩家未更新：源与目标存档的最近更新时间相同，CopyRoles 判定无需更新。");
            if (otherNoUpdateSteamIds.Count > 0)
                messageParts.Add($"{otherNoUpdateSteamIds.Count} 名玩家未更新：工具判定不需要更新，但更新时间不同；请查看日志确认具体情况。");

            if (messageParts.Count == 0)
                messageParts.Add("CopyRoles.exe 已正常退出，但输出中没有可识别的新增、更新或未更新玩家记录。");

            return new TransferResultSummary(
                updatedSteamIds.Count,
                sameTimeSteamIds.Count + otherNoUpdateSteamIds.Count,
                string.Join(Environment.NewLine, messageParts));
        }

        private sealed record TransferResultSummary(int UpdatedPlayerCount, int NotUpdatedPlayerCount, string Message);

        private sealed class CopyRolesException : Exception
        {
            public CopyRolesException(int exitCode, string? userId, string details)
                : base("CopyRoles.exe 执行失败。")
            {
                ExitCode = exitCode;
                UserId = userId;
                Details = details;
            }

            public int ExitCode { get; }
            public string? UserId { get; }
            public string Details { get; }
        }

        private sealed record PlayerDatabaseEntry(string SteamId, string LastUpdated);
    }
}
