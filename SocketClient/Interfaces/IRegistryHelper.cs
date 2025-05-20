using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Interfaces
{
    public interface IRegistryHelper
    {
        string GetUninstallString(string appName);
        string GetGuid(string appName);
    }
}
