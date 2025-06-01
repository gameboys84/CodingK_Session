using System;
using System.Threading.Tasks;
using CodingK_Session;
using proto.test;

namespace test.ClientSession
{
    /// <summary>
    /// 控制台模拟客户端
    /// </summary>
    class ClientStart
    {
        public const string ip = "127.0.0.1";
        public const int port = 17666;

        static CodingK_Net<ClientSession, NetMsg> client;
        static Task<bool> checkTask;

        static bool isTryToConnecting = true; // 正在尝试连接中
        static bool isReconnecting = false; // 正在重连中
        static int reconnectFailedCount = 0; // 重连超过一定次数就放弃
        static bool needReconnect = false; // 是否需要重连


        static void Main(string[] args)
        {
            ConnectServer();

            while (true)
            {
                if (client.IsConnected())
                {
                    string input = Console.ReadLine();

                    // 输完后重新检测一次
                    if (client.IsConnected())
                    {
                        if (input == "quit")
                        {
                            client.CloseClient();
                            break;
                        }
                        else if (input == "login")
                        {
                            client.clientSession.SendMsg(new NetMsg
                            {
                                Cmd = CMD.ReqLogin,
                                ReqLogin = new ReqLogin
                                {
                                    Acct = "test1",
                                    Psd = "test2",
                                }
                            });
                        }
                        else
                        {
                            client.clientSession.SendMsg(new NetMsg
                            {
                                Info = input,
                            });
                        }
                    }
                    else
                    {
                        needReconnect = true;
                    }
                }
                else
                {
                    needReconnect = true;
                }

                if (needReconnect)
                {
                    needReconnect = false;
                    if (CanReconnect())
                    { 
                        Reconnected(); 
                    }
                    else
                    {
                        CodingK_SessionTool.ColorLog(CodingK_LogColor.Red, "No need to Reconnect. reconnectFailedCount:{0}", reconnectFailedCount);
                        break;
                    }
                }
            }

            CodingK_SessionTool.ColorLog(CodingK_LogColor.Green, "Application Quit.");
            Console.ReadKey();
        }

        private static int linkCounter;
        static async Task ConnectCheck()
        {
            while (true)
            {
                await Task.Delay(3000);
                if (checkTask != null && checkTask.IsCompleted)
                {
                    // 尝试连接已经有结果了
                    isTryToConnecting = false;

                    // checkTask是指在尝试连接服务器的时候，每200ms检测一次连接状态，如果5s内还没连接成功，就认为是连接失败
                    if (checkTask.Result && client.IsConnected())
                    {
                        CodingK_SessionTool.ColorLog(CodingK_LogColor.Green, "ConnectServer Success.");
                        reconnectFailedCount = 0;
                        linkCounter = 0;
                        checkTask = null;
                        isReconnecting = false;

                        await Task.Run(SendPingMsg);
                    }
                    else
                    {
                        // 由于已经延迟了3秒才检测，因此只检测一次就够了,如果需要检测多次可以增加次数
                        if (++linkCounter > 0)
                        {
                            if (CanReconnect())
                            {
                                CodingK_SessionTool.Error($"Connect check failed with {linkCounter} times, Wait for Reconnecting, tried times:{reconnectFailedCount}.");
                            }
                            reconnectFailedCount++;
                            linkCounter = 0;
                            checkTask = null;
                            isReconnecting = false;

                            needReconnect = true;
                            break;
                        }
                        else
                        {
                            CodingK_SessionTool.Error($"Connect failed {linkCounter} Times, wait for next check...");
                            //checkTask = client.ConnectServer(200, 500);
                        }
                    }
                }
                else if (checkTask == null || checkTask.IsCanceled)
                {
                    CodingK_SessionTool.Warn($"checkTask is null or isCanceled. {0}", (checkTask != null && checkTask.IsCanceled) ? ("Canceled, Id:" + checkTask.Id) : "null");
                    break;
                }

            }
        }

        static async Task SendPingMsg()
        {
            while (true)
            {
                await Task.Delay(5000);
                // 为避免客户端不知道网络状态时，一直尝试发送，这里新增如果已经断开，就中断循环，避免此Task一直运行中， 如果需要更准确的知道状态，还是由服务器对心跳进行回应，以此判断是否断开了连接
                if (client != null && client.clientSession != null)
                {
                    if (client.IsConnected() == false)
                    {
                        CodingK_SessionTool.ColorLog(CodingK_LogColor.Red, "Client is Disconnected. Try to Reconnect");
                        needReconnect = true;
                        break;
                    }

                    client.clientSession.SendMsg(new NetMsg
                    {
                        Cmd = CMD.Ping,
                        Ping = new proto.test.Ping
                        {
                            IsOver = false,
                        }
                    });

                    CodingK_SessionTool.ColorLog(CodingK_LogColor.Green, "Client Send Ping Msg. sid:{0}", client.clientSession.GetSessionID());
                }
                else if (client == null || client.clientSession == null)
                {
                    CodingK_SessionTool.ColorLog(CodingK_LogColor.Green, "Ping Task Cancel. client:{0}", (client == null ? "is null" : "session is null"));
                    break;
                }
            }
        }

        static void ConnectServer()
        {
            isTryToConnecting = true;
            if (client != null)
            {
                client.CloseClient();
                client = null;
            }
            client = new CodingK_Net<ClientSession, NetMsg>();
            client.StartAsClient(ip, port, CodingK_ProtocolMode.Proto);
            checkTask = client.ConnectServer(200, 5000);
            Task.Run(ConnectCheck);
        }

        /// <summary>
        /// 是否可以尝试重连
        /// </summary>
        /// <returns></returns>
        public static bool CanReconnect()
        {
            // isTryToConnecting || isReconnecting ||
            if (reconnectFailedCount >= 4)
                return false;

            return true;
        }

        public static void Reconnected()
        {
            if (!isTryToConnecting && !isReconnecting)
            {
                // 尝试重连时，应该提供旧的数据由服务器进行快速验证，并回应重连结果，方便C/S双方更新连接状态
                if (CanReconnect())
                {
                    // 尝试重连
                    CodingK_SessionTool.ColorLog(CodingK_LogColor.Green, "Reconnecting...");
                    isReconnecting = true;

                    ConnectServer();
                }
                else
                {
                    CodingK_SessionTool.Error($"Reconnected failed too many times, Check your Network:reconnectFailedCount{reconnectFailedCount}.");
                }
            }
        }
    }
}
