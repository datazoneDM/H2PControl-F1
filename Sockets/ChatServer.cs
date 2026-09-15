using H2PControl.Events;
using H2PControl.Handlers;
using H2PControl.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Sockets
{
    public class ChatServer : ChatBase
    {
        private readonly TcpListener _listener;

        public override event EventHandler<ChatEventArgs> Connected;
        public override event EventHandler<ChatEventArgs> Disconnected;
        public override event EventHandler<ChatEventArgs> Received;

        public static readonly List<ClientHandler> _clients = new List<ClientHandler>();  //양방향 추가

        public ChatServer(IPAddress iPAddress, int port) : base(iPAddress, port)
        {
            _listener = new TcpListener(iPAddress, port);
        }
        public async Task StartAsync()
        {
            if (IsRunning)
                return;

            try
            {
                //재시작시 포트 재사용
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _listener.Start();
                IsRunning = true;
                Debug.Print("서버 시작");

                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Debug.Print($"클라이언트 연결 수락: {client.Client.Handle}");

                    ClientHandler clientHandler = new ClientHandler(client, true);

                    // 연결된 클라이언트를 리스트에 추가
                    _clients.Add(clientHandler);
                    // 연결 해제 시 자동 제거
                    clientHandler.Disconnected += (s, e) =>
                    {
                        _clients.Remove(e.ClientHandler);
                        Debug.Print($"클라이언트 연결 해제: {client.Client.Handle}");
                        OnClientDisconnected(e);
                    };

                    clientHandler.Connected += Connected;
                    clientHandler.Received += Received;
                    _ = clientHandler.HandleClientAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"서버 시작 중 오류 발생: {ex.Message}");
                IsRunning = false;
            }
        }
        protected virtual void OnClientDisconnected(ChatEventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }

        public void Stop()
        {
            IsRunning = false;
            _listener.Stop();
            Debug.Print("서버 정지");
        }



        // ============================================================
        // 🔹  서버 ===> 클라이언트 송신
        // ============================================================

        public async Task 서버To클라이언트송신(string targetUser, ChatHub hub)
        {
            try
            {
                var targets = new List<ClientHandler>();

                // 송신 대상 분기
                switch (hub.SendType)
                {
                    case SendType.ServerToAll:
                    case SendType.ServerToAllFile:
                        targets = ChatServer._clients.ToList();
                        break;

                    case SendType.ServerToOne:
                    case SendType.ServerToOneFile:
                        var t = ChatServer._clients.FirstOrDefault(c => c.InitialData?.UserName == targetUser);
                        if (t != null)
                            targets.Add(t);
                        break;

                    default:
                        Console.WriteLine("[서버] 잘못된 송신 타입입니다.");
                        return;
                }

                foreach (var client in targets)
                {
                    switch (hub.SendType)
                    {
                        case SendType.ServerToAll:
                        case SendType.ServerToOne:
                            client.Send(hub); // 메시지 전송
                            break;

                        case SendType.ServerToAllFile:
                        case SendType.ServerToOneFile:
                            // 파일 전송
                            if (!string.IsNullOrEmpty(hub.FileFrom))
                            {
                                string 원본폴더 = hub.FileFrom;
                                string 필터 = hub.FileFilter;
                                var 원본파일들 = Directory.GetFiles(원본폴더, 필터);
                                await client.파일전송(원본파일들, hub.RoomId, hub.UserName, hub.플레그);
                            }

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[서버 송신 오류] {ex.Message}");
            }
        }
    }


}
