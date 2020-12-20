using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Web.Script.Serialization;
using MySqlConnector;

namespace MockServer
{
    internal class MSSession
    {
        private static HashSet<MSSession> sessions = new HashSet<MSSession>();

        private string token;
        private DateTime start;
        private int sessionid;
        private string email;

        public string Token { get => token; set => token = value; }
        public DateTime Start { get => start; set => start = value; }
        public int Sessionid { get => sessionid; set => sessionid = value; }
        internal static HashSet<MSSession> Sessions { get => sessions; }
        public string Email { get => email; set => email = value; }

        public MSSession(string token, int sessionid, string email)
        {
            Tools.Log($"new mockserver session t:{token} sid:{sessionid}");
            this.token = token;
            this.start = DateTime.UtcNow;
            this.sessionid = sessionid;
            this.email = email;
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

        public APIServer()
        {
        }

        internal static void MockLogin(HttpListenerRequest request, HttpListenerResponse response)
        {
            Tools.Log($"MockLogin: {request.Url.LocalPath}");
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string data = reader.ReadToEnd();
                    object jsonResult = new JavaScriptSerializer().DeserializeObject(data);
                    Dictionary<string, object> json = ((Dictionary<string, object>)jsonResult);
                    foreach (string key in json.Keys)
                    {
                        if (key.Equals("email"))
                        {
                            MSSession session = CreateSession(json[key].ToString());
                            if (session != null)
                            {
                                response.StatusCode = (int)HttpStatusCode.OK;
                                WebServer.WriteString(response, @"{
  ""access_token"": """ + session.Token + @""",
  ""token_type"": ""bearer"",
  ""expires_in"": 3888000,
  ""refresh_token"": ""cba321"",
  ""created_at"": " + Tools.TimeStampNow() + @"
}
                            ");
                                return;
                            }
                        }
                    }
                }
                response.StatusCode = (int)HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            WebServer.WriteString(response, "");
        }

