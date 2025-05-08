using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Models
{
    public class AdapterDetails
    {
        [JsonProperty("nic_name")]
        public string NicName { get; set; }

        [JsonProperty("ip_address")]
        public string IpAddress { get; set; }

        [JsonProperty("mac_address")]
        public string MacAddress { get; set; }

        [JsonProperty("available")]
        public string Available { get; set; }
    }
}
