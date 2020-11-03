using System.Collections.Generic;

namespace MockServer
{
    public class DBSchema
    {
        public DBSchema()
        {
        }

        internal static Dictionary<string, string> tables = new Dictionary<string, string>() {
            { "charge_state" , "ms_charge_state" },
            { "climate_state", "ms_climate_state" },
            { "drive_state", "ms_drive_state" },
            { "vehicle_config", "ms_vehicle_config" },
            { "vehicle_state", "ms_vehicle_state" },
            { "vehicles", "ms_vehicles" },
        };
    }
}
