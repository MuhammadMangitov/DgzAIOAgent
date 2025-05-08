using ComputerInformation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Services
{

    public static class DiskInfo
    {
        public static async Task<List<DiskDetails>> GetDisksAsync()
        {
            return await Task.Run(() =>
            {
                var disks = new List<DiskDetails>();
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            var disk = new DiskDetails
                            {
                                DriveName = obj["DeviceID"].ToString(),
                                TotalSize = ConvertBytesToMB(Convert.ToInt64(obj["Size"])),
                                AvailableSpace = ConvertBytesToMB(Convert.ToInt64(obj["FreeSpace"]))
                            };
                            disks.Add(disk);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error retrieving disk information: {ex.Message}");
                    //SQLiteHelper.WriteError($"Error retrieving disk information: {ex.Message}");
                }
                return disks;
            });
        }

        private static long ConvertBytesToMB(long bytes)
        {
            return bytes / (1024 * 1024);
        }
    }
}
