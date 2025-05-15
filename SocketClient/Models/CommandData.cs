using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Models
{
    public class CommandData
    {
        public string command { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public List<string> arguments { get; set; }
    }
}
