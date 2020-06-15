using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MessageType
    {
        JoinResponse,
        ChatMessageResponse,
        LoginResponse,
        AddRoomResponse,
        GetMessagesResponse
    }
}
