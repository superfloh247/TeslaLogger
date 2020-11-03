using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;

namespace MockServer
{
    internal class MSSession
    {
        private static HashSet<MSSession> sessions = new HashSet<MSSession>();

        private string token;
        private DateTime start;
        private int sessionid;

        public string Token { get => token; set => token = value; }
        public DateTime Start { get => start; set => start = value; }
        public int Sessionid { get => sessionid; set => sessionid = value; }

        public MSSession(string token, int sessionid)
        {
            Tools.Log("new mockserver session");
            this.token = token;
            this.start = DateTime.UtcNow;
            this.sessionid = sessionid;
            sessions.Add(this);
        }

        internal static MSSession GetSessionByToken(string token)
        {
            foreach (MSSession session in sessions)
            {
                if (session.Token.Equals(token))
                {
                    return session;
                }
            }
            return null;
        }
    }

    internal class APIServer
    {

        private HttpListener listener = null;

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
                    case bool _ when request.Url.LocalPath.Equals("/api/1/vehicles"):
                        mock_vehicles(request, response);
                        break;
                    default:
                        Tools.Log("unhandled: " + request.Url.LocalPath);
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
            if (request.Headers.AllKeys.Contains("Authorization"))
            {
                if (request.Headers["Authorization"] != null && request.Headers["Authorization"].StartsWith("Bearer "))
                {
                    return request.Headers["Authorization"].Replace("Bearer ", "");
                }
            }
            return string.Empty;
        }

        private void mock_vehicles(HttpListenerRequest request, HttpListenerResponse response)
        {
            Tools.Log($"mock: {request.Url.LocalPath}");
            // check if this a valid mockserver session
            string token = GetAuthorizationBearerToken(request);
            if (!string.IsNullOrEmpty(token))
            {
                Tools.Log($"token: {token}");
                MSSession session = MSSession.GetSessionByToken(token);
                if (session == null)
                {
                    // create new session
                    session = CreateSession(token);
                }
                
            }
            else
            {
                // TODO
            }
        }

        private MSSession CreateSession(string token)
        {
            return new MSSession(token, 1); // TODO make sessionid somehow configurable
        }
    }
}