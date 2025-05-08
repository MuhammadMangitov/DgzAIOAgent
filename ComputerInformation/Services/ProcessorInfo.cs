using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Services
{
    public static class ProcessorInfo
    {
        public static async Task<string> GetCpuAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            return obj["Name"].ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting CPU information: {ex.Message}");
                    //DBHelper.SQLiteHelper.WriteError($"Error getting CPU information: {ex.Message}");
                }
                return "Unknown";
            });
        }
        public static async Task<int> GetCpuCoresAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            return Convert.ToInt32(obj["NumberOfCores"]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting information about CPU cores: {ex.Message}");
                    //DBHelper.SQLiteHelper.WriteError($"Error getting information about CPU cores: {ex.Message}");
                }
                return 0;
            });
        }
        public static async Task<string> GetCpuModelAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            return obj["Caption"].ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrieving CPU model information: {ex.Message}");
                    //DBHelper.SQLiteHelper.WriteError($"Error retrieving CPU model information: {ex.Message}");
                }
                return "Unknown";
            });
        }
    }
}
