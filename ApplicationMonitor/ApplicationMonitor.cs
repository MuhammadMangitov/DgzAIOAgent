using ApplicationMonitor.Models;
using DBHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace ApplicationMonitor
{
    public class ApplicationMonitor
    {
        public static async Task<List<ProgramDetails>> GetInstalledPrograms()
        {
            var programs = new List<ProgramDetails>();
            var seenPrograms = new HashSet<string>();

            string[] registryKeysLocalMachine = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",        // 64-bit
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" // 32-bit
            };

            string[] registryKeysCurrentUser = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in registryKeysLocalMachine)
            {
                int beforeCount = programs.Count;
                await GetProgramsFromRegistry(Registry.LocalMachine, keyPath, programs, seenPrograms);
                int afterCount = programs.Count;
                Console.WriteLine($"[DEBUG] Registry.LocalMachine - {keyPath}: {afterCount - beforeCount} new programs found.");
                //SQLiteHelper.WriteLog("a", "a", $"[DEBUG] Registry.LocalMachine - {keyPath}: {afterCount - beforeCount} new programs found.");
            }

            using (var usersRoot = Registry.Users)
            {
                foreach (var sid in usersRoot.GetSubKeyNames())
                {
                    if (!sid.StartsWith("S-1-5-21")) continue;

                    string uninstallKeyPath = $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Uninstall";
                    int beforeCount = programs.Count;
                    await GetProgramsFromRegistry(Registry.Users, uninstallKeyPath, programs, seenPrograms);
                    int afterCount = programs.Count;
                    Console.WriteLine($"[DEBUG] Registry.Users - {uninstallKeyPath}: {afterCount - beforeCount} new programs found.");
                    //SQLiteHelper.WriteLog("a", "a", $"[DEBUG] Registry.Users - {uninstallKeyPath}: {afterCount - beforeCount} new programs found.");
                }
            }

            foreach (var keyPath in registryKeysCurrentUser)
            {
                int beforeCount = programs.Count;
                await GetProgramsFromRegistry(Registry.CurrentUser, keyPath, programs, seenPrograms);
                int afterCount = programs.Count;
                Console.WriteLine($"[DEBUG] Registry.CurrentUser - {keyPath}: {afterCount - beforeCount} new programs found.");
                //SQLiteHelper.WriteLog("a", "a", $"[DEBUG] Registry.CurrentUser - {keyPath}: {afterCount - beforeCount} new programs found.");
            }


            Console.WriteLine($"[DEBUG] Total programs collected: {programs.Count}");

            return programs;
        }

        private static async Task GetProgramsFromRegistry(RegistryKey rootKey, string keyPath,
            List<ProgramDetails> programs, HashSet<string> seenPrograms)
        {
            try
            {
                using (var key = rootKey.OpenSubKey(keyPath))
                {
                    if (key == null) return;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                string name = subKey?.GetValue("DisplayName")?.ToString();
                                if (string.IsNullOrEmpty(name) || seenPrograms.Contains(name)) continue;
                                seenPrograms.Add(name);

                                if (subKey?.GetValue("NoDisplay") is int noDisplay && noDisplay == 1) continue;
                                if (subKey?.GetValue("SystemComponent") is int systemComponent && systemComponent == 1) continue;
                                if (subKey?.GetValue("ReleaseType")?.ToString()?.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                if (subKey?.GetValue("ParentKeyName")?.ToString()?.Equals("OperatingSystem", StringComparison.OrdinalIgnoreCase) == true) continue;

                                string version = subKey?.GetValue("DisplayVersion")?.ToString();
                                string installLocation = subKey?.GetValue("InstallLocation")?.ToString();
                                bool isWindowsInstaller = subKey?.GetValue("WindowsInstaller") is int installer && installer == 1;
                                object registrySize = subKey?.GetValue("EstimatedSize");

                                double? size = await GetProgramSizeSmartAsync(name, installLocation, registrySize);

                                programs.Add(new ProgramDetails
                                {
                                    Name = name,
                                    Size = size.HasValue ? Math.Round(size.Value, 2) : 0.0,
                                    Type = isWindowsInstaller ? "System" : "User",
                                    InstalledDate = ParseInstallDate(subKey?.GetValue("InstallDate")?.ToString(), name),
                                    Version = version
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Xatolik yuz berdi1: {ex.Message}");
                            //SQLiteHelper.WriteError($"Registry o‘qishda xatolik: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Registry o‘qishda xatolik: {ex.Message}");
                //SQLiteHelper.WriteError($"Registry o‘qishda xatolik: {ex.Message}");
            }
        }

        private static async Task<double?> GetProgramSizeSmartAsync(string programName, string installLocation, object registrySize)
        {
            if (registrySize != null)
            {
                return Convert.ToDouble(registrySize) / 1024;
            }

            double? wmiSize = await Task.Run(() => GetProgramSizeWMI(programName));
            if (wmiSize.HasValue)
                return wmiSize;

            return await GetProgramSizeAsync(installLocation);
        }

        private static double? GetProgramSizeWMI(string programName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, EstimatedSize FROM Win32_Product"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name) && name.Equals(programName, StringComparison.OrdinalIgnoreCase))
                        {
                            object sizeObj = obj["EstimatedSize"];
                            if (sizeObj != null)
                            {
                                return Convert.ToDouble(sizeObj) / 1024;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            return null;
        }

        private static async Task<double?> GetProgramSizeAsync(string installLocation)
        {
            if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                return null;
            try
            {
                long size = await Task.Run(() => Directory.EnumerateFiles(installLocation, "*.*", SearchOption.AllDirectories)
                                           .AsParallel()
                                           .Select(f => new FileInfo(f).Length)
                                           .Sum());

                return (double?)(size / 1024 / 1024);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fayllar hajmini hisoblashda xato: {ex.Message}");
                //SQLiteHelper.WriteError($"Fayllar hajmini hisoblashda xato: {ex.Message}");
                return null;
            }
        }

        private static DateTime? ParseInstallDate(string installDate, string programName)
        {
            if (!string.IsNullOrEmpty(installDate) &&
                DateTime.TryParseExact(installDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                return date;
            }

            return GetInstallDateViaCIM(programName);
        }


        private static DateTime? GetInstallDateViaCIM(string programName)
        {
            try
            {
                var options = new ConnectionOptions { EnablePrivileges = true };
                var scope = new ManagementScope(@"\\.\root\cimv2", options);
                var query = new ObjectQuery("SELECT Name, InstallDate FROM Win32_InstalledWin32Program");

                using (var searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (var obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name) && name.Equals(programName, StringComparison.OrdinalIgnoreCase))
                        {
                            string dateStr = obj["InstallDate"]?.ToString();
                            if (!string.IsNullOrEmpty(dateStr) &&
                                DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                            {
                                return date;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                //Console.WriteLine($"[WARN] CIM orqali InstallDate topilmadi: {ex.Message}");
            }

            return null;
        }

    }
    public static class RegistryExtensions
    {
        public static void TryGetValue(this RegistryKey key, string name, out string result)
        {
            result = key?.GetValue(name)?.ToString();
        }

        public static int GetIntValue(this RegistryKey key, string name)
        {
            return key?.GetValue(name) is int value ? value : 0;
        }

        public static string GetStringValue(this RegistryKey key, string name)
        {
            return key?.GetValue(name)?.ToString() ?? "";
        }

        public static bool ContainsIgnoreCase(this string source, string toCheck)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(toCheck, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool EqualsIgnoreCase(this string source, string toCompare)
        {
            return string.Equals(source, toCompare, StringComparison.OrdinalIgnoreCase);
        }
    }
}
