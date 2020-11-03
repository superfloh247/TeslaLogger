using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;

namespace MockServer
{
    internal class APIServer
    {

        private HttpListener listener = null;
        private Dictionary<string, DateTime> sessions = new Dictionary<string, DateTime>();

        public APIServer()
        {
            InitHTTPServer();
            while (listener != null)
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(OnContext, listener.GetContext());
                }
                catch (Exception ex)
                {
                    Tools.Log("Exception", ex);
                }
            }
        }

        private void InitHTTPServer()
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://*:24780/");
                listener.Start();
                Tools.Log($"HttpListener bound to http://*:http://*:24780//");
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
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
                    // mockserver internals
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listJSONDumps"):
                        WebServer.ListJSONDumps(request, response);
                        break;
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/mockserver/import/.+"):
                        WebServer.WriteString(response, "");
                        Importer.importFromDirectory(request.Url.LocalPath.Split('/').Last());
                        break;
                    // Tesla API
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listJSONDumps"):
                        mock_vehicles(request, response);
                        break;
                    default:
                        Tools.Log("Request: " + request.Url.LocalPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
        }

        private string GetAuthorizationBearerToken(HttpListenerRequest request)
        {
            return string.Empty;
        }

        private void mock_vehicles(HttpListenerRequest request, HttpListenerResponse response)
        {
            Tools.Log(request.Url.LocalPath);
            // check if this a valid mockserver session
            string token = GetAuthorizationBearerToken(request);
            if (!string.IsNullOrEmpty(token))
            {
                if (!sessions.ContainsKey(token))
                {
                    // create new session
                    CreateSession(token);
                }
                
            }
            else
            {
                // TODO
            }
        }

        private void CreateSession(string token)
        {
            Tools.Log("new mockserver session");
            sessions.Add(token, DateTime.UtcNow);
        }
    }
}