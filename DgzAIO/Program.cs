using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DgzAIO
{
    class Program
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            try
            {
                //
                AgentUpdater agentUpdater = new AgentUpdater();
                await agentUpdater.CheckAndUpdate();

                Modules.Start();
                Console.WriteLine("All modules have been launched.!");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex}");
            }

            Console.ReadLine();
        }

    }
}
