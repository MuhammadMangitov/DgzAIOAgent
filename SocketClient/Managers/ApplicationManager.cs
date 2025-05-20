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
        public async Task<AppResult> InstallApplicationAsync(string appName, string[] arguments, string comand_name)
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
                if (!downloaded)
                {
                    _logger.LogError("Fayl yuklab olishda xatolik yuz berdi.");
                    return AppResult.Fail("Fayl yuklab olishda xatolik yuz berdi.");
                }

                if (!File.Exists(savePath))
                {
                    _logger.LogError($"Fayl topilmadi: {savePath}");
                    return AppResult.Fail($"Fayl topilmadi: {savePath}");
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
                    await Task.Delay(3000); // O'rnatishdan so'ng kutish

                    try
                    {
                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                            _logger.LogInformation($"Installer fayli o'chirildi: {savePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Delete error: {ex}");
                    }

                    await SendApplicationForSocketAsync();
                    return AppResult.Success("Ilova muvaffaqiyatli o‘rnatildi.");
                }
                else
                {
                    _logger.LogError("Barcha o‘rnatish urinishlari muvaffaqiyatsiz tugadi.");
                    return AppResult.Fail("O‘rnatish bajarilmadi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Installation error: {ex}");
                return AppResult.Fail($"O‘rnatishda xatolik: {ex.Message}");
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

                    bool exited = process.WaitForExit(30000); // 30 sekund kutish

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

        public async Task<AppResult> UninstallApplicationAsync(string appName, string[] arguments, string type)
        {
            try
            {
                string uninstallString = _registryHelper.GetUninstallString(appName);
                _logger.LogInformation($"Uninstall string: {uninstallString} for {appName}");

                if (string.IsNullOrEmpty(uninstallString))
                {
                    _logger.LogError($"Uninstall string for {appName} not found.");
                    return AppResult.Fail($"Ilovani o‘chirish uchun uninstall string topilmadi: {appName}");
                }

                string fileName;
                string args;

                if (uninstallString.StartsWith("{") && uninstallString.EndsWith("}"))
                {
                    fileName = "msiexec";
                    args = $"/x {uninstallString} /qn /norestart";
                }
                else
                {
                    fileName = uninstallString;

                    string joinedArgs = arguments != null && arguments.Length > 0
                        ? string.Join(" ", arguments)
                        : "/qn /norestart";

                    args = joinedArgs;
                }

                _logger.LogInformation($"Executing uninstall command: {fileName} {args}");

                int exitCode = await ExecuteProcessAsync(fileName, args);

                if (exitCode == 0)
                {
                    await Task.Delay(10000); // O'chirishdan so'ng kutish

                    string checkUninstallString = _registryHelper.GetUninstallString(appName);
                    if (string.IsNullOrEmpty(checkUninstallString))
                    {
                        await SendApplicationForSocketAsync();
                        _logger.LogInformation($"Successfully uninstalled {appName}");
                        return AppResult.Success($"Ilova muvaffaqiyatli o‘chirildi: {appName}");
                    }

                    _logger.LogInformation($"check uninstallstring = {checkUninstallString}");
                    return AppResult.Fail($"Ilova o‘chirilmadi negadir");
                }
                else
                {
                    _logger.LogError($"Failed to uninstall {appName}, exit code: {exitCode}");
                    return AppResult.Fail($"Ilovani o‘chirishda xatolik bo‘ldi: {appName} (exit code: {exitCode})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during uninstallation of {appName}: {ex}");
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

                        return -1;
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


        /*public async Task<AppResult> InstallApplicationAsync(string appName, string[] arguments)
       {
           try
           {
               string jwtToken = await SQLiteHelper.GetJwtToken();
               if (string.IsNullOrEmpty(jwtToken))
               {
                   _logger.LogError("JWT token topilmadi!");
                   return AppResult.Fail("JWT token topilmadi");
               }

               string requestUrl = $"{_config.GetApiUrl()}{appName}";
               _logger.LogInformation($"Install app URL: {requestUrl}");

               string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
               string installerFolder = Path.Combine(programFiles, "DgzAIO", "Installers");

               Directory.CreateDirectory(installerFolder);

               string savePath = Path.Combine(installerFolder, appName);
               _logger.LogInformation($"Installer file path: {savePath}");

               bool downloaded = await _fileDownloader.DownloadFileAsync(requestUrl, savePath, jwtToken);
               if (!downloaded)
               {
                   _logger.LogError("Fayl yuklab olishda xatolik yuz berdi.");
                   return AppResult.Fail("Fayl yuklab olishda xatolik yuz berdi.");
               }

               if (!File.Exists(savePath))
               {
                   _logger.LogError($"Fayl topilmadi: {savePath}");
                   return AppResult.Fail($"Fayl topilmadi: {savePath}");
               }

               bool isMsi = Path.GetExtension(savePath).Equals(".msi", StringComparison.OrdinalIgnoreCase);
               bool installationSucceeded = false;

               foreach (var arg in arguments)
               {
                   _logger.LogInformation($"Trying install with argument: {arg}");

                   bool result = await TryInstallAsync(savePath, arg, isMsi);
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

                   //await SendApplicationForSocketAsync();
                   return AppResult.Success();
               }
               else
               {
                   _logger.LogError("All installation attempts failed.");
                   return AppResult.Fail("All installation attempts failed.");
               }
           }
           catch (Exception ex)
           {
               _logger.LogError($"Installation error: {ex}");
               return AppResult.Fail($"Installation error: {ex.Message}");
           }
       }
       private async Task<bool> TryInstallAsync(string filePath, string arguments, bool isMsi)
       {
           try
           {
               _logger.LogInformation($"[Process] Starting: {filePath} with arguments: {arguments}, isMsi: {isMsi}");

               using (var process = new Process())
               {
                   if (isMsi)
                   {
                       process.StartInfo.FileName = "msiexec";
                       process.StartInfo.Arguments = $"/I \"{filePath}\" /QN /NORESTART";
                       _logger.LogInformation($"[Process] msiexec command: {process.StartInfo.Arguments}");
                   }
                   else
                   {
                       process.StartInfo.FileName = filePath;
                       process.StartInfo.Arguments = arguments;
                       _logger.LogInformation($"[Process] Executable command: {process.StartInfo.Arguments}");
                   }

                   process.StartInfo.WorkingDirectory = Path.GetDirectoryName(filePath);
                   process.StartInfo.UseShellExecute = false;
                   process.StartInfo.CreateNoWindow = true;
                   process.StartInfo.RedirectStandardOutput = true;
                   process.StartInfo.RedirectStandardError = true;
                   process.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;

                   process.EnableRaisingEvents = true;
                   var tcs = new TaskCompletionSource<int>();

                   process.Exited += (sender, args) =>
                   {
                       _logger.LogInformation($"[Process] Process exited with code: {process.ExitCode}");
                       tcs.TrySetResult(process.ExitCode);
                   };

                   _logger.LogInformation("[Process] Starting process...");
                   process.Start();

                   _logger.LogInformation("[Process] Reading standard output...");
                   var outputTask = process.StandardOutput.ReadToEndAsync();

                   _logger.LogInformation("[Process] Reading standard error...");
                   var errorTask = process.StandardError.ReadToEndAsync();

                   int exitCode = await tcs.Task;

                   string output = await outputTask;
                   string error = await errorTask;

                   if (!string.IsNullOrWhiteSpace(output))
                   {
                       _logger.LogInformation($"[Process][StdOut]: {output}");
                   }

                   if (!string.IsNullOrWhiteSpace(error))
                   {
                       _logger.LogError($"[Process][StdErr]: {error}");
                   }

                   return exitCode == 0;
               }
           }
           catch (Exception ex)
           {
               _logger.LogError($"[Process] Exception: {ex}");
               return false;
           }
       }

       public async Task<AppResult> UninstallApplicationAsync(string appName, string[] arguments, string type)
       {
           try
           {
               var (uninstallValue, isMsi) = _registryHelper.GetUninstallString(appName);
               _logger.LogInformation($"Uninstall string/value: {uninstallValue} for {appName} (IsMSI: {isMsi})");

               if (string.IsNullOrEmpty(uninstallValue))
               {
                   string msg = $"Uninstall value for '{appName}' not found.";
                   _logger.LogError(msg);
                   return AppResult.Fail(msg);
               }

               if (type == "System" && isMsi)
               {
                   string msiCommand = $"msiexec /x \"{uninstallValue}\" /qn";
                   _logger.LogInformation($"Uninstalling MSI application '{appName}' with: {msiCommand}");

                   int exitCode = await ExecuteProcessAsync("cmd.exe", $"/C {msiCommand}");
                   if (exitCode == 0)
                   {
                       await SendApplicationForSocketAsync();
                       string msg = $"Successfully uninstalled MSI application '{appName}'.";
                       _logger.LogInformation(msg);
                       return AppResult.Success(msg);
                   }

                   string failMsg = $"Failed to uninstall MSI application '{appName}', exit code: {exitCode}";
                   _logger.LogError(failMsg);
                   return AppResult.Fail(failMsg);
               }

               foreach (var argument in arguments)
               {
                   string fullCommand = $"\"{uninstallValue}\" {argument}";
                   _logger.LogInformation($"Uninstalling '{appName}' with argument: {argument}");

                   int exitCode = await ExecuteProcessAsync("cmd.exe", $"/C {fullCommand}");

                   if (exitCode == 0)
                   {
                       await SendApplicationForSocketAsync();
                       string msg = $"Successfully uninstalled '{appName}' with argument: {argument}";
                       _logger.LogInformation(msg);
                       return AppResult.Success(msg);
                   }

                   _logger.LogError($"Failed to uninstall '{appName}' with argument: {argument}, exit code: {exitCode}");
               }

               string allFailMsg = $"All uninstall attempts for '{appName}' failed.";
               _logger.LogError(allFailMsg);
               return AppResult.Fail(allFailMsg);
           }
           catch (Exception ex)
           {
               string errorMsg = $"Error during uninstallation of '{appName}': {ex.Message}";
               _logger.LogError(errorMsg);
               return AppResult.Fail(errorMsg);
           }
       }
       private async Task<int> ExecuteProcessAsync(string fileName, string arguments)
       {
           try
           {
               _logger.LogInformation($"[Execute] Starting process: {fileName} + {arguments}");

               using (var process = new Process())
               {
                   process.StartInfo.FileName = fileName;
                   process.StartInfo.Arguments = arguments;
                   process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                   process.StartInfo.UseShellExecute = false;
                   process.StartInfo.CreateNoWindow = true;
                   process.StartInfo.RedirectStandardOutput = true;
                   process.StartInfo.RedirectStandardError = true;

                   var processCompletionSource = new TaskCompletionSource<bool>();

                   process.EnableRaisingEvents = true;
                   process.Exited += (sender, args) =>
                   {
                       _logger.LogInformation($"[Execute] Process exited with code: {process.ExitCode}");
                       processCompletionSource.TrySetResult(true);
                   };

                   _logger.LogInformation("[Execute] Starting process...");
                   process.Start();

                   _logger.LogInformation("[Execute] Reading standard output...");
                   var outputTask = process.StandardOutput.ReadToEndAsync();
                   _logger.LogInformation("[Execute] Reading standard error...");
                   var errorTask = process.StandardError.ReadToEndAsync();

                   await processCompletionSource.Task;

                   string output = await outputTask;
                   string error = await errorTask;

                   if (!string.IsNullOrWhiteSpace(output))
                       _logger.LogInformation($"[Execute] Output: {output}");

                   if (!string.IsNullOrWhiteSpace(error))
                       _logger.LogError($"[Execute] Error Output: {error}");

                   return process.ExitCode;
               }
           }
           catch (Exception ex)
           {
               _logger.LogError($"[Execute] Exception executing process: {ex.Message}");
               return -1;
           }
       }*/

        /*public async Task<AppResult> UninstallApplicationAsync(string appName, string[] arguments, string type)
       {
           try
           {
               if (string.IsNullOrEmpty(appName))
               {
                   _logger.LogError("Dastur nomi bo'sh bo'lmasligi kerak.");
                   return AppResult.Fail("Dastur nomi bo'sh bo'lmasligi kerak.");
               }

               var (uninstallValue, isMsi) = _registryHelper.GetUninstallString(appName);
               _logger.LogInformation($"Uninstall string/value: {uninstallValue} for {appName} (IsMSI: {isMsi})");

               if (string.IsNullOrEmpty(uninstallValue))
               {
                   string msg = $"Uninstall value for '{appName}' not found.";
                   _logger.LogError(msg);
                   return AppResult.Fail(msg);
               }

               bool uninstallSucceeded = false;
               if (type == "System" && isMsi)
               {
                   string msiCommand = $"msiexec /x \"{uninstallValue}\" /qn";
                   _logger.LogInformation($"Uninstalling MSI application '{appName}' with: {msiCommand}");

                   var result = await ExecuteProcessAsync("cmd.exe", $"/C {msiCommand}");
                   if (result)
                   {
                       await SendApplicationForSocketAsync();
                       string msg = $"Successfully uninstalled MSI application '{appName}'.";
                       _logger.LogInformation(msg);
                       return AppResult.Success(msg);
                   }

                   string failMsg = $"Failed to uninstall MSI application '{appName}'";
                   _logger.LogError(failMsg);
                   return AppResult.Fail(failMsg);
               }

               foreach (var argument in arguments)
               {
                   string fullCommand = $"\"{uninstallValue}\" {argument}";
                   _logger.LogInformation($"Uninstalling '{appName}' with argument: {argument}");

                   var result = await ExecuteProcessAsync("cmd.exe", $"/C {fullCommand}");
                   if (result)
                   {
                       await SendApplicationForSocketAsync();
                       string msg = $"Successfully uninstalled '{appName}' with argument: {argument}";
                       _logger.LogInformation(msg);
                       uninstallSucceeded = true;
                       break;
                   }

                   _logger.LogError($"Failed to uninstall '{appName}' with argument: {argument}");
               }

               if (uninstallSucceeded)
               {
                   return AppResult.Success($"Successfully uninstalled '{appName}'.");
               }

               string allFailMsg = $"All uninstall attempts for '{appName}' failed.";
               _logger.LogError(allFailMsg);
               return AppResult.Fail(allFailMsg);
           }
           catch (Exception ex)
           {
               string errorMsg = $"Error during uninstallation of '{appName}': {ex.Message}";
               _logger.LogError(errorMsg);
               return AppResult.Fail(errorMsg);
           }
       }
       private async Task<bool> ExecuteProcessAsync(string fileName, string arguments)
       {
           try
           {
               _logger.LogInformation($"[Process] Starting: {fileName} with arguments: {arguments}");

               using (var process = new Process())
               {
                   process.StartInfo.FileName = fileName;
                   process.StartInfo.Arguments = arguments;
                   process.StartInfo.UseShellExecute = false;
                   process.StartInfo.CreateNoWindow = true;
                   process.StartInfo.RedirectStandardOutput = true;
                   process.StartInfo.RedirectStandardError = true;
                   process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

                   var tcs = new TaskCompletionSource<int>();
                   process.EnableRaisingEvents = true;
                   process.Exited += (sender, args) =>
                   {
                       _logger.LogInformation($"[Process] Process exited with code: {process.ExitCode}");
                       tcs.TrySetResult(process.ExitCode);
                   };

                   _logger.LogInformation("[Process] Starting process...");
                   process.Start();

                   _logger.LogInformation("[Process] Reading standard output...");
                   var outputTask = process.StandardOutput.ReadToEndAsync();

                   _logger.LogInformation("[Process] Reading standard error...");
                   var errorTask = process.StandardError.ReadToEndAsync();

                   int exitCode = await tcs.Task;

                   string output = await outputTask;
                   string error = await errorTask;

                   if (!string.IsNullOrWhiteSpace(output))
                   {
                       _logger.LogInformation($"[Process][StdOut]: {output}");
                   }

                   if (!string.IsNullOrWhiteSpace(error))
                   {
                       _logger.LogError($"[Process][StdErr]: {error}");
                   }

                   return exitCode == 0;
               }
           }
           catch (Exception ex)
           {
               _logger.LogError($"[Process] Exception: {ex}");
               return false;
           }
       }*/

        /*private async Task<bool> TryInstallAsync(string filePath, string arguments, bool isMsi)
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
        }*/

        /*private async Task<int> ExecuteProcessAsync(string fileName, string arguments)
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

                    process.Start();

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(20000);
                    if (!exited)
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
                            string killOut = await killProc.StandardOutput.ReadToEndAsync();
                            string killErr = await killProc.StandardError.ReadToEndAsync();

                            _logger.LogInformation($"taskkill output: {killOut}");
                            if (!string.IsNullOrWhiteSpace(killErr))
                                _logger.LogError($"taskkill error: {killErr}");

                            killProc.WaitForExit();
                        }

                        return -1;
                    }

                    string output = await outputTask;
                    string error = await errorTask;

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
*/

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


    }
}
