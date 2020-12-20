using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace MockServer
{
    public class HTTPServer
    {
        private HttpListener listener = null;

        public HTTPServer()
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
                    case bool _ when request.Url.LocalPath.Equals("/"):
                        WebServer.Index(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listJSONDumps"):
                        WebServer.ListJSONDumps(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listSessions"):
                        WebServer.ListSessions(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/mockserver/listImports"):
                        WebServer.ListImports(request, response);
                        break;
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/mockserver/import/.+"):
                        WebServer.WriteString(response, "ImportFromDirectory");
                        Importer.ImportFromDirectory(request.Url.LocalPath.Split('/').Last());
                        break;
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/mockserver/deleteImport/.+"):
                        WebServer.WriteString(response, "DeleteImport");
                        WebServer.DeleteImport(request.Url.LocalPath.Split('/').Last());
                        break;
                    // Tesla API
                    case bool _ when request.Url.LocalPath.Equals("/api/1/vehicles"):
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/api/1/vehicles/[0-9]+/data_request/[a-z_]+"):
                        APIServer.MockEndpoint(request, response);
                        break;
                    case bool _ when request.Url.LocalPath.Equals("/oauth/token"):
                        APIServer.MockLogin(request, response);
                        break;
                    case bool _ when Regex.IsMatch(request.Url.LocalPath, @"/api/1/vehicles/[0-9]+/nearby_charging_sites"):
                        Tools.Log("TODO: " + request.Url.LocalPath);
                        WebServer.WriteString(response, "Retry later");
                        break;
                    default:
                        Tools.Log("unhandled: " + request.Url.LocalPath);
                        WebServer.WriteString(response, "");
                        break;
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
        }
    }
}
