﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AWSServerless1.Models.InMessages
{
    public class JoinRequest
    {
        public string User1ID { get; set; }
        public string User2ID { get; set; }
    }
}
