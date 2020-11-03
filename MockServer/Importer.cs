using System;
using System.Collections;
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
                SortedSet<FileInfo> files = new SortedSet<FileInfo>(Comparer<FileInfo>.Create((a, b) => a.Name.CompareTo(b.Name)));
                foreach (FileInfo file in dir.GetFiles())
                {
                    files.Add(file);
                }
                
                // check 1: TeslaLogger always requests /api/1/vehicles first, so skip all files before first vehicles.json

                while (!Tools.ExtractEndpointFromJSONFileName(files.First()).Equals("vehicles") && files.Count > 5)
                {
                    Tools.Log($"skip {files.First().Name}");
                    _ = files.Remove(files.First());
                }

                // check 2:
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
                    Tools.Log("Check #2 OK: dump contains endpoints charge_state, climate_state, drive_state, vehicle_state, vehicles");

                    // stage 3: check DB schema
                    // - parse all files sorted by end point and collect keys
                    // - check (and adjust) DB schema

                    if (CheckDBSchema())
                    {
                        Tools.Log("Check #3: OK: database schema up to date");

                        int sessionid = Database.CreateSession().Result;

                        if (sessionid < 1)
                        {
                            // TODO error handling
                        }

                        Tools.Log($"importing {files.Count} files ...");

                        // get timestamp offset from first file
                        FileInfo first = files.First();

                        // teslaAPI stores timestamp in unix time + milliseconds
                        // dumpJSON files have YYYYMMDDHHmmSSmmm

                        long tsoffset = Tools.FileDateToTimestamp(first);
                        foreach (FileInfo file in files)
                        {
                            Database.ImportJSONFile(file, tsoffset, sessionid);
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
            Tools.Log($"importFromDirectory: {dirname} done");
        }

        private static bool CheckDBSchema()
        {
            // check tables

            foreach (string tablename in DBSchema.tables.Values)
            {
                if (!DBTools.TableExists(tablename).Result)
                {
                    _ = DBTools.CreateTableWithIDAndFieldlistAndSessions(tablename).Result;
                }
            }
            // check columns

            foreach (string endpoint in DBSchema.tables.Keys)
            {
                foreach (string field in DBSchema.EndpointFieldDBType[endpoint].Keys)
                {
                    if (!DBTools.ColumnExists(DBSchema.tables[endpoint], field).Result)
                    {
                        _ = DBTools.CreateColumn(DBSchema.tables[endpoint], field, DBSchema.EndpointFieldDBType[endpoint][field], true).Result;
                    }
                }
            }

            // todo implement checks to return false

            return true;
        }
    }
}
