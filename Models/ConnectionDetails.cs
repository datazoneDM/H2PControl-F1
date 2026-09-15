using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Models
{
    public class ConnectionDetails
    {
       // public int UserId { get; set; }
        public string UserName { get; set; }
        public int RoomId { get; set; }

        //추가
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public long ChunkIndex { get; set; }
        public byte[] FileData { get; set; }
        public SendType SendType { get; set; }
        public string FileFrom { get; set; }
        public string FileFilter { get; set; }
        public string 플레그 { get; set; }
        public string 상황 { get; set; }
        public override string ToString()
        {
            return $"RoomId: {RoomId}, UserName: {UserName}";
        }

    }
}
