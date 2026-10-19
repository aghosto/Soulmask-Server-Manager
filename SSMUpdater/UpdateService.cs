using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SSMUpdater;

public sealed record UpdateProgress(string Stage, string Detail, double? Percent = null);

public sealed record UpdateResult(
    bool Success,
    string Message,
    bool HandedOff = false,
    string? CleanupDirectory = null,
    string? InstallDirectory = null);

/// <summary>
/// 负责检查、下载、解压、安装和回滚更新文件。
/// </summary>
public sealed class UpdateService
{
    private const string MainProcessName = "SoulmaskServerManager";
    private const string MainExecutableName = "SoulmaskServerManager.exe";

    public async Task<UpdateResult> RunAsync(string[] args, IProgress<UpdateProgress> progress)
    {
        if (args.Length > 0 && string.Equals(args[0], "--install-staged", StringComparison.OrdinalIgnoreCase) &&
            !TryGetStagedInstallArguments(args, out _, out _, out _, out _, out _))
        {
            return new UpdateResult(false, "新版更新器启动参数不完整，已取消安装。");
        }

        if (TryGetStagedInstallArguments(args, out string stagedInstallDir, out string stagedExtractedDir,
                out string stagedTempDir, out string? stagedVersion, out int oldUpdaterProcessId))
        {
            return await InstallStagedUpdateAsync(
                stagedInstallDir, stagedExtractedDir, stagedTempDir, stagedVersion, oldUpdaterProcessId, progress);
        }

        string installDir = AppContext.BaseDirectory;
        string mainExecutable = Path.Combine(installDir, MainExecutableName);
        if (!File.Exists(mainExecutable))
            return new UpdateResult(false, "找不到主程序 SoulmaskServerManager.exe，无法继续更新。");

        int? parentProcessId = GetParentProcessId(args);
        if (!await WaitForManagerToExitAsync(parentProcessId, progress))
            return new UpdateResult(false, "主程序未能正常关闭，或仍有其他管理器实例运行。请关闭所有管理器窗口后重试。");

        string tempDir = Path.Combine(Path.GetTempPath(), $"SoulmaskServerManagerUpdate-{Guid.NewGuid():N}");
        string downloadDir = Path.Combine(tempDir, "download");
        string extractedDir = Path.Combine(tempDir, "extracted");
        bool preserveTempDir = false;

        try
        {
            Directory.CreateDirectory(downloadDir);
            Directory.CreateDirectory(extractedDir);

            string zipPath = Path.Combine(downloadDir, "SSM.zip");
            string? latestVersion = null;
            bool downloadedFromGitHub = false;

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            try
            {
                progress.Report(new UpdateProgress("正在连接 GitHub", "正在获取最新版本更新包..."));
                await DownloadToFileAsync(
                    httpClient,
                    "https://github.com/aghosto/Soulmask-Server-Manager/releases/latest/download/SSM.zip",
                    zipPath,
                    progress);
                downloadedFromGitHub = true;
            }
            catch (Exception githubException)
            {
                progress.Report(new UpdateProgress("GitHub 下载失败", $"正在切换到 Gitee 更新源：{githubException.Message}"));
                try
                {
                    var giteeRelease = await GetGiteeReleaseAsync(httpClient);
                    latestVersion = giteeRelease.Version;
                    await DownloadToFileAsync(httpClient, giteeRelease.DownloadUrl, zipPath, progress);
                }
                catch (Exception giteeException)
                {
                    return new UpdateResult(false, $"GitHub 和 Gitee 更新源均下载失败。\n\n{giteeException.Message}");
                }
            }

            if (downloadedFromGitHub)
            {
                try
                {
                    latestVersion = await TryGetGitHubVersionAsync(httpClient);
                }
                catch (Exception versionException)
                {
                    progress.Report(new UpdateProgress("更新包已下载", $"无法读取版本号，继续安装：{versionException.Message}"));
                }
            }

            progress.Report(new UpdateProgress("正在解压更新包", "正在准备文件，请稍候..."));
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractedDir));

            if (!File.Exists(Path.Combine(extractedDir, MainExecutableName)))
                return new UpdateResult(false, "更新包格式不正确：找不到 SoulmaskServerManager.exe。");

