#pragma warning disable CS8600, CS8601, CS8602, CS8625

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless;
using MySql.Data.MySqlClient;
using static TeslaLogger.Tools;

namespace TeslaLogger
{
    public partial class DBHelper
    {
        internal void GetEconomy_Wh_km(WebHelper wh)
        {
            ArgumentNullException.ThrowIfNull(wh);

            try
            {
                // Query economy data from database with detailed charging analytics
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    COUNT(*) AS anz,
    ROUND(charging_End.charge_energy_added / (charging_End.ideal_battery_range_km - charging.ideal_battery_range_km), 3) AS economy_Wh_km
FROM
    charging
INNER JOIN chargingstate ON charging.id = chargingstate.StartChargingID
LEFT OUTER JOIN charging AS charging_End ON chargingstate.EndChargingID = charging_End.id
WHERE
    TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 100
    AND chargingstate.EndChargingID - chargingstate.StartChargingID > 4
    AND charging_End.battery_level <= 90
    AND chargingstate.CarID = @CarID
    AND charging_End.charge_energy_added > 5
GROUP BY economy_Wh_km
ORDER BY anz DESC
LIMIT 1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        
                        // Extract and validate economy data
                        if (dr.Read())
                        {
                            int anz = Convert.ToInt32(dr["anz"], Tools.ciEnUS);
                            double wh_km = (double)dr["economy_Wh_km"];

                            // Log and store result
                            car.Log($"Economy from DB: {wh_km} Wh/km - count: {anz}");

                            wh.car.DBWhTR = wh_km;
                            wh.car.DBWhTRcount = anz;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        internal double GetLatestOdometer()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    EndKm
FROM
    trip
WHERE
    CarID = @carid
ORDER BY
    StartDate DESC
LIMIT 1", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return (double)dr[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Logfile.ExceptionWriter(ex, "getLatestOdometer");
            }

            return 0;
        }

        internal static void UpdateAllChargingMaxPower()
        {
            Tools.DebugLog("UpdateAllChargingMaxPower()");
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id,
    StartChargingID,
    EndChargingID,
    CarId
FROM
    chargingstate
WHERE
    max_charger_power IS NULL", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            int id = Convert.ToInt32(dr["id"], Tools.ciEnUS);
                            int StartChargingID = Convert.ToInt32(dr["StartChargingID"], Tools.ciEnUS);
                            int EndChargingID = Convert.ToInt32(dr["EndChargingID"], Tools.ciEnUS);
                            int carid = dr["CarId"] as Int32? ?? 1;

                            Car c = Car.GetCarByID(carid);
                            if (c is not null)
                            {
                                c.DbHelper.UpdateMaxChargerPower(id, StartChargingID, EndChargingID);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Logfile.Log(ex.Message);
            }
        }
    }
}
