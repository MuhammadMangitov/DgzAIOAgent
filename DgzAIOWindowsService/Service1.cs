using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceModel;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DgzAIOWindowsService
{
    public partial class Service1 : ServiceBase
    {
        private Thread workerThread;
        private ServiceHost serviceHost;
        private bool isRunning = true;
        private readonly string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgzAIO.exe");

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            serviceHost = new ServiceHost(typeof(AgentService));
            serviceHost.AddServiceEndpoint(
                typeof(IAgentService),
                new NetNamedPipeBinding(),
                "net.pipe://localhost/DgzAIOWindowsService");
            serviceHost.Open();

            workerThread = new Thread(MonitorAndStartDgzAIO) { IsBackground = true };
            workerThread.Start();

            Logger.Log("Service started with WCF host.");
        }

        private void MonitorAndStartDgzAIO()
        {
            while (isRunning)
            {
                try
                {
                    if (IsInternetAvailable())
                    {
                        var processes = Process.GetProcessesByName("DgzAIO");
                        if (processes.Length == 0)
                        {
                            StartDgzAIO();
                            Logger.Log("DgzAIO started because internet is available.");
                        }
                    }
                    else
                    {
                        Logger.Log("Internet not available. Skipping DgzAIO start.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Monitoring error: {ex.Message}");
                }

                Thread.Sleep(5000);
            }
        }

        private bool IsInternetAvailable()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send("8.8.8.8", 3000); // Google DNS
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private void StartDgzAIO()
        {
            try
            {
                if (!File.Exists(exePath))
                {
                    Logger.LogError($"DgzAIO.exe not found: {exePath}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"DgzAIO startup error: {ex.Message}");
            }
        }

        protected override void OnStop()
        {
            isRunning = false;

            if (workerThread != null && workerThread.IsAlive)
                workerThread.Join(3000);

            if (serviceHost != null)
                serviceHost.Close();

            Logger.Log("Service stopped.");
        }
    }
}

/*using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DgzAIOWindowsService
{
public partial class Service1 : ServiceBase
{
private Thread workerThread;
private ServiceHost serviceHost;
private bool isRunning = true;
private readonly string serviceDir = AppDomain.CurrentDomain.BaseDirectory;
private readonly string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DgzAIO.exe");

```
    public Service1()
    {
        InitializeComponent();
    }

    protected override void OnStart(string[] args)
    {
        string projectDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DgzAIO");

        string dbPath = Path.Combine(projectDataPath, "DgzAIODb");
        string logsPath = Path.Combine(projectDataPath, "Logs");

        serviceHost = new ServiceHost(typeof(AgentService));
        serviceHost.AddServiceEndpoint(
            typeof(IAgentService),
            new NetNamedPipeBinding(),
            "net.pipe://localhost/DgzAIOWindowsService");
        serviceHost.Open();

        workerThread = new Thread(MonitorAndStartDgzAIO) { IsBackground = true };
        workerThread.Start();

        Logger.Log("The service and WCF are up and running.");
    }
    private void MonitorAndStartDgzAIO()
    {
        while (isRunning)
        {
            try
            {
                var processes = Process.GetProcessesByName("DgzAIO");
                if (processes.Length == 0)
                {
                    StartDgzAIO();
                    Logger.Log("DgzAIO has been restarted.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Monitoring error: {ex.Message}");
            }

            Thread.Sleep(5000);
        }
    }


    private void StartDgzAIO()
    {
        try
        {
            if (!File.Exists(exePath))
            {
                Logger.LogError($"DgzAIO.exe not found: {exePath}");
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };

            using (Process process = new Process { StartInfo = startInfo })
            {
                process.Start();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"DgzAIO startup error: {ex.Message}");
        }
    }

    protected override void OnStop()
    {
        isRunning = false;

        if (workerThread != null && workerThread.IsAlive)
            workerThread.Join(3000);

        if (serviceHost != null)
            serviceHost.Close();

        Logger.Log("Service stopped.");
    }

}
```

}
*/