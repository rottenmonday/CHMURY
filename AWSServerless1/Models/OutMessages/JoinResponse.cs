using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    public class JoinResponse : ServerMessage
    {
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; } = MessageType.JoinResponse;
        [JsonPropertyName("userId")]
        public string UserID { get; set; }
        [JsonPropertyName("roomId")]
        public string RoomID { get; set; }
    }
}
