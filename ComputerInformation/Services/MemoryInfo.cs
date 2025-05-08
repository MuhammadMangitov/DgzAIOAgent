using DBHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Services
{
    public static class MemoryInfo
    {
        public static async Task<long> GetRamAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            return Convert.ToInt64(obj["TotalVisibleMemorySize"]) / 1024;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrieving RAM data: {ex.Message}");
                    //SQLiteHelper.WriteError($"Error retrieving RAM data: {ex.Message}");
                }
                return 0;
            });
        }
    }
}
