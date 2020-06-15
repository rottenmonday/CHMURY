using System;
using System.Collections.Generic;
using System.Text;

namespace AWSServerless1.Utility
{
    public class Messsages
    {
        /// <summary>
        /// Message content.
        /// </summary>
        public List<string> Messages { get; set; }
        /// <summary>
        /// Date of the message in the Unix Epoch Time system.
        /// </summary>
        public List<string> Dates { get; set; }
        /// <summary>
        /// Author of the message.
        /// </summary>
        public List<string> UserNames { get; set; }
    }
}
