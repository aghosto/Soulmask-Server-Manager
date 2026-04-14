using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Text.Json;
using System.Collections.ObjectModel;
using ModernWpf.Controls;
using SoulMaskServerManager.Controls;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Runtime;
using System.Text.Encodings.Web;
using System.Windows.Documents;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;

namespace SoulMaskServerManager
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

        private List<string> _originalLines;


        public class ServerListItem
        {
            // 绑定用的数据
            public Server Server { get; set; }
            public int Port { get; set; }

            // 显示用的组合文本
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

        /// <summary>
        /// 加载服务器配置
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        /// <returns>反序列化后的 ServerSettings 对象</returns>
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

        /// <summary>
        /// 保存服务器配置
        /// </summary>
        /// <param name="filePath">保存路径</param>
        /// <param name="settings">要保存的配置对象</param>
        public static void SaveServerSettings(string filePath, ServerSettings settings)
        {
            try
            {
                // 自动创建目录
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // 序列化 + 保存
                string SettingsJSON = System.Text.Json.JsonSerializer.Serialize(settings, serializerOptions);
                using (StreamWriter sw = new StreamWriter(filePath, false))
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

        private async void CheckAndLoadSettingsFile(int serverIndex)
        {
            string settingsFilePath = Path.Combine(servers[serverIndex].Path, @"SaveData\Settings\ServerSettings.json");

            try
            {

                string filePath = settingsFilePath;
                string dir = Path.GetDirectoryName(settingsFilePath);

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(settingsFilePath))
                {
                    var dialog = new ContentDialog
                    {
                        Owner = this,
                        Title = "文件不存在",
                        Content = "未找到服务器配置文件，是否创建新的配置？",
                        PrimaryButtonText = "创建",
                        SecondaryButtonText = "取消"
                    };

                    if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                        //Close();
                        return;

                    CreateDefaultSettingsFile(servers[serverIndex]);
                }

                //ServerIdMapping.EnsureServerHasId(servers[serverIndex]);
                LoadSettingsFileSafe(servers[serverIndex]);
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
                    CreateDefaultSettingsFile(server);
                    json = File.ReadAllText(server.Path + @"\SaveData\Settings\ServerSettings.json");
                }

                serverSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                string iniFilePath = Path.Combine(server.Path, "WS", "Saved", "Config", "WindowsServer", "Engine.ini");
                serverSettings.SteamServerName = IniReadServerName(iniFilePath);
                
                DataContext = serverSettings;

                if (serverSettings.Map == "Level01_Main")
                    MapSelectComboBox.SelectedIndex = 0;
                else
                    MapSelectComboBox.SelectedIndex = 1;

                if (serverSettings.PVP == false)
                    PVPSelectComboBox.SelectedIndex = 0;
                else
                    PVPSelectComboBox.SelectedIndex = 1;

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
            catch
            {
                CreateDefaultSettingsFile(server);
                LoadSettingsFileSafe(server);
            }
        }

        public void ExportFilesToCsv(string folderPath, bool includeSubDir, string csvSavePath)
        {
            try
            {
                // 检查文件夹是否存在
                if (!Directory.Exists(folderPath))
                {
                    return;
                }

                var utf8WithoutBom = new UTF8Encoding(true);

                // 写入 CSV
                using (var sw = new StreamWriter(csvSavePath, false, utf8WithoutBom))
                {
                    // 表头
                    sw.WriteLine("文件名,文件扩展名,完整路径");

                    // 遍历文件
                    var files = Directory.EnumerateFiles(
                        folderPath,
                        "*.*",
                        includeSubDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var ext = Path.GetExtension(file).TrimStart('.');
                        var fullPath = file;

                        // 写入 CSV（处理逗号问题）
                        sw.WriteLine($"{EscapeCsv(fileName)},{EscapeCsv(ext)},{EscapeCsv(fullPath)}");
                    }
                }

            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// 处理 CSV 里的逗号、引号，防止格式错乱
        /// </summary>
        private string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(",") || text.Contains("\""))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
        }

        private void SelectMainServerByName(string serverName)
        {
            if (string.IsNullOrEmpty(serverName)) return;

            foreach (var item in MainServerListBox.Items)
            {

                string text = item.ToString();

                if (text.StartsWith(serverName.Trim()))
                {
                    MainServerListBox.SelectedItem = item;
                    break;
                }
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

        private async void AutoLoad(int serverIndex)
        {
            string fileToLoad = servers[serverIndex].Path + @"\SaveData\Settings\ServerSettings.json";
            if (!File.Exists(fileToLoad))
            {
                var createFileDialog = new ContentDialog
                {
                    Owner = this,
                    Title = "错误",
                    Content = $"加载服务器连接配置文件失败：{fileToLoad}\r，请点击下方确认以生成新的配置文件以便操作 ",
                    PrimaryButtonText = "确认",
                    SecondaryButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary
                };
                if (await createFileDialog.ShowAsync() is ContentDialogResult.Primary)
                    File.Create(fileToLoad);
                return;
            }

            using (StreamReader reader = new StreamReader(fileToLoad))
            {
                string LoadedJSON = reader.ReadToEnd();
                ServerSettings LoadedSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(LoadedJSON);
                serverSettings = LoadedSettings;
                DataContext = serverSettings;
            }
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
            string selectedMainServerUid = "";
            int selectedMainServerPort = 0;

            if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedIndex == -1)
            {
                var yesNoDialog = new ContentDialog()
                {
                    Content = "你选择了作为副服务器，请选择一个绑定的主服务器！",
                    PrimaryButtonText = "是",
                }.ShowAsync();
                return;
            }

            //if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedIndex != -1)
            //{

            //    // 检查是否选择了自身
            //    if (selectedMainServerUid == )
            //    {
            //        var warningDialog = new ContentDialog
            //        {
            //            Owner = this,
            //            Title = "无效选择",
            //            Content = "不能将自身设为主服务器，请重新选择。",
            //            PrimaryButtonText = "确定"
            //        };
            //        await warningDialog.ShowAsync();
            //        return;
            //    }

            //    selectedMainServerPort = GetMainServerPortByUniqueId(selectedMainServerUid);
            //}


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
                        string iniPath = Path.Combine(server.Path, "WS", "Saved", "Config", "WindowsServer", "Engine.ini");
                        
                        if (!Directory.Exists(settingFolderPath))
                            Directory.CreateDirectory(settingFolderPath);

                        if (File.Exists(settingPath))
                            File.Copy(settingPath, settingFolderPath + @"\ServerSettings.bak", true);

                        if (ClusterModeComboBox.SelectedIndex == 2 && MainServerListBox.SelectedIndex != -1)
                        {
                            if (MainServerListBox.SelectedItem is ServerListItem listItem)
                            {
                                selectedMainServerUid = listItem.Server.UniqueId;
                                selectedMainServerPort = listItem.Port;

                                serverSettings.MainPort = listItem.Port;
                                serverSettings.MainServerUniqueId = listItem.Server.UniqueId;
                            }
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

                        if (PVPSelectComboBox.SelectedIndex == 0)
                        {
                            serverSettings.PVP = false;
                        }
                        else
                            serverSettings.PVP = true;

                        if (ServerSettingsPresetComboBox.SelectedItem is PresetItem selectedPreset)
                        {
                            serverSettings.ServerPresetSettings = selectedPreset.FileName;
                        }
                        else
                        {
                            serverSettings.ServerPresetSettings = string.Empty; // 没选择就清空
                        }

                        IniWriteName(serverSettings, iniPath);
                        SaveServerSettings(settingPath, serverSettings);
                        RefreshMainServerList();

                        var closeFileDialog = new ContentDialog()
                        {
                            Content = "文件成功保存于：\n" + settingPath,
                            PrimaryButtonText = "是",
                        }.ShowAsync();

                        serverSettings = LoadServerSettings(settingPath);
                        MainWindow.StartBackupCleanTimer(server, serverSettings.AutoCleanInterval, serverSettings.AutoSaveCount);
                        LoadSettingsFileSafe(server);
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

            try
            {
                string SettingsJSON = System.Text.Json.JsonSerializer.Serialize(serverSettings, serializerOptions);
                SaveFileDialog SaveSettingsDialog = new SaveFileDialog
                {
                    Filter = "\"JSON files\"|*.json",
                    DefaultExt = "json",
                    FileName = "ServerSettings.json",
                    InitialDirectory = Directory.GetCurrentDirectory()
                };
                if (SaveSettingsDialog.ShowDialog() == true)
                {
                    if (File.Exists(SaveSettingsDialog.FileName))
                    {
                        File.Copy(SaveSettingsDialog.FileName, SaveSettingsDialog.FileName + ".bak", true);
                    }
                    File.WriteAllText(SaveSettingsDialog.FileName, SettingsJSON, System.Text.Encoding.UTF8);
                    await Task.Delay(1000);
                    MessageBox.Show("文件成功保存于：\n" + SaveSettingsDialog.FileName);
                }
            }
            catch (Exception)
            {
                throw;
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
                                Port = settings.Port
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
            MainSettings mainSettings = MainSettings.LoadManagerSettings();
            foreach (var server in mainSettings.Servers)
            {
                if (server.UniqueId == uniqueId)
                {
                    string serverSaveDataPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                    string json = File.ReadAllText(serverSaveDataPath).Trim();

                    ServerSettings readServerSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                    return readServerSettings.Port;
                }
            }
            return -1;
        }

        private string GetMainServerNameByUniqueId(string uniqueId)
        {
            MainSettings mainSettings = MainSettings.LoadManagerSettings();
            foreach (var server in mainSettings.Servers)
            {
                if (server.UniqueId == uniqueId)
                {
                    string serverSaveDataPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                    string json = File.ReadAllText(serverSaveDataPath).Trim();

                    ServerSettings readServerSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                    return server.ssmServerName;
                }
            }
            return null;
        }
        
        private string GetMainServerFolderByUniqueId(string uniqueId)
        {
            mainSettings = MainSettings.LoadManagerSettings();
            foreach (var server in mainSettings.Servers)
            {
                if (server.UniqueId == uniqueId)
                {
                    return server.Path;
                }
            }
            return null;
        }

        private string GetMainServerUniqueIdByName(string serverName)
        {
            MainSettings mainSettings = MainSettings.LoadManagerSettings();
            foreach (var server in mainSettings.Servers)
            {
                if (server.ssmServerName == serverName)
                {
                    string serverSaveDataPath = Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json");
                    string json = File.ReadAllText(serverSaveDataPath).Trim();

                    ServerSettings readServerSettings = System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json);
                    return server.UniqueId;
                }
            }
            return null;
        }

        /// <summary>
        /// 读取 INI 值
        /// </summary>
        private string IniReadServerName(string iniPath)
        {
            string serverName = "";
            if (!File.Exists(iniPath))
            {
                MessageBox.Show("文件不存在");
                Close();
                return "";
            }

            _originalLines = File.ReadAllLines(iniPath).ToList();
            string section = "";

            foreach (var line in _originalLines)
            {
                var t = line.Trim();
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith(";")) continue;

                if (t.StartsWith("["))
                {
                    section = t.Trim('[', ']');
                    continue;
                }

                var kv = t.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                var val = kv[1].Trim();

                if (section == "Dedicated.Settings")
                {
                    if (key == "SteamServerName") 
                        serverName = val;
                }
            }
            return serverName;
        }

        /// <summary>
        /// 写入 INI 值
        /// </summary>
        private void IniWriteName(ServerSettings serverSettings, string iniPath)
        {
            try
            {
                var output = new List<string>();
                string currentSection = "";

                foreach (var line in _originalLines)
                {
                    var t = line.Trim();
                    if (t.StartsWith("[")) currentSection = t.Trim('[', ']');

                    // 完全保留 Core.System
                    if (currentSection == "Core.System")
                    {
                        output.Add(line);
                        continue;
                    }

                    // 跳过旧节点
                    if (currentSection is "Dedicated.Settings" or "URL" or "OnlineSubsystemSteam")
                        continue;

                    output.Add(line);
                }

                // 写入新节点
                output.Add("[Dedicated.Settings]");
                output.Add($"SteamServerName={serverSettings.SteamServerName}");
                output.Add($"MaxPlayers={serverSettings.MaxPlayers}");
                output.Add($"pvp={serverSettings.PVP}");
                output.Add($"backup={serverSettings.Backup}");
                output.Add($"saving={serverSettings.Saving}");

                File.WriteAllLines(iniPath, output);
            }
            catch { }
        }

        private async Task<string> GetPublicIPAsync()
        {
            try
            {
                string publicIP = "";
                using (HttpClient client = new HttpClient())
                {
                    publicIP = await client.GetStringAsync("http://api.ipify.cn");
                }
                return publicIP;
            }
            catch
            {
                return "fail";
            }
        }
    }
}
