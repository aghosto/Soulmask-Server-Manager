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
using System.Windows.Threading;
using ModernWpf.Controls;

namespace SoulmaskServerManager
{
    public partial class ImportServerWindow : Window
    {
        private Server _server;
        private bool _isImporting;

        public ImportServerWindow(Server server)
        {
            InitializeComponent();
            _server = server;
            TargetPathTextBox.Text = _server.Path;
            this.DragOver += (s, e) =>
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
        }

        private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择服务器文件夹或 steamapps 文件夹",
                SelectedPath = Directory.Exists(SourcePathTextBox.Text) ? SourcePathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourcePathTextBox.Text = dialog.SelectedPath;
                _ = StartImportFromSourceAsync(dialog.SelectedPath);
            }
        }

        private async Task StartImportFromSourceAsync(string sourcePath)
        {
            if (_isImporting)
                return;

            _isImporting = true;
            try
            {
                await ImportDroppedPathAsync(sourcePath);
            }
            finally
            {
                _isImporting = false;
                DropArea.Background = Brushes.White;
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

            _isImporting = true;
            DropArea.Background = Brushes.White;

            // Let the native OLE drag loop finish before opening a dialog or doing async work.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                try
                {
                    Activate();
                    await ImportDroppedPathAsync(paths[0]);
                }
                finally
                {
                    _isImporting = false;
                    DropArea.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }
            }));
        }

        private async Task ImportDroppedPathAsync(string importPath)
        {
            try
            {
                SourcePathTextBox.Text = Path.GetFullPath(importPath);
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
                            Owner = this,
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
                        Owner = this,
                        Content = "无法识别的文件夹结构",
                        PrimaryButtonText = "确定"
                    }.ShowAsync();
                    return;
                }

                var yesDialog = new ContentDialog()
                {
                    Owner = this,
                    Title = "确认导入服务器",
                    Content = $"源路径：\n{sourceFolderToCopy}\n\n目标路径：\n{targetPath}\n\n是否开始导入？",
                    PrimaryButtonText = "确认",
                    SecondaryButtonText = "取消"
                };
                if (await yesDialog.ShowAsync() is ContentDialogResult.Secondary)
                    return;

                if (PathsOverlap(sourceFolderToCopy, targetPath))
                    throw new InvalidOperationException("源服务器目录与目标服务器目录相同或相互包含，无法安全导入。");

                ImportProgressText.Text = "服务器导入中，请勿关闭本窗口";
                ImportProgressText.Foreground = Brushes.Orange;
                ImportFileText.Visibility = Visibility.Visible;
                ImportProgressBar.Visibility = Visibility.Visible;
                ImportProgressBar.Value = 0;
                var copyProgress = new Progress<ImportCopyProgress>(progress =>
                {
                    ImportFileText.Text = $"正在复制：{progress.FileName}（{progress.CopiedFiles}/{progress.TotalFiles}）";
                    ImportProgressBar.Value = progress.Percent;
                });

                await Task.Run(() =>
                {
                    DirectoryCopy(sourceFolderToCopy, targetPath, copyProgress);
                });
                ImportProgressBar.Value = 100;
                ImportFileText.Text = "文件复制完成";

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
                string settingsFile = Path.Combine(_server.Path, "SaveData", "Settings", "ServerSettings.json");
                var importSettings = ServerSettingsEditor.LoadServerSettings(settingsFile);
                if (string.IsNullOrWhiteSpace(_server.UniqueId))
                    _server.UniqueId = ServerIdMapping.NewId();

                // Keep the ID assigned to this manager entry and replace the imported file's old ID.
                importSettings.SelfServerUniqueId = _server.UniqueId;
                if (importSettings.ServerId <= 0)
                {
                    importSettings.ServerId = ServerSettingsEditor.GetNextAvailableServerId(MainSettings.LoadManagerSettings().Servers);
                }
                ServerSettingsEditor.SaveServerSettings(_server, importSettings);

                bool applicationSettingsSynced = SyncApplicationSettings(importSettings);

                await new ContentDialog
                {
                    Owner = this,
                    Title = "服务器导入结果",
                    Content = applicationSettingsSynced
                        ? $"服务器文件已导入到：\n{targetPath}\n\n应用设置中的 RCON 信息和订阅 Mod 列表已同步。"
                        : $"服务器文件已导入到：\n{targetPath}\n\n未找到对应的应用服务器配置，RCON 设置和订阅 Mod 列表未能同步。",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
                this.Close();
            }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Owner = this,
                    Title = "服务器导入结果",
                    Content = $"导入失败：{ex.Message}",
                    PrimaryButtonText = "确定"
                }.ShowAsync();
            }
        }

        private bool SyncApplicationSettings(ServerSettings importedSettings)
        {
            MainSettings applicationSettings = MainSettings.LoadManagerSettings();
            Server? managedServer = applicationSettings.Servers.FirstOrDefault(server =>
                server.UniqueId == importedSettings.SelfServerUniqueId);

            if (managedServer == null)
                return false;

            SyncRconSettings(managedServer, importedSettings);
            SyncRconSettings(_server, importedSettings);

            List<string> subscribedMods = (importedSettings.Mods ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(modId => modId.Trim())
                .Where(modId => !string.IsNullOrWhiteSpace(modId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            managedServer.SubscribedMods = new List<string>(subscribedMods);
            _server.SubscribedMods = new List<string>(subscribedMods);

            MainSettings.Save(applicationSettings);
            return true;
        }

        private static void SyncRconSettings(Server server, ServerSettings importedSettings)
        {
            if (!string.IsNullOrWhiteSpace(importedSettings.Rcon?.IP))
                server.RconServerSettings.IPAddress = importedSettings.Rcon.IP;

            server.RconServerSettings.Port = importedSettings.Rcon?.Port ?? server.RconServerSettings.Port;
            server.RconServerSettings.EchoPort = importedSettings.EchoPort;
            server.RconServerSettings.Password = importedSettings.Rcon?.Password ?? "";
        }

        private static bool PathsOverlap(string firstPath, string secondPath)
        {
            string first = Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string second = Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return first.Equals(second, StringComparison.OrdinalIgnoreCase)
                || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private void DropArea_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropArea.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            DropArea.Background = Brushes.White;
        }

        private void DirectoryCopy(string sourceDir, string destDir, IProgress<ImportCopyProgress> progress)
        {
            if (!Directory.Exists(sourceDir))
                return;

            Directory.CreateDirectory(destDir);

            string[] directories = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
            foreach (string directory in directories)
            {
                string relativeDirectory = Path.GetRelativePath(sourceDir, directory);
                Directory.CreateDirectory(Path.Combine(destDir, relativeDirectory));
            }

            string[] files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            long totalBytes = files.Sum(file => new FileInfo(file).Length);
            long copiedBytes = 0;
            int copiedFiles = 0;
            int lastReportedPercent = -1;
            string lastReportedFile = null;

            const int bufferSize = 1024 * 1024;
            byte[] buffer = new byte[bufferSize];
            foreach (string sourceFile in files)
            {
                string relativeFile = Path.GetRelativePath(sourceDir, sourceFile);
                string destinationFile = Path.Combine(destDir, relativeFile);
                string fileName = Path.GetFileName(sourceFile);

                using (var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                using (var output = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan))
                {
                    int bytesRead;
                    while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, bytesRead);
                        copiedBytes += bytesRead;

                        int percent = totalBytes > 0
                            ? (int)(copiedBytes * 100 / totalBytes)
                            : files.Length == 0 ? 100 : copiedFiles * 100 / files.Length;
                        if (percent != lastReportedPercent || fileName != lastReportedFile)
                        {
                            progress.Report(new ImportCopyProgress(fileName, copiedFiles + 1, files.Length, percent));
                            lastReportedPercent = percent;
                            lastReportedFile = fileName;
                        }
                    }
                }

                copiedFiles++;
                int completedPercent = totalBytes > 0
                    ? (int)(copiedBytes * 100 / totalBytes)
                    : files.Length == 0 ? 100 : copiedFiles * 100 / files.Length;
                if (completedPercent != lastReportedPercent || fileName != lastReportedFile)
                {
                    progress.Report(new ImportCopyProgress(fileName, copiedFiles, files.Length, completedPercent));
                    lastReportedPercent = completedPercent;
                    lastReportedFile = fileName;
                }
            }

            progress.Report(new ImportCopyProgress(string.Empty, files.Length, files.Length, 100));
        }

        private sealed record ImportCopyProgress(string FileName, int CopiedFiles, int TotalFiles, int Percent);
    }
}
