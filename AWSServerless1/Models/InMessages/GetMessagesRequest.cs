using System;
using System.Collections.Generic;
using System.Text;

namespace AWSServerless1.Models.InMessages
{
    class GetMessagesRequest
    {
        public string RoomID { get; set; }
        /// <summary>
        /// Request for the messages before TimeStamp
        /// </summary>
        public string TimeStamp { get; set; }
    }
}
