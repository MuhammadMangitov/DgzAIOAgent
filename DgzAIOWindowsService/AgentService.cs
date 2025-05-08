using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DgzAIOWindowsService
{
    public class AgentService : IAgentService
    {
        private static readonly string logFilePath = @"C:\ProgramData\DgzAIO\Logs\AgentService.log";

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiQueryProductState(string productCode);

        public void UninstallAgent()
        {
            try
            {
                string guid = ReadProductGUID();
                if (string.IsNullOrEmpty(guid))
                {
                    Log("[Uninstall] GUID not found. Uninstall canceled.");
                    return;
                }

                int state = MsiQueryProductState(guid);
                Log($"[Uninstall] Product state: {state} (0 - installed, 1 - not installed, -1 - error).");
                if (state < 0)
                {
                    Log($"[Uninstall] Product not installed (state={state}). Uninstall command will not be executed.");
                    return;
                }
                string command = $"/c \"MsiExec.exe /x{guid} /qn\"";
                Log($"commands = msiexec.exe -- {command}");

                ProcessStartInfo psi = new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = command,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process process = new Process();
                process.StartInfo = psi;

                Log("Uninstall process starting...");
                Log($"{psi.Arguments.ToString()}");
                process.Start();

                if (process != null)
                {
                    process.WaitForExit();
                    int exitCode = process.ExitCode;
                    if (exitCode == 0)
                    {
                        Log("[Uninstall] Uninstallation completed successfully.");
                    }
                    else if (exitCode == 1605)
                    {
                        Log("[Uninstall] Product not found (error 1605), possibly already uninstalled.");
                    }
                    else
                    {
                        Log($"[Uninstall] Uninstallation error: Exit Code {exitCode}");
                    }
                }
                else
                {
                    Log("[Uninstall] BAT file failed to start.");
                }

                Thread.Sleep(5000);
            }
            catch (Exception ex)
            {
                Log("[Uninstall] Error: " + ex.Message);
            }
        }

        private string ReadProductGUID()
        {
            string guid = null;
            RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Datagaze\DLP", false);
            if (key != null)
            {
                guid = key.GetValue("guid")?.ToString();
                Log("[Uninstall] (64-bit) GUID: " + guid);
                key.Close();
            }

            if (string.IsNullOrEmpty(guid))
            {
                key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Datagaze\DLP", false);
                if (key != null)
                {
                    guid = key.GetValue("guid")?.ToString();
                    Log("[Uninstall] (32-bit) GUID: " + guid);
                    key.Close();
                }
            }

            if (!string.IsNullOrEmpty(guid) && !guid.StartsWith("{"))
            {
                guid = "{" + guid + "}";
            }
            return guid;
        }

        private void StopService()
        {
            try
            {
                ServiceController sc = new ServiceController("DgzAIOService");
                if (sc.Status != ServiceControllerStatus.Stopped &&
                    sc.Status != ServiceControllerStatus.StopPending)
                {
                    sc.Stop();
                    Log("[Uninstall] Service stop command sent.");
                }
            }
            catch (Exception ex)
            {
                Log("[Uninstall] Error stopping service: " + ex.Message);
            }
        }

        private void Log(string message)
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

        public void UpdateAgent(string zipPath, string localPath)
        {
            string tempDir = Path.Combine("C:\\ProgramData\\DgzAIO", "DgzAIO_Update");
            string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DgzAIO");
            string batFilePath = Path.Combine("C:\\ProgramData\\DgzAIO", "update_dgzaio.bat");

            string logFile = logFilePath;

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                ZipFile.ExtractToDirectory(zipPath, tempDir);

                using (StreamWriter sw = new StreamWriter(batFilePath, false, Encoding.UTF8))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine($"echo [{DateTime.Now}] Update started. >> \"{logFile}\"");

                    sw.WriteLine("echo Stopping service... >> \"" + logFile + "\"");
                    sw.WriteLine("net stop DgzAIOService >> \"" + logFile + "\"");

                    sw.WriteLine("echo Stopping main application... >> \"" + logFile + "\"");
                    sw.WriteLine("taskkill /F /IM DgzAIO.exe >> \"" + logFile + "\"");

                    sw.WriteLine(":waitloop");
                    sw.WriteLine("sc query DgzAIOService | find \"RUNNING\" >nul");
                    sw.WriteLine("if %errorlevel%==0 (");
                    sw.WriteLine("  timeout /t 1 >nul");
                    sw.WriteLine("  goto waitloop");
                    sw.WriteLine(")");

                    sw.WriteLine("timeout /t 2 >nul");

                    foreach (string sourceFile in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = GetRelativePath(tempDir, sourceFile);
                        string destFile = Path.Combine(targetDir, relativePath);
                        string destDir = Path.GetDirectoryName(destFile);

                        sw.WriteLine($"if not exist \"{destDir}\" mkdir \"{destDir}\"");
                        sw.WriteLine($"copy /Y \"{sourceFile}\" \"{destFile}\" >> \"{logFile}\"");
                    }

                    sw.WriteLine($"echo Deleting temporary files... >> \"{logFile}\"");
                    sw.WriteLine($"rmdir /F /Q \"{localPath}\" >> \"{logFile}\"");
                    sw.WriteLine($"rmdir /S /Q \"{tempDir}\" >> \"{logFile}\"");

                    // Start service
                    sw.WriteLine("echo Starting service... >> \"" + logFile + "\"");
                    sw.WriteLine("net start DgzAIOService >> \"" + logFile + "\"");

                    sw.WriteLine($"echo [{DateTime.Now}] Update completed. >> \"{logFile}\"");
                    sw.WriteLine("exit");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = batFilePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                Log($".bat file created and started: {batFilePath}");
            }
            catch (Exception ex)
            {
                Log($"Error during update process: {ex.Message}");
                throw;
            }
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(basePath.EndsWith("\\") ? basePath : basePath + "\\");
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
