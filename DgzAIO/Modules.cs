using DBHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ApplicationMonitor;
using ApiClient;
using System.Timers;
using System.Threading.Tasks;

namespace DgzAIO
{
    public class Modules
    {
        private static Timer _infoTimer;

        public static void Start()
        {
            if (StartDBHelper())
            {
                StartApiClient();
                //StartApplicationMonitor();
                StartComputerInformation();
            }
            else
            {
                Console.WriteLine("[DBHelper] Error occurred while connecting to the database. Application failed to start.");
            }
        }

        public static void StartSocketClient()
        {
            Task.Run(StartSocketClientThread);
        }

        private static async Task StartSocketClientThread()
        {
            try
            {
                Console.WriteLine("[Socket Client] Connection starting...");
                var socketManager = new SocketClient.Services.SocketClient();
                bool isConnected = await socketManager.StartSocketListener();

                if (isConnected)
                {
                    SQLiteHelper.WriteLog("Modules", "StartSocketClientThread", "Socket.io connected successfully");
                    Console.WriteLine("[Socket Client] Socket.io connected successfully!");
                }
                else
                {
                    SQLiteHelper.WriteLog("Modules", "StartSocketClientThread", "Error occurred while connecting to Socket.io");
                    Console.WriteLine("[Socket Client] Error occurred while connecting!");
                }
            }
            catch (Exception ex)
            {
                SQLiteHelper.WriteLog("Modules", "StartSocketClientThread", $"Error: {ex.Message}");
                Console.WriteLine($"[Socket Client] Error: {ex.Message}");
            }
        }

        public static bool StartDBHelper()
        {
            Console.WriteLine("[DBHelper] Establishing SQLite connection...");
            var connection = SQLiteHelper.CreateConnection();

            if (connection != null)
            {
                Console.WriteLine("[DBHelper] SQLite connection successful!");
                SQLiteHelper.CreateTablesIfNotExists();
                return true;
            }

            Console.WriteLine("[DBHelper] Error occurred!");
            return false;
        }

        public static void StartComputerInformation()
        {
            Task.Run(StartComputerInformationThread);
        }

        private static void StartComputerInformationThread()
        {
            Console.WriteLine("[Computer Information] Timer starting...");
            SQLiteHelper.WriteLog("Modules", "StartComputerInformationThread", "Timer starting...");
            StartTimer();
        }

        /*public static void StartApplicationMonitor()
        {
            Task.Run(StartApplicationMonitorThread);
        }

        private static async Task StartApplicationMonitorThread()
        {
            if (SQLiteHelper.ShouldSendProgramInfo())
            {
                SQLiteHelper.WriteLog("Modules", "StartApplicationMonitorThread", "Sending program list to API");
                Console.WriteLine("[Application Monitor] Sending program list to API...");
               // await SendProgramInfo();
            }
        }*/

        public static void StartApiClient()
        {
            Task.Run(StartApiClientThread);
        }

        private static async Task StartApiClientThread()
        {
            Console.WriteLine("[API Client] Retrieving JWT token...");
            var (token, statusCode) = await ApiClient.ApiClient.GetJwtTokenFromApi();

            if (!string.IsNullOrEmpty(token))
            {
                SQLiteHelper.InsertJwtToken(token);
                SQLiteHelper.WriteLog("Modules", "StartApiClientThread", "JWT token saved");
                Console.WriteLine("[API Client] JWT token saved!");
                Console.WriteLine($"statusCode: {statusCode}");

                if (statusCode == 201 || statusCode == 200)
                {
                    await SendProgramInfo();
                }
            }
            else
            {
                SQLiteHelper.WriteLog("Modules", "StartApiClientThread", "Error occurred while retrieving JWT token");
                Console.WriteLine("[API Client] Error occurred while retrieving JWT token!");
            }

            StartSocketClient();
        }
        private static async Task SendProgramInfo()
        {
            Console.WriteLine("[Application Monitor] Retrieving program list...");
            var programs = await ApplicationMonitor.ApplicationMonitor.GetInstalledPrograms();
            SQLiteHelper.WriteLog("Modules", "SendProgramInfo", $"Program list retrieved: {programs.Count} programs");
            bool success = await ApiClient.ApiClient.SendProgramInfo(programs);

            if (success)
            {
                SQLiteHelper.WriteLog("Modules", "SendProgramInfo", $"succesdan keyin : {programs.Count} programs");
                SQLiteHelper.WriteLog("Modules", "SendProgramInfo", "Program list successfully sent to API");
                Console.WriteLine("[Application Monitor] Program list successfully sent.");
                SQLiteHelper.UpdateLastSentTime(DateTime.UtcNow);
            }
            else
            {
                SQLiteHelper.WriteLog("Modules", "SendProgramInfo", "Error occurred while sending program list");
                Console.WriteLine("[Application Monitor] Error occurred while sending program list!");
            }
        }

        private static void StartTimer()
        {
            SQLiteHelper.WriteLog("Modules", "StartTimer", "24-hour timer started");

            _infoTimer = new Timer(TimeSpan.FromHours(24).TotalMilliseconds)
            {
                AutoReset = true
            };

            _infoTimer.Elapsed += async (sender, e) =>
            {
                Console.WriteLine("[Timer] 24 hours passed, sending new information...");
                SQLiteHelper.WriteLog("Modules", "StartTimer", "24 hours passed, sending new information");

                if (!SQLiteHelper.ShouldSendProgramInfo())
                {
                    SQLiteHelper.WriteLog("Modules", "StartTimer", "Program list not sent because 24 hours have not passed");
                    Console.WriteLine("[Timer] Program list not sent because 24 hours have not passed.");
                    return;
                }

                await SendProgramInfo();
            };

            _infoTimer.Start();
        }
    }
}
