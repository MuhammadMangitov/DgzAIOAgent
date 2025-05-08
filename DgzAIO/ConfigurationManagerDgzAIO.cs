using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DgzAIO
{
    public class ConfigurationManagerDgzAIO
    {
        public static class DgzApi
        {
            public static string SERVER_URL_UPDATE_AGENT => Environment.GetEnvironmentVariable("API_SERVER_URL_UPDATE_AGENT") ?? "https://d.dev-baxa.me/api/1/agent/update-info";
            public static string DOWNLOAD_URL_AGENT_ZIP => Environment.GetEnvironmentVariable("API_DOWNLOAD_URL_AGENT_ZIP") ?? "https://d.dev-baxa.me/api/1/agent/update/";
        }
    }
}
