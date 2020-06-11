using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    public class ChatMessageResponse : ServerMessage
    {
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; } = MessageType.ChatMessageResponse;
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("message")]
        public string Message { get; set; }
        [JsonPropertyName("author")]
        public string Author { get; set; }
        [JsonPropertyName("date")]
        public string Date { get; set; }
    }
}
