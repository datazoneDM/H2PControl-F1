using H2PControl.Handlers;
using H2PControl.Models;
using System;

namespace H2PControl.Events
{
    public class ChatEventArgs : EventArgs
    {
        public ChatEventArgs(ClientHandler clientHandler, ChatHub hub)
        {
            ClientHandler = clientHandler;
            Hub = hub;
        }

        public ClientHandler ClientHandler { get; }
        public ChatHub Hub { get; }
    }
}
