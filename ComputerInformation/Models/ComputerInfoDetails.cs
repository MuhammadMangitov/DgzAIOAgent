using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Models
{
    public class ComputerInfoDetails
    {
        [JsonProperty("hostname")]
        public string HostName { get; set; }
        // OS
        [JsonProperty("operation_system")]
        public string OperationSystem { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("build_number")]
        public string BuildNumber { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("ram")]
        public long Ram { get; set; }

        // Processor
        [JsonProperty("cpu")]
        public string CPU { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("cores")]
        public int Cores { get; set; }


        [JsonProperty("network_adapters")]
        public List<AdapterDetails> NetworkAdapters { get; set; }


        [JsonProperty("disks")]
        public List<DiskDetails> Disks { get; set; }
    }
}
