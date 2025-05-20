using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Models
{
    public class UpdateAppData
    {
        public string name { get; set; }
        public string userId { get; set; }
        public List<string> arguments { get; set; }
    }
}
