using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ModernWpf.Controls;
using SoulmaskServerManager;
using Microsoft.Win32;

namespace SoulmaskServerManager
{
    public partial class ChangeSaveWindow : Window
    {
        private Server _server;
        private bool _isImporting;

        public ChangeSaveWindow(Server server)
        {
            InitializeComponent();
            _server = server;
            TargetPathTextBox.Text = Path.Combine(_server.Path, "WS", "Saved", "Worlds", "Dedicated");
            this.DragOver += (s, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
        }

        private async void BrowseFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择地图存档或玩家数据文件",
                Filter = "存档数据库 (*.db)|*.db",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
                await ProcessSelectedPathAsync(dialog.FileName);
        }

        private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含地图存档的服务器文件夹",
                SelectedPath = Directory.Exists(SourcePathTextBox.Text) ? SourcePathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                await ProcessSelectedPathAsync(dialog.SelectedPath);
        }

        private async Task ProcessSelectedPathAsync(string path)
        {
            if (_isImporting)
                return;

            _isImporting = true;
            try
            {
                await ProcessDroppedPathAsync(path);
            }
            catch (Exception ex)
            {
                await ShowImportResultAsync("导入失败", ex.Message);
            }
            finally
            {
                _isImporting = false;
                RestoreImportArea();
            }
        }

        private void DropArea_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.Copy;

