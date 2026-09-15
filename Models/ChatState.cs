using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Models
{
    public enum ChatState
    {
        None, Initial, Connect, Disconnect,
        Message,
        FileStart,
        FileChunk,
        FileEnd,
        Ack,
        진행상황
    }
    public enum SendType
    {
        ServerToOne,
        ServerToAll,
        ClientToServer,
        ServerToOneFile,
        ServerToAllFile,
        ClientToServerFile
    }

    //Message 부터 추가
}
