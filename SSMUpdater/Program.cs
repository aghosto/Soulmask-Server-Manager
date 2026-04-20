//using System.Diagnostics;
//using System.IO.Compression;
//using System.Text.Json;

//Console.WriteLine("准备下载最新版本。");

//Process[] vsmProcesses = Process.GetProcessesByName("SoulMaskServerManager");
//if (vsmProcesses.Length != 0)
//{
//    Console.WriteLine("SoulmaskServerManager 正在运行，请关闭后再更新！");
//    Console.ReadKey();
//    Environment.Exit(2);
//}

//string workingDir = AppDomain.CurrentDomain.BaseDirectory;
//HttpClient httpClient = new HttpClient();
//httpClient.Timeout = TimeSpan.FromSeconds(10);

//if (!File.Exists(Path.Combine(workingDir, "SoulmaskServerManager.exe")))
//{
//    Console.WriteLine("找不到主程序 SoulmaskServerManager.exe");
//    Console.ReadKey();
//    Environment.Exit(2);
//}

//byte[] fileBytes = null;
//bool downloadSuccess = false;

//try
//{
//    Console.WriteLine("正在尝试从 GitHub 下载更新...");
//    fileBytes = await httpClient.GetByteArrayAsync(@"https://github.com/aghosto/Soulmask-Server-Manager/releases/latest/download/SSM.zip");
//    Console.WriteLine("GitHub 下载成功！");
//    downloadSuccess = true;
//}
//catch (Exception ex)
//{
//    Console.WriteLine($"GitHub 下载失败：{ex.Message}");
//    Console.WriteLine("正在切换到 Gitee 下载...");

//    try
//    {
//        string apiUrl = "https://gitee.com/api/v5/repos/aGHOSToZero/Soulmask-Server-Manager/releases/latest";
//        string json = await httpClient.GetStringAsync(apiUrl);
//        using JsonDocument doc = JsonDocument.Parse(json);
//        JsonElement root = doc.RootElement;

//        string downloadUrl = null;
//        if (root.TryGetProperty("assets", out JsonElement assets))
//        {
//            foreach (var asset in assets.EnumerateArray())
//            {
//                if (asset.TryGetProperty("name", out JsonElement nameEl) && nameEl.GetString() == "SSM.zip")
//                {
//                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
//                    break;
//                }
//            }
//        }

//        if (string.IsNullOrEmpty(downloadUrl))
//        {
//            Console.WriteLine("Gitee 无法获取下载地址");
//            Console.ReadKey();
//            Environment.Exit(2);
//        }

//        Console.WriteLine("正在从 Gitee 下载...");
//        fileBytes = await httpClient.GetByteArrayAsync(downloadUrl);
//        Console.WriteLine("Gitee 下载成功！");
//        downloadSuccess = true;
//    }
//    catch (Exception ex2)
//    {
//        Console.WriteLine($"Gitee 下载失败：{ex2.Message}");
//        Console.WriteLine("两个更新源均失败，请检查网络");
//        Console.ReadKey();
//        Environment.Exit(2);
//    }
//}

//if (!downloadSuccess || fileBytes == null)
//{
//    Console.WriteLine("下载失败");
//    Console.ReadKey();
//    return;
//}

//// 临时目录
//string tempDir = Path.Combine(workingDir, "temp");
//if (Directory.Exists(tempDir))
//    Directory.Delete(tempDir, true);
//Directory.CreateDirectory(tempDir);

//// 保存压缩包
//string zipPath = Path.Combine(tempDir, "SSM.zip");
//await File.WriteAllBytesAsync(zipPath, fileBytes);

//// 备份配置
//Console.WriteLine("\n正在备份配置...");
//string backupDir = Path.Combine(workingDir, "Backups");
//if (!Directory.Exists(backupDir))
//    Directory.CreateDirectory(backupDir);

//string configFile = Path.Combine(workingDir, "SSMSettings.json");
//if (File.Exists(configFile))
//{
//    File.Copy(configFile, Path.Combine(backupDir, "SSMSettings.bak"), true);
//}

//// 解压
//ZipFile.ExtractToDirectory(zipPath, tempDir, true);

//// 覆盖文件
//Console.WriteLine("\n正在更新文件...");
//foreach (string file in Directory.GetFiles(tempDir))
//{
//    string fileName = Path.GetFileName(file);
//    if (fileName is "SSMUpdater.exe" or "SSMUpdater.dll")
//        continue;

//    string dest = Path.Combine(workingDir, fileName);
//    //Console.WriteLine("覆盖: " + fileName);
//    File.Copy(file, dest, true);
//}

//// 清理
//Directory.Delete(tempDir, true);
//File.Delete("SSM.zip");

//Console.WriteLine("\n更新完成！");
//Console.WriteLine("备份位于: Backups");
//Console.WriteLine("按任意键启动程序...");
//Console.ReadKey();
//Process.Start(Path.Combine(workingDir, "SoulmaskServerManager.exe"));

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

Console.WriteLine("准备下载最新版本。");

Process[] vsmProcesses = Process.GetProcessesByName("SoulmaskServerManager");
if (vsmProcesses.Length != 0)
{
    Console.WriteLine("SoulmaskServerManager 正在运行，请关闭后再更新！");
    Console.ReadKey();
    Environment.Exit(2);
}

