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
            try
            {
                string jwtToken = await SQLiteHelper.GetJwtToken();
                if (string.IsNullOrEmpty(jwtToken))
                {
                    _logger.LogError("JWT token topilmadi!");
                    return AppResult.Fail("JWT token topilmadi!");
                }

                string requestUrl = $"{_config.GetApiUrl()}{appName}";
                _logger.LogInformation($"Install app URL: {requestUrl}");

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string installerFolder = Path.Combine(programFiles, "DgzAIO", "Installers");
                Directory.CreateDirectory(installerFolder);

                string savePath = Path.Combine(installerFolder, appName);
                _logger.LogInformation($"Installer file path: {savePath}");

                bool downloaded = await _fileDownloader.DownloadFileAsync(requestUrl, savePath, jwtToken);
                if (!downloaded || !File.Exists(savePath))
                {
                    _logger.LogError("Fayl yuklab olinmadi yoki topilmadi.");
                    return AppResult.Fail("Fayl yuklab olinmadi yoki topilmadi.");
                }

                bool isMsi = Path.GetExtension(savePath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
                bool installationSucceeded = false;

                if (isMsi)
                {
                    _logger.LogInformation("MSI fayl aniqlandi — yagona o‘rnatish jarayoni boshlanadi.");

                    // MSI o‘rnatsa: uninstall (agar update bo‘lsa) + install
                    bool result = await TryInstallAsync(savePath, "", isMsi, comand_name, realName);
                    if (result)
                    {
                        await SendApplicationForSocketAsync();
                        _logger.LogInformation("MSI o‘rnatish muvaffaqiyatli.");
                        installationSucceeded = true;
                    }
                }
                else
                {
                    // EXE fayl: har bir argumentni sinab ko‘ramiz
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
                    await Task.Delay(3000); // optional delay
                    try
                    {
                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                            _logger.LogInformation($"Installer fayli o‘chirildi: {savePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Delete error: {ex}");
                    }

                    return AppResult.Success("Ilova muvaffaqiyatli o‘rnatildi.");
                }
                else
                {
                    _logger.LogError("O‘rnatish muvaffaqiyatsiz.");
                    return AppResult.Fail("O‘rnatish bajarilmadi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Installation error: {ex}");
                return AppResult.Fail($"O‘rnatishda xatolik: {ex.Message}");
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
                            _logger.LogError("Old MSI ilovani o‘chirish muvaffaqiyatsiz.");
                            return false;
                        }

                        _logger.LogInformation("Old MSI ilova muvaffaqiyatli o‘chirildi.");
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
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(30000);

                    string output = await outputTask;
                    string error = await errorTask;

                    if (!exited)
                    {
                        _logger.LogInformation("Process time out — taskkill boshlanmoqda.");

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
                            _logger.LogError($"taskkill xatosi: {killEx.Message}");
                        }

                        return false;
                    }

                    _logger.LogInformation($"Exit Code: {process.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(output)) _logger.LogInformation($"Output: {output}");
                    if (!string.IsNullOrWhiteSpace(error)) _logger.LogError($"Error Output: {error}");

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
                        return AppResult.Fail($"Ilovaning GUID topilmadi: {appName}");
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
                        return AppResult.Fail($"Ilovani o‘chirish uchun uninstall string topilmadi: {appName}");
                    }

                    if (uninstallString.StartsWith("\""))
                    {
                        int closingQuote = uninstallString.IndexOf('\"', 1);
                        fileName = uninstallString.Substring(1, closingQuote - 1);
                        args = uninstallString.Substring(closingQuote + 1).Trim();
                    }
                    else
                    {
                        // 2. Tirnoqsiz bo‘lsa: C:\Program Files (x86)\Steam\uninstall.exe /arg
                        // Ya'ni faqat birinchi qismni executable deb qabul qilamiz
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
                    return AppResult.Fail($"Noto‘g‘ri type: {type}. Faqat 'System' yoki 'User' bo‘lishi kerak.");
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
                        return AppResult.Success($"Ilova muvaffaqiyatli o‘chirildi: {appName}");
                    }

                    return AppResult.Fail($"Ilova hali registry'da mavjud, to‘liq o‘chirilmadi: {appName}");
                }
                else
                {
                    return AppResult.Fail($"Ilovani o‘chirishda xatolik bo‘ldi: {appName} (exit code: {exitCode})");
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
