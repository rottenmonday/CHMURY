using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.InMessages
{
    public class LoginRequest
    {
        public string UserID { get; set; }
    }
}
