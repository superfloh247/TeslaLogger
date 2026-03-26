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
        private Queue<int> FindCombineCandidates()
        {
            Queue<int> combineCandidates = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  chargingstate.id
FROM
  chargingstate,
  pos
WHERE
  chargingstate.pos = pos.id
  AND pos.CarID=@CarID
  AND chargingstate.fast_charger_brand <> 'Tesla'
GROUP BY
  pos.odometer
HAVING
  COUNT(chargingstate.id) > 1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] is not DBNull)
                        {
                            if (int.TryParse(dr[0].ToString(), out int id))
                            {
                                combineCandidates.Enqueue(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during FindCombineCandidates(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during FindCombineCandidates()");
            }
            return combineCandidates;
        }

        private Queue<int> FindSimilarChargingStates(int referenceID)
        {
            Queue<int> chargingStates = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id
FROM
    chargingstate,
    pos
WHERE
    chargingstate.CarID = @CarID
    AND chargingstate.Pos = pos.id
    AND chargingstate.id <> @referenceID
    AND pos.odometer =(
        SELECT
            pos.odometer
        FROM
            chargingstate,
            pos
        WHERE
            pos.CarID = @CarID
            AND chargingstate.id = @referenceID
            AND chargingstate.Pos = pos.id
    ) AND chargingstate.conn_charge_cable =(
        SELECT
            conn_charge_cable
        FROM
            chargingstate
        WHERE
            chargingstate.CarID = @CarID
            AND id = @referenceID
    ) AND chargingstate.fast_charger_type =(
        SELECT
            fast_charger_type
        FROM
            chargingstate
        WHERE
            chargingstate.CarID = @CarID
            AND id = @referenceID
    )  AND chargingstate.wheel_type =(
        SELECT
            wheel_type
        FROM
            chargingstate
        WHERE
            chargingstate.CarID = @CarID
            AND id = @referenceID
    )
ORDER BY
    chargingstate.id ASC", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@referenceID", referenceID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] != DBNull.Value)
                        {
                            if (int.TryParse(dr[0].ToString(), out int id))
                            {
                                chargingStates.Enqueue(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during FindChargingStatesByOdometer(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during FindChargingStatesByOdometer()");
            }
            return chargingStates;
        }

        internal static bool GetStartValuesFromChargingState(int ChargingStateID, out DateTime startDate, out int startChargingID, out int posID, out string posName, out object meter_vehicle_kwh_start, out object meter_utility_kwh_start)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.StartDate,
    chargingstate.StartChargingID,
    chargingstate.pos,
    pos.address,
    chargingstate.meter_vehicle_kwh_start,
    chargingstate.meter_utility_kwh_start
FROM
    chargingstate
JOIN
    pos ON chargingstate.pos = pos.id
WHERE
    chargingstate.id = @ChargingStateID", con))
                    {
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read()
                            && dr[0] != DBNull.Value
                            && dr[1] != DBNull.Value
                            && dr[2] != DBNull.Value
                            && dr[3] != DBNull.Value
                            )
                        {
                            if (DateTime.TryParse(dr[0].ToString(), out startDate)
                                && int.TryParse(dr[1].ToString(), out startChargingID)
                                && int.TryParse(dr[2].ToString(), out posID)
                                )
                            {
                                posName = dr[3].ToString();
                                meter_vehicle_kwh_start = dr[4];
                                meter_utility_kwh_start = dr[5];
                                Tools.DebugLog($"GetStartValuesFromChargingState -> ChargingStateID:{ChargingStateID} startDate:{startDate} startdID:{startChargingID} posID:{posID} posName:{posName} meter_vehicle_kwh_start:{meter_vehicle_kwh_start} meter_utility_kwh_start:{meter_utility_kwh_start}");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Tools.DebugLog($"Exception during GetStartValuesFromChargingState(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during GetStartValuesFromChargingState()");
            }
            startDate = DateTime.MinValue;
            startChargingID = int.MinValue;
            posID = int.MinValue;
            posName = string.Empty;
            meter_vehicle_kwh_start = DBNull.Value;
            meter_utility_kwh_start = DBNull.Value;
            return false;
        }

        private Address GetAddressFromChargingState(int ChargingStateID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    pos.lat,
    pos.lng
FROM
    chargingstate,
    pos
WHERE
    chargingstate.CarID = @CarID
    AND chargingstate.Pos = pos.id
    AND chargingstate.id = @ChargingStateID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read() && dr[0] != DBNull.Value && dr[1] != DBNull.Value)
                        {
                            if (double.TryParse(dr[0].ToString(), out double lat)
                                && double.TryParse(dr[1].ToString(), out double lng))
                            {
                                Address addr = Geofence.GetInstance().GetPOI(lat, lng, false);
                                // works well enough, no debug output needed at the moment Tools.DebugLog("GetAddressFromChargingState: " + addr);
                                return addr;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Tools.DebugLog($"Exception during GetAddressFromChargingState(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during GetAddressFromChargingState()");
            }
            return null;
        }
    }
}
