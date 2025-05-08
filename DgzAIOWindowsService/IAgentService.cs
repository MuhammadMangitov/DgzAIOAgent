using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace DgzAIOWindowsService
{
    [ServiceContract]
    public interface IAgentService
    {
        [OperationContract]
        void UpdateAgent(string zipPath, string localPath);

        [OperationContract]
        void UninstallAgent();
    }
}
