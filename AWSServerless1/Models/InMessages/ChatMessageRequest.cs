using System;
using System.Collections.Generic;
using System.Text;

namespace AWSServerless1.Models.InMessages
{
    public class ChatMessageRequest
    {
        public string UserID { get; set; }
        public string RoomID { get; set; }
        public string Message { get; set; }
    }
}
