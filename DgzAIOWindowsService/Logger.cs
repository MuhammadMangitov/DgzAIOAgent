using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DgzAIOWindowsService
{
    public static class Logger
    {
        private static readonly string logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DgzAIO", "Logs");

        private static readonly string logFile = Path.Combine(logsPath, "MyServiceLog.txt");
        private static readonly string errorFile = Path.Combine(logsPath, "MyServiceErrors.txt");

        static Logger()
        {
            try
            {
                if (!Directory.Exists(logsPath))
                    Directory.CreateDirectory(logsPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Log directory could not be created.: " + ex.Message);
            }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(logFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public static void LogError(string message)
        {
            try
            {
                File.AppendAllText(errorFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
