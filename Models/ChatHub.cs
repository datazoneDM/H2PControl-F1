using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace H2PControl.Models
{
    public class ChatHub : ConnectionDetails
    {
        public static ChatHub Parse(string json)
        {
            return JsonConvert.DeserializeObject<ChatHub>(json);
        }


        public ChatState State { get; set; }
        public string Message { get; set; } = string.Empty;

        //데이터 직열화 ex) { "State": 1, "Message": "hello" }
        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
