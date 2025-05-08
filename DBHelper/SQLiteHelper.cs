using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBHelper
{
    public class SQLiteHelper
    {
        public static string ProjectDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DgzAIO");
        public static string ApplicationDirectory =>
                Path.Combine(ProjectDataPath, "DgzAIODb");

        public static string logFilePath =>
                Path.Combine(ProjectDataPath, "Logs", "DgzAIODbLog.txt");

        private static SQLiteConnection _connection;
        private static readonly object _lock = new object();

        public static SQLiteConnection CreateConnection()
        {
            lock (_lock) // Ko‘p ipli muhitda xavfsizlikni ta'minlash
            {
                try
                {
                    if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
                    {
                        return _connection; // Agar ulanish ochiq bo‘lsa, uni qaytarish
                    }

                    if (!Directory.Exists(ProjectDataPath))
                    {
                        Directory.CreateDirectory(ProjectDataPath);
                        Console.WriteLine($"Directory created: {ProjectDataPath}");
                    }

                    if (!Directory.Exists(Path.Combine(ProjectDataPath, "Logs")))
                    {
                        Directory.CreateDirectory(Path.Combine(ProjectDataPath, "Logs"));
                        Console.WriteLine($"Log directory created: {Path.Combine(ProjectDataPath, "Logs")}");
                    }

                    if (!File.Exists(logFilePath))
                    {
                        File.Create(logFilePath).Dispose();
                        Log("Log file created: " + logFilePath);
                        Console.WriteLine($"Log file created: {logFilePath}");
                    }

                    if (!Directory.Exists(ApplicationDirectory))
                    {
                        Directory.CreateDirectory(ApplicationDirectory);
                        Log($"DgzAIODb directory created: {ApplicationDirectory}");
                        Console.WriteLine($"DgzAIODb directory created: {ApplicationDirectory}");
                    }

                    string dbPath = Path.Combine(ApplicationDirectory, "DgzAIO.db");

                    if (!File.Exists(dbPath))
                    {
                        Console.WriteLine("Database does not exist! Creating file...");
                        SQLiteConnection.CreateFile(dbPath);
                        Log($"Database file created: {dbPath}");
                    }

                    _connection = new SQLiteConnection($"Data Source={dbPath};Version=3;");
                    _connection.Open();
                    Log($"Successfully connected to database: {dbPath}");
                    return _connection;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Log($"Error: {ex.Message}");
                    throw;
                }
            }
        }

        public static void CloseConnection()
        {
            lock (_lock)
            {
                if (_connection != null && _connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                    _connection = null;
                    Log("Database connection closed.");
                }
            }
        }

        public static void CreateTablesIfNotExists()
        {
            var connection = CreateConnection(); // using bloki olib tashlandi
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS LogEntry (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        module TEXT NOT NULL,
                        function TEXT NOT NULL,
                        created_date TEXT NOT NULL,
                        message TEXT NOT NULL
                    )";
                    command.ExecuteNonQuery();
                    Log("LogEntry table created or already exists.");

                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Error (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        message TEXT NOT NULL,
                        created_date TEXT NOT NULL
                    )";
                    command.ExecuteNonQuery();
                    Log("Error table created or already exists.");

                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Configurations (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        server_ip TEXT NOT NULL,
                        Jwt_token TEXT NOT NULL,
                        report_time INTEGER,
                        modules TEXT NOT NULL,
                        last_sent_time TEXT
                    )";
                    command.ExecuteNonQuery();
                    Log("Configurations table created or already exists.");

                    command.CommandText = @"
                    INSERT OR IGNORE INTO Configurations (id, server_ip, Jwt_token, report_time, modules, last_sent_time) 
                    VALUES (1, 'default_ip', 'default_token', 0, 'default_modules', NULL)";
                    command.ExecuteNonQuery();
                    Log("Default values inserted into Configurations table or already exist.");
                }

                Console.WriteLine("Tables successfully created or already exist.");
            }
            catch (Exception ex)
            {
                Log($"Error creating tables: {ex.Message}");
                Console.WriteLine($"Error creating tables: {ex.Message}");
            }
        }

        public static void InsertJwtToken(string token)
        {
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE Configurations SET Jwt_token = @jwt_token WHERE id = 1";
                    command.Parameters.AddWithValue("@jwt_token", token);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static void DeleteOldJwtToken()
        {
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE Configurations SET Jwt_token = NULL WHERE Jwt_token IS NOT NULL";
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static async Task<string> GetJwtToken()
        {
            string token = null;
            var connection = CreateConnection();
            if (connection == null) return null;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Jwt_token FROM Configurations LIMIT 1";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            token = reader["Jwt_token"]?.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return token;
        }

        public static void ClearLogs()
        {
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.CommandText = "DELETE FROM LogEntry";
                    deleteCommand.ExecuteNonQuery();
                }

                using (var resetCommand = connection.CreateCommand())
                {
                    resetCommand.CommandText = "UPDATE sqlite_sequence SET seq = 0 WHERE name = 'LogEntry'";
                    resetCommand.ExecuteNonQuery();
                }

                Console.WriteLine("Logs successfully cleared and ID reset to start from 1.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing logs: {ex.Message}");
            }
        }

        public static void WriteLog(string module, string function, string message)
        {
            if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(function) || string.IsNullOrEmpty(message))
            {
                Console.WriteLine("Module, function, or message cannot be empty.");
                return;
            }

            var createdDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO LogEntry (module, function, created_date, message) 
                        VALUES (@module, @function, @created_date, @message)";
                    command.Parameters.AddWithValue("@module", module);
                    command.Parameters.AddWithValue("@function", function);
                    command.Parameters.AddWithValue("@created_date", createdDate);
                    command.Parameters.AddWithValue("@message", message);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing log: {ex.Message}");
            }
        }

        public static void WriteError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Console.WriteLine("Error message cannot be empty.");
                return;
            }

            var createdDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var command = new SQLiteCommand(@"INSERT INTO ""Error"" (""message"", ""created_date"") 
                                                 VALUES (@message, @created_date)", connection))
                {
                    command.Parameters.AddWithValue("@message", message);
                    command.Parameters.AddWithValue("@created_date", createdDate);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while writing error: {ex.Message}");
            }
        }

        public static DateTime? GetLastSentTime()
        {
            var connection = CreateConnection();
            if (connection == null) return null;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT last_sent_time FROM Configurations LIMIT 1";

                    var result = command.ExecuteScalar();
                    if (result != null && DateTime.TryParse(result.ToString(), out DateTime lastSentTime))
                    {
                        return lastSentTime;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            return null;
        }

        public static void UpdateLastSentTime(DateTime dateTime)
        {
            var connection = CreateConnection();
            if (connection == null) return;

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE Configurations SET last_sent_time = @last_sent_time WHERE id = 1";
                    command.Parameters.AddWithValue("@last_sent_time", dateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static bool ShouldSendProgramInfo()
        {
            DateTime? lastSentTime = GetLastSentTime();
            if (lastSentTime == null) return true;

            return (DateTime.UtcNow - lastSentTime.Value).TotalHours >= 24;
        }

        public static void Log(string message)
        {
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine(logMessage);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing log: {ex.Message}");
            }
        }
    }
}
