using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Interfaces
{
    public interface ILogger
    {
        void LogInformation(string message);
        void LogError(string message);
    }
}
