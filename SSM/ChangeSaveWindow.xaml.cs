using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ModernWpf.Controls;
using SoulmaskServerManager;

namespace SoulmaskServerManager
{
    public partial class ChangeSaveWindow : Window
    {
        private Server _server;
        private MainWindow _mainWindow = Application.Current.MainWindow as MainWindow;

        public ChangeSaveWindow(Server server)
        {
            InitializeComponent();
            _server = server;
            this.DragOver += (s, e) => e.Effects = DragDropEffects.Copy;
        }

        private async void DropArea_Drop(object sender, DragEventArgs e)
        {
            await Task.Yield();
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths == null || paths.Length == 0) return;

                string firstPath = paths[0];

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
                    Title = "错误",
                    Content = "请拖入：\n• 地图存档 world.db\n• 玩家数据 account.db\n• 服务器根目录",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                IsEnabled = true;
                _mainWindow.ShowLogMsg($"导入失败:{ex.Message}", Brushes.Red);
            }
        }

        private async Task HandleDropWorldDb(string dbFilePath)
        {
            var dialog = new ContentDialog
            {
                Title = "选择存档所属地图",
                Content = "请选择当前 world.db 要导入到哪个地图目录：",
                PrimaryButtonText = "云雾之森",
                SecondaryButtonText = "金色浮沙"
            };

            var res = await dialog.ShowAsync();
            string mapFolder = res == ContentDialogResult.Primary
                ? "Level01_Main"
                : "DLC_Level01_Main";

            string targetDedicated = Path.Combine(_server.Path, "WS", "Saved", "Worlds", "Dedicated");
            string targetMapDir = Path.Combine(targetDedicated, mapFolder);
            string targetDbPath = Path.Combine(targetMapDir, "world.db");

            if (!Directory.Exists(targetDedicated))
                Directory.CreateDirectory(targetDedicated);
            if (!Directory.Exists(targetMapDir))
                Directory.CreateDirectory(targetMapDir);

            var confirm = new ContentDialog
            {
                Title = "确认覆盖",
                Content = $"确定要将存档导入到：\n{mapFolder}\n并覆盖现有 world.db 吗？",
                PrimaryButtonText = "确认覆盖",
                SecondaryButtonText = "取消"
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            ImportSaveFileText.Text = "正在导入 world.db...";
            ImportSaveFileText.Foreground = Brushes.Orange;
            IsEnabled = false;

            await Task.Run(() =>
            {
                File.Copy(dbFilePath, targetDbPath, overwrite: true);
            });

            IsEnabled = true;
            var finalDialog = new ContentDialog
            {
                Title = "导入成功",
                Content = $"存档已导入到 {mapFolder}",
                PrimaryButtonText = "确定",
            };
            await finalDialog.ShowAsync();

            ImportSaveFileText.Text = "拖入服务器文件夹或存档文件到这里";
            ImportSaveFileText.Foreground = Brushes.LightGray;
            DropArea.Background = Brushes.Transparent;
        }

        private async Task HandleDropFolder(string importRoot)
        {
            string sourceDedicated = Path.Combine(importRoot, "WS", "Saved", "Worlds", "Dedicated");
            if (!Directory.Exists(sourceDedicated))
            {
                var dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = "未找到存档目录：WS/Saved/Worlds/Dedicated",
                    PrimaryButtonText = "确定",
                }.ShowAsync();
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
                    Title = "错误",
                    Content = "未找到任何有效的 world.db 存档",
                    PrimaryButtonText = "确定",
                }.ShowAsync();
                return;
            }

            string selectedMap;
            if (hasMain && hasDLC)
            {
                var chooseDlg = new ContentDialog
                {
                    Title = "选择要导入的存档",
                    Content = "检测到两个地图存档，请选择：",
                    PrimaryButtonText = "云雾之森",
                    SecondaryButtonText = "金色浮沙"
                };
                var result = await chooseDlg.ShowAsync();
                selectedMap = result == ContentDialogResult.Primary ? "Level01_Main" : "DLC_Level01_Main";
            }
            else
            {
                selectedMap = hasMain ? "Level01_Main" : "DLC_Level01_Main";
            }

            var confirm = new ContentDialog
            {
                Title = "确认导入",
                Content = $"即将导入存档：{(selectedMap == "Level01_Main" ? "云雾之森" : "金色浮沙")}",
                PrimaryButtonText = "确定导入",
                SecondaryButtonText = "取消"
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            ImportSaveFileText.Text = "正在导入存档...请勿关闭窗口";
            ImportSaveFileText.Foreground = Brushes.Orange;
            IsEnabled = false;

            await Task.Run(() =>
            {
                string srcDb = Path.Combine(sourceDedicated, selectedMap, "world.db");
                string targetMapDir = Path.Combine(targetDedicated, selectedMap);

                if (!Directory.Exists(targetMapDir))
                    Directory.CreateDirectory(targetMapDir);

                string targetDb = Path.Combine(targetMapDir, "world.db");
                File.Copy(srcDb, targetDb, overwrite: true);
            });

            IsEnabled = true; 
            var finalDialog = new ContentDialog
            {
                Title = "导入成功",
                Content = $"存档已导入到 {selectedMap}",
                PrimaryButtonText = "确定",
            };
            await finalDialog.ShowAsync();

            ImportSaveFileText.Text = "拖入服务器文件夹或存档文件到这里";
            ImportSaveFileText.Foreground = Brushes.LightGray;
            DropArea.Background = Brushes.Transparent;
        }

        private async Task HandleDropAccountDb(string accountFilePath)
        {
            try
            {
                string targetAccountDir = Path.Combine(_server.Path, "WS", "Saved", "Accounts");
                string targetAccountPath = Path.Combine(targetAccountDir, "account.db");

                if (!Directory.Exists(targetAccountDir))
                    Directory.CreateDirectory(targetAccountDir);

                var confirm = new ContentDialog
                {
                    Title = "导入玩家数据",
                    Content = $"确定要覆盖玩家数据 account.db 吗？",
                    PrimaryButtonText = "确认覆盖",
                    SecondaryButtonText = "取消"
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                    return;

                ImportSaveFileText.Text = "正在导入玩家数据...";
                ImportSaveFileText.Foreground = Brushes.Orange;
                IsEnabled = false;

                await Task.Run(() =>
                {
                    if (!Directory.Exists(targetAccountDir))
                        Directory.CreateDirectory(targetAccountDir);

                    File.Copy(accountFilePath, targetAccountPath, overwrite: true);
                });

                IsEnabled = true;
                var finalDialog = new ContentDialog
                {
                    Title = "导入成功",
                    Content = "玩家数据 account.db 已导入完成！",
                    PrimaryButtonText = "确定"
                };
                await finalDialog.ShowAsync();

                ImportSaveFileText.Text = "拖入服务器文件夹或存档文件到这里";
                ImportSaveFileText.Foreground = Brushes.LightGray;
                DropArea.Background = Brushes.Transparent;
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private bool CheckHasValidSave(string dedicatedPath, string mapName)
        {
            string folder = Path.Combine(dedicatedPath, mapName);
            string dbFile = Path.Combine(folder, "world.db");
            return Directory.Exists(folder) && File.Exists(dbFile);
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            DropArea.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            DropArea.Background = Brushes.Transparent;
        }
    }
}