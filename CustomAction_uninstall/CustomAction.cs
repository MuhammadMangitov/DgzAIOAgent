using Microsoft.Deployment.WindowsInstaller;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;

namespace CustomAction_uninstall
{
    public class CustomActions
    {
        public static Session session;

        public static void Log(string text)
        {
            Console.WriteLine(text);

            try
            {
                string logFilePath = @"C:\ProgramData\DgzAIO\Logs\CustomActionLog.txt";
                using (StreamWriter writer = new StreamWriter(logFilePath, true))
                {
                    writer.WriteLine($"{DateTime.Now}: {text}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to log: {ex.Message}");
            }

            if (session != null)
            {
                session.Log(text);
            }
        }

        [CustomAction]
        public static ActionResult CustomAction_uninstall(Session session)
        {
            CustomActions.session = session;
            session.Log("CustomAction_uninstall started.");
            Log("CustomAction_uninstall started.");

            var serviceName = "DgzAIOService";

            try
            {
                ServiceController sc = new ServiceController(serviceName);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    Log($"Service is being stopped: {serviceName}...");
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                Process.Start("sc", $"delete {serviceName}").WaitForExit();
                Log($"Service deleted: {serviceName}.");
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("ServiceHelper", ex.Message, EventLogEntryType.Error);
            }

            try
            {
                string processName = "DgzAIO.exe";
                session.Log($"Terminating process: {processName}...");

                var taskKill = new Process();
                taskKill.StartInfo.FileName = "taskkill";
                taskKill.StartInfo.Arguments = $"/IM \"{processName}\" /F";
                taskKill.StartInfo.UseShellExecute = false;
                taskKill.StartInfo.CreateNoWindow = true;
                taskKill.Start();
                taskKill.WaitForExit();

                if (taskKill.ExitCode == 0)
                {
                    Log($"{processName} terminated successfully.");
                    session.Log($"{processName} terminated successfully.");
                }
                else
                {
                    Log($"Warning: Could not terminate {processName}. It may not be running.");
                    session.Log($"Warning: Could not terminate {processName}. It may not be running.");
                }

                string installPath = @"C:\Program Files (x86)\DgzAIO";
                session.Log($"Checking installation directory: {installPath}");

                if (Directory.Exists(installPath))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(installPath, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, System.IO.FileAttributes.Normal);

                        Directory.Delete(installPath, true);
                        session.Log($"Installation directory deleted: {installPath}.");
                        Log($"Installation directory deleted: {installPath}.");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log($"Access denied: {ex.Message} while deleting {installPath}");
                        session.Log($"Access denied: {ex.Message} while deleting {installPath}");
                    }
                    catch (IOException ex)
                    {
                        Log($"IO error: {ex.Message} while deleting {installPath}. Some files may be in use.");
                        session.Log($"IO error: {ex.Message} while deleting {installPath}. Some files may be in use.");
                    }
                }
                else
                {
                    Log($"Installation directory not found: {installPath}.");
                    session.Log($"Installation directory not found: {installPath}.");
                }

                string dbPath = @"C:\ProgramData\DgzAIO\DgzAIODb";
                session.Log($"Checking database directory: {dbPath}");

                if (Directory.Exists(dbPath))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(dbPath, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, System.IO.FileAttributes.Normal);

                        Directory.Delete(dbPath, true);
                        Log($"Database directory deleted: {dbPath}.");
                        session.Log($"Database directory deleted: {dbPath}.");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log($"Access denied: {ex.Message} while deleting {dbPath}");
                        session.Log($"Access denied: {ex.Message} while deleting {dbPath}");
                    }
                    catch (IOException ex)
                    {
                        Log($"IO error: {ex.Message} while deleting {dbPath}. Some files may be in use.");
                        session.Log($"IO error: {ex.Message} while deleting {dbPath}. Some files may be in use.");
                    }
                }
                else
                {
                    Log($"Database directory not found: {dbPath}.");
                    session.Log($"Database directory not found: {dbPath}.");
                }

                try
                {
                    string taskName = "DgzAIO";
                    session.Log($"Deleting scheduled task: {taskName}...");

                    var taskDelete = new Process();
                    taskDelete.StartInfo.FileName = "schtasks";
                    taskDelete.StartInfo.Arguments = $"/Delete /TN \"{taskName}\" /F";
                    taskDelete.StartInfo.UseShellExecute = false;
                    taskDelete.StartInfo.CreateNoWindow = true;
                    taskDelete.Start();
                    taskDelete.WaitForExit();

                    if (taskDelete.ExitCode == 0)
                    {
                        Log($"Scheduled task deleted successfully: {taskName}.");
                        session.Log($"Scheduled task deleted successfully: {taskName}.");
                    }
                    else
                    {
                        Log($"Warning: Could not delete scheduled task: {taskName}. It may not exist.");
                        session.Log($"Warning: Could not delete scheduled task: {taskName}. It may not exist.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message} while deleting scheduled task");
                    session.Log($"Error: {ex.Message} while deleting scheduled task");
                }

                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    {
                        baseKey.DeleteSubKeyTree(@"SOFTWARE\WOW6432Node\Datagaze", throwOnMissingSubKey: false);

                        session.Log(@"Deleted 'HKLM\SOFTWARE\WOW6432Node\Datagaze' from registry.");
                        Log(@"Deleted 'HKLM\SOFTWARE\WOW6432Node\Datagaze' from registry.");
                    }
                }
                catch (Exception ex)
                {
                    session.Log($"Warning: Error deleting Datagaze registry key: {ex.Message}");
                    Log($"Warning: Error deleting Datagaze registry key: {ex.Message}");
                }

                session.Log("CustomAction_uninstall completed successfully.");
                Log("CustomAction_uninstall completed successfully.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                Log($"ERROR: During CustomAction_uninstall: {ex.Message}");
                session.Log($"ERROR: During CustomAction_uninstall: {ex.Message}");
                return ActionResult.Failure;
            }
        }
    }
}
