using ApiClient;
using ApplicationMonitor;
using DBHelper;
using SocketClient.Models;
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
        public async Task<AppResult> InstallApplicationAsync(string appName, string[] arguments, string comand_name, string realName)
        {
            string savePath = null;

            try
            {
                string jwtToken = await SQLiteHelper.GetJwtToken();
                if (string.IsNullOrEmpty(jwtToken))
                {
                    _logger.LogError("JWT token not found!");
                    return AppResult.Fail("JWT token not found!");
                }

                string requestUrl = $"{_config.GetApiUrl()}{appName}";
                _logger.LogInformation($"Install app URL: {requestUrl}");

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string installerFolder = Path.Combine(programFiles, "DgzAIO", "Installers");
                Directory.CreateDirectory(installerFolder);

                savePath = Path.Combine(installerFolder, appName);
                _logger.LogInformation($"Installer file path: {savePath}");

                bool downloaded = await _fileDownloader.DownloadFileAsync(requestUrl, savePath, jwtToken);
                if (!downloaded || !File.Exists(savePath))
                {
                    _logger.LogError("The file was not downloaded or found.");
                    return AppResult.Fail("The file was not downloaded or found.");
                }

                bool isMsi = Path.GetExtension(savePath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                bool installationSucceeded = false;

                if (isMsi)
                {
                    _logger.LogInformation("MSI file detected — single installation process begins.");

                    bool result = await TryInstallAsync(savePath, "", isMsi, comand_name, realName);
                    if (result)
                    {
                        await SendApplicationForSocketAsync();
                        _logger.LogInformation("MSI installation successful.");
                        installationSucceeded = true;
                    }
                }
                else
                {
                    foreach (var arg in arguments)
                    {
                        _logger.LogInformation($"Trying install with argument: {arg}");

                        bool result = await TryInstallAsync(savePath, arg, isMsi, comand_name, realName);
                        if (result)
                        {
                            await SendApplicationForSocketAsync();
                            _logger.LogInformation($"Installation succeeded with argument: {arg}");
                            installationSucceeded = true;
                            break;
                        }
                        else
                        {
                            _logger.LogInformation($"Installation failed with argument: {arg}");
                        }
                    }
                }

                if (installationSucceeded)
                {
                    await Task.Delay(1000); 
                    return AppResult.Success("The application was successfully installed.");
                }
                else
                {
                    _logger.LogError("Installation failed..");
                    return AppResult.Fail("Installation failed..");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Installation error: {ex}");
                return AppResult.Fail($"Installation error: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(savePath) && File.Exists(savePath))
                    {
                        File.Delete(savePath);
                        _logger.LogInformation($"Installer file deleted (finally): {savePath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error deleting file in finally block: {ex.Message}");
                }
            }
        }
        private async Task<bool> TryInstallAsync(string filePath, string arguments, bool isMsi, string commandName, string realName)
        {
            try
            {
                _logger.LogInformation($"Starting process: {filePath} with arguments: {arguments} and isMsi: {isMsi}");

                if (isMsi && commandName == "update_app")
                {
                    string productGuid = _registryHelper.GetGuid(realName);
                    if (!string.IsNullOrEmpty(productGuid))
                    {
                        _logger.LogInformation($"[UPDATE] Old version GUID: {productGuid}");

                        bool uninstallSuccess = await UninstallMsiByGuidAsync(productGuid);
                        if (!uninstallSuccess)
                        {
                            _logger.LogError("Uninstalling the old MSI application failed.");
                            return false;
                        }

                        _logger.LogInformation("The old MSI application was successfully uninstalled.");
                        await Task.Delay(2000);
                    }
                    else
                    {
                        _logger.LogInformation($"[UPDATE] GUID topilmadi: {realName}");
                    }
                }

                using (var process = new Process())
                {
                    process.StartInfo.FileName = isMsi ? "msiexec" : filePath;

                    string finalArgs = isMsi
                        ? $"/i \"{filePath}\" {(string.IsNullOrWhiteSpace(arguments) ? "/qn /norestart" : arguments)}"
                        : arguments;

                    process.StartInfo.Arguments = finalArgs;
                    process.StartInfo.WorkingDirectory = Path.GetDirectoryName(filePath);
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = false;
                    process.StartInfo.RedirectStandardError = false;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    process.Start();
                    _logger.LogInformation($"Process started: {process.Id}");


                    var exitTask = Task.Run(() =>
                    {
                        process.WaitForExit();
                        return true;
                    });

                    var timeoutTask = Task.Delay(30000);

                    if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/PID {process.Id} /F /T",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"taskkill failed: {ex.Message}");
                        }

                        return false;
                    }
/*
                    string output = await outputTask;
                    string error = await errorTask;

                    _logger.LogInformation($"Exit Code: {process.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(output)) _logger.LogInformation($"Output: {output}");
                    if (!string.IsNullOrWhiteSpace(error)) _logger.LogError($"Error Output: {error}");
*/
                    return process.ExitCode == 0;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError($"TryInstallAsync error: {ex}");
                return false;
            }
        }

        public async Task<AppResult> UninstallApplicationAsync(string appName, string[] arguments, string type)
        {
            try
            {
                string fileName;
                string args;

                if (type == "System")
                {
                    string guid = _registryHelper.GetGuid(appName);
                    _logger.LogInformation($"[System] GUID for {appName}: {guid}");

                    if (string.IsNullOrEmpty(guid))
                    {
                        return AppResult.Fail($"Application GUID not found: {appName}");
                    }

                    fileName = "msiexec";
                    args = $"/x {guid} /qn /norestart";
                }
                else if (type == "User")
                {
                    string uninstallString = _registryHelper.GetUninstallString(appName);
                    _logger.LogInformation($"[User] Uninstall string for {appName}: {uninstallString}");

                    if (string.IsNullOrEmpty(uninstallString))
                    {
                        return AppResult.Fail($"Uninstall string to uninstall the application was not found: {appName}");
                    }

                    if (uninstallString.StartsWith("\""))
                    {
                        int closingQuote = uninstallString.IndexOf('\"', 1);
                        fileName = uninstallString.Substring(1, closingQuote - 1);
                        args = uninstallString.Substring(closingQuote + 1).Trim();
                    }
                    else
                    {
                        int spaceIndex = uninstallString.IndexOf(' ');
                        if (spaceIndex > 0)
                        {
                            // Tekshirish: bu path mavjud executable faylmi?
                            string potentialPath = uninstallString.Substring(0, spaceIndex);
                            if (File.Exists(potentialPath))
                            {
                                fileName = potentialPath;
                                args = uninstallString.Substring(spaceIndex + 1).Trim();
                            }
                            else
                            {
                                fileName = uninstallString.Trim(); 
                                args = ""; // Argument yo‘q
                            }
                        }
                        else
                        {
                            fileName = uninstallString.Trim();
                            args = "";
                        }
                    }

                    if (arguments != null && arguments.Length > 0)
                    {
                        args += " " + string.Join(" ", arguments);
                    }

                }
                else
                {
                    return AppResult.Fail($"Invalid type: {type}. Must be 'System' or 'User' only.");
                }

                _logger.LogInformation($"Executing uninstall command: {fileName} {args}");

                int exitCode = await ExecuteProcessAsync(fileName, args);

                if (exitCode == 0)
                {
                    await Task.Delay(10000); 

                    string check = _registryHelper.GetUninstallString(appName);
                    if (string.IsNullOrEmpty(check))
                    {
                        await SendApplicationForSocketAsync();
                        return AppResult.Success($"The application was successfully deleted: {appName}");
                    }

                    return AppResult.Fail($"The application is still in the registry, it has not been completely removed: {appName}");
                }
                else
                {
                    return AppResult.Fail($"There was an error uninstalling the app: {appName} (exit code: {exitCode})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Uninstallation error for {appName}: {ex}");
                return AppResult.Fail($"Ilovani o‘chirishda xatolik: {ex.Message}");
            }
        }
        private async Task<int> ExecuteProcessAsync(string fileName, string arguments)
        {
            try
            {
                _logger.LogInformation($"Executing process: {fileName} {arguments}");

                using (var process = new Process())
                {
                    process.StartInfo.FileName = fileName;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    _logger.LogInformation("Starting process...");
                    process.Start();


                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(30000); // 30 sekund

                    string output = await outputTask;
                    string error = await errorTask;

                    if (!exited)
                    {
                        _logger.LogError("Process did not exit in time. Killing it...");

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
                            _logger.LogError($"taskkill failed: {killEx.Message}");
                        }

                        return -2;
                    }

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

        private async Task<bool> UninstallMsiByGuidAsync(string productGuid)
        {
            try
            {
                string uninstallArgs = $"/x {productGuid} /qn /norestart";

                using (var proc = new Process())
                {
                    proc.StartInfo.FileName = "msiexec";
                    proc.StartInfo.Arguments = uninstallArgs;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.RedirectStandardError = true;

                    proc.Start();

                    string output = await proc.StandardOutput.ReadToEndAsync();
                    string error = await proc.StandardError.ReadToEndAsync();

                    bool exited = proc.WaitForExit(60000);

                    _logger.LogInformation($"MSI uninstall output: {output}");
                    if (!string.IsNullOrWhiteSpace(error))
                        _logger.LogError($"MSI uninstall error: {error}");

                    return exited && proc.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"UninstallMsiByGuidAsync error: {ex.Message}");
                return false;
            }
        }

    }
}
