using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoulmaskServerManager
{
    public partial class ClusterPlayerDataTransferWindow : Window
    {
        private readonly Server _server;
        private bool _updatingConflictOptions;
        private bool _isTransferring;

        public ClusterPlayerDataTransferWindow(Server server)
        {
            InitializeComponent();
            _server = server;

            UseRecentConflictCheckBox.Checked += ConflictOption_Checked;
            UseRecentConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseSourceConflictCheckBox.Checked += ConflictOption_Checked;
            UseSourceConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseTargetConflictCheckBox.Checked += ConflictOption_Checked;
            UseTargetConflictCheckBox.Unchecked += ConflictOption_Unchecked;
            UseRecentConflictCheckBox.IsChecked = true;
        }

        private void BrowseSourceButton_Click(object sender, RoutedEventArgs e) => BrowseSourceFile();

        private void DropArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            BrowseSourceFile();
        }

        private void BrowseSourceFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择源数据库文件",
                Filter = "数据库文件 (*.db)|*.db",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
                SetSourceFile(dialog.FileName);
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropArea.Background = new SolidColorBrush(Color.FromRgb(55, 55, 55));
            DropAreaTextBlock.Foreground = Brushes.White;
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            RestoreDropAreaBackground();
        }

        private void DropArea_MouseEnter(object sender, MouseEventArgs e)
        {
            DropArea.Background = new SolidColorBrush(Color.FromRgb(55, 55, 55));
            DropAreaTextBlock.Foreground = Brushes.White;
        }

        private void DropArea_MouseLeave(object sender, MouseEventArgs e)
        {
            RestoreDropAreaBackground();
        }

        private void RestoreDropAreaBackground()
        {
            DropArea.Background = Brushes.White;
            DropAreaTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68));
        }

        private void DropArea_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Copy;
            RestoreDropAreaBackground();

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
                return;

            string droppedPath = paths[0];
            // Defer UI work until the native drag/drop loop has returned.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SetSourceFile(droppedPath)));
        }

        private void SetSourceFile(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "源文件必须是 .db 文件。", "文件类型不正确", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show(this, $"找不到文件：\n{path}", "文件不存在", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fullPath = Path.GetFullPath(path);
            SourcePathTextBox.Text = fullPath;
            DropAreaTextBlock.Text = $"已选择源文件：{Path.GetFileName(fullPath)}";
            StatusTextBlock.Text = "已选择源文件。目标为当前服务器的 WS\\Saved\\Accounts\\account.db。";
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
            if (_isTransferring)
                return;

            string sourcePath = SourcePathTextBox.Text;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)
                || !string.Equals(Path.GetExtension(sourcePath), ".db", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "请先选择或拖入一个存在的 .db 源文件。", "缺少源文件", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetPath = Path.Combine(_server.Path, "WS", "Saved", "Accounts", "account.db");
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "源文件与目标文件相同，无法进行转移。", "路径冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string copyRolesPath = Path.Combine(_server.Path, "WS", "Plugins", "DBAgent", "ThirdParty", "Binaries", "CopyRoles.exe");
            if (!File.Exists(copyRolesPath))
            {
                MessageBox.Show(this, $"未找到 CopyRoles.exe：\n{copyRolesPath}", "缺少转移工具", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string conflictDescription = UseSourceConflictCheckBox.IsChecked == true
                ? "冲突时采用源文件存档"
                : UseTargetConflictCheckBox.IsChecked == true
                    ? "冲突时采用目标文件存档"
                    : "冲突时采用最近修改的存档";
            var confirmation = new ContentDialog
            {
                Owner = this,
                Title = "确认转移玩家数据",
                Content = $"源文件：\n{sourcePath}\n\n目标文件：\n{targetPath}\n\n{conflictDescription}\n\n请确保目标服务器已停止运行。",
                PrimaryButtonText = "开始转移",
                SecondaryButtonText = "取消"
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
                return;

            _isTransferring = true;
            TransferButton.IsEnabled = false;
            StatusTextBlock.Text = "正在执行玩家数据转移，请勿关闭窗口……";

            try
            {
                string targetDirectory = Path.GetDirectoryName(targetPath);
                Directory.CreateDirectory(targetDirectory);

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
                if (UseSourceConflictCheckBox.IsChecked == true)
                    startInfo.ArgumentList.Add("-type=1");
                else if (UseTargetConflictCheckBox.IsChecked == true)
                    startInfo.ArgumentList.Add("-type=2");

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("无法启动 CopyRoles.exe。");
                Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string standardOutput = await standardOutputTask;
                string standardError = await standardErrorTask;

                if (process.ExitCode == 0)
                {
                    StatusTextBlock.Text = "玩家数据转移完成。";
                    await new ContentDialog
                    {
                        Owner = this,
                        Title = "转移成功",
                        Content = $"已将源文件中的玩家数据转移到集群主服务器。\n\n{standardOutput}".Trim(),
                        PrimaryButtonText = "确定"
                    }.ShowAsync();
                }
                else
                {
                    string errorDetails = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                    StatusTextBlock.Text = $"转移失败，退出代码：{process.ExitCode}";
                    await new ContentDialog
                    {
                        Owner = this,
                        Title = "转移失败",
                        Content = $"CopyRoles.exe 退出代码：{process.ExitCode}\n\n{errorDetails}",
                        PrimaryButtonText = "确定"
                    }.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"转移失败：{ex.Message}";
                await new ContentDialog
                {
                    Owner = this,
                    Title = "转移失败",
                    Content = ex.Message,
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
            finally
            {
                _isTransferring = false;
                TransferButton.IsEnabled = true;
            }
        }
    }
}
