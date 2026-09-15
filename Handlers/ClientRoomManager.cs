using H2PControl.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Handlers
{
    public class ClientRoomManager
    {
        private Dictionary<int, List<ClientHandler>> _roomHandlersDict = new Dictionary<int, List<ClientHandler>>();

        public void Add(ClientHandler clientHandler)
        {
            int roomId = clientHandler.InitialData.RoomId;

            if (_roomHandlersDict.TryGetValue(roomId, out _))
            {
                _roomHandlersDict[roomId].Add(clientHandler);
            }
            else
            {
                _roomHandlersDict[roomId] = new List<ClientHandler>() { clientHandler };
            }
        }

        public void Remove(ClientHandler clientHandler)
        {
            int roomId = clientHandler.InitialData.RoomId;

            if (_roomHandlersDict.TryGetValue(roomId, out List<ClientHandler> roomHandlers))
            {
                _roomHandlersDict[roomId] = roomHandlers.FindAll(handler => !handler.Equals(clientHandler));
            }
        }

        public void SendToMyRoom(ChatHub hub)
        {
            if (_roomHandlersDict.TryGetValue(hub.RoomId, out List<ClientHandler> roomHandlers))
            {
                roomHandlers.ForEach(handler => handler.Send(hub));
            }
        }

        public void DisconnectAllClients()
        {
            // 모든 방을 돌면서
            foreach (var roomList in _roomHandlersDict.Values)
            {
                // 방 안의 모든 클라이언트 연결선을 직접 끊어버림
                foreach (var handler in roomList)
                {
                    handler.ForceDisconnect();
                }
            }
            // 방명록 완전 백지화
            _roomHandlersDict.Clear();
        }

        public void RemoveOldClient(string userName)
        {
            // 모든 방(RoomId)을 하나씩 뒤져봅니다.
            foreach (var roomHandlers in _roomHandlersDict.Values)
            {
                // 해당 방에서 이름(UserName)이 똑같은 놈들을 모조리 찾습니다.
                // (주의: 컬렉션을 수정할 때는 반드시 .ToList()로 복사본을 떠서 순회해야 에러가 안 납니다)
                var zombies = roomHandlers.Where(h => h.InitialData != null && h.InitialData.UserName == userName).ToList();

                foreach (var zombie in zombies)
                {
                    // 1. 좀비의 TCP 연결선(Socket)을 직접 가위로 잘라버립니다! (매우 중요)
                    zombie.ForceDisconnect();

                    // 2. 방명록 리스트에서 완전히 지워버립니다.
                    roomHandlers.Remove(zombie);
                }
            }
        }
    }
}

