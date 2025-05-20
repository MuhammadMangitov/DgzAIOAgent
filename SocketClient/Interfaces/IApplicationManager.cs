using SocketClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Interfaces
{
    public interface IApplicationManager
    {   
        Task<AppResult> InstallApplicationAsync(string appName, string[] arguments, string comand_name, string realName);
        Task<AppResult> UninstallApplicationAsync(string appName, string[] arguments, string type);
        bool CloseApplication(string appName);
        Task SendApplicationForSocketAsync();
    }
}
