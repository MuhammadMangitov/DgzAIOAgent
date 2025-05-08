using SocketClient.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Helpers
{
    public class ConfigurationManagerSocket : IConfiguration
    {
        public static class SocketSettings
        {
            public static string InstallerApiUrl => "https://d.dev-baxa.me/api/1/agent/files/";
            public static string ServerUrl => "wss://d.dev-baxa.me/agent";
        }

        public string GetApiUrl()
        {
            return SocketSettings.InstallerApiUrl;
        }

        public string GetSocketUrl()
        {
            return SocketSettings.ServerUrl;
        }
    }
}
