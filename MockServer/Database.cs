using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MySqlConnector;

namespace MockServer
{
    public class Database
    {
        // TODO read from mockserversettings.json
        internal static string DBConnectionstring = "Server=teslalogger;Database=teslaloggermock;Uid=root;Password=teslalogger;CharSet=utf8mb4;";

        public Database()
        {
        }

        internal static void ImportJSONFile(FileInfo file, long tsoffset, int sessionid)
        {
            Tools.Log($"ImportJSONFile {file.Name}");
            Dictionary<string, object> json = new Dictionary<string, object>();
            switch (Tools.ExtractEndpointFromJSONFileName(file))
            {
                case "vehicles":
                    object jsonResult = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file.FullName));
                    object r1 = ((Dictionary<string, object>)jsonResult)["response"];
                    object[] r2 = (object[])r1;
                    object r3 = r2[0]; // TODO restriction! this will always take the first car and ignore all others
                    json = (Dictionary<string, object>)r3;
                    // inject timestamp from file name
                    json.Add("timestamp", Tools.FileDateToTimestamp(file));
                    break;
                default:
                    json = Tools.ExtractResponse(File.ReadAllText(file.FullName));
                    break;
            }
            foreach (string key in json.Keys.Where(k => k != null))
            {
                switch (key)
                {
                    case "timestamp":
                        if (json[key] != null && long.TryParse(json[key].ToString(), out long timestamp))
                        {
                            Tools.Log($"file {file.Name} ts: {timestamp} fts: {Tools.FileDateToTimestamp(file)} diff: {timestamp - tsoffset}");
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        internal static async Task<int> CreateSessionAsync()
        {
            int? newsessionid = DBTools.GetMaxValue("ms_sessions", "sessionid").Result;
            if (newsessionid == null)
            {
                newsessionid = 1;
            }
            else
            {
                newsessionid += 1;
            }
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"INSERT ms_sessions (sessionid) VALUES (@sessionid)", conn))
                    {
                        cmd.Parameters.AddWithValue("@sessionid", newsessionid);
                        Tools.Log(cmd);
                        int rows = cmd.ExecuteNonQueryAsync().Result;
                        if (rows > 0)
                        {
                            return (int)newsessionid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return -1;
        }

    }
}
