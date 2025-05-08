using ComputerInformation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace ComputerInformation.Services
{
    public static class NetworkInfo
    {
        public static async Task<List<AdapterDetails>> GetNetworkAdaptersAsync()
        {
            return await Task.Run(() =>
            {
                var adapters = new List<AdapterDetails>();
                try
                {
                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus == OperationalStatus.Up &&
                            (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                        {
                            var adapter = new AdapterDetails
                            {
                                NicName = nic.Name,
                                IpAddress = nic.GetIPProperties().UnicastAddresses
                                    .FirstOrDefault(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString(),
                                MacAddress = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                                Available = nic.OperationalStatus.ToString()
                            };
                            adapters.Add(adapter);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred while retrieving information about network adapters.: {ex.Message}");
                    DBHelper.SQLiteHelper.WriteError($"An error occurred while retrieving information about network adapters.: {ex.Message}");
                }
                return adapters;
            });
        }
    }
}
