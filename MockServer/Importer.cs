using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace MockServer
{
    public class Importer
    {
        public Importer()
        {
        }

        internal static void importFromDirectory(string dirname)
        {
            Tools.Log($"importFromDirectory: {dirname}");
            if (Directory.Exists($"JSON/{dirname}"))
            {
                DirectoryInfo dir = new DirectoryInfo($"JSON/{dirname}");
                FileInfo[] files = dir.GetFiles();
                if (files.Length > 1)
                {
                    files = files.OrderBy(f => f.Name).ToArray();
                }

                // analyze files
                // we need at least one of those
                // - charge_state
                // - climate_state
                // - drive_state
                // - vehicle_state
                // - vehicles

                bool charge_state_found = false;
                bool climate_state_found = false;
                bool drive_state_found = false;
                bool vehicle_state_found = false;
                bool vehicles_found = false;

                foreach (FileInfo file in files)
                {
                    switch (Tools.ExtractEndpointFromJSONFileName(file))
                    {
                        case "charge_state":
                            charge_state_found = true;
                            break;
                        case "climate_state":
                            climate_state_found = true;
                            break;
                        case "drive_state":
                            drive_state_found = true;
                            break;
                        case "vehicle_state":
                            vehicle_state_found = true;
                            break;
                        case "vehicles":
                            vehicles_found = true;
                            break;
                    }
                }

                if (charge_state_found && climate_state_found && drive_state_found && vehicle_state_found && vehicles_found)
                {
                    Tools.Log("Check #1 OK: dump contains endpoints charge_state, climate_state, drive_state, vehicle_state, vehicles");

                    // stage 2: check DB schema
                    // - parse all files sorted by end point and collect keys
                    // - check (and adjust) DB schema

                    if (CheckDBSchema(files))
                    {
                        Tools.Log("");

                        int sessionid = Database.CreateSessionAsync().Result;

                        if (sessionid < 1)
                        {
                            // TODO error handling
                        }

                        FileInfo first = files.First();

                        foreach (FileInfo file in files)
                        {
                            Database.ImportJSONFile(file, first, sessionid);
                        }
                    }
                    else
                    {
                        // TODO
                    }
                }
                else
                {
                    Tools.Log("Check #1: dump incomplete");
                }
            }
            else
            {
                // TODO
            }
        }

        private static bool CheckDBSchema(FileInfo[] files)
        {
            SortedDictionary<string, SortedDictionary<string, string>> fields = new SortedDictionary<string, SortedDictionary<string, string>>();

            // parse files

            foreach (string endpoint in DBSchema.tables.Keys)
            {
                fields[endpoint] = new SortedDictionary<string, string>();
                foreach (FileInfo file in files.Where(f => f.Name.Contains(endpoint)))
                {
                    Dictionary<string, object> json = new Dictionary<string, object>();
                    switch (endpoint)
                    {
                        case "vehicles":
                                object jsonResult = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file.FullName));
                                object r1 = ((Dictionary<string, object>)jsonResult)["response"];
                                object[] r2 = (object[])r1;
                                object r3 = r2[0]; // TODO restriction! this will always take the first car and ignore all others
                                json = (Dictionary<string, object>)r3;
                            break;
                        default:
                                json = Tools.ExtractResponse(File.ReadAllText(file.FullName));
                            break;
                    }
                    foreach (string key in json.Keys.Where(k => k != null))
                    {
                        if (json[key] != null && !string.IsNullOrEmpty(DBTools.TypeToDBType(json[key].GetType())) && !fields[endpoint].ContainsKey(key))
                        {
                            switch(DBTools.TypeToDBType(json[key].GetType()))
                            {
                                case "_OBJECT_":
                                    if (json[key] is Dictionary<string, object> dictionary && dictionary.Count > 0)
                                    {
                                        foreach (string subkey in dictionary.Keys)
                                        {
                                            if (!fields[endpoint].ContainsKey($"{key}__{subkey}"))
                                            {
                                                fields[endpoint].Add($"{key}__{subkey}", DBTools.TypeToDBType(dictionary[subkey].GetType()));
                                            }
                                        }
                                    }
                                    else if (json[key] is Object[] array)
                                    {
                                        int index = 0;
                                        foreach (Object value in array)
                                        {
                                            if (!fields[endpoint].ContainsKey($"{key}__{index}"))
                                            {
                                                fields[endpoint].Add($"{key}__{index}", DBTools.TypeToDBType(value.GetType()));
                                            }
                                            index++;
                                        }
                                    }
                                    break;
                                default:
                                    fields[endpoint].Add(key, DBTools.TypeToDBType(json[key].GetType()));
                                    break;
                            }
                        }
                        else
                        {
                            // TODO
                        }
                    }
                }
            }
            // check tables

            foreach (string tablename in DBSchema.tables.Values)
            {
                if (!DBTools.TableExists(tablename).Result)
                {
                    _ = DBTools.CreateTableWithIDAndFields(tablename).Result;
                }
            }
            // check columns

            foreach (string endpoint in DBSchema.tables.Keys)
            {
                foreach (string field in fields[endpoint].Keys)
                {
                    if (!DBTools.ColumnExists(DBSchema.tables[endpoint], field).Result)
                    {
                        _ = DBTools.CreateColumn(DBSchema.tables[endpoint], field, fields[endpoint][field], true);
                    }
                }
            }

            // todo implement checks to return false

            return true;
        }
    }
}
