using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MockServer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Tools.Log("Starting");
            try
            {
                Thread APIServerThread = new Thread(() =>
                {
                    APIServer apiServer = new APIServer();
                })
                {
                    Name = "APIServerThread"
                };
                APIServerThread.Start();
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
        }

    }
}
