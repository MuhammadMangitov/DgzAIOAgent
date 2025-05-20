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
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall" 
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
                                    if (string.IsNullOrEmpty(displayName) ||
                                        displayName.IndexOf(appName, StringComparison.OrdinalIgnoreCase) < 0)
                                        continue;

                                    string uninstallString = subKey.GetValue("UninstallString")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(uninstallString))
                                    {
                                        if (uninstallString.StartsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) &&
                                            uninstallString.Contains("/I"))
                                        {
                                            uninstallString = uninstallString.Replace("/I", "/x").Trim();
                                        }

                                        return uninstallString;
                                    }

                                    object isMsi = subKey.GetValue("WindowsInstaller");
                                    object productCode = subKey.GetValue("ProductCode");

                                    if (isMsi != null && isMsi.ToString() == "1" &&
                                        productCode != null && Guid.TryParse(productCode.ToString(), out _))
                                    {
                                        return $"MsiExec.exe /x {productCode} /qn /norestart";
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

        public string GetGuid(string appName)
        {
            // MSI asosida o‘rnatilgan ilovalar GUID bilan saqlanadi
            string[] registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var path in registryPaths)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key == null) continue;

                    foreach (string subkeyName in key.GetSubKeyNames())
                    {
                        // MSI uninstall GUID formatida: {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
                        if (!subkeyName.StartsWith("{") || !subkeyName.EndsWith("}"))
                            continue;

                        using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                        {
                            if (subkey == null) continue;

                            string displayName = subkey.GetValue("DisplayName") as string;

                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Equals(appName, StringComparison.OrdinalIgnoreCase))
                            {
                                return subkeyName; 
                            }
                        }
                    }
                }
            }

            return null; 
        }

    }
}