using ModernWpf;
using RCONServerLib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoulmaskServerManager.RCON
{
    public class RemoteConClient
    {
        private const int CONNECT_TIMEOUT_MS = 5_000;
        private const int READ_TIMEOUT_MS = 1_00;

        /// <summary>
        /// </summary>
        /// <param name="success">If the authentication request was successful or not</param>
        public delegate void AuthEventHandler(bool success);

        /// <summary>
        /// </summary>
        /// <param name="result">A string containing the answer of the server</param>
        public delegate void CommandResult(string result);

        /// <summary>
        /// </summary>
        /// <param name="type">The type of the change</param>
        public delegate void ConnectionEventHandler(ConnectionStateChange type);

        /// <summary>
        /// </summary>
        /// <param name="message">The message we want to log</param>
        public delegate void LogEventHandler(string message);

        public enum ConnectionStateChange
        {
            Connected,
            Disconnected,
            NoConnection,
            ConnectionTimeout,
            ConnectionLost
        }

        private const int MaxAllowedPacketSize = 4096;

        /// <summary>
        ///     A list containing all requested commands for event handling
        /// </summary>
        private readonly Dictionary<int, CommandResult> _requestedCommands;

        /// <summary>
        ///     A buffer containing the packet
        /// </summary>
        private byte[] _buffer;

        /// <summary>
        ///     The TCP Client
        /// </summary>
        private TcpClient _client;

        /// <summary>
        ///     Underlaying NetworkStream
        /// </summary>
        private NetworkStream _ns;

        /// <summary>
        ///     Current packetId we're on
        /// </summary>
        private int _packetId;

        /// <summary>
        ///     If the client is authenticated
        /// </summary>
        public bool Authenticated;

        public RemoteConClient()
        {
            _client = new TcpClient();

            _packetId = 0;
            _requestedCommands = new Dictionary<int, CommandResult>();

            UseUtf8 = true;
        }

        /// <summary>
        ///     Wether or not the TcpClient is still connected
        /// </summary>
        public bool Connected
        {
            get { return _client.Connected; }
        }

        /// <summary>
        ///     Whether to use UTF8 to encode the packet payload
        /// </summary>
        public bool UseUtf8 { get; set; }

        /// <summary>
        ///     An event handler when the result of the authentication is received
        /// </summary>
        public event AuthEventHandler OnAuthResult;

        /// <summary>
        ///     An event handler when the class wants to log something
        ///     Supressed when empty.
        /// </summary>
        public event LogEventHandler OnLog;

        /// <summary>
        ///     An event handler when the connection state changes
        ///     Ex. when disconnected or connection is lost
        /// </summary>
        public event ConnectionEventHandler OnConnectionStateChange;

        private async Task<TcpClient> ConnectAsync(string host, int port)
        {
            var client = new TcpClient { NoDelay = true };
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(CONNECT_TIMEOUT_MS)) != connectTask)
            {
                client.Dispose();
                throw new TimeoutException($"EchoPort connection to {host}:{port} timed out.");
            }
            if (connectTask.IsFaulted)
            {
                client.Dispose();
                throw connectTask.Exception?.InnerException ?? new IOException("EchoPort connection failed.");
            }
            return client;
        }

        /// <summary>
        ///     Connects to the specified RCON Server
        /// </summary>
        /// <param name="hostname">The hostname of the RCON Server</param>
        /// <param name="port">The port to connect to</param>
        public void Connect(string hostname, int port)
        {
            Log(string.Format("正在连接 {0}:{1}", hostname, port));
            try
            {
                IAsyncResult asyncResult = null;
                try
                {
                    asyncResult = _client.BeginConnect(hostname, port, null, null);
                }
                catch (ObjectDisposedException)
                {
                    _client = new TcpClient();
                    try
                    {
                        asyncResult = _client.BeginConnect(hostname, port, null, null);
                    }
                    catch (Exception)
                    {
                        Log("未知错误。");
                    }
                }

                if (asyncResult == null)
                {
                    Log("异步连接失败！");
                    return;
                }

                asyncResult.AsyncWaitHandle.WaitOne(2000); // wait 2 seconds
                if (!asyncResult.IsCompleted)
                {
                    if (OnConnectionStateChange != null) OnConnectionStateChange(ConnectionStateChange.NoConnection);
                    _client.Client.Close();
                }
            }
            catch (SocketException)
            {
                if (OnConnectionStateChange != null)
                {
                    OnConnectionStateChange(ConnectionStateChange.ConnectionTimeout);
                    _client.Client.Close();
                }

                return;
            }

            if (!_client.Connected) 
                return;
            _ns = _client.GetStream();

            // As indicated by specification the maximum packet size is 4096
            // NOTE: Not sure if only the server is allowed to sent packets with max 4096 or both parties!
            _buffer = new byte[MaxAllowedPacketSize];
            _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);

            Log("已连接");
            if (OnConnectionStateChange != null)
                OnConnectionStateChange(ConnectionStateChange.Connected);
        }

        /// <summary>
        ///     Outputs a log to <see cref="OnLog" />
        /// </summary>
        /// <param name="message"></param>
        private void Log(string message)
        {
            if (OnLog != null)
                OnLog(message);
        }

        /// <summary>
        ///     Disconnects the client from the server
        /// </summary>
        public void Disconnect()
        {
            if (_client.Connected)
            {
                _client.Client.Disconnect(false);
                if (OnConnectionStateChange != null)
                    OnConnectionStateChange(ConnectionStateChange.Disconnected);
            }

            _client.Close();
        }

        /// <summary>
        ///     Sends the authentication to the server
        /// </summary>
        /// <param name="password">RCON Password</param>
        public void Authenticate(string password)
        {
            _packetId++;
            var packet = new RemoteConPacket(_packetId, RemoteConPacket.PacketType.Auth, password, UseUtf8);
            SendPacket(packet);
        }

        /// <summary>
        ///     Sends a RCON Command to the server
        /// </summary>
        /// <param name="command">The RCON command with parameters</param>
        /// <param name="resultFunc">A function that will be executed after the server has processed the request</param>
        /// <exception cref="NotAuthenticatedException">If we're not authenticated</exception>
        public void SendCommand(string command, CommandResult resultFunc)
        {
            if (!_client.Connected)
                return;

            if (!Authenticated)
                throw new NotAuthenticatedException();

            _packetId++;
            _requestedCommands.Add(_packetId, resultFunc);

            var packet = new RemoteConPacket(_packetId, RemoteConPacket.PacketType.ExecCommand, command, UseUtf8);
            SendPacket(packet);
        }

        /// <summary>
        ///     Sends the specified packet to the client
        /// </summary>
        /// <param name="packet">The packet to send</param>
        /// <exception cref="Exception">Not connected</exception>
        private void SendPacket(RemoteConPacket packet)
        {
            if (_client == null || !_client.Connected)
                throw new Exception("Not connected.");

            var packetBytes = packet.GetBytes();

            try
            {
                _ns.BeginWrite(packetBytes, 0, packetBytes.Length - 1, ar => { _ns.EndWrite(ar); }, null);
            }
            catch (ObjectDisposedException)
            {
            } // Do not write to NetworkStream when it's closed.
            catch (IOException)
            {
            } // Do not write to Socket when it's closed.
        }

        /// <summary>
        /// </summary>
        /// <param name="result"></param>
        private void OnPacket(IAsyncResult result)
        {
            try
            {
                var bytesRead = _ns.EndRead(result);
                if (!_client.Connected)
                {
                    if (OnConnectionStateChange != null)
                        OnConnectionStateChange(ConnectionStateChange.ConnectionLost);
                    return;
                }

                if (bytesRead == 0)
                {
                    _buffer = new byte[MaxAllowedPacketSize];
                    _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);
                    return;
                }

                Array.Resize(ref _buffer, bytesRead);

                ParsePacket(_buffer);

                if (!_client.Connected)
                {
                    if (OnConnectionStateChange != null)
                        OnConnectionStateChange(ConnectionStateChange.ConnectionLost);
                    return;
                }

                _buffer = new byte[MaxAllowedPacketSize];
                _ns.BeginRead(_buffer, 0, MaxAllowedPacketSize, OnPacket, null);
            }
            catch (IOException)
            {
                if (OnConnectionStateChange != null)
                    OnConnectionStateChange(ConnectionStateChange.ConnectionLost);
                Disconnect();
            }
            catch (ObjectDisposedException)
            {
                if (OnConnectionStateChange != null)
                    OnConnectionStateChange(ConnectionStateChange.ConnectionLost);
                Disconnect();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Log(e.ToString());
            }
        }

        /// <summary>
        ///     Parses raw bytes to RemoteConPacket
        /// </summary>
        /// <param name="rawPacket"></param>
        private void ParsePacket(byte[] rawPacket)
        {
            try
            {
                var packet = new RemoteConPacket(rawPacket, UseUtf8);
                if (!Authenticated)
                {
                    // ExecCommand is AuthResponse too.
                    if (packet.Type == RemoteConPacket.PacketType.ExecCommand)
                    {
                        if (packet.Id == -1)
                        {
                            Log("验证失败。");
                            Authenticated = false;
                        }
                        else
                        {
                            Log("验证成功。");
                            Authenticated = true;
                        }

                        if (OnAuthResult != null)
                            OnAuthResult(Authenticated);
                    }

                    return;
                }

                if (_requestedCommands.ContainsKey(packet.Id) &&
                    packet.Type == RemoteConPacket.PacketType.ResponseValue)
                    _requestedCommands[packet.Id](packet.Payload);
                else
                    Log("带有无效ID的数据包 " + packet.Id);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Log(e.ToString());
            }
        }

        private static List<PlayerInfo> ParsePlayers(string response)
        {
            var players = new List<PlayerInfo>();
            if (string.IsNullOrWhiteSpace(response)) 
                return players;

            // | Account | PlayerName | PawnID | Position |
            // | 76561197993213308 | 'Sibercat' | 8XRQ... | V(X=...) |
            bool foundHeader = false;
            foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith('|')) continue;

                var cols = trimmed.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 2) continue;

                if (cols[0].Trim().Equals("Account", StringComparison.OrdinalIgnoreCase))
                {
                    foundHeader = true;
                    continue;
                }

                if (!foundHeader) continue;

                string SteamId = cols[0].Trim();
                string CharacterName = cols[1].Trim().Trim('\'');
                players.Add(new PlayerInfo(CharacterName, SteamId));
            }

            return players;
        }

        #region EchoPortCommand

        // 直接使用监听端口对服务器进行发送指令通信

        public async Task<List<PlayerInfo>?> GetPlayersAsync(string host, int port)
        {
            var response = await ExecuteAsync(host, port, "lp");
            return response == null ? null : ParsePlayers(response);
        }

        public Task<bool> SaveWorldAsync(string host, int port) =>
            ExecuteAndCheck(host, port, "SaveWorld 0");

        public Task<bool> ShutdownAsync(string host, int port, int delaySeconds = 10) =>
            ExecuteAndCheck(host, port, $"shutdown {delaySeconds}");

        public Task<bool> CancelShutdownAsync(string host, int port) =>
            ExecuteAndCheck(host, port, "cc");

        public Task<bool> BanPlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 1 1 {steamId}");

        public Task<bool> UnbanPlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 1 0 {steamId}");

        public Task<bool> MutePlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 4 1 {steamId}");

        public Task<bool> UnmutePlayerAsync(string host, int port, string steamId) =>
            ExecuteAndCheck(host, port, $"usp 4 0 {steamId}");

        private async Task<bool> SendRestartAnnounceToSingleServer(string host, int port, string password, string text)
        {
            try
            {
                var client = new RemoteConClient();
                client.Connect(host, port);
                await Task.Delay(100);

                var authTcs = new TaskCompletionSource<bool>();
                client.OnAuthResult += success => authTcs.TrySetResult(success);
                client.Authenticate(password);

                bool authOk = await authTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (!authOk)
                {
                    client.Disconnect();
                    return false;
                }

                var cmdTcs = new TaskCompletionSource<string>();
                client.SendCommand($"Say {text}", res => cmdTcs.TrySetResult(res));
                await cmdTcs.Task.WaitAsync(TimeSpan.FromSeconds(1));

                client.Disconnect();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task SendRestartAnnounceToSingleServer(Server server, string msg)
        {
            try
            {
                var settings = ServerSettingsEditor.LoadServerSettings(Path.Combine(server.Path, "SaveData", "Settings", "ServerSettings.json"));
                var rcon = new RemoteConClient();
                await rcon.SendRestartAnnounceToSingleServer("127.0.0.1", settings.Rcon.Port, settings.Password, msg);
            }
            catch
            { }
        }

        // 只有在游戏中踢人的指令，在游戏外只能通过先加入黑名单再把他从黑名单移除的方法模拟踢人操作
        public async Task<bool> KickPlayerAsync(string host, int port, string steamId)
        {
            await ExecuteAsync(host, port, $"usp 1 1 {steamId}"); 
            await Task.Delay(500);
            await ExecuteAsync(host, port, $"usp 1 0 {steamId}"); 
            return true;
        }


        public async Task<string?> ExecuteAsync(string host, int port, string command)
        {
            try
            {
                using var client = await ConnectAsync(host, port);
                var stream = client.GetStream();
                stream.WriteTimeout = READ_TIMEOUT_MS;

                byte[] cmd = Encoding.UTF8.GetBytes(command + "\r\n");
                await stream.WriteAsync(cmd);
                await stream.FlushAsync();

                var buf = new byte[4096];
                var sb = new StringBuilder();
                // NetworkStream.ReadTimeout does not apply to ReadAsync — use CancellationToken instead.
                using var cts = new CancellationTokenSource(READ_TIMEOUT_MS);
                try
                {
                    int n;
                    while ((n = await stream.ReadAsync(buf, cts.Token)) > 0)
                        sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                }
                catch (OperationCanceledException) { /* read timeout = end of response */ }
                catch (IOException) { /* connection closed by server */ }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private async Task<bool> ExecuteAndCheck(string host, int port, string cmd)
        {
            var result = await ExecuteAsync(host, port, cmd);
            return result != null;
        }

        #endregion EchoPortCommand

    }
}