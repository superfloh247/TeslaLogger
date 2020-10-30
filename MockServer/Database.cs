using System;
using System.IO;

namespace MockServer
{
    public class Database
    {
        public Database()
        {
        }

        internal static void ImportJSONFile(FileInfo file)
        {
            Program.Log($"ImportJSONFile {file.Name}");
            switch (Tools.ExtractEndpointFromJSONFileName(file))
            {
                case "charge_state":
                    ImportChargeState(file);
                    break;
                case "climate_state":
                    ImportClimateState(file);
                    break;
                case "drive_state":
                    ImportDriveState(file);
                    break;
                case "vehicle_state":
                    ImportVehicleState(file);
                    break;
                case "vehicles":
                    ImportVehicles(file);
                    break;
            }
        }

        private static void ImportChargeState(FileInfo file)
        {
            throw new NotImplementedException();
        }

        private static void ImportClimateState(FileInfo file)
        {
            throw new NotImplementedException();
        }

        private static void ImportDriveState(FileInfo file)
        {
            throw new NotImplementedException();
        }

        private static void ImportVehicleState(FileInfo file)
        {
            throw new NotImplementedException();
        }

        private static void ImportVehicles(FileInfo file)
        {
            throw new NotImplementedException();
        }
    }
}
