using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.InMessages
{
    public class JoinRequest
    {
        public string UserID { get; set; }
        public string RoomID { get; set; }
    }
}
