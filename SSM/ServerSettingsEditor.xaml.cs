using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Text.Json;
using System.Collections.ObjectModel;
using ModernWpf.Controls;
using SoulmaskServerManager.Controls;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Text.Encodings.Web;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace SoulmaskServerManager
{
    /// <summary>
    /// Interaction logic for ServerSettingsEditor.xaml
    /// </summary>
    public partial class ServerSettingsEditor : Window
    {
        private ServerSettings serverSettings;
        private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
                                                                            {
                                                                                WriteIndented = true,
                                                                                IncludeFields = true,
                                                                                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                                                            };
        private readonly ObservableCollection<Server> servers;
        private MainSettings mainSettings;

        public class ServerListItem
        {
            public Server Server { get; set; }
            public int Port { get; set; }
            public string UniqueId { get; set; }
            public string DisplayText => $"{Server.ssmServerName}  端口：{Port}";
        }

        private readonly List<PresetItem> _presetItems = new List<PresetItem>
        {
            new PresetItem{ DisplayName="无", FileName="None"},
            new PresetItem{ DisplayName="GameXishuConfig_Template", FileName="GameXishuConfig_Template.json"},
            new PresetItem{ DisplayName="GameXishuConfig_Template_Action", FileName="GameXishuConfig_Template_Action.json"},
            new PresetItem{ DisplayName="GameXishuConfig_Template_Management", FileName="GameXishuConfig_Template_Management.json"},
            new PresetItem{ DisplayName="GameXishuConfig_Template_PVP", FileName="GameXishuConfig_Template_PVP.json"},
            new PresetItem{ DisplayName="GameXishu_Template", FileName="GameXishu_Template.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action", FileName="GameXishu_Template_Action.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Custom", FileName="GameXishu_Template_Action_Custom.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Dashi", FileName="GameXishu_Template_Action_Dashi.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Jiandan", FileName="GameXishu_Template_Action_Jiandan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Kunnan", FileName="GameXishu_Template_Action_Kunnan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Putong", FileName="GameXishu_Template_Action_Putong.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Action_Xiuxian", FileName="GameXishu_Template_Action_Xiuxian.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Custom", FileName="GameXishu_Template_Custom.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Dashi", FileName="GameXishu_Template_Dashi.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Jiandan", FileName="GameXishu_Template_Jiandan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Kunnan", FileName="GameXishu_Template_Kunnan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management", FileName="GameXishu_Template_Management.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Custom", FileName="GameXishu_Template_Management_Custom.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Dashi", FileName="GameXishu_Template_Management_Dashi.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Jiandan", FileName="GameXishu_Template_Management_Jiandan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Kunnan", FileName="GameXishu_Template_Management_Kunnan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Putong", FileName="GameXishu_Template_Management_Putong.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Management_Xiuxian", FileName="GameXishu_Template_Management_Xiuxian.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Putong", FileName="GameXishu_Template_Putong.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE", FileName="GameXishu_Template_PvE.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_04P", FileName="GameXishu_Template_PvE_04P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_04P_3x", FileName="GameXishu_Template_PvE_04P_3x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_04P_Hardcore", FileName="GameXishu_Template_PvE_04P_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_10P", FileName="GameXishu_Template_PvE_10P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_10P_3x", FileName="GameXishu_Template_PvE_10P_3x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_10P_Hardcore", FileName="GameXishu_Template_PvE_10P_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_20P_3x", FileName="GameXishu_Template_PvE_20P_3x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_20P_5x", FileName="GameXishu_Template_PvE_20P_5x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_20P_merge", FileName="GameXishu_Template_PvE_20P_merge.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_AS_04P_Hardcore", FileName="GameXishu_Template_PvE_AS_04P_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_AS_06P", FileName="GameXishu_Template_PvE_AS_06P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_AS_20P", FileName="GameXishu_Template_PvE_AS_20P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_EU_04P_Hardcore", FileName="GameXishu_Template_PvE_EU_04P_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_EU_06P", FileName="GameXishu_Template_PvE_EU_06P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_EU_20P", FileName="GameXishu_Template_PvE_EU_20P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_NA_04P_Hardcore", FileName="GameXishu_Template_PvE_NA_04P_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_NA_06P", FileName="GameXishu_Template_PvE_NA_06P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_NA_20P", FileName="GameXishu_Template_PvE_NA_20P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_OA_03P", FileName="GameXishu_Template_PvE_OA_03P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_OA_04P", FileName="GameXishu_Template_PvE_OA_04P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_OA_06P", FileName="GameXishu_Template_PvE_OA_06P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_OA_20P", FileName="GameXishu_Template_PvE_OA_20P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_SA_03P", FileName="GameXishu_Template_PvE_SA_03P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_SA_04P", FileName="GameXishu_Template_PvE_SA_04P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_SA_06P", FileName="GameXishu_Template_PvE_SA_06P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvE_SA_20P", FileName="GameXishu_Template_PvE_SA_20P.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP", FileName="GameXishu_Template_PVP.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_04P_No_Raid", FileName="GameXishu_Template_PvP_04P_No_Raid.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_04P_No_Raid_3x", FileName="GameXishu_Template_PvP_04P_No_Raid_3x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_10P_No_Raid", FileName="GameXishu_Template_PvP_10P_No_Raid.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_10P_No_Raid_3x", FileName="GameXishu_Template_PvP_10P_No_Raid_3x.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_03P_UTC12~16", FileName="GameXishu_Template_PvP_AS_03P_UTC12~16.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M", FileName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M_Hardcore", FileName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M_Weekend", FileName="GameXishu_Template_PvP_AS_04P_UTC11~15_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC12~14_3M", FileName="GameXishu_Template_PvP_AS_04P_UTC12~14_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC12~14_LongTerm", FileName="GameXishu_Template_PvP_AS_04P_UTC12~14_LongTerm.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_04P_UTC12~15_1M", FileName="GameXishu_Template_PvP_AS_04P_UTC12~15_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_06P_UTC12~16", FileName="GameXishu_Template_PvP_AS_06P_UTC12~16.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M", FileName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M_Hardcore", FileName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M_Weekend", FileName="GameXishu_Template_PvP_AS_10P_UTC11~15_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_10P_UTC12~14_3M", FileName="GameXishu_Template_PvP_AS_10P_UTC12~14_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_10P_UTC12~15_1M", FileName="GameXishu_Template_PvP_AS_10P_UTC12~15_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_20P_UTC05~17", FileName="GameXishu_Template_PvP_AS_20P_UTC05~17.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_20P_UTC09~17", FileName="GameXishu_Template_PvP_AS_20P_UTC09~17.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_AS_20P_UTC12~16", FileName="GameXishu_Template_PvP_AS_20P_UTC12~16.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_BattleField", FileName="GameXishu_Template_PvP_BattleField.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Custom", FileName="GameXishu_Template_PVP_Custom.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Dashi", FileName="GameXishu_Template_PVP_Dashi.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_03P_UTC19~23", FileName="GameXishu_Template_PvP_EU_03P_UTC19~23.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M", FileName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M_Hardcore", FileName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M_Weekend", FileName="GameXishu_Template_PvP_EU_04P_UTC18~22_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC19~21_3M", FileName="GameXishu_Template_PvP_EU_04P_UTC19~21_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC19~21_LongTerm", FileName="GameXishu_Template_PvP_EU_04P_UTC19~21_LongTerm.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_04P_UTC19~22_1M", FileName="GameXishu_Template_PvP_EU_04P_UTC19~22_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_06P_UTC19~23", FileName="GameXishu_Template_PvP_EU_06P_UTC19~23.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M", FileName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M_Hardcore", FileName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M_Weekend", FileName="GameXishu_Template_PvP_EU_10P_UTC18~22_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_10P_UTC19~21_3M", FileName="GameXishu_Template_PvP_EU_10P_UTC19~21_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_10P_UTC19~22_1M", FileName="GameXishu_Template_PvP_EU_10P_UTC19~22_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_20P_UTC12~24", FileName="GameXishu_Template_PvP_EU_20P_UTC12~24.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_EU_20P_UTC16~24", FileName="GameXishu_Template_PvP_EU_20P_UTC16~24.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Jiandan", FileName="GameXishu_Template_PVP_Jiandan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Kunnan", FileName="GameXishu_Template_PVP_Kunnan.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_03P_UTC01~05", FileName="GameXishu_Template_PvP_NA_03P_UTC01~05.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M", FileName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M_Hardcore", FileName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M_Weekend", FileName="GameXishu_Template_PvP_NA_04P_UTC01~05_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC02~04_3M", FileName="GameXishu_Template_PvP_NA_04P_UTC02~04_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC02~04_LongTerm", FileName="GameXishu_Template_PvP_NA_04P_UTC02~04_LongTerm.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_04P_UTC02~05_1M", FileName="GameXishu_Template_PvP_NA_04P_UTC02~05_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_06P_UTC01~05", FileName="GameXishu_Template_PvP_NA_06P_UTC01~05.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M", FileName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M_Hardcore", FileName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M_Hardcore.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M_Weekend", FileName="GameXishu_Template_PvP_NA_10P_UTC01~05_1M_Weekend.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_10P_UTC02~04_3M", FileName="GameXishu_Template_PvP_NA_10P_UTC02~04_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_10P_UTC02~05_1M", FileName="GameXishu_Template_PvP_NA_10P_UTC02~05_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_20P_UTC18~06", FileName="GameXishu_Template_PvP_NA_20P_UTC18~06.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_NA_20P_UTC22~06", FileName="GameXishu_Template_PvP_NA_20P_UTC22~06.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA", FileName="GameXishu_Template_PvP_OA.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_03P_UTC08~12", FileName="GameXishu_Template_PvP_OA_03P_UTC08~12.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_04P_UTC08~12_1M", FileName="GameXishu_Template_PvP_OA_04P_UTC08~12_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_04P_UTC09~11_3M", FileName="GameXishu_Template_PvP_OA_04P_UTC09~11_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_04P_UTC09~12_1M", FileName="GameXishu_Template_PvP_OA_04P_UTC09~12_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_10P_UTC08~12_1M", FileName="GameXishu_Template_PvP_OA_10P_UTC08~12_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_10P_UTC09~11_3M", FileName="GameXishu_Template_PvP_OA_10P_UTC09~11_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_10P_UTC09~12_1M", FileName="GameXishu_Template_PvP_OA_10P_UTC09~12_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_OA_20P_UTC08~12", FileName="GameXishu_Template_PvP_OA_20P_UTC08~12.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Putong", FileName="GameXishu_Template_PVP_Putong.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA", FileName="GameXishu_Template_PvP_SA.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_03P_UTC23~03", FileName="GameXishu_Template_PvP_SA_03P_UTC23~03.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_04P_UTC23~03_1M", FileName="GameXishu_Template_PvP_SA_04P_UTC23~03_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_04P_UTC24~02_3M", FileName="GameXishu_Template_PvP_SA_04P_UTC24~02_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_04P_UTC24~03_1M", FileName="GameXishu_Template_PvP_SA_04P_UTC24~03_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_06P_UTC23~03", FileName="GameXishu_Template_PvP_SA_06P_UTC23~03.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_10P_UTC23~03_1M", FileName="GameXishu_Template_PvP_SA_10P_UTC23~03_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_10P_UTC24~02_3M", FileName="GameXishu_Template_PvP_SA_10P_UTC24~02_3M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_10P_UTC24~03_1M", FileName="GameXishu_Template_PvP_SA_10P_UTC24~03_1M.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PvP_SA_20P_UTC23~03", FileName="GameXishu_Template_PvP_SA_20P_UTC23~03.json"},
            new PresetItem{ DisplayName="GameXishu_Template_PVP_Xiuxian", FileName="GameXishu_Template_PVP_Xiuxian.json"},
            new PresetItem{ DisplayName="GameXishu_Template_Xiuxian", FileName="GameXishu_Template_Xiuxian.json"},
        };

        public ServerSettingsEditor(ObservableCollection<Server> sentServers, bool autoLoad = false, int indexToLoad = -1)
        {
            servers = sentServers;
            serverSettings = new ServerSettings();
            DataContext = serverSettings;

            InitializeComponent();
            //ExportFilesToCsv(Path.Combine(servers[indexToLoad].Path, "WS", "Config", "GameplaySettings"), false, Path.Combine(Directory.GetCurrentDirectory(), "Output.csv"));

            if (autoLoad && indexToLoad != -1 && servers.Count > 0)
            {
                CheckAndLoadSettingsFile(indexToLoad);
            }
            RefreshMainServerList();
        }

        public static ServerSettings LoadServerSettings(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    using (StreamReader sr = new StreamReader(filePath))
                    {
                        string SettingsJSON = sr.ReadToEnd();
                        ServerSettings LoadedSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(SettingsJSON);
                        return LoadedSettings;
                    }
                }
                catch
                {
                    ContentDialog errorDialog = new()
                    {
                        Content = $"服务器配置文件损坏，已使用默认配置。",
                        PrimaryButtonText = "确定",
                    };
                    errorDialog.ShowAsync();

                    return new ServerSettings();
                }
            }
            else
            {
                ContentDialog noDialog = new()
                {
                    Content = $"未找到服务器配置文件，已使用默认配置。",
                    PrimaryButtonText = "确定",
                };
                noDialog.ShowAsync();

                ServerSettings DefaultSettings = new ServerSettings();
                return DefaultSettings;
            }
        }

        public static void SaveServerSettings(Server server, ServerSettings settings)
        {
            string settingPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

            try
            {
                string directory = Path.GetDirectoryName(settingPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string SettingsJSON = System.Text.Json.JsonSerializer.Serialize(settings, serializerOptions);
                using (StreamWriter sw = new StreamWriter(settingPath, false))
                {
                    sw.Write(SettingsJSON);
                }

            }
            catch
            {
                ContentDialog failDialog = new()
                {
                    Content = $"配置保存失败！",
                    PrimaryButtonText = "确定",
                };
                failDialog.ShowAsync();
            }
        }

        private void CheckAndLoadSettingsFile(int serverIndex)
        {
            string settingsFilePath = Path.Combine(servers[serverIndex].Path, @"SaveData\Settings\ServerSettings.json");

            try
            {
                if (File.Exists(settingsFilePath))
                {
                    LoadSettingsFileSafe(servers[serverIndex]);
                }
                else
                {
                    // 文件不存在，使用默认配置（不写入磁盘，等用户点保存再写入）
                    serverSettings = new ServerSettings();
                    DataContext = serverSettings;
                    InitializeEditorUI();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private async void LoadSettingsFileSafe(Server server)
        {
            try
            {
                string json = File.ReadAllText(server.Path + @"\SaveData\Settings\ServerSettings.json").Trim();

                if (string.IsNullOrEmpty(json))
                {
                    // 文件为空，使用默认配置（不写入磁盘）
                    serverSettings = new ServerSettings();
                    DataContext = serverSettings;
                    InitializeEditorUI();
                    return;
                }

                serverSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                if (serverSettings == null)
                {
                    serverSettings = new ServerSettings();
                }

                DataContext = serverSettings;
                InitializeEditorUI();
            }
            catch
            {
                // 加载失败，使用默认配置（不写入磁盘）
                serverSettings = new ServerSettings();
                DataContext = serverSettings;
                InitializeEditorUI();
            }
        }

        private async void InitializeEditorUI()
        {
            if (serverSettings.Map == "Level01_Main")
                MapSelectComboBox.SelectedIndex = 0;
            else
                MapSelectComboBox.SelectedIndex = 1;

            if (ManualIPCheckBox.IsChecked == true)
            {
                PublicIPTextBox.Text = serverSettings.PublicIP;
            }
            else
            {
                serverSettings.PublicIP = await GetPublicIPAsync();
                PublicIPTextBox.Text = serverSettings.PublicIP;

                if (PublicIPTextBox.Text == "fail")
                {
                    var dialog = new ContentDialog
                    {
                        Content = "自动获取公网ip失败，请检查网络连接或点左下角反馈\r已打开手动填写公网IP设置，请手动填写公网IP",
                        CloseButtonText = "确定"
                    }.ShowAsync();
                    ManualIPCheckBox.IsChecked = true;
                    PublicIPTextBox.Text = "";
                }
            }

            if (ManualMainPortCheckBox.IsChecked == true)
            {
                MainServerPortTextBox.Text = serverSettings.ManualMainPort.ToString();
            }
            else
            {
                serverSettings.MainPort = GetMainServerPortByUniqueId(serverSettings.MainServerUniqueId);
                MainServerPortTextBox.Text = serverSettings.MainPort.ToString();

                if (MainServerPortTextBox.Text == "fail")
                {
                    var dialog = new ContentDialog
                    {
                        Content = "自动获取主服务器端口失败，请检查网络连接或点左下角反馈\r已打开手动填写端口设置，请手动填写主服务器端口",
                        CloseButtonText = "确定"
                    }.ShowAsync();
                    ManualMainPortCheckBox.IsChecked = true;
                    MainServerPortTextBox.Text = "";
                }
            }

            ServerSettingsPresetComboBox.ItemsSource = _presetItems;
            ServerSettingsPresetComboBox.SelectedItem = _presetItems.FirstOrDefault(p => p.FileName == serverSettings.ServerPresetSettings);

            RefreshMainServerList();

            if (!string.IsNullOrEmpty(serverSettings.MainServerUniqueId))
            {
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    SelectMainServerByUniqueId(serverSettings.MainServerUniqueId);
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void SelectMainServerByUniqueId(string targetUid)
        {
            foreach (var item in MainServerListBox.Items)
            {
                if (item is ServerListItem listItem && listItem.Server.UniqueId == targetUid)
                {
                    MainServerListBox.SelectedItem = item;
                    break;
                }
            }
        }

        public static void CreateDefaultSettingsFile(Server server)
        {
            var defaultSettings = new ServerSettings();
            string defaultJson = System.Text.Json.JsonSerializer.Serialize(defaultSettings, serializerOptions);
            File.WriteAllText(server.Path + @"\SaveData\Settings\ServerSettings.json", defaultJson);
        }

        private void FileMenuLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? FileToLoad = "temp";
                OpenFileDialog OpenSettingsDialog = new OpenFileDialog
                {
                    Filter = "\"JSON files\"|*.json",
                    DefaultExt = "json",
                    FileName = "ServerSettings.json",
                    InitialDirectory = Directory.GetCurrentDirectory()
                };
                if (OpenSettingsDialog.ShowDialog() == true && FileToLoad != null)
                {
                    FileToLoad = OpenSettingsDialog.FileName;
                }
                else
                {
                    return;
                }
                using (StreamReader reader = new StreamReader(FileToLoad))
                {
                    string LoadedJSON = reader.ReadToEnd();
                    ServerSettings LoadedSettings = LoadServerSettings(LoadedJSON);
                    serverSettings = LoadedSettings;
                    DataContext = serverSettings;
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async void FileMenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedIndex == -1)
            {
                var yesNoDialog = new ContentDialog()
                {
                    Content = "你选择了作为副服务器，请选择一个绑定的主服务器！",
                    PrimaryButtonText = "是",
                }.ShowAsync();
                return;
            }

            mainSettings = MainSettings.LoadManagerSettings();
            if (servers.Count > 0)
            {
                ContentDialog yesNoDialog = new()
                {
                    Content = "是否自动保存到服务器？如果原始文件存在，将创建其备份。",
                    PrimaryButtonText = "是",
                    SecondaryButtonText = "否"
                };

                if (await yesNoDialog.ShowAsync() is ContentDialogResult.Primary)
                {
                    EditorSaveDialog dialog = new(servers)
                    {
                        PrimaryButtonText = "保存",
                        CloseButtonText = "取消"
                    };
                    Server server;

                    if (await dialog.ShowAsync() is ContentDialogResult.Primary)
                    {
                        server = dialog.GetServer();
                        string settingFolderPath = Path.Combine(server.Path, "SaveData", "Settings");
                        string settingPath = Path.Combine(settingFolderPath, "ServerSettings.json");
                        
                        if (!Directory.Exists(settingFolderPath))
                            Directory.CreateDirectory(settingFolderPath);

                        if (File.Exists(settingPath))
                            File.Copy(settingPath, settingFolderPath + @"\ServerSettings.bak", true);

                        if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedItem is ServerListItem listItem)
                        {
                            serverSettings.UseManualMainPort = ManualMainPortCheckBox.IsChecked == true;
                            if (int.TryParse(MainServerPortTextBox.Text, out int customPort))
                            {
                                serverSettings.ManualMainPort = customPort;
                            }

                            if (listItem.UniqueId == serverSettings.SelfServerUniqueId)
                            {
                                var warningDialog = new ContentDialog
                                {
                                    Owner = this,
                                    Title = "无效选择",
                                    Content = "不能同时作为主副服务器，请重新选择。",
                                    PrimaryButtonText = "确定"
                                };
                                await warningDialog.ShowAsync();
                                return;
                            }

                            if (serverSettings.UseManualMainPort && serverSettings.ManualMainPort > 0)
                                serverSettings.MainPort = serverSettings.ManualMainPort;
                            else
                                serverSettings.MainPort = listItem.Port;

                            serverSettings.MainServerUniqueId = listItem.Server.UniqueId;
                        }
                        else
                        {
                            serverSettings.MainPort = 0;
                            serverSettings.MainServerUniqueId = "";
                        }

                        if (MapSelectComboBox.SelectedIndex == 0)
                        {
                            serverSettings.Map = "Level01_Main";
                        }
                        else
                            serverSettings.Map = "DLC_Level01_Main";

                        if (ServerSettingsPresetComboBox.SelectedItem is PresetItem selectedPreset)
                        {
                            serverSettings.ServerPresetSettings = selectedPreset.FileName;
                        }
                        else
                        {
                            serverSettings.ServerPresetSettings = string.Empty;
                        }

                        SaveServerSettings(server, serverSettings);
                        RefreshMainServerList();

                        var closeFileDialog = new ContentDialog()
                        {
                            Content = "文件成功保存于：\n" + settingPath,
                            PrimaryButtonText = "是",
                        }.ShowAsync();

                        //serverSettings = LoadServerSettings(settingPath);
                        ServerIdMapping.EnsureAllServersHaveIds();
                        MainWindow.StartBackupCleanTimer(server, serverSettings.AutoCleanInterval, serverSettings.AutoSaveCount);
                        LoadSettingsFileSafe(server);

                        foreach (var index in mainSettings.Servers)
                        {
                            if (index.UniqueId == serverSettings.SelfServerUniqueId)
                            {
                                index.RconServerSettings.Port = serverSettings.Rcon.Port;
                                index.RconServerSettings.EchoPort = serverSettings.EchoPort;
                                index.RconServerSettings.Password = serverSettings.Rcon.Password;
                            }
                        }

                        MainSettings.Save(mainSettings);
                        return;
                    }
                    else
                    {
                        RefreshMainServerList();
                        return;
                    }
                }
                else
                    return;
            }
            RefreshMainServerList();
        }

        private void FileMenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshMainServerList()
        {
            MainServerListBox.Items.Clear();

            foreach (var server in servers)
            {
                try
                {
                    string serverSettingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");

                    if (!File.Exists(serverSettingsPath))
                        continue;

                    ServerSettings settings = ServerSettingsEditor.LoadServerSettings(serverSettingsPath);

                    if (settings.ClusterMode == 1)
                    {
                        string serverName = server.ssmServerName;
                        bool exists = false;

                        foreach (var item in MainServerListBox.Items)
                        {
                            if (item.ToString() == serverName)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            MainServerListBox.Items.Add(new ServerListItem
                            {
                                Server = server,
                                Port = settings.Port,
                                UniqueId = settings.SelfServerUniqueId
                            });
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        private int GetMainServerPortByUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return -1;

            try
            {
                string serverSaveDataPath = Path.Combine(GetMainServerFolderByUniqueId(uniqueId), "SaveData", "Settings", "ServerSettings.json");
                if (!File.Exists(serverSaveDataPath))
                    return -1;

                string json = File.ReadAllText(serverSaveDataPath).Trim();
                ServerSettings readServerSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                return readServerSettings?.Port ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private string GetMainServerFolderByUniqueId(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
                return string.Empty;

            if (mainSettings == null)
                mainSettings = MainSettings.LoadManagerSettings();

            var server = mainSettings.Servers.FirstOrDefault(s => s.UniqueId == uniqueId);
            return server?.Path ?? string.Empty;
        }

        private async Task<string> GetPublicIPAsync()
        {
            try
            {
                string publicIP = "";
                using (HttpClient client = new HttpClient())
                {
                    publicIP = await client.GetStringAsync("http://ipinfo.io/ip");
                }
                return publicIP;
            }
            catch
            {
                return "fail";
            }
        }

        public static int GetNextAvailableServerId(System.Collections.ObjectModel.ObservableCollection<Server> servers)
        {
            var usedIds = new HashSet<int>();
            foreach (var server in servers)
            {
                string settingsPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(settingsPath);
                        var settings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                        if (settings != null && settings.ServerId > 0)
                            usedIds.Add(settings.ServerId);
                    }
                    catch { }
                }
            }
            int id = 1;
            while (usedIds.Contains(id))
                id++;
            return id;
        }
    }
}
