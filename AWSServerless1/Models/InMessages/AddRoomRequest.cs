using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.InMessages
{
    public class AddRoomRequest
    {
        public string UserId { get; set; }
        public List<string> OtherUsers { get; set; }
        public string RoomName { get; set; }
    }
}
