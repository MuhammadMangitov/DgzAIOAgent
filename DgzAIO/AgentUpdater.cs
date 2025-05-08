using DgzAIO.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DgzAIO
{
    public class AgentUpdater
    {
        private static readonly string SERVER_URL = ConfigurationManagerDgzAIO.DgzApi.SERVER_URL_UPDATE_AGENT;
        private static readonly string DOWNLOAD_URL = ConfigurationManagerDgzAIO.DgzApi.DOWNLOAD_URL_AGENT_ZIP;

        private const string TOKEN_FOR_UPDATE = "Assalomu alaykum. DGZ server updateni tekshirishga keldim!!!";
        private static readonly HttpClient client = new HttpClient();
        private readonly string currentVersion;
        private readonly string localPath;

        public AgentUpdater()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            currentVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion;

            localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Agent");

            if (!Directory.Exists(localPath))
            {
                try
                {
                    Directory.CreateDirectory(localPath);
                }
                catch (UnauthorizedAccessException)
                {
                    Log("Administrator permissions are required to create the C:\\ProgramData\\Agent\\ directory!");
                    Console.WriteLine("Administrator permissions are required to create the C:\\ProgramData\\Agent\\ directory!");
                    throw;
                }
            }
            client.DefaultRequestHeaders.Add("authorization", TOKEN_FOR_UPDATE);
        }

        [ServiceContract]
        interface IAgentService
        {
            [OperationContract]
            void UpdateAgent(string zipPath, string localPath);
        }

        public async Task CheckAndUpdate()
        {
            try
            {
                Console.WriteLine($"{localPath}");

                Console.WriteLine("Checking for updates...");
                Log("Checking for updates...");

                string jsonResponse = await client.GetStringAsync(SERVER_URL);
                Log("Server response: " + jsonResponse);

                Console.WriteLine("Server response: " + jsonResponse);
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(jsonResponse);
                if (updateInfo == null)
                {
                    Log("Deserialization failed!");
                    Console.WriteLine("Deserialization failed!");
                    return;
                }

                Log("Deserialization successful!");
                Console.WriteLine("Deserialization successful!");
                Log("Server version: " + updateInfo.Version);
                Log("Current version: " + currentVersion);
                Console.WriteLine("Server version: " + updateInfo.Version);
                Console.WriteLine("Current version: " + currentVersion);

                if (IsNewerVersion(updateInfo.Version, currentVersion))
                {
                    string zipFileName = updateInfo.FileName;
                    string downloadUrl = DOWNLOAD_URL + zipFileName;
                    string zipPath = Path.Combine(localPath, zipFileName);

                    Log($"Downloading ZIP file: {downloadUrl}");
                    Console.WriteLine($"Downloading ZIP file: {downloadUrl}");
                    var zipBytes = await client.GetByteArrayAsync(downloadUrl);
                    try
                    {
                        File.WriteAllBytes(zipPath, zipBytes);
                        Log($"ZIP file saved: {zipPath}");
                    }
                    catch (IOException ex)
                    {
                        Log($"Error writing file to {zipPath}: {ex.Message}");
                        Console.WriteLine($"Error writing file to {zipPath}: {ex.Message}");
                        throw;
                    }

                    SendUpdateToService(zipPath);
                    Log("Update downloaded and sent to service.");
                    Console.WriteLine("Update downloaded and sent to service.");
                }
                else
                {
                    Log("Current version is up-to-date or newer.");
                    Console.WriteLine("Current version is up-to-date or newer.");
                }
            }
            catch (HttpRequestException ex)
            {
                Log($"Server communication error: {ex.Message}");
                Console.WriteLine($"Server communication error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"An error occurred: {ex.Message}");
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        private bool IsNewerVersion(string serverVersion, string currentVersion)
        {
            try
            {
                Version serverVer = new Version(serverVersion);
                Version currentVer = new Version(currentVersion);
                return serverVer > currentVer;
            }
            catch (Exception ex)
            {
                Log($"Error comparing versions: {ex.Message}");
                return false;
            }
        }

        private void SendUpdateToService(string zipPath)
        {
            Log("Sending update to service...");
            Console.WriteLine("Sending update to service...");
            var binding = new NetNamedPipeBinding();
            var endpoint = new EndpointAddress("net.pipe://localhost/DgzAIOWindowsService");

            using (var factory = new ChannelFactory<IAgentService>(binding, endpoint))
            {
                var channel = factory.CreateChannel();
                var clientChannel = (IClientChannel)channel;

                bool success = false;

                try
                {
                    channel.UpdateAgent(zipPath, localPath);
                    success = true;
                }
                catch (EndpointNotFoundException)
                {
                    Log("Service not found, please check if it is running.");
                }
                catch (CommunicationException ex)
                {
                    Log($"WCF communication error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log($"Error: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (clientChannel.State != CommunicationState.Faulted)
                        {
                            clientChannel.Close();
                        }
                    }
                    catch
                    {
                        clientChannel.Abort();
                    }

                    if (success)
                    {
                        Log("Update successfully sent to service.");
                    }
                }
            }
        }

        private void Log(string message)
        {
            string logPath = Path.Combine(localPath, "agent_updater.log");
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
            }
            catch
            {
                Console.WriteLine(message);
            }
        }
    }
}