            string stagedUpdater = Path.Combine(extractedDir, "SSMUpdater.exe");
            if (File.Exists(stagedUpdater) && HasCompleteUpdaterFileSet(extractedDir))
            {
                progress.Report(new UpdateProgress("正在准备更新程序", "将使用更新包中的新版更新器继续安装..."));
                var updaterStartInfo = new ProcessStartInfo
                {
                    FileName = stagedUpdater,
                    WorkingDirectory = extractedDir,
                    UseShellExecute = true
                };
                updaterStartInfo.ArgumentList.Add("--install-staged");
                updaterStartInfo.ArgumentList.Add(installDir);
                updaterStartInfo.ArgumentList.Add(extractedDir);
                updaterStartInfo.ArgumentList.Add(tempDir);
                updaterStartInfo.ArgumentList.Add(latestVersion ?? string.Empty);
                updaterStartInfo.ArgumentList.Add(Environment.ProcessId.ToString());

                Process.Start(updaterStartInfo);
                preserveTempDir = true;
                return new UpdateResult(true, "新版更新程序已启动，正在接管安装。", HandedOff: true);
            }

            return new UpdateResult(false,
                "更新包未包含完整的新版本更新器，已停止安装，当前管理器和更新器文件均未更改。请重新下载完整更新包后重试。");
        }
        catch (Exception exception)
        {
            return new UpdateResult(false, $"更新失败：{exception.Message}");
        }
        finally
        {
            try
            {
                if (!preserveTempDir && Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // 清理失败不改变更新结果，避免覆盖原始错误信息。
            }
        }
    }

    private static async Task<UpdateResult> InstallStagedUpdateAsync(
        string installDir,
        string extractedDir,
        string tempDir,
        string? latestVersion,
        int oldUpdaterProcessId,
        IProgress<UpdateProgress> progress)
    {
        string fullInstallDir = Path.GetFullPath(installDir);
        string fullExtractedDir = Path.GetFullPath(extractedDir);
        string fullTempDir = Path.GetFullPath(tempDir);
        string expectedTempRoot = Path.GetFullPath(Path.GetTempPath());
        string requiredTempPrefix = "SoulmaskServerManagerUpdate-";

        if (!fullTempDir.StartsWith(expectedTempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(fullTempDir).StartsWith(requiredTempPrefix, StringComparison.OrdinalIgnoreCase) ||
            !fullExtractedDir.StartsWith(fullTempDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateResult(false, "更新临时目录参数无效，已取消安装以保护本地文件。");
        }

        if (!File.Exists(Path.Combine(fullExtractedDir, MainExecutableName)) ||
            !HasCompleteUpdaterFileSet(fullExtractedDir))
        {
            return new UpdateResult(false, "更新包缺少管理器或完整的更新器文件，未安装任何文件。");
        }

        progress.Report(new UpdateProgress("等待旧版更新器退出", "正在安全切换更新程序..."));
        if (!await WaitForProcessExitAsync(oldUpdaterProcessId, TimeSpan.FromSeconds(60)))
            return new UpdateResult(false, "旧版更新器未能正常退出，更新已取消，原有文件保持不变。");

        string configFile = Path.Combine(fullInstallDir, "SSMSettings.json");
        if (File.Exists(configFile))
        {
            string backupDir = Path.Combine(fullInstallDir, "Backups");
            Directory.CreateDirectory(backupDir);
            File.Copy(configFile, Path.Combine(backupDir, "SSMSettings.bak"), true);
        }

        string rollbackDir = Path.Combine(fullTempDir, "rollback");
        Directory.CreateDirectory(rollbackDir);
        var changedFiles = new List<ChangedFile>();
        List<string> updateFiles = Directory
            .EnumerateFiles(fullExtractedDir, "*", SearchOption.AllDirectories)
            .ToList();

        try
        {
            await Task.Run(() => InstallFiles(
                updateFiles, fullExtractedDir, fullInstallDir, rollbackDir, changedFiles, progress));

            if (!string.IsNullOrWhiteSpace(latestVersion) && File.Exists(configFile))
                await UpdateSettingsVersionAsync(configFile, latestVersion, rollbackDir, changedFiles, progress);
        }
        catch (Exception updateException)
        {
            progress.Report(new UpdateProgress("更新遇到问题", "正在恢复更新前的文件..."));
            List<string> rollbackErrors = await Task.Run(() => RollBack(changedFiles));
            if (rollbackErrors.Count == 0)
                return new UpdateResult(false, $"安装更新失败，已恢复原文件。\n\n原因：{updateException.Message}");

            return new UpdateResult(
                false,
                $"安装更新失败，且部分文件无法自动恢复。\n\n原因：{updateException.Message}\n\n请检查安装目录，并保留回滚文件：{rollbackDir}\n\n{string.Join("\n", rollbackErrors)}");
        }

        return new UpdateResult(
            true,
            "管理器和更新程序均已更新。点击“启动管理器”打开新版本。",
            CleanupDirectory: fullTempDir,
            InstallDirectory: fullInstallDir);
    }

    private static bool HasCompleteUpdaterFileSet(string directory)
    {
        string[] requiredFiles =
        {
            "SSMUpdater.exe",
            "SSMUpdater.dll",
            "SSMUpdater.deps.json",
            "SSMUpdater.runtimeconfig.json",
            "ModernWpf.dll",
            "ModernWpf.Controls.dll"
        };

        return requiredFiles.All(file => File.Exists(Path.Combine(directory, file)));
    }

    private static bool TryGetStagedInstallArguments(
        string[] args,
        out string installDir,
        out string extractedDir,
        out string tempDir,
        out string? version,
        out int oldUpdaterProcessId)
    {
        installDir = extractedDir = tempDir = string.Empty;
        version = null;
        oldUpdaterProcessId = 0;
        if (args.Length < 6 || !string.Equals(args[0], "--install-staged", StringComparison.OrdinalIgnoreCase))
            return false;

        installDir = args[1];
        extractedDir = args[2];
        tempDir = args[3];
        version = string.IsNullOrWhiteSpace(args[4]) ? null : args[4];
        return int.TryParse(args[5], out oldUpdaterProcessId) && oldUpdaterProcessId > 0;
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (IsProcessRunning(processId) && DateTime.UtcNow < deadline)
            await Task.Delay(200);

        return !IsProcessRunning(processId);
    }

    private static void InstallFiles(List<string> updateFiles, string extractedDir, string installDir, string rollbackDir, List<ChangedFile> changedFiles, IProgress<UpdateProgress> progress)
    {
        int completed = 0;
        foreach (string sourceFile in updateFiles)
        {
            string relativePath = Path.GetRelativePath(extractedDir, sourceFile);
            string destination = GetSafeDestinationPath(installDir, relativePath);
            string? backupFile = null;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                backupFile = Path.Combine(rollbackDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                File.Copy(destination, backupFile, true);
            }

            // 在覆盖之前登记，确保发生部分写入时也能恢复。
            changedFiles.Add(new ChangedFile(destination, backupFile));
            File.Copy(sourceFile, destination, true);

            completed++;
            double percent = completed * 100d / updateFiles.Count;
            progress.Report(new UpdateProgress(
                "正在安装更新",
                $"已安装 {completed} / {updateFiles.Count} 个文件",
                percent));
        }
    }

    private static async Task UpdateSettingsVersionAsync(
        string configFile,
        string latestVersion,
        string rollbackDir,
        List<ChangedFile> changedFiles,
        IProgress<UpdateProgress> progress)
    {
        try
        {
            if (!changedFiles.Any(file => string.Equals(file.Destination, configFile, StringComparison.OrdinalIgnoreCase)))
            {
                string backupFile = Path.Combine(rollbackDir, "SSMSettings.json");
                File.Copy(configFile, backupFile, true);
                changedFiles.Add(new ChangedFile(configFile, backupFile));
            }

            JsonNode? settings = JsonNode.Parse(await File.ReadAllTextAsync(configFile));
            if (settings is not JsonObject settingsObject)
                return;

            settingsObject["Version"] = latestVersion;
            var options = new JsonSerializerOptions { WriteIndented = true };
            string versionTempFile = Path.Combine(Path.GetDirectoryName(configFile)!, $".SSMSettings-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(versionTempFile, settingsObject.ToJsonString(options));
                File.Move(versionTempFile, configFile, true);
            }
            finally
            {
                if (File.Exists(versionTempFile))
                    File.Delete(versionTempFile);
            }
        }
        catch (Exception versionException)
        {
            progress.Report(new UpdateProgress("程序文件已更新", $"版本号写入失败：{versionException.Message}", 100));
        }
    }

    private static async Task<bool> WaitForManagerToExitAsync(int? parentProcessId, IProgress<UpdateProgress> progress)
    {
        if (parentProcessId.HasValue)
        {
            progress.Report(new UpdateProgress("等待管理器关闭", "正在等待主程序完全退出..."));
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (IsProcessRunning(parentProcessId.Value) && DateTime.UtcNow < deadline)
                await Task.Delay(250);

            if (IsProcessRunning(parentProcessId.Value))
                return false;
        }

        foreach (Process process in Process.GetProcessesByName(MainProcessName))
        {
            using (process)
            {
                if (process.Id != parentProcessId)
                    return false;
            }
        }

        return true;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task DownloadToFileAsync(
        HttpClient httpClient,
        string url,
        string destination,
        IProgress<UpdateProgress> progress)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();

        await using Stream input = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        long? totalBytes = response.Content.Headers.ContentLength;
        byte[] buffer = new byte[81920];
        long downloadedBytes = 0;
        int lastReportedPercent = -1;
        Stopwatch unknownLengthProgressTimer = Stopwatch.StartNew();
        int bytesRead;

        while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
            downloadedBytes += bytesRead;

            if (totalBytes is > 0)
            {
                int percent = (int)(downloadedBytes * 100 / totalBytes.Value);
                if (percent != lastReportedPercent)
                {
                    progress.Report(new UpdateProgress(
                        "正在下载更新",
                        $"已下载 {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes.Value)}",
                        percent));
                    lastReportedPercent = percent;
                }
            }
            else if (unknownLengthProgressTimer.Elapsed >= TimeSpan.FromSeconds(1))
            {
                progress.Report(new UpdateProgress("正在下载更新", $"已下载 {FormatBytes(downloadedBytes)}"));
                unknownLengthProgressTimer.Restart();
            }
        }

        await output.FlushAsync(timeout.Token);
        if (totalBytes is null or <= 0)
            progress.Report(new UpdateProgress("正在下载更新", $"已下载 {FormatBytes(downloadedBytes)}"));
    }

    private static async Task<string?> TryGetGitHubVersionAsync(HttpClient httpClient)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/repos/aghosto/Soulmask-Server-Manager/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SoulmaskServerManager", "1.0"));

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("tag_name", out JsonElement tag)
            ? tag.GetString()?.TrimStart('v')
            : null;
    }

    private static async Task<(string? Version, string DownloadUrl)> GetGiteeReleaseAsync(HttpClient httpClient)
    {
        using JsonDocument document = JsonDocument.Parse(await httpClient.GetStringAsync("https://gitee.com/api/v5/repos/aGHOSToZero/Soulmask-Server-Manager/releases/latest"));
        JsonElement root = document.RootElement;

        string? version = root.TryGetProperty("tag_name", out JsonElement tag)
            ? tag.GetString()?.TrimStart('v')
            : null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out JsonElement name) &&
                    string.Equals(name.GetString(), "SSM.zip", StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out JsonElement url))
                {
                    downloadUrl = url.GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException("Gitee 最新版本中没有 SSM.zip 下载地址。");

        return (version, downloadUrl);
    }

    private static string GetSafeDestinationPath(string installDir, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".."))
        {
            throw new InvalidDataException($"更新包包含无效路径：{relativePath}");
        }

        string root = Path.GetFullPath(installDir);
        string destination = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"更新文件路径超出安装目录：{relativePath}");

        return destination;
    }

    private static List<string> RollBack(List<ChangedFile> changedFiles)
    {
        var errors = new List<string>();
        for (int i = changedFiles.Count - 1; i >= 0; i--)
        {
            ChangedFile changedFile = changedFiles[i];
            try
            {
                if (changedFile.Backup is null)
                {
                    if (File.Exists(changedFile.Destination))
                        File.Delete(changedFile.Destination);
                }
                else if (File.Exists(changedFile.Backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(changedFile.Destination)!);
                    File.Copy(changedFile.Backup, changedFile.Destination, true);
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{changedFile.Destination}: {exception.Message}");
            }
        }

        return errors;
    }

    private static int? GetParentProcessId(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--wait-for-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int processId) && processId > 0)
            {
                return processId;
            }
        }

        return null;
    }

    private static string FormatBytes(long byteCount)
    {
        double megabytes = byteCount / 1024d / 1024d;
        return $"{megabytes:0.0} MB";
    }

    private sealed record ChangedFile(string Destination, string? Backup);
}
