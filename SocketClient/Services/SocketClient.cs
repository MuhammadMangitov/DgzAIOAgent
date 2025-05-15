using DBHelper;
using Newtonsoft.Json;
using SocketClient.Interfaces;
using SocketClient.Models;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Services
{
    public class SocketClient
    {
        private readonly SocketIOClient.SocketIO _client;
        private readonly IApplicationManager _appManager;
        private readonly IServiceCommunicator _serviceCommunicator;
        private readonly ILogger _logger;
        private readonly IConfiguration _config;
        private bool _isRegistered = false;

        public SocketClient()
        {
            _logger = new Helpers.Logger();
            _config = new Helpers.ConfigurationManagerSocket();
            var httpClient = new HttpClient();
            var fileDownloader = new Utilities.FileDownloader(httpClient, _logger);
            var registryHelper = new Helpers.RegistryHelper(_logger);
            _appManager = new Managers.ApplicationManager(fileDownloader, registryHelper, _config, _logger);
            _serviceCommunicator = new Helpers.ServiceCommunicator(_logger);

            try
            {
                string socketUrl = _config.GetSocketUrl();
                _logger.LogInformation($"Initializing SocketIO client with URL: {socketUrl}");
                _client = new SocketIOClient.SocketIO(socketUrl, new SocketIOOptions
                {
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                    Reconnection = true,
                    ReconnectionAttempts = 50,
                    ReconnectionDelay = 2000,
                    ConnectionTimeout = TimeSpan.FromSeconds(20)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize SocketIO client: {ex.Message}");
                throw;
            }

            RegisterEvents();
        }


        private void RegisterEvents()
        {
            _client.On("connect", async response =>
            {
                _logger.LogInformation("Socket.io connected successfully! 1");
                if (!_isRegistered)
                {
                    await _client.EmitAsync("register", "SystemMonitor_Client");
                    _isRegistered = true;
                    _logger.LogInformation("Client registered.");
                }
            });

            _client.On("disconnect", response =>
            {
                string disconnectReason = response.GetValue<string>();
                _logger.LogInformation($"Socket disconnected at {DateTime.Now:yyyy-MM-dd HH:mm:ss}. URL: {_config.GetSocketUrl()}, Reason: {disconnectReason}. Reconnection will be attempted.");
                _isRegistered = false;
            });

            _client.On("command", async response =>
            {
                _logger.LogInformation($"Received command event: {response}");
                var commandData = response.GetValue<CommandData>();
                _logger.LogInformation($"Command: {commandData.command}, Type : {commandData.type}, App Name: {commandData.name}, Arguments: {commandData.arguments}");
                await HandleAppCommand(commandData);
            });

            _client.On("delete_agent", async response =>
            {
                _logger.LogInformation("Agent deletion requested.");
                await EmitDeleteResponse("success", "Agent is being deleted...");
                _serviceCommunicator.SendUninstallToService();

            });
        }

        public async Task<bool> StartSocketListener()
        {
            try
            {
                string jwtToken = await SQLiteHelper.GetJwtToken();
                if (string.IsNullOrEmpty(jwtToken))
                {
                    _logger.LogError("Token not found!");
                    return false;
                }

                _client.Options.ExtraHeaders = new Dictionary<string, string> { { "authorization", $"Bearer {jwtToken}" } };
                _logger.LogInformation($"SocketURL: {_config.GetSocketUrl()}");

                await _client.ConnectAsync();

                if (!_client.Connected)
                {
                    _logger.LogError("Failed to connect to socket server!");
                    return false;
                }

                _logger.LogInformation("Successfully connected to socket server!");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Socket connection error: {ex.Message}");
                return false;
            }
        }

        private async Task HandleAppCommand(CommandData data)
        {
            try
            {
                if (data == null || string.IsNullOrEmpty(data.command))
                {
                    _logger.LogError("Empty or invalid command!");
                    await EmitResponseAsync("unknown", false, "Empty or invalid command");
                    return;
                }

                string command = data.command.ToLower();
                string appName = data.name ?? "";
                string type = data.type ?? "";
                var arguments = (data.arguments ?? new List<string>()).ToArray();

                bool success = false;

                switch (command)
                {
                    case "delete_app":
                        _logger.LogInformation($"Uninstalling application: {appName}");
                        success = await _appManager.UninstallApplicationAsync(appName, arguments, type);
                        break;
                    case "install_app":
                    case "update_app":
                        success = await _appManager.InstallApplicationAsync(appName, command, arguments);
                        _logger.LogInformation($"InstallApplicationAsync completed. Success: {success}");
                        break;
                    default:
                        _logger.LogError($"Unknown command: {command}");
                        await EmitResponseAsync(command, false, appName);
                        return;
                }

                _logger.LogInformation($"About to emit response for command: {command}, Success: {success}, App Name: {appName}");
                await EmitResponseAsync(command, success, appName);
                _logger.LogInformation("EmitResponseAsync completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                await EmitResponseAsync(data?.command ?? "unknown", false, data?.name ?? "");
            }
        }

        private async Task EmitResponseAsync(string command, bool success, string appName)
        {
            _logger.LogInformation($"Emitting response for command: {command}, Success: {success}, App Name: {appName}");
            var result = new
            {
                status = success ? "success" : "error",
                command,
                name = appName
            };

            try
            {
                _logger.LogInformation($"Sending response to server: {JsonConvert.SerializeObject(result)}");
                if (!_client.Connected)
                {
                    _logger.LogError("Socket is not connected! Cannot emit response.");
                    return;
                }
                await _client.EmitAsync("response", result);
                _logger.LogInformation($"Command: {command}, Status: {result.status}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send response to server: {ex.Message}");
            }
        }

        private async Task EmitDeleteResponse(string status, string message)
        {
            try
            {
                _logger.LogInformation($"Emitting delete_agent response: {status}, {message}");
                await _client.EmitAsync("delete_agent", new
                {
                    status,
                    message
                });
                _logger.LogInformation("Delete response sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send delete response: {ex.Message}");
            }
        }
    }
}
