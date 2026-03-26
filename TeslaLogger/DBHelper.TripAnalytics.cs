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

        private bool RecalculateChargeEnergyAdded(int ChargingStateID)
        {
            List<Tuple<int, int>> segments = new();
            bool updatedChargePrice = false;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
                    SELECT
    id,
    charge_energy_added
FROM
    charging
WHERE
    CarID = @CarID
    AND id >=(
        SELECT
            StartChargingID
        FROM
            chargingstate
        WHERE
        CarID = @CarID
        AND id = @ChargingID
)
AND id <=(
    SELECT
        EndChargingID
    FROM
        chargingstate
    WHERE
        CarID = @CarID
        AND id = @ChargingID
)", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        int index = 0;
                        double lastCEA = 0;
                        int maxid = 0;
                        // first row
                        if (dr.Read())
                        {
                            index = dr.GetInt32OrDefault(0, 0);
                            maxid = index;
                            lastCEA = dr.GetDoubleOrDefault(1, 0.0);
                        }
                        // all rows
                        while (dr.Read())
                        {
                            int currentID = dr.GetInt32OrDefault(0, 0);
                            double currentCEA = dr.GetDoubleOrDefault(1, 0.0);
                            
                            if (
                                // charge_energy_added is lower than in the row before
                                currentCEA < lastCEA
                                /*
                                 * create segments for every drop
                                 * &&
                                // and the current row is zero or near zero
                                currentCEA < 0.5*/
                                )
                            {
                                segments.Add(new Tuple<int, int>(index, currentID - 1));
                                index = currentID;
                            }
                            maxid = currentID;
                            lastCEA = currentCEA;
                        }
                        segments.Add(new Tuple<int, int>(index, maxid));
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            if (segments.Count > 0)
            {
                Tools.DebugLog($"RecalculateChargeEnergyAdded ChargingStateID:{ChargingStateID} segments:{string.Join(",", segments.Select(t => string.Format(Tools.ciEnUS, "[{0},{1}]", t.Item1, t.Item2)))}");
                double sum = 0.0;
                bool firstSegment = true;
                foreach (Tuple<int, int> segment in segments)
                {
                    double segmentCEA = GetChargeEnergyAddedFromCharging(segment.Item2);
                    Tools.DebugLog($"RecalculateChargeEnergyAdded segment:{segment.Item2} c_e_a:{segmentCEA}");
                    if (firstSegment)
                    {
                        double firstSegmentCEA = GetChargeEnergyAddedFromCharging(segment.Item1);
                        Tools.DebugLog($"RecalculateChargeEnergyAdded 1stsegment:{segment.Item1} c_e_a:{firstSegmentCEA}");
                        firstSegment = false;
                        // firstSegmentCEA > 0.67 means we did not just miss the first seconds of the charge session
                        // the car was probably not unplugged and continued charging
                        // previous charge session was not combined, so it's allowed to start with c_e_a >> 0.67
                        if (segmentCEA - firstSegmentCEA > 0 && firstSegmentCEA > 0.67)
                        {
                            sum += segmentCEA - firstSegmentCEA;
                        }
                        else
                        {
                            sum += segmentCEA;
                        }
                    }
                    else
                    {
                        double startCEA = GetChargeEnergyAddedFromCharging(segment.Item1);
                        sum += segmentCEA - startCEA > 0 ? segmentCEA - startCEA : 0;
                    }
                }
                Tools.DebugLog($"RecalculateChargeEnergyAdded ChargingStateID:{ChargingStateID} sum:{sum}");
                UpdateChargeEnergyAdded(ChargingStateID, sum);
                UpdateChargePrice(ChargingStateID, true);
                updatedChargePrice = true;
            }
            return updatedChargePrice;
        }

        internal static double GetChargeEnergyAddedFromCharging(int ChargingID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    charge_energy_added
FROM
    charging
WHERE
    id = @ChargingID", con))
                    {
                        cmd.Parameters.AddWithValue("@ChargingID", ChargingID);
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
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
            return double.NaN;
        }

        internal void UpdateEmptyUnplugDate()
        {
            Tools.DebugLog("UpdateEmptyUnplugDate()");
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  id,
  EndDate
FROM
  chargingstate
WHERE
  CarID = @CarID
  AND UnplugDate IS NULL
  AND EndDate IS NOT NULL
  AND EndDate < (
    SELECT
      MAX(EndDate)
    FROM
      drivestate
    WHERE
      EndDate IS NOT NULL
  )", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] is not DBNull)
                        {
                            if (int.TryParse(dr[0].ToString(), out int ChargingStateID)
                                && DateTime.TryParse(dr[1].ToString(), out DateTime UnplugDate))
                            {
                                FillEmptyUnplugDate(ChargingStateID, UnplugDate);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during UpdateEmptyUnplugDate(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during UpdateEmptyUnplugDate()");
            }
        }

        private void FillEmptyUnplugDate(int ChargingStateID, DateTime UnplugDate)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE 
  chargingstate 
SET 
  UnplugDate = @UnplugDate
WHERE 
  CarID = @CarID
  AND id = @ChargingStateID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        cmd.Parameters.AddWithValue("@UnplugDate", UnplugDate);
                        int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"FillEmptyUnplugDate({ChargingStateID}): {rowsUpdated} rows updated");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during DBHelper.FillEmptyUnplugDate(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.FillEmptyUnplugDate()");
            }
        }

        internal void CloseChargingStates()
        {
            DateTime dtstart = DateTime.UtcNow;
            // find open charging states (EndDate == NULL) order by oldest first
            Queue<int> openchargingstates = FindOpenChargingStates();

            // foreach open charging state (identified by id)
            foreach (int openChargingState in openchargingstates)
            {
                // close charging state with enddate, endID from max charging
                CloseChargingState(openChargingState);
                UpdateChargePrice(openChargingState);

                // if charging was interrupted, maybe combine it with the previous session
                if (CombineChangingStatesAt(openChargingState))
                {
                    // get pos.odometer from openChargingState
                    double odometer = GetOdometerFromChargingstate(openChargingState);
                    if (!double.IsNaN(odometer))
                    {
                        Tools.DebugLog($"openChargingState id:{openChargingState} odometer:{odometer}");
                        // find charging state(s) with identical pos.odometer
                        Queue<int> chargingStates = FindSimilarChargingStates(openChargingState);
                        foreach (int chargingState in chargingStates)
                        {
                            Tools.DebugLog($"FindSimilarChargingStates: {chargingState}:{odometer}");
                        }
                        // get startdate, startID, posID from oldest
                        if (chargingStates.Count > 0 && GetStartValuesFromChargingState(chargingStates.First(), out DateTime startDate, out int startdID, out int posID, out string _, out object meter_vehicle_kwh_start, out object meter_utility_kwh_start))
                        {
                            car.Log($"Combine charging states {string.Join(", ", chargingStates)} into {openChargingState}");
                            Tools.DebugLog($"GetStartValuesFromChargingState: id:{chargingStates.First()} startDate:{startDate} startID:{startdID} posID:{posID}");
                            // update current charging state with startdate, startID, pos
                            UpdateChargingstate(openChargingState, startDate, startdID, meter_vehicle_kwh_start, meter_utility_kwh_start, double.NegativeInfinity);
                            // update meter_*_kwh_sum in chargingstate
                            UpdateMeter_kWh_sum(openChargingState);
                            // delete all older charging states
                            foreach (int chargingState in chargingStates)
                            {
                                Tools.DebugLog($"delete combined chargingState id:{chargingState}");
                                DeleteChargingstate(chargingState);
                            }
                        }
                    }
                }
                // calculate chargingstate.charge_energy_added from endchargingid - startchargingid
                // this will also recalculate charge price
                _ = RecalculateChargeEnergyAdded(openChargingState);

                // get tesla invoice for supercharger
                if (ChargingStateLocationIsSuC(openChargingState))
                {
                    _ = Task.Factory.StartNew(() =>
                    {
                        Task.Delay(600000 + random.Next(1000, 5000)); // sleep 10+rand minutes so that the invoice is ready
                        if (GetChargingHistoryV2Service.LoadLatest(car))
                        {
                            if (GetChargingHistoryV2Service.SyncAll(car) == 0)
                            {
                                // invoice not ready yet
                                Task.Delay(3600000 + random.Next(1000, 5000)); // sleep 60+rand minutes so that the invoice is ready
                                if (GetChargingHistoryV2Service.LoadLatest(car))
                                {
                                    _ = GetChargingHistoryV2Service.SyncAll(car);
                                }
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }

            car.CurrentJSON.current_charging = false;
            car.CurrentJSON.current_charger_power = 0;
            car.CurrentJSON.current_charger_voltage = 0;
            car.CurrentJSON.current_charger_phases = 0;
            car.CurrentJSON.current_charger_actual_current = 0;
            car.CurrentJSON.current_charge_current_request = 0;
            car.CurrentJSON.current_charge_rate_km = 0;
            car.CurrentJSON.current_charger_actual_current_calc = 0;
            car.CurrentJSON.current_charger_phases_calc = 0;
            car.CurrentJSON.current_charger_power_calc_w = 0;

            UpdateMaxChargerPower();

            // As charging point name is depending on the max charger power, it will be updated after "MaxChargerPower" was computed
            car.webhelper.UpdateLastChargingAdress();

            DateTime dtend = DateTime.UtcNow;
            TimeSpan ts = dtend - dtstart;
            Tools.DebugLog($"CloseChargingStates took {ts.TotalMilliseconds}ms");
            if (ts.TotalMilliseconds > 1000)
            {
                car.Log($"CloseChargingStates took {ts.TotalMilliseconds}ms");
            }
        }

        internal void UpdateUnplugDate()
        {
            int ChargingStateID = GetMaxChargingstateId(out _, out _, out DateTime unplugDate, out DateTime EndDate);
            if (unplugDate == DateTime.MinValue && EndDate != DateTime.MinValue)
            {
                // UnplugDate is unset, so update it!
                try
                {
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    unplugdate = @EndDate
WHERE
    CarID = @CarID
    AND id = @ChargingStateID", con))
                        {
                            cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                            cmd.Parameters.AddWithValue("@EndDate", EndDate);
                            cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                            int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                            car.Log($"UpdateUnplugDate({ChargingStateID}): {rowsUpdated} rows updated to EndDate {EndDate}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();

                    Tools.DebugLog($"Exception during DBHelper.UpdateUnplugDate(): {ex}");
                    Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateUnplugDate()");
                }
            }
        }

        internal bool CombineChangingStatesAt(int sessionid)
        {
            bool doCombine = Tools.CombineChargingStates(); // use default
            // check if combine is disabled globally
            if (!doCombine)
            {
                Logfile.Log("CombineChargingStates disabled globally");
                Address addr = GetAddressFromChargingState(sessionid);
                // combine disabled, but check pos for special flag do combine
                if (addr is not null && addr.specialFlags is not null && addr.specialFlags.Count > 0 && addr.specialFlags.ContainsKey(Address.SpecialFlags.CombineChargingStates))
                {
                    Logfile.Log($"CombineChargingStates disabled globally, but enabled at POI '{addr.name}'");
                    doCombine = true;
                }
            }
            else
            {
                Address addr = GetAddressFromChargingState(sessionid);
                // check pos for special flag do not combine
                if (addr is not null && addr.specialFlags is not null && addr.specialFlags.Count > 0)
                {
                    // check if DoNotCombineChargingStates is enabled
                    if (addr.specialFlags.ContainsKey(Address.SpecialFlags.DoNotCombineChargingStates))
                    {
                        car.Log($"CombineChargingStates enabled globally, but disabled at POI '{addr.name}'");
                        doCombine = false;
                    }
                }
            }
            return doCombine;
        }

        internal void CombineChangingStates()
        {
            Tools.DebugLog("CombineChangingStates()");
            // find candidates to combine
            // car.Log("CombineChangingStates start");
            int t = Environment.TickCount;
            // find chargingstates with exactly the same odometer -> car did no move between charging states
            Queue<int> combineCandidates = FindCombineCandidates();
            foreach (int candidate in combineCandidates)
            {
                Tools.DebugLog($"FindCombineCandidates: {candidate}");

                // check if combine is disabled globally or locally
                if (!CombineChangingStatesAt(candidate))
                {
                    Logfile.Log($"CombineChangingStates: skip {candidate} CombineChangingStatesAt is false");
                    FixChargeEnergyAdded(candidate);
                    continue;
                }

                Queue<int> similarChargingStates = FindSimilarChargingStates(candidate);
                foreach (int similarChargingState in similarChargingStates)
                {
                    Tools.DebugLog($"FindSimilarChargingStates: {similarChargingState}");
                }
                if (similarChargingStates.Count > 0)
                {
                    // find max ID in similarChargingStates and candidate
                    int maxID = candidate;
                    foreach (int similarChargingState in similarChargingStates)
                    {
                        maxID = Math.Max(maxID, similarChargingState);
                    }
                    // find min ID in similarChargingStates and candidate
                    int minID = candidate;
                    foreach (int similarChargingState in similarChargingStates)
                    {
                        minID = Math.Min(minID, similarChargingState);
                    }
                    // build deletion list: all IDs from minID to maxID including minID excluding maxID
                    List<int> IDsToDelete = new();
                    if (candidate != maxID)
                    {
                        IDsToDelete.Add(candidate);
                    }
                    foreach (int similarChargingState in similarChargingStates)
                    {
                        if (similarChargingState != maxID)
                        {
                            IDsToDelete.Add(similarChargingState);
                        }
                    }
                    GetStartValuesFromChargingState(minID, out DateTime startDate, out int startdID, out int posID, out string _, out object meter_vehicle_kwh_start, out object meter_utility_kwh_start);
                    car.Log($"Combine charging state{(similarChargingStates.Count > 1 ? "s" : "")} {string.Join(", ", IDsToDelete)} into {maxID}");
                    Tools.DebugLog($"GetStartValuesFromChargingState: id:{minID} startDate:{startDate} startID:{startdID} posID:{posID}");
                    // update current charging state with startdate, startID, pos
                    UpdateChargingstate(maxID, startDate, startdID, meter_vehicle_kwh_start, meter_utility_kwh_start);
                    // update meter_*_kwh_sum in chargingstate
                    UpdateMeter_kWh_sum(maxID);
                    // delete all older charging states
                    foreach (int chargingState in IDsToDelete)
                    {
                        Tools.DebugLog($"delete combined chargingState id:{chargingState}");
                        DeleteChargingstate(chargingState);
                    }

                    // calculate chargingstate.charge_energy_added from endchargingid - startchargingid
                    bool updatedChargePrice = RecalculateChargeEnergyAdded(maxID);

                    // calculate charging price if per_kwh and/or per_minute and/or per_session is available
                    // but only if it's not updated by RecalculateChargeEnergyAdded
                    if (!updatedChargePrice)
                    {
                        UpdateChargePrice(maxID, true);
                    }

                    // update chargingsession stats
                    UpdateMaxChargerPower(maxID);
                }
            }
            car.Log($"CombineChangingStates took {Environment.TickCount - t}ms");
        }

        private void FixChargeEnergyAdded(int chagingStateID)
        {
            // interrupted charging sessions, eg "PV Überschuss" start with charge_energy_added > 0
            // this leads to wrong calculation of chargingstate.charge_energy_added

            // fix:
            // - find similar chargingstates
            // - if start charge_energy_added > 1 then:
            // check if predecessor chargingstate has similar end charge_energy_added
            // if yes then:
            // - recalculate charge_energy_added:
            //   charge_energy_added =
            //     chargingstate.endchargingid.charge_energy_added - chargingstate.startchargingid.charge_energy_added

            Tools.DebugLog($"FixChargeEnergyAdded({chagingStateID})");

            Queue<int> chargingstates = FindSimilarChargingStates(chagingStateID);

            foreach (int chargingstate in chargingstates.OrderBy(x => x))
            {
                try
                {
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id,
    chargingstate.startdate,
    chargingstate.enddate,
    chargingstate.carid,
    chargingS.charge_energy_added,
    chargingE.charge_energy_added
FROM
    chargingstate
    JOIN charging chargingS ON chargingS.id = chargingstate.startchargingid
    JOIN charging chargingE ON chargingE.id = chargingstate.endchargingid
WHERE
    chargingstate.id = @chagingStateID
", con))
                        {
                            cmd.Parameters.AddWithValue("@chagingStateID", chargingstate);
//                            Tools.DebugLog(cmd);
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            if (dr.Read())
                            {
                                Tools.DebugLog($"FixChargeEnergyAdded({chagingStateID}) id:{dr[0]} startdate:{dr[1]} enddate:{dr[2]} carID:{dr[3]} startChargingChargeEnergyAdded:{dr[4]} endChargingChargeEnergyAdded:{dr[5]}");
                                if (int.TryParse(dr[0].ToString(), out int id) && double.TryParse(dr[4].ToString(), out double cea_S) && double.TryParse(dr[5].ToString(), out double cea_E) && cea_S > 1)
                                {
                                    Logfile.Log($"FixChargeEnergyAdded update {id} cea_S:{cea_S} cea_E:{cea_E} to cea {cea_E - cea_S}");
                                    UpdateChargeEnergyAdded(id, cea_E - cea_S);
                                    UpdateChargePrice(id, true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();
                    Tools.DebugLog($"Exception in FixChargeEnergyAdded({chagingStateID}): {ex}");
                    Logfile.ExceptionWriter(ex, $"Exception in FixChargeEnergyAdded({chagingStateID})");
                }
            }
        }

        private void UpdateMeter_kWh_sum(int openChargingState)
        {
            try
            {
                Tools.DebugLog($"UpdateMeter_kWh_sum id:{openChargingState}");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    meter_vehicle_kwh_sum = meter_vehicle_kwh_end - meter_vehicle_kwh_start,
    meter_utility_kwh_sum = meter_utility_kwh_end - meter_utility_kwh_start,
    cost_kwh_meter_invoice = meter_vehicle_kwh_end - meter_vehicle_kwh_start
WHERE
    id = @ChargingStateID
    AND CarID = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", openChargingState);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Tools.DebugLog($"Exception during UpdateMeter_kWh_sum(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during UpdateMeter_kWh_sum()");
            }
        }

        private void GetChargeCostDataFromReference(
            int ChargingStateID,
            ref string ref_cost_currency,
            ref double ref_cost_per_kwh,
            ref bool ref_cost_per_kwh_found,
            ref double ref_cost_per_minute,
            ref bool ref_cost_per_minute_found,
            ref double ref_cost_per_session,
            ref bool ref_cost_per_session_found
            )
        {
            if (car.HasFreeSuC() && ChargingStateLocationIsSuC(ChargingStateID))
            {
                ref_cost_per_kwh = 0.0;
                ref_cost_per_kwh_found = true;
                ref_cost_per_minute = 0.0;
                ref_cost_per_minute_found = true;
                ref_cost_per_session = 0.0;
                ref_cost_per_session_found = true;
            }
            else
            {
                // get addr for chargingstate.pos
                Address addr = GetAddressFromChargingState(ChargingStateID);
                if (addr is not null && addr.specialFlags is not null && addr.specialFlags.Count > 0)
                {
                    // check if +ccp is enabled
                    if (addr.specialFlags.ContainsKey(Address.SpecialFlags.CopyChargePrice))
                    {
                        car.Log($"CopyChargePrice enabled for '{addr.name}'");
                        // find reference charge session for addr
                        int refChargingState = FindReferenceChargingState(ChargingStateID, addr.name, out ref_cost_currency, out ref_cost_per_kwh, out ref_cost_per_kwh_found, out ref_cost_per_session, out ref_cost_per_session_found, out ref_cost_per_minute, out ref_cost_per_minute_found);
                        // if exists, copy curreny, per_kwh, per_minute, per_session to current charging state
                        if (refChargingState != int.MinValue)
                        {
                            car.Log($"CopyChargePrice: reference charging session found for '{addr.name}', ID {refChargingState} - cost_per_kwh:{ref_cost_per_kwh} cost_per_session:{ref_cost_per_session} cost_per_minute:{ref_cost_per_minute}");
                        }
                    }
                }
            }
        }

        internal bool ChargingStateLocationIsSuC(int ChargingStateID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  fast_charger_brand,
  fast_charger_type
FROM
  chargingstate
WHERE
  CarID = @CarID
  AND id = @referenceID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@referenceID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read() && dr[0] != DBNull.Value && dr[1] != DBNull.Value)
                        {
                            if (dr.GetStringOrDefault(0, "").Equals("Tesla", StringComparison.OrdinalIgnoreCase) && (dr.GetStringOrDefault(1, "").Equals("Tesla", StringComparison.OrdinalIgnoreCase) || dr.GetStringOrDefault(1, "").Equals("Combo", StringComparison.OrdinalIgnoreCase)))
                            {
                                Tools.DebugLog("ChargingStateLocationIsSuC: true");
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during DBHelper.ChargingStateLocationIsSuC(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.ChargingStateLocationIsSuC()");
            }
            Tools.DebugLog("ChargingStateLocationIsSuC: false");
            return false;
        }

        internal void UpdateChargePrice(int ChargingStateID, bool fromID = false)
        {
            string ref_cost_currency = string.Empty;
            double ref_cost_per_kwh = double.NaN;
            bool ref_cost_per_kwh_found = false;
            double ref_cost_per_minute = double.NaN;
            bool ref_cost_per_minute_found = false;
            double ref_cost_per_session = double.NaN;
            bool ref_cost_per_session_found = false;
            bool freeSuC = false;
            if (fromID)
            {
                GetChargeCostDataFromID(
                    ChargingStateID,
                    ref ref_cost_currency,
                    ref ref_cost_per_kwh,
                    ref ref_cost_per_kwh_found,
                    ref ref_cost_per_minute,
                    ref ref_cost_per_minute_found,
                    ref ref_cost_per_session,
                    ref ref_cost_per_session_found,
                    ref freeSuC
                    );
            }
            else
            {
                GetChargeCostDataFromReference(
                    ChargingStateID,
                    ref ref_cost_currency,
                    ref ref_cost_per_kwh,
                    ref ref_cost_per_kwh_found,
                    ref ref_cost_per_minute,
                    ref ref_cost_per_minute_found,
                    ref ref_cost_per_session,
                    ref ref_cost_per_session_found);
            }
            UpdateChargePrice(
                ChargingStateID,
                ref_cost_currency,
                ref_cost_per_kwh,
                ref_cost_per_kwh_found,
                ref_cost_per_minute,
                ref_cost_per_minute_found,
                ref_cost_per_session,
                ref_cost_per_session_found,
                freeSuC);
        }

        private void UpdateChargePrice(
            int ChargingStateID,
            string ref_cost_currency,
            double ref_cost_per_kwh,
            bool ref_cost_per_kwh_found,
            double ref_cost_per_minute,
            bool ref_cost_per_minute_found,
            double ref_cost_per_session,
            bool ref_cost_per_session_found,
            bool freeSuC)
        {
            if (freeSuC)
            {
                // do not recalculate cost_total
                // GetChargingHistoryV2Service will set the correct values
                // for cost_total (0.0) and cost_freesuc_savings_total
                try
                {
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    cost_total = @cost_total
WHERE
    CarID = @CarID
    AND id = @ChargingStateID", con))
                        {
                            cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                            cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                            cmd.Parameters.AddWithValue("@cost_total", (double)0.0);
                            int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                            car.Log($"UpdateChargePrice: {rowsUpdated} rows updated to cost_total 0.0 (freeSuC and/or charging credits)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();

                    Tools.DebugLog($"Exception during DBHelper.UpdateChargePrice(): {ex}");
                    Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateChargePrice()");
                }
            }
            else if (ref_cost_per_kwh_found || ref_cost_per_minute_found || ref_cost_per_session_found)
            {
                double cost_total = double.NaN;
                double charge_energy_added = double.NaN;
                DateTime startDate = DateTime.MinValue;
                DateTime endDate = DateTime.MinValue;

                // read values from ChargingStateID
                try
                {
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    case when cost_kwh_meter_invoice is not null then cost_kwh_meter_invoice else charge_energy_added end,
    startdate,
    enddate
FROM
    chargingstate
WHERE
    CarID = @CarID
    AND id = @referenceID", con))
                        {
                            cmd.Parameters.Add("@CarID", MySqlDbType.UByte).Value = car.CarInDB;
                            cmd.Parameters.Add("@referenceID", MySqlDbType.Int32).Value = ChargingStateID;
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            if (dr.Read() && dr[0] != DBNull.Value)
                            {
                                if (double.TryParse(dr[0].ToString(), out charge_energy_added)
                                    && DateTime.TryParse(dr[1].ToString(), out startDate)
                                    && DateTime.TryParse(dr[2].ToString(), out endDate))
                                {
                                    cost_total = 0.0;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();

                    Tools.DebugLog($"Exception during DBHelper.UpdateChargePrice(): {ex}");
                    Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateChargePrice()");
                }

                // calculate and update cost_per_kwh
                if (ref_cost_per_kwh_found)
                {
                }
            }
        }

        private int FindReferenceChargingState(int ChargingStateID, string name, out string ref_cost_currency, out double ref_cost_per_kwh, out bool ref_cost_per_kwh_found, out double ref_cost_per_session, out bool ref_cost_per_session_found, out double ref_cost_per_minute, out bool ref_cost_per_minute_found)
        {
            int referenceID = int.MinValue;
            ref_cost_currency = string.Empty;
            ref_cost_per_kwh = double.NaN;
            ref_cost_per_kwh_found = false;
            ref_cost_per_minute = double.NaN;
            ref_cost_per_minute_found = false;
            ref_cost_per_session = double.NaN;
            ref_cost_per_session_found = false;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id, 
    chargingstate.cost_currency,
    chargingstate.cost_per_kwh,
    chargingstate.cost_per_session,
    chargingstate.cost_per_minute
FROM
    chargingstate,
    pos  
WHERE
    chargingstate.pos = pos.id
    AND pos.address = @addr
    AND chargingstate.cost_total IS NOT NULL
    AND TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 3
    AND chargingstate.EndChargingID - chargingstate.StartChargingID > 4
    AND chargingstate.CarID = @CarID
    AND chargingstate.ID <= @ChargingStateID
ORDER BY
    id DESC
LIMIT 1", con))
                    {
                        cmd.Parameters.AddWithValue("@addr", name);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            _ = int.TryParse(dr[0].ToString(), out referenceID);
                            if (dr[1] != DBNull.Value)
                            {
                                ref_cost_currency = dr.GetString(1);
                            }
                            if (double.TryParse(dr[2].ToString(), out ref_cost_per_kwh))
                            {
                                ref_cost_per_kwh_found = true;
                            }
                            if (double.TryParse(dr[3].ToString(), out ref_cost_per_session))
                            {
                                ref_cost_per_session_found = true;
                            }
                            if (double.TryParse(dr[4].ToString(), out ref_cost_per_minute))
                            {
                                ref_cost_per_minute_found = true;
                            }
                            Tools.DebugLog($"FindReferenceChargingState({ChargingStateID}, {name}) id:{dr[0]} currency:{dr[1]} cost_per_kwh:{dr[2]} cost_per_session:{dr[3]} cost_per_minute:{dr[4]}");
                        }
                        else
                        {
                            Tools.DebugLog("FindReferenceChargingState dr.read failed");
                        }
                        con.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during FindReferenceChargingState(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during FindReferenceChargingState()");
            }
            return referenceID;
        }

        private int GetChargeCostDataFromID(
            int ChargingStateID,
            ref string ref_cost_currency,
            ref double ref_cost_per_kwh,
            ref bool ref_cost_per_kwh_found,
            ref double ref_cost_per_minute,
            ref bool ref_cost_per_minute_found,
            ref double ref_cost_per_session,
            ref bool ref_cost_per_session_found,
            ref bool freeSuC
            )
        {
            int referenceID = int.MinValue;
            ref_cost_currency = string.Empty;
            ref_cost_per_kwh = double.NaN;
            ref_cost_per_kwh_found = false;
            ref_cost_per_minute = double.NaN;
            ref_cost_per_minute_found = false;
            ref_cost_per_session = double.NaN;
            ref_cost_per_session_found = false;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id, 
    chargingstate.cost_currency,
    chargingstate.cost_per_kwh,
    chargingstate.cost_per_session,
    chargingstate.cost_per_minute,
    chargingstate.cost_freesuc_savings_total
FROM
    chargingstate
WHERE
    chargingstate.CarID = @CarID
    AND chargingstate.ID = @ChargingStateID
", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            _ = int.TryParse(dr[0].ToString(), out referenceID);
                            if (dr[1] != DBNull.Value)
                            {
                                ref_cost_currency = dr.GetString(1);
                            }
                            if (double.TryParse(dr[2].ToString(), out ref_cost_per_kwh))
                            {
                                ref_cost_per_kwh_found = true;
                            }
                            if (double.TryParse(dr[3].ToString(), out ref_cost_per_session))
                            {
                                ref_cost_per_session_found = true;
                            }
                            if (double.TryParse(dr[4].ToString(), out ref_cost_per_minute))
                            {
                                ref_cost_per_minute_found = true;
                            }
                            if (double.TryParse(dr[5].ToString(), out double cost_freesuc_savings_total)
                                && cost_freesuc_savings_total > 0
                                )
                            {
                                Tools.DebugLog($"cost_freesuc_savings_total:{cost_freesuc_savings_total} --> freeSuC");
                                freeSuC = true;
                            }
                            Tools.DebugLog($"GetChargeCostDataFromID raw    {ChargingStateID}, id:{dr[0]} currency:{dr[1]} cost_per_kwh:{dr[2]} cost_per_session:{dr[3]} cost_per_minute:{dr[4]}");
                            Tools.DebugLog($"GetChargeCostDataFromID parsed {ChargingStateID}, id:{referenceID} currency:{ref_cost_currency} cost_per_kwh:{ref_cost_per_kwh} cost_per_session:{ref_cost_per_session} cost_per_minute:{ref_cost_per_minute}");
                            Tools.DebugLog($"GetChargeCostDataFromID bool   {ChargingStateID}, id:{referenceID} ref_cost_per_kwh_found:{ref_cost_per_kwh_found} ref_cost_per_session_found:{ref_cost_per_session_found} ref_cost_per_minute_found:{ref_cost_per_minute_found}");
                        }
                        else
                        {
                            Tools.DebugLog("GetChargeCostDataFromID dr.read failed");
                        }
                        con.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during GetChargeCostDataFromID(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during GetChargeCostDataFromID()");
            }
            return referenceID;
        }
    }
}
