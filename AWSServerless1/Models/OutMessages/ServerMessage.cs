using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.OutMessages
{
    public interface ServerMessage
    {
        public MessageType MessageType { get; set; }
        public bool Success { get; set; }

    }
}
