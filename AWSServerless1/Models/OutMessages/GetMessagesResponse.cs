using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    class GetMessagesResponse
    {
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; } = MessageType.GetMessagesResponse;
        [JsonPropertyName("messages")]
        public List<string> Messages { get; set; }
        [JsonPropertyName("dates")]
        public List<string> Dates { get; set; }
        [JsonPropertyName("users")]
        public List<string> Users { get; set; }
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }
}
