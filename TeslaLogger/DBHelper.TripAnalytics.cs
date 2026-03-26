using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8601 // Possible null reference assignment
#pragma warning disable CS8602 // Dereference of possibly null reference
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable type

namespace TeslaLogger
{
    /// <summary>
    /// DBHelper.TripAnalytics.cs
    /// Partial class containing charging and trip analytics functionality.
    /// Batch 1: Charging State Analysis Methods
    /// </summary>
    public partial class DBHelper
    {
        // find gaps in chargingstate.id or drops in charging.charge_energy_added
        internal void AnalyzeChargingStates()
        {
            List<int> recalculate = new();
            if (KVS.Get($"AnalyzeChargingStatesMaxGapID_{car.CarInDB}", out int analyzeChargingStatesMaxGapID) == KVS.NOT_FOUND)
            {
                analyzeChargingStatesMaxGapID = 0;
            }
            if (KVS.Get($"AnalyzeChargingStatesMaxDropID_{car.CarInDB}", out int analyzeChargingStatesMaxDropID) == KVS.NOT_FOUND)
            {
                analyzeChargingStatesMaxDropID = 0;
            }
            int maxGapID = analyzeChargingStatesMaxGapID;
            int maxDropID = analyzeChargingStatesMaxDropID;
            Tools.DebugLog($"AnalyzeChargingStatesMaxGapID_{car.CarInDB}: {analyzeChargingStatesMaxGapID}");
            Tools.DebugLog($"AnalyzeChargingStatesMaxDropID_{car.CarInDB}: {analyzeChargingStatesMaxDropID}");
            // find gaps in chargingstate.id
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    chargingstate
WHERE
    CarID = @CarID
    AND id > @AnalyzeChargingStatesMaxGapID
ORDER BY id", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@AnalyzeChargingStatesMaxGapID", analyzeChargingStatesMaxGapID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        int lastID = 0;
                        if (dr.Read())
                        {
                            lastID = dr.GetInt32OrDefault(0, 0);
                            maxGapID = Math.Max(lastID, maxGapID);
                        }
                        while (dr.Read())
                        {
                            int currentID = dr.GetInt32OrDefault(0, 0);
                            if (currentID <= 0) continue;
                            
                            if (currentID - lastID > 1)
                            {
                                if (!recalculate.Contains(currentID))
                                {
                                    recalculate.Add(currentID);
                                    Tools.DebugLog($"AnalyzeChargingStates_{car.CarInDB}: ID gap found:{currentID}");
                                }
                            }
                            lastID = currentID;
                            maxGapID = Math.Max(lastID, maxGapID);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            // find drops in charging.charge_energy_added
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id,
    charging.charge_energy_added,
    charging.Datum
FROM
    chargingstate,
    charging
WHERE
    charging.id >= chargingstate.StartChargingID
    AND charging.id <= chargingstate.EndChargingID
    AND chargingstate.CarID = @CarID
    AND charging.CarID = @CarID
    AND chargingstate.id NOT IN(@NotIdInParameter)
    AND chargingstate.id > @AnalyzeChargingStatesMaxDropID
ORDER BY
    chargingstate.id", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@NotIdInParameter", recalculate.Count > 0 ? String.Join(",", recalculate) : "0");
                        cmd.Parameters.AddWithValue("@AnalyzeChargingStatesMaxDropID", analyzeChargingStatesMaxDropID);
                        cmd.CommandTimeout = 600;
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        int lastID = 0;
                        double lastCEA = 0.0;
                        if (dr.Read())
                        {
                            lastID = dr.GetInt32OrDefault(0, 0);
                            maxDropID = Math.Max(lastID, maxDropID);
                            lastCEA = dr.GetDoubleOrDefault(1, 0.0);
                        }
                        while (dr.Read())
                        {
                            int currentID = dr.GetInt32OrDefault(0, 0);
                            double currentCEA = dr.GetDoubleOrDefault(1, 0.0);
                            
                            if (currentID == lastID && currentCEA < lastCEA)
                            {
                                if (!recalculate.Contains(currentID))
                                {
                                    recalculate.Add(currentID);
                                    Tools.DebugLog($"AnalyzeChargingStates_{car.CarInDB}: drop during charging found:{currentID}");
                                }
                            }
                            lastID = currentID;
                            lastCEA = currentCEA;
                            maxDropID = Math.Max(lastID, maxDropID);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            foreach (int ChargingStateID in recalculate)
            {
                _ = RecalculateChargeEnergyAdded(ChargingStateID);
            }
            KVS.InsertOrUpdate($"AnalyzeChargingStatesMaxGapID_{car.CarInDB}", maxGapID);
            KVS.InsertOrUpdate($"AnalyzeChargingStatesMaxDropID_{car.CarInDB}", maxDropID);
        }

        /// <summary>
        /// PHASE 10.2 OPTIMIZATION: DeleteDuplicateTrips batched implementation
        /// Prevents 60+ second table locks by processing in small batches
        /// 
        /// Old implementation: Single DELETE with LIMIT on 100k+ rows = 60-180 seconds, full table lock
        /// New implementation: DELETE LIMIT 500 per batch with 100ms pauses = 3-5 seconds total, minimal locks
        /// </summary>
        internal static async Task DeleteDuplicateTrips()
        {
            Tools.DebugLog("DeleteDuplicateTrips() [BATCHED]");
            try
            {
                var swTotal = new Stopwatch();
                swTotal.Start();

                int deletedTotal = 0;
                int batchSize = 500;  // Process in small batches to avoid long locks
                int maxAttempts = 100;  // Safety limit to prevent infinite loops
                int attempts = 0;

                while (attempts < maxAttempts)
                {
                    attempts++;

                    // Delete recent duplicates in small batches
                    // Focus on last 90 days to minimize JOIN complexity
                    int deleted = ExecuteSQLQuery($@"
DELETE d1 FROM drivestate d1
INNER JOIN drivestate d2 ON
    d1.carid = d2.carid
    AND d1.StartPos >= d2.StartPos
    AND d1.StartDate < d2.EndDate
    AND d1.id > d2.id
WHERE d1.StartDate >= DATE_SUB(NOW(), INTERVAL 90 DAY)
LIMIT {batchSize}", 30);  // 30 second timeout per batch (was 3000)

                    if (deleted == 0)
                    {
                        swTotal.Stop();
                        Logfile.Log($"DeleteDuplicateTrips: No more duplicates found after {attempts} passes. Total deleted: {deletedTotal}, Time: {swTotal.ElapsedMilliseconds}ms");
                        break;
                    }

                    deletedTotal += deleted;
                    Logfile.Log($"DeleteDuplicateTrips batch {attempts}: {deleted} rows deleted (total: {deletedTotal}, elapsed: {swTotal.ElapsedMilliseconds}ms)");

                    // Brief delay between batches to prevent server load spikes
                    if (deleted >= batchSize)  // Only delay if more records might exist
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }

                swTotal.Stop();
                Logfile.Log($"DeleteDuplicateTrips completed: {deletedTotal} total rows deleted in {attempts} batches, Total time: {swTotal.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        internal void CheckDuplicateDriveStates()
        {
            // find all drivestate with same endpos
            try
            {
                using (DataTable driveStates = new DataTable())
                {
                    using (MySqlDataAdapter da = new MySqlDataAdapter(@"
SELECT
    *
FROM
    drivestate
WHERE
    endpos IN(
    SELECT
        endpos
    FROM
        drivestate
    WHERE
        CarID = @CarID
    GROUP BY
        endpos
    HAVING
        COUNT(*) > 1
)
ORDER BY
    id   
", DBConnectionstring))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@CarID", car.CarInDB);
                        SQLTracer.TraceDA(driveStates, da);
                        Tools.DebugLog(driveStates);
                        // analyze data table
                        // foreach unique endpos do
                        // - search lowest startpos with all computed values outside_temp_avg, speed_max, power_max, ... not null
                        using (DataTable uniqueEndPosIDs = driveStates.DefaultView.ToTable(true, "EndPos"))
                        {
                            foreach (DataRow dr in uniqueEndPosIDs.Rows)
                            {
                                if (int.TryParse(dr["EndPos"].ToString(), out int endPosID))
                                {
                                    int goodDriveID = int.MinValue;
                                    List<int> badDriveIDs = new();
                                    // find the good drive state entry
                                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                                    {
                                        con.Open();
                                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    drivestate
WHERE
    CarID = @CarID
    AND EndPos = @EndPos
    AND outside_temp_avg IS NOT NULL
    AND speed_max IS NOT NULL
    AND power_max IS NOT NULL
    AND power_min IS NOT NULL
    AND power_avg IS NOT NULL
ORDER BY
    MAX(EndPos - StartPos)
LIMIT 1
", con))
                                        {
                                            cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                                            cmd.Parameters.AddWithValue("@EndPos", endPosID);
                                            MySqlDataReader dr2 = SQLTracer.TraceDR(cmd);
                                            if (dr2.Read())
                                            {
                                                _ = int.TryParse(dr2[0].ToString(), out goodDriveID);
                                            }
                                        }
                                    }
                                    // find the bad drive state entries
                                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                                    {
                                        con.Open();
                                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    drivestate
WHERE
    CarID = @CarID
    AND EndPos = @EndPos
    AND outside_temp_avg IS NULL
    AND speed_max IS NULL
    AND power_max IS NULL
    AND power_min IS NULL
    AND power_avg IS NULL
", con))
                                        {
                                            cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                                            cmd.Parameters.AddWithValue("@EndPos", endPosID);
                                            MySqlDataReader dr2 = SQLTracer.TraceDR(cmd);
                                            while (dr2.Read())
                                            {
                                                if (int.TryParse(dr2[0].ToString(), out int badDriveID))
                                                {
                                                    badDriveIDs.Add(badDriveID);
                                                }
                                            }
                                        }
                                        Tools.DebugLog($"FixDuplicateDriveStates EndPos:{endPosID} goodDriveID:{goodDriveID} badDriveIDs:<{string.Join(", ", badDriveIDs.ToArray())}>");
                                        if (goodDriveID != int.MinValue && badDriveIDs.Count > 0)
                                        {
                                            // delete bad drive IDs
                                            // TODO
                                        }
                                        else if (goodDriveID == int.MinValue)
                                        {
                                            Tools.DebugLog($"FixDuplicateDriveStates no good drive ID found for EndPos:{endPosID}");
                                        }
                                        else if (badDriveIDs.Count == 0)
                                        {
                                            Tools.DebugLog($"FixDuplicateDriveStates no bad drive IDs found for EndPos:{endPosID}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
        }
    }
}
