using ApiClient;
using ApplicationMonitor;
using DBHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Managers
{
    public class ApplicationManager : Interfaces.IApplicationManager
    {
        private readonly Interfaces.IFileDownloader _fileDownloader;
        private readonly Interfaces.IRegistryHelper _registryHelper;
        private readonly Interfaces.IConfiguration _config;
        private readonly Interfaces.ILogger _logger;

        public ApplicationManager(Interfaces.IFileDownloader fileDownloader, Interfaces.IRegistryHelper registryHelper, Interfaces.IConfiguration config, Interfaces.ILogger logger)
        {
            _fileDownloader = fileDownloader;
            _registryHelper = registryHelper;
            _config = config;
            _logger = logger;
        }

        public async Task<bool> InstallApplicationAsync(string appName, string command, string[] arguments)
        {
            try
            {
                string jwtToken = await SQLiteHelper.GetJwtToken();
                if (string.IsNullOrEmpty(jwtToken))
                {
                    _logger.LogError("JWT token topilmadi!");
                    return false;
                }

                string requestUrl = $"{_config.GetApiUrl()}{appName}";
                _logger.LogInformation($"Install app URL: {requestUrl}");

                string installerFolder = Path.Combine("C:\\Program Files (x86)", "DgzAIO", "Installers");
                Directory.CreateDirectory(installerFolder);

                string savePath = Path.Combine(installerFolder, appName);
                _logger.LogInformation($"Installer file path: {savePath}");

                bool downloaded = await _fileDownloader.DownloadFileAsync(requestUrl, savePath, jwtToken);
                if (!downloaded)
                {

                    _logger.LogError("Fayl yuklab olishda xatolik yuz berdi.");
                    return false;   
                }

                if (!File.Exists(savePath))
                {
                    _logger.LogError($"Fayl topilmadi: {savePath}");
                    return false;
                }

                bool isMsi = Path.GetExtension(savePath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                bool installationSucceeded = false;

                foreach (var arg in arguments)
                {
                    _logger.LogInformation($"Trying install with argument: {arg}");

                    bool result = await TryInstallAsync(savePath, arg, isMsi);
                    if (result)
                    {
                        SendApplicationForSocketAsync().Wait();
                        _logger.LogInformation($"Installation succeeded with argument: {arg}");

                        installationSucceeded = true;
                        break; 
                    }
                    else
                    {
                        _logger.LogInformation($"Installation failed with argument: {arg}");
                    }
                }

                if (installationSucceeded)
                {
                    await Task.Delay(3000);
                    try
                    {
                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                            _logger.LogInformation($"Installer fayli o'chirildi: {savePath}");
                        }
                        else
                        {
                            _logger.LogError($"Installer fayli topilmadi o'chirish uchun: {savePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Delete error: {ex}");
                    }

                    await SendApplicationForSocketAsync();
                    return true;
                }
                else
                {
                    _logger.LogError("All installation attempts failed.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Installation error: {ex}");
                return false;
            }
        }
        private async Task<bool> TryInstallAsync(string filePath, string arguments, bool isMsi)
        {
            try
            {
                _logger.LogInformation($"Starting process: {filePath} with arguments: {arguments} and isMsi: {isMsi}");

                using (var process = new Process())
                {
                    if (isMsi)
                    {
                        process.StartInfo.FileName = "msiexec";
                        process.StartInfo.Arguments = $"/I \"{filePath}\" /QN /NORESTART";
                        _logger.LogInformation($"msiexec command: {process.StartInfo.Arguments}");
                    }
                    else
                    {
                        process.StartInfo.FileName = filePath;
                        process.StartInfo.Arguments = arguments;
                        _logger.LogInformation($"Executable command: {process.StartInfo.Arguments}");
                    }

                    process.StartInfo.WorkingDirectory = Path.GetDirectoryName(filePath);
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(30000);

                    string output = "";
                    string error = "";

                    if (!exited)
                    {
                        _logger.LogInformation("Process did not exit in time. Attempting to taskkill...");

                        try
                        {
                            var killer = new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/PID {process.Id} /F /T",
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var killProc = Process.Start(killer))
                            {
                                string killOut = await killProc.StandardOutput.ReadToEndAsync();
                                string killErr = await killProc.StandardError.ReadToEndAsync();

                                _logger.LogInformation($"taskkill output: {killOut}");
                                if (!string.IsNullOrWhiteSpace(killErr))
                                    _logger.LogError($"taskkill error: {killErr}");

                                killProc.WaitForExit();
                            }
                        }
                        catch (Exception killEx)
                        {
                            _logger.LogError($"Failed to execute taskkill: {killEx.Message}");
                        }

                        try
                        {
                            output = await outputTask;
                            error = await errorTask;
                        }
                        catch (Exception streamEx)
                        {
                            _logger.LogError($"Error reading process streams after taskkill: {streamEx.Message}");
                        }

                        return false;
                    }

                    output = await outputTask;
                    error = await errorTask;

                    _logger.LogInformation($"Exit Code: {process.ExitCode}");

                    if (!string.IsNullOrWhiteSpace(output))
                        _logger.LogInformation($"Output: {output}");

                    if (!string.IsNullOrWhiteSpace(error))
                        _logger.LogError($"Error Output: {error}");

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"TryInstallAsync error: {ex}");
                return false;
            }
        }

        public async Task<bool> UninstallApplicationAsync(string appName, string[] arguments, string type)
        {
            try
            {
                var (uninstallValue, isMsi) = _registryHelper.GetUninstallString(appName);
                _logger.LogInformation($"Uninstall string/value: {uninstallValue} for {appName} (IsMSI: {isMsi})");

                if (string.IsNullOrEmpty(uninstallValue))
                {
                    _logger.LogError($"Uninstall value for {appName} not found.");
                    return false;
                }

                if (type == "Windows Installer" && isMsi)
                {
                    // MSI uninstall
                    _logger.LogInformation($"Type: {type}, isMsi {isMsi}");
                    string msiCommand = $"msiexec /x \"{uninstallValue}\" /qn";
                    _logger.LogInformation($"Uninstalling MSI application {appName} with: {msiCommand}");

                    int exitCode = await ExecuteProcessAsync("cmd.exe", $"/C {msiCommand}");
                    if (exitCode == 0)
                    {
                        await SendApplicationForSocketAsync();
                        _logger.LogInformation($"Successfully uninstalled MSI application {appName}");
                        return true;
                    }

                    _logger.LogError($"Failed to uninstall MSI application {appName}, exit code: {exitCode}");
                    return false;
                }

                // EXE uninstall
                foreach (var argument in arguments)
                {
                    string fullUninstallCommand = $"\"{uninstallValue}\" {argument}";
                    _logger.LogInformation($"Uninstalling {appName} with argument: {argument}");

                    int exitCode = await ExecuteProcessAsync("cmd.exe", $"/C {fullUninstallCommand}");

                    if (exitCode == 0)
                    {
                        await SendApplicationForSocketAsync();
                        _logger.LogInformation($"Successfully uninstalled {appName} with argument: {argument}");
                        return true;
                    }
                    else
                    {
                        _logger.LogError($"Failed to uninstall {appName} with argument: {argument}, exit code: {exitCode}");
                    }
                }

                _logger.LogError($"All uninstall attempts for {appName} failed.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during uninstallation of {appName}: {ex}");
                return false;
            }
        }
        private async Task<int> ExecuteProcessAsync(string fileName, string arguments)
        {
            try
            {
                _logger.LogInformation($"Executing process: {fileName} {arguments}");

                string output = "";
                string error = "";


                using (var process = new Process())
                {
                    process.StartInfo.FileName = fileName;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(20000);
                    if (!exited)
                    {
                        try
                        {
                            _logger.LogInformation("Process did not exit in time. Attempting to taskkill...");

                            var killer = new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/PID {process.Id} /F /T",
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var killProc = Process.Start(killer))
                            {
                                string killOut = killProc.StandardOutput.ReadToEnd();
                                string killErr = killProc.StandardError.ReadToEnd();

                                _logger.LogInformation($"taskkill output: {killOut}");
                                if (!string.IsNullOrWhiteSpace(killErr))
                                    _logger.LogError($"taskkill error: {killErr}");

                                killProc.WaitForExit();
                            }

                            return -1;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"taskkill failed: {ex.Message}");
                            return -1;
                        }
                    }

                    output = await outputTask;
                    error = await errorTask;

                    if (!string.IsNullOrWhiteSpace(output))
                        _logger.LogInformation($"Process Output: {output}");

                    if (!string.IsNullOrWhiteSpace(error))
                        _logger.LogError($"Process Error: {error}");

                    _logger.LogInformation($"Process Exit Code: {process.ExitCode}");
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception executing process: {ex.Message}");
                return -1;
            }
        }

        /*public async Task<bool> UninstallApplicationAsync(string appName, string[] arguments, string type)
        {
            try
            {
                string uninstallString = _registryHelper.GetUninstallString(appName);
                _logger.LogInformation($"Uninstall string: {uninstallString} for {appName}");

                if (string.IsNullOrEmpty(uninstallString))
                {
                    _logger.LogError($"Uninstall string for {appName} not found.");
                    return false;
                }
                if (type == "Windows Installer")
                {

                }

                foreach (var argument in arguments)
                {
                    string fullUninstallCommand = $"\"{uninstallString}\" {argument}";
                    _logger.LogInformation($"Uninstalling {appName} with argument: {argument}");

                    int exitCode = await ExecuteProcessAsync("cmd.exe", $"/C {fullUninstallCommand}");

                    if (exitCode == 0)
                    {
                        await SendApplicationForSocketAsync(); 
                        _logger.LogInformation($"Successfully uninstalled {appName} with argument: {argument}");
                        return true; 
                    }
                    else
                    {
                        _logger.LogError($"Failed to uninstall {appName} with argument: {argument}, exit code: {exitCode}");
                    }
                }

                _logger.LogError($"All uninstall attempts for {appName} failed.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during uninstallation of {appName}: {ex}");
                return false;
            }
        }
        private async Task<int> ExecuteProcessAsync(string fileName, string arguments)
        {
            try
            {
                _logger.LogInformation($"Executing process: {fileName} {arguments}");

                string output = "";
                string error = "";


                using (var process = new Process())
                {
                    process.StartInfo.FileName = fileName;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(20000);
                    if (!exited)
                    {
                        try
                        {
                            _logger.LogInformation("Process did not exit in time. Attempting to taskkill...");

                            var killer = new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/PID {process.Id} /F /T",
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var killProc = Process.Start(killer))
                            {
                                string killOut = killProc.StandardOutput.ReadToEnd();
                                string killErr = killProc.StandardError.ReadToEnd();

                                _logger.LogInformation($"taskkill output: {killOut}");
                                if (!string.IsNullOrWhiteSpace(killErr))
                                    _logger.LogError($"taskkill error: {killErr}");

                                killProc.WaitForExit();
                            }

                            return -1;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"taskkill failed: {ex.Message}");
                            return -1;
                        }
                    }

                    output = await outputTask;
                    error = await errorTask;

                    if (!string.IsNullOrWhiteSpace(output))
                        _logger.LogInformation($"Process Output: {output}");

                    if (!string.IsNullOrWhiteSpace(error))
                        _logger.LogError($"Process Error: {error}");

                    _logger.LogInformation($"Process Exit Code: {process.ExitCode}");
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception executing process: {ex.Message}");
                return -1;
            }
        }*/

        public bool CloseApplication(string appName)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(appName))
                {
                    process.Kill();
                    process.WaitForExit();
                }
                _logger.LogInformation($"Application {appName} closed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error closing application {appName}: {ex.Message}");
                return false;
            }
        }
        public async Task SendApplicationForSocketAsync()
        {
            _logger.LogInformation("[Application Monitor] Retrieving installed programs...");

            var programs = await ApplicationMonitor.ApplicationMonitor.GetInstalledPrograms();
            bool success = await ApiClient.ApiClient.SendProgramInfo(programs);

            if (success)
            {
                _logger.LogInformation("Installed programs list sent to server.");
            }
            else
            {
                _logger.LogError("Error sending installed programs list to server.");
            }
        }
    }
}
