using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Interfaces
{
    public interface IRegistryHelper
    {
        (string uninstallValue, bool isMsi) GetUninstallString(string appName);
    }
}
