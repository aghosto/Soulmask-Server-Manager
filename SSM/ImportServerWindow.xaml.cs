using SoulmaskServerManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ModernWpf.Controls;

namespace SoulmaskServerManager
{
    public partial class ImportServerWindow : Window
    {
        private Server _server;

        public ImportServerWindow(Server server)
        {
            InitializeComponent();
            _server = server;
            this.DragOver += (s, e) => e.Effects = DragDropEffects.Copy;
        }
        private async void DropArea_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (paths.Length == 0) 
                    return;

                string importPath = paths[0];
                string targetPath = _server.Path;
                string sourceFolderToCopy = "";

                if (Directory.Exists(Path.Combine(importPath, "steamapps")))
                {
                    sourceFolderToCopy = importPath;
                }
                else if (Path.GetFileName(importPath).Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    string gamePath = Path.Combine(importPath, "common", "Soulmask Dedicated Server For Windows");
                    if (!Directory.Exists(gamePath))
                    {
                        await new ContentDialog
                        {
                            Content = "未找到游戏服务器文件夹",
                            PrimaryButtonText = "确定"
                        }.ShowAsync();
                        return;
                    }
                    sourceFolderToCopy = gamePath;
                }
                else
                {
                    await new ContentDialog
                    {
                        Content = "无法识别的文件夹结构",
                        PrimaryButtonText = "确定"
                    }.ShowAsync();
                    return;
                }

                var yesDialog = new ContentDialog()
                {
                    Content = $"是否导入服务器文件夹路径：{sourceFolderToCopy}",
                    PrimaryButtonText = "确认",
                    SecondaryButtonText = "取消"
                };
                if (await yesDialog.ShowAsync() is ContentDialogResult.Secondary)
                    return;

                ImportProgressText.Text = "服务器导入中，请勿关闭本窗口";
                ImportProgressText.Foreground = Brushes.Orange;

                this.IsEnabled = false;
                await Task.Run(() =>
                {
                    DirectoryCopy(sourceFolderToCopy, targetPath, true);
                });

                string targetSteamapps = Path.Combine(targetPath, "steamapps");
                Directory.CreateDirectory(targetSteamapps);
                Directory.CreateDirectory(Path.Combine(targetSteamapps, "downloading"));
                Directory.CreateDirectory(Path.Combine(targetSteamapps, "temp"));

                string[] acfFiles = {
                    "appmanifest_228980.acf",
                    "appmanifest_3017310.acf"
                };

                string correctLauncherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SteamCMD", "steamcmd.exe");
                string escapedPath = correctLauncherPath.Replace(@"\", @"\\");

                foreach (var acf in acfFiles)
                {
                    string src = Path.Combine(importPath, acf);
                    string dst = Path.Combine(targetSteamapps, acf);

                    if (File.Exists(src))
                    {
                        string content = File.ReadAllText(src);
                        content = Regex.Replace(content, @"""LauncherPath""\s+""[^""]+""", $"\"LauncherPath\"		\"{escapedPath}\"");
                        File.WriteAllText(dst, content);
                    }
                }
                ServerIdMapping.EnsureServerHasId(_server);

                string settingsFile = Path.Combine(_server.Path, "SaveData", "Settings", "ServerSettings.json");
                var importSettings = ServerSettingsEditor.LoadServerSettings(settingsFile);
                if (importSettings.ServerId <= 0)
                {
                    importSettings.ServerId = ServerSettingsEditor.GetNextAvailableServerId(MainSettings.LoadManagerSettings().Servers);
                    ServerSettingsEditor.SaveServerSettings(_server, importSettings);
                }

                this.IsEnabled = true;
                await new ContentDialog
                {
                    Title = "服务器导入结果",
                    Content = "服务器导入成功！",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                this.Close();
            }
            catch (Exception ex)
            {
                this.IsEnabled = true;
                await new ContentDialog
                {
                    Title = "服务器导入结果",
                    Content = $"导入失败：{ex.Message}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            DropArea.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            DropArea.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        }

        private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                return;

            Directory.CreateDirectory(destDir);

            foreach (FileInfo file in dir.GetFiles())
                file.CopyTo(Path.Combine(destDir, file.Name), true);

            if (copySubDirs)
            {
                foreach (DirectoryInfo sub in dir.GetDirectories())
                    DirectoryCopy(sub.FullName, Path.Combine(destDir, sub.Name), true);
            }
        }
    }
}