            if (_isImporting || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
                return;

            SourcePathTextBox.Text = Path.GetFullPath(paths[0]);

            _isImporting = true;
            DropArea.Background = Brushes.White;

            // Finish the native drag/drop event before showing dialogs or starting async work.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                try
                {
                    Activate();
                    await ProcessDroppedPathAsync(paths[0]);
                }
                catch (Exception ex)
                {
                    await ShowImportResultAsync("导入失败", ex.Message);
                }
                finally
                {
                    _isImporting = false;
                    IsEnabled = true;
                    RestoreImportArea();
                }
            }));
        }

        private async Task ProcessDroppedPathAsync(string firstPath)
        {
            SourcePathTextBox.Text = Path.GetFullPath(firstPath);
            if (File.Exists(firstPath) && Path.GetFileName(firstPath).Equals("world.db", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDropWorldDb(firstPath);
                return;
            }

            if (Directory.Exists(firstPath))
            {
                await HandleDropFolder(firstPath);
                return;
            }

            if (File.Exists(firstPath) && Path.GetFileName(firstPath).Equals("account.db", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDropAccountDb(firstPath);
                return;
            }

            await new ContentDialog
            {
                Owner = this,
                Title = "错误",
                Content = "请拖入：\n• 地图存档 world.db\n• 玩家数据 account.db\n• 服务器根目录",
                PrimaryButtonText = "确定"
            }.ShowAsync();
        }

        private async Task HandleDropWorldDb(string dbFilePath)
        {
            var dialog = new ContentDialog
            {
                Owner = this,
                Title = "选择存档所属地图",
                Content = "请选择当前 world.db 要导入到哪个地图目录：",
                PrimaryButtonText = "云雾之森",
                SecondaryButtonText = "金色浮沙"
            };

            var res = await dialog.ShowAsync();
            if (res is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
                return;
            string mapFolder = res == ContentDialogResult.Primary
                ? "Level01_Main"
                : "DLC_Level01_Main";

            string targetDedicated = Path.Combine(_server.Path, "WS", "Saved", "Worlds", "Dedicated");
            string targetMapDir = Path.Combine(targetDedicated, mapFolder);
            string targetDbPath = Path.Combine(targetMapDir, "world.db");
            TargetPathTextBox.Text = targetDbPath;

            if (!Directory.Exists(targetDedicated))
                Directory.CreateDirectory(targetDedicated);
            if (!Directory.Exists(targetMapDir))
                Directory.CreateDirectory(targetMapDir);

            var confirm = new ContentDialog
            {
                Owner = this,
                Title = "确认覆盖",
                Content = $"源文件：\n{dbFilePath}\n\n目标文件：\n{targetDbPath}\n\n确认覆盖吗？",
                PrimaryButtonText = "确认覆盖",
                SecondaryButtonText = "取消"
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            ImportSaveFileText.Text = "正在导入 world.db...";
            ImportSaveFileText.Foreground = Brushes.Orange;

            await CopyFileWithProgressAsync(dbFilePath, targetDbPath);

            var finalDialog = new ContentDialog
            {
                Owner = this,
                Title = "导入成功",
                Content = $"存档已导入到：\n{targetDbPath}",
                PrimaryButtonText = "确定",
            };
            await finalDialog.ShowAsync();

            ImportSaveFileText.Text = "拖入服务器文件夹或存档文件到这里";
            ImportSaveFileText.Foreground = Brushes.LightGray;
            DropArea.Background = Brushes.White;
        }

        private async Task HandleDropFolder(string importRoot)
        {
            string sourceDedicated = Path.Combine(importRoot, "WS", "Saved", "Worlds", "Dedicated");
            if (!Directory.Exists(sourceDedicated))
            {
                var dialog = new ContentDialog
                {
                    Owner = this,
                    Title = "错误",
                    Content = "未找到存档目录：WS/Saved/Worlds/Dedicated",
                    PrimaryButtonText = "确定",
                };
                await dialog.ShowAsync();
                return;
            }

            string targetDedicated = Path.Combine(_server.Path, "WS", "Saved", "Worlds", "Dedicated");
            if (!Directory.Exists(targetDedicated))
                Directory.CreateDirectory(targetDedicated);

            bool hasMain = CheckHasValidSave(sourceDedicated, "Level01_Main");
            bool hasDLC = CheckHasValidSave(sourceDedicated, "DLC_Level01_Main");

            if (!hasMain && !hasDLC)
            {
                var dialog = new ContentDialog
                {
                    Owner = this,
                    Title = "错误",
                    Content = "未找到任何有效的 world.db 存档",
                    PrimaryButtonText = "确定",
                };
                await dialog.ShowAsync();
                return;
            }

            string selectedMap;
            if (hasMain && hasDLC)
            {
                var chooseDlg = new ContentDialog
                {
                    Owner = this,
                    Title = "选择要导入的存档",
                    Content = "检测到两个地图存档，请选择：",
                    PrimaryButtonText = "云雾之森",
                    SecondaryButtonText = "金色浮沙"
                };
                var result = await chooseDlg.ShowAsync();
                if (result is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
                    return;
                selectedMap = result == ContentDialogResult.Primary ? "Level01_Main" : "DLC_Level01_Main";
            }
            else
            {
                selectedMap = hasMain ? "Level01_Main" : "DLC_Level01_Main";
            }

            var confirm = new ContentDialog
            {
                Owner = this,
                Title = "确认导入",
                Content = $"源目录：\n{importRoot}\n\n地图：{(selectedMap == "Level01_Main" ? "云雾之森" : "金色浮沙")}\n\n目标文件：\n{Path.Combine(targetDedicated, selectedMap, "world.db")}",
                PrimaryButtonText = "确定导入",
                SecondaryButtonText = "取消"
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            TargetPathTextBox.Text = Path.Combine(targetDedicated, selectedMap, "world.db");

            ImportSaveFileText.Text = "正在导入存档...请勿关闭窗口";
            ImportSaveFileText.Foreground = Brushes.Orange;

            string srcDb = Path.Combine(sourceDedicated, selectedMap, "world.db");
            string targetMapDir = Path.Combine(targetDedicated, selectedMap);
            Directory.CreateDirectory(targetMapDir);
            string targetDb = Path.Combine(targetMapDir, "world.db");
            await CopyFileWithProgressAsync(srcDb, targetDb);

            var finalDialog = new ContentDialog
            {
                Owner = this,
                Title = "导入成功",
                Content = $"存档已导入到：\n{targetDb}",
                PrimaryButtonText = "确定",
            };
            await finalDialog.ShowAsync();

            ImportSaveFileText.Text = "拖入服务器文件夹或存档文件到这里";
            ImportSaveFileText.Foreground = Brushes.LightGray;
            DropArea.Background = Brushes.White;
        }

        private async Task HandleDropAccountDb(string accountFilePath)
        {
            string targetAccountDir = Path.Combine(_server.Path, "WS", "Saved", "Accounts");
            string targetAccountPath = Path.Combine(targetAccountDir, "account.db");
            TargetPathTextBox.Text = targetAccountPath;

            var confirm = new ContentDialog
            {
                Owner = this,
                Title = "导入玩家数据",
                Content = $"源文件：\n{accountFilePath}\n\n目标文件：\n{targetAccountPath}\n\n确认覆盖吗？",
                PrimaryButtonText = "确认覆盖",
                SecondaryButtonText = "取消"
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            ImportSaveFileText.Text = "正在导入玩家数据...";
            ImportSaveFileText.Foreground = Brushes.Orange;

            Directory.CreateDirectory(targetAccountDir);
            await CopyFileWithProgressAsync(accountFilePath, targetAccountPath);

            var finalDialog = new ContentDialog
            {
                Owner = this,
                Title = "导入成功",
                Content = $"玩家数据已导入到：\n{targetAccountPath}",
                PrimaryButtonText = "确定"
            };
            await finalDialog.ShowAsync();
        }

        private bool CheckHasValidSave(string dedicatedPath, string mapName)
        {
            string folder = Path.Combine(dedicatedPath, mapName);
            string dbFile = Path.Combine(folder, "world.db");
            return Directory.Exists(folder) && File.Exists(dbFile);
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropArea.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            ImportSaveFileText.Foreground = Brushes.White;
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            DropArea.Background = Brushes.White;
            ImportSaveFileText.Foreground = Brushes.Black;
        }

        private async Task CopyFileWithProgressAsync(string sourcePath, string targetPath)
        {
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("源文件与目标文件相同，无法导入。");

            ImportProgressFileText.Visibility = Visibility.Visible;
            ImportProgressBar.Visibility = Visibility.Visible;
            ImportProgressBar.Value = 0;
            ImportSaveFileText.Text = "正在导入，请勿关闭窗口……";
            ImportSaveFileText.Foreground = Brushes.DarkOrange;

            long totalBytes = new FileInfo(sourcePath).Length;
            long copiedBytes = 0;
            byte[] buffer = new byte[1024 * 1024];
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);
            await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);

            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await output.WriteAsync(buffer, 0, bytesRead);
                copiedBytes += bytesRead;
                int percent = totalBytes > 0 ? (int)(copiedBytes * 100 / totalBytes) : 100;
                ImportProgressBar.Value = percent;
                ImportProgressFileText.Text = $"正在复制：{Path.GetFileName(sourcePath)}（{percent}%）";
            }

            ImportProgressBar.Value = 100;
            ImportProgressFileText.Text = "复制完成";
        }

        private void RestoreImportArea()
        {
            ImportSaveFileText.Text = "拖入服务器文件夹、world.db 或 account.db 到这里";
            ImportSaveFileText.Foreground = Brushes.Black;
            ImportProgressFileText.Visibility = Visibility.Collapsed;
            ImportProgressBar.Visibility = Visibility.Collapsed;
            DropArea.Background = Brushes.White;
        }

        private async Task ShowImportResultAsync(string title, string message)
        {
            await new ContentDialog
            {
                Owner = this,
                Title = title,
                Content = message,
                PrimaryButtonText = "确定"
            }.ShowAsync();
        }
    }
}
