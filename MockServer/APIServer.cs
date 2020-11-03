using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Text;

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

        internal long GetTimestampDiff()
        {
            return Convert.ToInt64((DateTime.UtcNow - start).TotalMilliseconds);
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
                Dictionary<string, string> dbjson = Database.GetDatapoint("vehicles", session.GetTimestampDiff()).Result;

                /*
                 * {
    "response":
    [
        {
            "id":24342078186973552,
            "vehicle_id":1154578208,
            "vin":"5YJSA7H17FF088440",
            "display_name":"Tessi",
            "option_codes":"AD15,MDL3,PBSB,RENA,BT37,ID3W,RF3G,S3PB,DRLH,DV2W,W39B,APF0,COUS,BC3B,CH07,PC30,FC3P,FG31,GLFR,HL31,HM31,IL31,LTPB,MR31,FM3B,RS3H,SA3P,STCP,SC04,SU3C,T3CA,TW00,TM00,UT3P,WR00,AU3P,APH3,AF00,ZCST,MI00,CDM0",
            "color":null,
            "access_type":"OWNER",
            "tokens":
            [
                "5ace2425f973259e",
                "0307c122e535091d"
            ],
            "state":"online",
            "in_service":false,
            "id_s":"24342078186973552",
            "calendar_enabled":true,
            "api_version":10,
            "backseat_token":null,
            "backseat_token_updated_at":null,
            "vehicle_config":null
        }
    ],
    "count":1
}
                 */

                StringBuilder JSON = new StringBuilder();
                JSON.Append(@"{
    ""response"":
    [
        {
");
                JSON.Append(JSONAppend(dbjson, "id") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "vehicle_id") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "vin", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "display_name", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "option_codes", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "color", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "access_type", true) + "," + Environment.NewLine);
                if (dbjson.ContainsKey("tokens__0") && dbjson.ContainsKey("tokens__1"))
                {
                    JSON.Append("\"tokens\":" + Environment.NewLine);
                    JSON.Append("[" + Environment.NewLine);
                    JSON.Append($"\"{dbjson["tokens__0"]}\"," + Environment.NewLine);
                    JSON.Append($"\"{dbjson["tokens__1"]}\"" + Environment.NewLine);
                    JSON.Append("]," + Environment.NewLine);
                }
                JSON.Append(JSONAppend(dbjson, "state", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "in_service") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "id_s", true) + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "calendar_enabled") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "api_version") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "backseat_token") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "backseat_token_updated_at") + "," + Environment.NewLine);
                JSON.Append(JSONAppend(dbjson, "vehicle_config", true) + Environment.NewLine);
                JSON.Append(@"}
    ],
    ""count"":1
}
");
                response.AddHeader("Content-Type", "application/json; charset=utf-8");
                Tools.Log(JSON.ToString());
                WebServer.WriteString(response, JSON.ToString());
            }
            else
            {
                // TODO
            }
        }

        private string JSONAppend(Dictionary<string, string> dbjson, string key, bool isstring = false)
        {
            if (dbjson.ContainsKey(key)) {
                string value = dbjson[key];
                if (string.IsNullOrEmpty(value)) {
                    return $"\"{key}\":null";
                }
                if (value.Equals("True") || value.Equals("False"))
                {
                    value = value.ToLower();
                }
                if (isstring)
                {
                    return $"\"{key}\":\"{value}\"";
                }
                return $"\"{key}\":{value}";
            }
            else
            {
                return $"\"{key}\":null";
            }
        }

        private MSSession CreateSession(string token)
        {
            return new MSSession(token, 1); // TODO make sessionid somehow configurable
        }
    }
}