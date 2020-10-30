using System;
using System.Net;
using System.Threading;

namespace MockServer
{
    internal class APIServer
    {

        private HttpListener listener = null;

        public APIServer()
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://*:24780/");
                listener.Start();
                Program.Log($"HttpListener bound to http://*:http://*:24780//");
            }
            catch (Exception ex)
            {
                Program.Log("Exception", ex);
            }
            while (listener != null)
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(OnContext, listener.GetContext());
                }
                catch (Exception ex)
                {
                    Program.Log("Exception", ex);
                }
            }
        }

        private void OnContext(object o)
        {
            try
            {
                HttpListenerContext context = o as HttpListenerContext;

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                switch (true)
                {
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listJSONDumps"):
                        WebServer.ListJSONDumps(request, response);
                        break;
                    default:
                        Program.Log("Request: " + request.Url.LocalPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Program.Log("Exception", ex);
            }
        }
    }
}