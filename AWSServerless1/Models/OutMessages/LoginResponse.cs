using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    public class LoginResponse : ServerMessage
    {
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; } = MessageType.LoginResponse;
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("users")]
        public List<string> Users { get; set; }
        [JsonPropertyName("customRoomsNames")]
        public List<string> CustomRoomsNames { get; set; }
        [JsonPropertyName("customRoomsIds")]
        public List<string> CustomRoomsIds { get; set; }
    }
}
