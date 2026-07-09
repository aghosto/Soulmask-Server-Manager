using SoulmaskServerManager;
using SoulmaskServerManager.RCON;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoulmaskServerManager
{
    public class RconCommand
    {
        public string DisplayName { get; set; }

        public string Command { get; set; }

        public string ToolTip { get; set; }
    }

    /// <summary>
    /// Interaction logic for RconConsole.xaml
    /// </summary>
    public partial class RconConsole : Window
    {
        RemoteConClient rClient;
        Server _server;
        ServerSettings serverSettings;
        MainSettings MainSettings = MainSettings.LoadManagerSettings();
        public ObservableCollection<RconCommand> Commands { get; set; }

        public RconConsole(Server server)
        {
            InitializeComponent();
            _server = server;
            serverSettings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));

            Port.Value = serverSettings.Rcon.Port;
            Password.Text = _server.RconServerSettings.Password;
            Ipaddress.Text = _server.RconServerSettings.IPAddress;

            if (!string.IsNullOrEmpty(serverSettings.Rcon.Password))
                _server.RconServerSettings.Password = serverSettings.Rcon.Password;
            _server.RconServerSettings.Port = serverSettings.Rcon.Port;

            MainSettings.Save(MainSettings);

            DataContext = MainSettings;
            Commands = new ObservableCollection<RconCommand>
            {
                new RconCommand { DisplayName = "帮助", Command = "help", ToolTip = "列出服务器 RCON 所有命令，没有参数，一共3页，如要翻页需要选择空白指令发送2、3" },
                new RconCommand { DisplayName = "空白指令", Command = "", ToolTip = "发送空白指令，用于自定义发送" },
                new RconCommand { DisplayName = "系统聊天", Command = "say", ToolTip = "向所有在线玩家发送系统消息，可发送中文英文数字" },
                new RconCommand { DisplayName = "关闭服务器", Command = "shutdown", ToolTip = "在指定秒后保存并关闭服务器" },
                new RconCommand { DisplayName = "取消关闭", Command = "cancelclose", ToolTip = "取消关闭服务器的指令，只有在之前有设定关服指令并且倒计时还没结束才有用" },
                new RconCommand { DisplayName = "保存存档", Command = "sav", ToolTip = "不退出服务器的情况下马上保存最新的服务器存档，需指定一个参数为任意数字" },
                new RconCommand { DisplayName = "备份存档", Command = "backup", ToolTip = "备份服务器存档，参数为新存档的名称，保存在服务器存档同一位置" },
                new RconCommand { DisplayName = "按时间备份存档", Command = "bkh", ToolTip = "备份服务器存档，直接命名为当下的UTC时间，保存在服务器存档同一位置" },
                new RconCommand { DisplayName = "查看所有角色位置", Command = "dap", ToolTip = "导出所有角色（包括npc）目前所在位置，保存位置为Saved/ACTOR_POSI_DATA.log" },
                new RconCommand { DisplayName = "绘制角色位置位图", Command = "dai", ToolTip = "导出角色所在位置为bmp图片（没啥用）" },
                new RconCommand { DisplayName = "服务器邀请码", Command = "qi", ToolTip = "查看服务器邀请码" },
                new RconCommand { DisplayName = "服务器FPS", Command = "fps", ToolTip = "查询服务器短时间内的平均帧率" },
                new RconCommand { DisplayName = "服务器登录设置", Command = "sl", ToolTip = "继续/暂停玩家登录游戏，参数0表示禁止新玩家登录游戏，1表示允许玩家继续登录" },
                new RconCommand { DisplayName = "在线玩家", Command = "lp", ToolTip = "列出当前服务器在线玩家" },
                new RconCommand { DisplayName = "所有玩家", Command = "lap", ToolTip = "列出服务器所有玩家" },
                new RconCommand { DisplayName = "列出相同归属对象", Command = "ls", ToolTip = "列出具有相同归属对象的项（参数为玩家账号或玩家控制角色的uid）" },
                new RconCommand { DisplayName = "传送角色到点", Command = "go", ToolTip = "指定玩家的用户ID/账号，并将玩家传送到坐标(x,y,z)附近的位置" },
                new RconCommand { DisplayName = "传送角色到角色", Command = "gonpc", ToolTip = "指定玩家的 uid/账号，将该玩家传送到指定玩家/NPC（通过名称、uid或账号指定）的位置" },
                new RconCommand { DisplayName = "创建奴隶", Command = "cnpc", ToolTip = "给定玩家账号/控制角色的uid，创建具有指定属性的野蛮人。如xxxxxx（steamid） 1（野人预设，越接近0熟练度有概率更高） 0（野人性别，有部分没有女性）" },
                new RconCommand { DisplayName = "创建动物", Command = "create", ToolTip = "指定一个玩家，把参数指定的生物创建给该玩家，参数如：xxxxxx（steamid） 生物蓝图 is_baby 等级 数量 品质（不如直接在游戏里创建实用" },
                new RconCommand { DisplayName = "飞行模式", Command = "fly", ToolTip = "指定一个玩家，使其变成幽灵模式（可穿墙），如xxxxxx（steamid） 1（1开0关）" },
                new RconCommand { DisplayName = "输出聊天", Command = "soc", ToolTip = "将世界聊天、附近聊天和公会聊天的内容输出到日志文件，1开0关" },
                new RconCommand { DisplayName = "清除全图野人", Command = "can", ToolTip = "清除全图没有归属的野人，没有参数" },
            };

            CommandList.ItemsSource = Commands;
            CommandList.SelectedIndex = 0;

            RconConsoleOutput.AppendText("远程控制(RCON)客户端已就绪。");
        }

        private void LogToConsole(string output)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                RconConsoleOutput.AppendText("\r" + output);
                RconConsoleOutput.ScrollToEnd();
            }));
        }

        private void Connect()
        {
            rClient = new()
            {
                UseUtf8 = true
            };
            Dispatcher.Invoke(new Action(() =>
            {
                ConnectButton.IsEnabled = false;
            }));
            rClient.OnLog += message =>
            {
                LogToConsole(message);
            };
            rClient.OnConnectionStateChange += state =>
            {
                if (state == RemoteConClient.ConnectionStateChange.Connected)
                {
                    rClient.Authenticate(_server.RconServerSettings.Password);
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = true;
                        SendCommandButton.IsEnabled = true;
                    }));
                }
                if (state == RemoteConClient.ConnectionStateChange.Disconnected)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("已连接。");
                }
                if (state == RemoteConClient.ConnectionStateChange.ConnectionLost)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("连接丢失。");
                }
                if (state == RemoteConClient.ConnectionStateChange.ConnectionTimeout)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole("连接超时。");
                }
                if (state == RemoteConClient.ConnectionStateChange.NoConnection)
                {
                    Dispatcher.Invoke(new Action(() =>
                    {
                        DisconnectButton.IsEnabled = false;
                        ConnectButton.IsEnabled = true;
                        ParamaterTextbox.IsEnabled = false;
                        SendCommandButton.IsEnabled = false;
                    }));
                    LogToConsole($"未能连接至 {_server.RconServerSettings.IPAddress}:{_server.RconServerSettings.Port}。");
                }
            };
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Connect();
                rClient.Connect(_server.RconServerSettings.IPAddress, _server.RconServerSettings.Port);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (rClient.Connected)
            {
                rClient.Disconnect();
            }
        }

        private void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (CommandList.SelectedItem is RconCommand cmd && rClient?.Connected == true)
            {
                try
                {
                    string fullCommand = $"{cmd.Command} {ParamaterTextbox.Text}".Trim();
                    rClient.SendCommand(fullCommand, res => { LogToConsole(res); });
                    LogToConsole($"已发送：{cmd.DisplayName}，参数：{ParamaterTextbox.Text}");
                }
                catch
                {
                    LogToConsole("发送失败，请检查连接。");
                }
            }
            else
            {
                LogToConsole("发送失败，请确认已连接并选择指令。");
            }
        }
    }
}
