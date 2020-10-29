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
            Log("Starting");
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
                Log("Exception", ex);
            }
        }

        internal static void Log(string text, Exception ex = null, [CallerFilePath] string _cfp = null, [CallerLineNumber] int _cln = 0)
        {
            Console.WriteLine($"DEBUG : {text} ({Path.GetFileName(_cfp)}:{_cln})");
        }

    }
}
