using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace SocketClient.Helpers
{
    public class ServiceCommunicator : Interfaces.IServiceCommunicator
    {
        private readonly Interfaces.ILogger _logger;

        public ServiceCommunicator(Interfaces.ILogger logger)
        {
            _logger = logger;
        }

        [ServiceContract]
        private interface IAgentService
        {
            [OperationContract]
            void UninstallAgent();
        }

        public void SendUninstallToService()
        {
            _logger.LogInformation("Sending uninstall request to service...");

            var binding = new NetNamedPipeBinding();
            var endpoint = new EndpointAddress("net.pipe://localhost/DgzAIOWindowsService");

            using (var factory = new ChannelFactory<IAgentService>(binding, endpoint))
            {
                var channel = factory.CreateChannel();
                var clientChannel = (IClientChannel)channel;

                bool success = false;

                try
                {
                    channel.UninstallAgent();
                    success = true;
                }
                catch (EndpointNotFoundException)
                {
                    _logger.LogError("Service not found, please ensure the service is running.");
                }
                catch (CommunicationException ex)
                {
                    _logger.LogError($"WCF communication error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        if (clientChannel.State != CommunicationState.Faulted)
                        {
                            clientChannel.Close();
                        }
                        else
                        {
                            clientChannel.Abort();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error closing channel: {ex.Message}");
                    }

                    if (success)
                    {
                        _logger.LogInformation("Uninstall request successfully sent to service.");
                    }
                }
            }
        }
    }
}
