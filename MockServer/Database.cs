using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            // collect columns
            SortedSet<string> columns = new SortedSet<string>();
            if (json != null)
            {
                foreach (string key in json.Keys.Where(k => k != null))
                {
                    switch (DBTools.TypeToDBType(json[key]))
                    {
                        case "_OBJECT_":
                            if (json[key] is Dictionary<string, object> dictionary && dictionary.Count > 0)
                            {
                                foreach (string subkey in dictionary.Keys)
                                {
                                    columns.Add($"{key}__{subkey}");
                                }
                            }
                            else if (json[key] is Object[] array)
                            {
                                int index = 0;
                                foreach (Object value in array)
                                {
                                    columns.Add($"{key}__{index}");
                                    index++;
                                }
                            }
                            break;
                        case "_NULL":
                            // TODO
                            break;
                        default:
                            columns.Add(key);
                            break;
                    }

                }
                // build SQL statement
                StringBuilder sqlcmd = new StringBuilder();
                sqlcmd.Append("INSERT INTO ");
                string endpoint = Tools.ExtractEndpointFromJSONFileName(file);
                sqlcmd.Append(DBSchema.tables[endpoint]);
                sqlcmd.Append(" (ms_fieldlist, ms_sessionid");
                foreach (string col in columns)
                {
                    sqlcmd.Append(string.Concat(", ", col));
                }
                sqlcmd.Append(" ) VALUES (@fieldlist, @sessionid");
                foreach (string col in columns)
                {
                    sqlcmd.Append(string.Concat(", @", col));
                }
                sqlcmd.Append(" )");
                // check DB schema
                // apply parameters and INSERT
                foreach (string field in columns)
                {
                    if (!DBTools.ColumnExists(DBSchema.tables[endpoint], field).Result)
                    {
                        if (DBSchema.EndpointFieldDBType[endpoint].ContainsKey(field))
                        {
                            _ = DBTools.CreateColumn(DBSchema.tables[endpoint], field, DBSchema.EndpointFieldDBType[endpoint][field], true).Result;
                        }
                        else
                        {
                            // fallback to TEXT storage
                            _ = DBTools.CreateColumn(DBSchema.tables[endpoint], field, "TEXT", true).Result;
                        }
                    }
                }
                try
                {
                    using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                    {
                        conn.Open();
                        using (MySqlCommand cmd = new MySqlCommand(sqlcmd.ToString(), conn))
                        {
                            cmd.Parameters.AddWithValue("@fieldlist", string.Join(",", columns));
                            cmd.Parameters.AddWithValue("@sessionid", sessionid);
                            foreach (string key in json.Keys.Where(k => k != null))
                            {
                                switch (key)
                                {
                                    case "timestamp":
                                        if (json[key] != null && long.TryParse(json[key].ToString(), out long timestamp))
                                        {
                                            if (timestamp - tsoffset >= 0)
                                            {
                                                cmd.Parameters.AddWithValue("@timestamp", timestamp - tsoffset);
                                            }
                                            else
                                            {
                                                // TODO error handling
                                            }
                                        }
                                        break;
                                    default:
                                        switch (DBTools.TypeToDBType(json[key]))
                                        {
                                            case "_OBJECT_":
                                                if (json[key] is Dictionary<string, object> dictionary && dictionary.Count > 0)
                                                {
                                                    foreach (string subkey in dictionary.Keys)
                                                    {
                                                        cmd.Parameters.AddWithValue($"@{key}__{subkey}", dictionary[subkey]);
                                                    }
                                                }
                                                else if (json[key] is Object[] array)
                                                {
                                                    int index = 0;
                                                    foreach (Object value in array)
                                                    {
                                                        cmd.Parameters.AddWithValue($"@{key}__{index}", array[index]);
                                                        index++;
                                                    }
                                                }
                                                break;
                                            case "_NULL":
                                                cmd.Parameters.AddWithValue($"@{key}", DBNull.Value);
                                                break;
                                            default:
                                                cmd.Parameters.AddWithValue($"@{key}", json[key]);
                                                break;
                                        }
                                        break;
                                }
                            }
                            Tools.Log(cmd);
                            int rows = cmd.ExecuteNonQueryAsync().Result;
                            if (rows > 0)
                            {
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Tools.Log("Exception", ex);
                }
            }
            else
            {
                // TODO json == null handling
            }
            Tools.Log($"ImportJSONFile {file.Name} done");
        }

        internal static async Task<Dictionary<string, string>> GetDatapoint(string endpoint, long timestampDiff)
        {
            Dictionary<string, string> dbjson = new Dictionary<string, string>();
            try
            {
                int id = 0;
                string fieldlist = string.Empty;
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"SELECT ms_id, ms_fieldlist, ABS(timestamp - {timestampDiff}) AS tsdiff FROM {DBSchema.tables[endpoint]} ORDER BY tsdiff ASC LIMIT 1", conn))
                    {
                        Tools.Log(cmd);
                        using (MySqlDataReader dr = await cmd.ExecuteReaderAsync())
                        {
                            if (dr.Read())
                            {
                                if (int.TryParse(dr[0].ToString(), out id))
                                {
                                    fieldlist = dr[1].ToString();
                                }
                            }
                        }
                    }
                }
                // datapoint found
                if (id > 0 && !string.IsNullOrEmpty(fieldlist))
                {
                    using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                    {
                        await conn.OpenAsync();
                        using (MySqlCommand cmd = new MySqlCommand($"SELECT {fieldlist} FROM {DBSchema.tables[endpoint]} WHERE ms_id = {id}", conn))
                        {
                            Tools.Log(cmd);
                            using (MySqlDataReader dr = await cmd.ExecuteReaderAsync())
                            {
                                if (dr.Read())
                                {
                                    foreach (string field in fieldlist.Split(','))
                                    {
                                        dbjson.Add(field, dr[field].ToString());
                                    }
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
            return dbjson;
        }

        internal static async Task<int> CreateSession()
        {
            int? newsessionid = DBTools.GetMaxValue("ms_sessions", "ms_sessionid").Result;
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
                    using (MySqlCommand cmd = new MySqlCommand($"INSERT ms_sessions (ms_sessionid) VALUES (@sessionid)", conn))
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
