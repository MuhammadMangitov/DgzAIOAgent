using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Helpers
{
    public class RegistryHelper : Interfaces.IRegistryHelper
    {
        private readonly Interfaces.ILogger _logger;

        public RegistryHelper(Interfaces.ILogger logger)
        {
            _logger = logger;
        }

        public string GetUninstallString(string appName)
        {
            string[] registryPaths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall" // HKCU uchun
            };

            try
            {
                foreach (string path in registryPaths)
                {
                    RegistryKey baseKey = path.StartsWith(@"Software\") ? Registry.CurrentUser : Registry.LocalMachine;
                    using (RegistryKey key = baseKey.OpenSubKey(path))
                    {
                        if (key == null) continue;

                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                                {
                                    if (subKey == null) continue;

                                    string displayName = subKey.GetValue("DisplayName")?.ToString();
                                    string uninstallString = subKey.GetValue("UninstallString")?.ToString();

                                    if (!string.IsNullOrEmpty(displayName) &&
                                        displayName.IndexOf(appName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return uninstallString;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Error reading subkey `{subKeyName}`: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error reading registry: {ex.Message}");
            }

            return null;
        }
    }
}
