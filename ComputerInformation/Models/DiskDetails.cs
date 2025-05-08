using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Models
{
    public class DiskDetails
    {
        [JsonProperty("drive_name")]
        public string DriveName { get; set; }

        [JsonProperty("total_size")]
        public long TotalSize { get; set; }

        [JsonProperty("free_size")]
        public long AvailableSpace { get; set; }
    }
}
