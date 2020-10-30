using System.IO;
using System.Linq;

namespace MockServer
{
    public class Importer
    {
        public Importer()
        {
        }

        internal static void importFromDirectory(string dirname)
        {
            Program.Log($"importFromDirectory: {dirname}");
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
                    Program.Log("Check #1: dump contains endpoints charge_state, climate_state, drive_state, vehicle_state, vehicles");
                    foreach (FileInfo file in files)
                    {
                        Database.ImportJSONFile(file);
                    }
                }
                else
                {
                    Program.Log("Check #1: dump incomplete");
                }
            }
            else
            {
                // TODO
            }
        }
    }
}