string workingDir = AppDomain.CurrentDomain.BaseDirectory;
HttpClient httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(10);

if (!File.Exists(Path.Combine(workingDir, "SoulmaskServerManager.exe")))
{
    Console.WriteLine("找不到主程序 SoulmaskServerManager.exe");
    Console.ReadKey();
    Environment.Exit(2);
}

byte[] fileBytes = null;
bool downloadSuccess = false;
string latestVersion = null; // 用来保存最新版本号

try
{
    Console.WriteLine("正在尝试从 GitHub 下载更新...");
    fileBytes = await httpClient.GetByteArrayAsync(@"https://github.com/aghosto/Soulmask-Server-Manager/releases/latest/download/SSM.zip");
    Console.WriteLine("GitHub 下载成功！");
    downloadSuccess = true;

    // 获取 GitHub 最新版本号
    string apiUrl = "https://api.github.com/repos/aghosto/Soulmask-Server-Manager/releases/latest";
    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
    request.Headers.Add("User-Agent", "SoulmaskServerManager");
    var response = await httpClient.SendAsync(request);
    if (response.IsSuccessStatusCode)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("tag_name", out var tag))
            latestVersion = tag.GetString().TrimStart('v'); // 去掉 v 得到纯版本号 如 1.2.3
    }
}
catch (Exception ex)
{
    Console.WriteLine($"GitHub 下载失败：{ex.Message}");
    Console.WriteLine("正在切换到 Gitee 下载...");

    try
    {
        string apiUrl = "https://gitee.com/api/v5/repos/aGHOSToZero/Soulmask-Server-Manager/releases/latest";
        string json = await httpClient.GetStringAsync(apiUrl);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // 获取 Gitee 最新版本号
        if (root.TryGetProperty("tag_name", out JsonElement tag))
            latestVersion = tag.GetString().TrimStart('v');

        string downloadUrl = null;
        if (root.TryGetProperty("assets", out JsonElement assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out JsonElement nameEl) && nameEl.GetString() == "SSM.zip")
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            Console.WriteLine("Gitee 无法获取下载地址");
            Console.ReadKey();
            Environment.Exit(2);
        }

        Console.WriteLine("正在从 Gitee 下载...");
        fileBytes = await httpClient.GetByteArrayAsync(downloadUrl);
        Console.WriteLine("Gitee 下载成功！");
        downloadSuccess = true;
    }
    catch (Exception ex2)
    {
        Console.WriteLine($"Gitee 下载失败：{ex2.Message}");
        Console.WriteLine("两个更新源均失败，请检查网络");
        Console.ReadKey();
        Environment.Exit(2);
    }
}

if (!downloadSuccess || fileBytes == null)
{
    Console.WriteLine("下载失败");
    Console.ReadKey();
    return;
}

// 临时目录
string tempDir = Path.Combine(workingDir, "temp");
if (Directory.Exists(tempDir))
    Directory.Delete(tempDir, true);
Directory.CreateDirectory(tempDir);

// 保存压缩包
string zipPath = Path.Combine(tempDir, "SSM.zip");
await File.WriteAllBytesAsync(zipPath, fileBytes);

// 备份配置
Console.WriteLine("\n正在备份配置...");
string backupDir = Path.Combine(workingDir, "Backups");
if (!Directory.Exists(backupDir))
    Directory.CreateDirectory(backupDir);

string configFile = Path.Combine(workingDir, "SSMSettings.json");
if (File.Exists(configFile))
{
    File.Copy(configFile, Path.Combine(backupDir, "SSMSettings.bak"), true);
}

// 解压
ZipFile.ExtractToDirectory(zipPath, tempDir, true);

// 覆盖文件
Console.WriteLine("\n正在更新文件...");
foreach (string file in Directory.GetFiles(tempDir))
{
    string fileName = Path.GetFileName(file);
    if (fileName is "SSMUpdater.exe" or "SSMUpdater.dll")
        continue;

    string dest = Path.Combine(workingDir, fileName);
    File.Copy(file, dest, true);
}

if (!string.IsNullOrEmpty(latestVersion) && File.Exists(configFile))
{
    try
    {
        string jsonContent = await File.ReadAllTextAsync(configFile);
        JsonNode jsonNode = JsonNode.Parse(jsonContent);
        jsonNode["Version"] = latestVersion;
        var options = new JsonSerializerOptions { WriteIndented = true };
        string newJson = JsonSerializer.Serialize(jsonNode, options);
        await File.WriteAllTextAsync(configFile, newJson);
    }
    catch
    {
        Console.WriteLine("\n⚠️ 配置文件版本更新失败，但主程序已更新完成");
    }
}

// 清理
Directory.Delete(tempDir, true);
if (File.Exists("SSM.zip")) 
    File.Delete("SSM.zip");

Console.WriteLine("\n更新完成！");
Console.WriteLine("备份位于: Backups");
Console.WriteLine("按任意键启动程序...");
Console.ReadKey();
Process.Start(Path.Combine(workingDir, "SoulmaskServerManager.exe"));