using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    public class AddRoomResponse : ServerMessage
    {
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; } = MessageType.AddRoomResponse;
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; }
        [JsonPropertyName("roomName")]
        public string RoomName { get; set; }
    }
}
