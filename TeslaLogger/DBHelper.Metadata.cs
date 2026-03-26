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
    }
}
