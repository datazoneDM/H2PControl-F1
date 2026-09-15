using H2PControl.Events;
using H2PControl.Handlers;
using H2PControl.Models;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace H2PControl.Sockets
{
    public class ChatClient : ChatBase
    {
        private TcpClient _client;

        private ChatHub ConvertChatHub(ConnectionDetails details)
        {
            return new ChatHub
            {
                //UserId = details.UserId,
                UserName = details.UserName,
                RoomId = details.RoomId,
                State = ChatState.Initial,
            };
        }

        public override event EventHandler<ChatEventArgs> Connected;
        public override event EventHandler<ChatEventArgs> Disconnected;
        public override event EventHandler<ChatEventArgs> Received;


        public ChatClient(IPAddress iPAddress, int port) : base(iPAddress, port)
        {

        }
        public async Task ConnectAsync(ConnectionDetails details)
        {
            if (IsRunning) return;

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(IPAddress, Port);
                IsRunning = true;

                ChatHub hub = ConvertChatHub(details);
                ClientHandler clientHandler = new ClientHandler(_client);
                Connected?.Invoke(this, new ChatEventArgs(clientHandler, hub));
                clientHandler.Disconnected += ClientHandler_Disconnected;
                clientHandler.Received += Received;

                _ = clientHandler.HandleClientAsync();
                clientHandler?.Send(hub);
            }
            catch (Exception ex)
            {
                DisposeClient();
                Debug.Print($"서버 연결 시도 중 오류 발생: {ex.Message}");
            }
        }

        private void DisposeClient()
        {
            _client?.Dispose();
            IsRunning = false;
        }

        private void ClientHandler_Disconnected(object sender, ChatEventArgs e)
        {
            DisposeClient();
            Disconnected?.Invoke(sender, e);
        }

        public void Close()
        {
            DisposeClient();
        }

    }
}