        internal static void MockEndpoint(HttpListenerRequest request, HttpListenerResponse response)
        {
            Tools.Log($"MockEndpoint: {request.Url.LocalPath}");
            string endpoint = string.Empty;
            Match m = Regex.Match(request.Url.LocalPath, @"/api/1/vehicles/([0-9]+)/data_request/([a-z_]+)");
            if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
            {
                // todo handle tesla_id
                endpoint = m.Groups[2].Captures[0].ToString();
            }
            else if (request.Url.LocalPath.Equals("/api/1/vehicles"))
            {
                endpoint = "vehicles";
            }
            string token = GetAuthorizationBearerToken(request);
            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(endpoint))
            {
                Tools.Log($"token: {token}");
                MSSession session = MSSession.GetSessionByToken(token);
                if (session == null)
                {
                    // force login
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    WebServer.WriteString(response, "");
                    return;
                }
                Dictionary<string, string> dbjson = Database.GetDatapoint(endpoint, session.GetTimestampDiff()).Result;
                StringBuilder JSON = new StringBuilder();
                if (endpoint.Equals("vehicles"))
                {
                    JSON.Append(@"{
    ""response"":
    [
        {
");
                }
                else
                {
                    JSON.Append(@"{
    ""response"":
    {
");
                }
                DBtoJSON(endpoint, dbjson, JSON);
                if (endpoint.Equals("vehicles"))
                {
                    JSON.Append(@"}
    ],
    ""count"":1
}
");
                }
                else
                {
                    JSON.Append(@"    }
}
");
                }
                response.AddHeader("Content-Type", "application/json; charset=utf-8");
                response.StatusCode = (int)HttpStatusCode.OK;
                string sJSON = new Tools.JsonFormatter(JSON.ToString()).Format();
                Tools.Log(sJSON);
                WebServer.WriteString(response, sJSON);
                return;
            }
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            WebServer.WriteString(response, "");
        }

        private static void DBtoJSON(string endpoint, Dictionary<string, string> dbjson, StringBuilder JSON)
        {
            HashSet<string> arrays = new HashSet<string>();
            HashSet<string> dictionaries = new HashSet<string>();
            foreach (string field in dbjson.Keys)
            {
                // special case: timestamp
                if (field.Equals("timestamp"))
                {
                    JSON.Append($"\"timestamp\":{Tools.TimeStampNowMilliseconds()}");
                }
                // arrays and dictionaries
                else if (field.Contains("__"))
                {
                    // array type
                    Match m = Regex.Match(field, @"(.+)__([0-9]+)");
                    if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
                    {
                        string key = m.Groups[1].Captures[0].ToString();
                        if (!arrays.Contains(key))
                        {
                            arrays.Add(key);
                            JSON.Append($"\"{key}\":" + Environment.NewLine);
                            JSON.Append("[" + Environment.NewLine);
                            var arrayelements = dbjson.Keys.Where(k => k.StartsWith($"{key}__"));
                            for (int index = 0; index < arrayelements.Count(); index++)
                            {
                                // not string
                                if (DBSchema.EndpointFieldDBType[endpoint].ContainsKey($"{key}__{index}") && !DBSchema.EndpointFieldDBType[endpoint][$"{key}__{index}"].Equals("TEXT"))
                                {
                                    JSON.Append(dbjson[$"{key}__{index}"]);
                                }
                                // treat everything else as string
                                else
                                {
                                    JSON.Append($"\"{dbjson[$"{key}__{index}"]}\"");
                                }
                                if (index < arrayelements.Count() - 1)
                                {
                                    JSON.Append(",");
                                }
                                JSON.Append(Environment.NewLine);
                            }
                            JSON.Append("]");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // dictionary type
                        m = Regex.Match(field, @"(.+)__([a-z_]+)");
                        if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
                        {
                            string key = m.Groups[1].Captures[0].ToString();
                            if (!dictionaries.Contains(key))
                            {
                                dictionaries.Add(key);
                                JSON.Append($"\"{key}\":" + Environment.NewLine);
                                JSON.Append("{" + Environment.NewLine);
                                var dictionaryelements = dbjson.Keys.Where(k => k.StartsWith($"{key}__"));
                                foreach (string dictionarykey in dictionaryelements) 
                                {
                                    Match m2 = Regex.Match(dictionarykey, @"(.+)__([a-z_]+)");
                                    if (m2.Success && m2.Groups.Count == 3 && m2.Groups[1].Captures.Count == 1 && m2.Groups[2].Captures.Count == 1)
                                    {
                                        // not string
                                        if (DBSchema.EndpointFieldDBType[endpoint].ContainsKey(dictionarykey) && !DBSchema.EndpointFieldDBType[endpoint][dictionarykey].Equals("TEXT"))
                                        {
                                            JSON.Append($"{m2.Groups[2].Captures[0].ToString()}:{dbjson[dictionarykey]}");
                                        }
                                        // treat everything else as string
                                        else
                                        {
                                            JSON.Append($"{m2.Groups[2].Captures[0].ToString()}:\"{dbjson[dictionarykey]}\"");
                                        }
                                        if (!dictionarykey.Equals(dictionaryelements.Last()))
                                        {
                                            JSON.Append(",");
                                        }
                                        JSON.Append(Environment.NewLine);
                                    }
                                }
                                JSON.Append("}");
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                }
                // not string
                else if (DBSchema.EndpointFieldDBType[endpoint].ContainsKey(field) && !DBSchema.EndpointFieldDBType[endpoint][field].Equals("TEXT"))
                {
                    JSON.Append(JSONAppend(dbjson, field));
                }
                // treat everything else as string
                else
                {
                    JSON.Append(JSONAppend(dbjson, field, true));
                }
                if (!field.Equals(dbjson.Keys.Last()))
                {
                    JSON.Append(",");
                }
                JSON.Append(Environment.NewLine);
            }
        }

        private static string GetAuthorizationBearerToken(HttpListenerRequest request)
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

        private static string JSONAppend(Dictionary<string, string> dbjson, string key, bool isstring = false)
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

        private static MSSession CreateSession(string email)
        {
            // try to find email in ms_cars
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SELECT ms_id, ms_sessionid FROM ms_cars where ms_email = @email", conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            if (dr.Read())
                            {
                                if (int.TryParse(dr[0].ToString(), out int carid) && int.TryParse(dr[1].ToString(), out int sessionid))
                                {
                                    // car found, generate random token
                                    return new MSSession(Tools.RandomString(32), sessionid, email);   
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            // car not found, so create it, assing session and return MSSession
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"INSERT INTO ms_cars (ms_email, ms_sessionid) VALUES (@ms_email, (SELECT COALESCE(MAX(ms_sessionid), 1) FROM ms_sessions))", conn))
                    {
                        cmd.Parameters.AddWithValue("@ms_email", email);
                        Tools.Log(cmd);
                        int rows = cmd.ExecuteNonQueryAsync().Result;
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    conn.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SELECT ms_id, ms_sessionid FROM ms_cars where ms_email = @email", conn))
                    {
                        cmd.Parameters.AddWithValue("@email", email);
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = cmd.ExecuteReader())
                        {
                            if (dr.Read())
                            {
                                if (int.TryParse(dr[0].ToString(), out int carid) && int.TryParse(dr[1].ToString(), out int sessionid))
                                {
                                    // car found, generate random token
                                    return new MSSession(Tools.RandomString(32), sessionid, email);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            // this should not happen
            return null;
        }
    }
}