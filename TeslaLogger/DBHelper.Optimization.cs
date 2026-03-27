using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using MySql.Data.MySqlClient;

#nullable enable

namespace TeslaLogger
{
    /// <summary>
    /// Database optimization and data migration utilities.
    /// Extracted from main DBHelper.cs for better code organization.
    /// </summary>
    internal partial class DBHelper
    {
        /// <summary>
        /// Ensures all records have a valid CarID, setting null values to 1 (default car).
        /// Runs on startup to clean up legacy data.
        /// </summary>
        /// <remarks>
        /// Iterates through all tables: can, car_version, charging, chargingstate, drivestate, pos, shiftstate, state
        /// Updates any NULL CarID values to 1 (the default/first vehicle).
        /// Performance: O(n) per table, 6000s command timeout for large datasets.
        /// </remarks>
        internal static void UpdateCarIDNull()
        {
            Tools.DebugLog("UpdateCarIDNull()");
            string[] tables = { "can", "car_version", "charging", "chargingstate", "drivestate", "pos", "shiftstate", "state" };
            foreach (string table in tables)
            {
                try
                {
                    int rows = 0;
                    int t = Environment.TickCount;
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand($@"
UPDATE
    {table}
SET
    carid = 1
WHERE
    carid IS NULL", con))
                        {
                            cmd.CommandTimeout = 6000;
                            _ = SQLTracer.TraceNQ(cmd, out _);
                        }
                    }
                    t = Environment.TickCount - t;

                    if (rows > 0)
                    {
                        Logfile.Log($"update {table} set carid = 1 where carid is null; ms: {t} / Rows: {rows}");
                    }

                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Logfile.Log(ex.ToString());
                }
            }
        }

        /// <summary>
        /// Checks if a database index exists.
        /// Used to selectively create indexes only if they don't already exist.
        /// </summary>
        /// <param name="index">The index name to check</param>
        /// <param name="table">The table name the index belongs to</param>
        /// <returns>True if index exists, false otherwise</returns>
        /// <remarks>
        /// Queries information_schema.statistics to check index existence.
        /// Safe approach: check before ALTER TABLE ADD INDEX to avoid duplicate key errors.
        /// </remarks>
        internal static bool IndexExists(string index, string table)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    *
FROM
    information_schema.statistics
WHERE
    table_name = '{table}'
    AND INDEX_NAME ='{index}'", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Migrates speed data from legacy floor() conversion to new round() conversion.
        /// One-time migration for historical data consistency.
        /// </summary>
        /// <remarks>
        /// Legacy Issue: Speed stored in km/h but API returns mph. Older versions used floor() for conversion,
        /// newer versions use round(). This causes historical data inconsistency.
        /// 
        /// Migration Strategy:
        /// 1. Add temporary index on speed for performance (idx_migration_speed)
        /// 2. Iterate speedpoints and convert legacy floor values to new round values
        /// 3. Recalculate drive statistics for all vehicles (UpdateDriveStatistics)
        /// 4. Drop temporary index and optimize table
        /// 5. Create status file to prevent re-running
        /// 
        /// Performance: 6000s timeout per query, may take several minutes for large datasets
        /// </remarks>
        internal static void MigrateFloorRound()
        {
            string migrationstatusfile = "migrate_floor_round.txt";

            /*
             * DB stores speed in km/h
             * API has speed in mph
             * rounding takes place twice: car display in km/h -> API speed in mph -> mpt to km/h in TeslaLogger
             * 
             * migrate errors coming from older versions originating from floor() vs. round()
             * 
             */

            if (!File.Exists(migrationstatusfile))
            {
                try
                {
                    StringBuilder migrationlog = new StringBuilder();
                    Logfile.Log("MigrateFloorRound() start");
                    migrationlog.Append($"{DateTime.Now} MigrateFloorRound() start{Environment.NewLine}");

                    // add indexes to speed up things
                    Logfile.Log("MigrateFloorRound() ADD INDEX speed");
                    migrationlog.Append($"{DateTime.Now} ADD INDEX speed{Environment.NewLine}");
                    int sqlresult = ExecuteSQLQuery("ALTER TABLE pos ADD INDEX idx_migration_speed (speed)", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    // get max speed

                    int maxspeed_kmh = 0;

                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(speed)
FROM
    pos", con))
                        {
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            if (dr.Read() && dr[0] != DBNull.Value)
                            {
                                _ = int.TryParse(dr[0].ToString(), out maxspeed_kmh);
                            }
                        }
                        con.Close();
                    }

                    if (maxspeed_kmh == 0)
                    {
                        maxspeed_kmh = 500;
                    }

                    Logfile.Log($"maxspeed_kmh: {maxspeed_kmh}");
                    migrationlog.Append($"maxspeed_kmh: {maxspeed_kmh}{Environment.NewLine}");

                    // migrate floor round error for pos.speed

                    for (int speed_mph = (int)Math.Round(maxspeed_kmh * 0.62137119223733) + 1; speed_mph > 0; speed_mph--)
                    {
                        int speed_floor = (int)(speed_mph * 1.609344); // old conversion
                        int speed_round = (int)Tools.MphToKmhRounded(speed_mph); // new conversion
                        if (speed_floor != speed_round)
                        {
                            DateTime start = DateTime.Now;
                            Logfile.Log($"MigrateFloorRound(): speed {speed_floor} -> {speed_round}");
                            migrationlog.Append($"{DateTime.Now} speed {speed_floor} -> {speed_round}{Environment.NewLine}");
                            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                            {
                                con.Open();
                                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    pos
SET
    speed = @speedround
WHERE
    speed = @speedfloor", con))
                                {
                                    cmd.Parameters.Add("speedround", MySqlDbType.Int32).Value = speed_round;
                                    cmd.Parameters.Add("speedfloor", MySqlDbType.Int32).Value = speed_floor;
                                    int updated_rows = SQLTracer.TraceNQ(cmd, out _);
                                    Logfile.Log($" rows updated: {updated_rows} duration: {(DateTime.Now - start).TotalMilliseconds}ms");
                                    migrationlog.Append($"{DateTime.Now} rows updated: {updated_rows} duration: {(DateTime.Now - start).TotalMilliseconds}ms{Environment.NewLine}");
                                }
                                con.Close();
                            }
                        }
                    }

                    // update all drivestate statistics
                    foreach (Car c in Car.Allcars)
                    {
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    StartPos,
    EndPos
FROM
    drivestate
WHERE
    CarID = @CarID", con))
                            {
                                cmd.Parameters.Add("@CarID", MySqlDbType.UByte).Value = c.CarInDB;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                while (dr.Read())
                                {
                                    if (dr[0] is not null && int.TryParse(dr[0].ToString(), out int startpos)
                                        && dr[1] is not null && int.TryParse(dr[1].ToString(), out int endpos))
                                    {
                                        DateTime start = DateTime.Now;
                                        c.DbHelper.UpdateDriveStatistics(startpos, endpos, false);
                                        c.Log($"UpdateDriveStatistics: {startpos} -> {endpos} duration: {(DateTime.Now - start).TotalMilliseconds}ms");
                                        migrationlog.Append($"{DateTime.Now} {c.CarInDB}# UpdateDriveStatistics: {startpos} -> {endpos} duration: {(DateTime.Now - start).TotalMilliseconds}ms{Environment.NewLine}");
                                    }
                                }
                            }
                        }
                    }

                    // remove indexes
                    Logfile.Log("MigrateFloorRound() DROP INDEX speed");
                    migrationlog.Append($"{DateTime.Now} DROP INDEX speed{Environment.NewLine}");
                    sqlresult = ExecuteSQLQuery("ALTER TABLE pos DROP INDEX idx_migration_speed", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    // cleanup DB files
                    Logfile.Log("MigrateFloorRound() REBUILD");
                    migrationlog.Append($"{DateTime.Now} REBUILD{Environment.NewLine}");
                    sqlresult = ExecuteSQLQuery("ALTER TABLE pos FORCE", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    Logfile.Log("MigrateFloorRound() finished");
                    migrationlog.Append($"{DateTime.Now} MigrateFloorRound() finished{Environment.NewLine}");

                    // persist that migration ran successful to prevent another run
                    File.WriteAllText(migrationstatusfile, migrationlog.ToString());
                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Tools.DebugLog("Exception MigrateFloorRound()", ex);
                }
            }
        }

        /// <summary>
        /// Fills missing odometer values in position records using interpolation.
        /// One-time migration for data consistency (called on first startup).
        /// </summary>
        /// <remarks>
        /// Issue: Some position records have NULL odometer values due to API inconsistencies or legacy data.
        /// Solution: Find nearest positions with valid odometer values (both before and after)
        /// and interpolate/average the odometer value.
        /// 
        /// Process:
        /// 1. Find all pos.id records with odometer IS NULL
        /// 2. For each NULL odometer:
        ///    - Find nearest position before with valid odometer (lowerOdo)
        ///    - Find nearest position after with valid odometer (higherOdo)
        ///    - If both exist: average them
        ///    - If only lower exists: use lower value
        /// 3. Update pos.odometer with computed value
        /// 4. Track maximum posid processed to resume if interrupted (KVS storage)
        /// 
        /// Performance: Iterative with per-record lookups, may take minutes for large datasets
        /// </remarks>
        internal static void MigratePosOdometerNullValues()
        {
            Tools.DebugLog("MigratePosOdometerNullValues start");
            try
            {
                if (KVS.Get("MigratePosOdometerNullValuesMaxPosID", out int migratePosOdometerNullValuesMaxPosID) == KVS.NOT_FOUND)
                {
                    migratePosOdometerNullValuesMaxPosID = 0;
                }
                int maxPosID = migratePosOdometerNullValuesMaxPosID;
                // find all pos.id where odometer IS NULL
                List<Tuple<int, int>> IDs = new();
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id,
    carid
FROM
    pos
WHERE
    id > @migratePosOdometerNullValuesMaxPosID
    AND odometer IS NULL", con))
                    {
                        cmd.CommandTimeout = 6000;
                        cmd.Parameters.AddWithValue("@migratePosOdometerNullValuesMaxPosID", migratePosOdometerNullValuesMaxPosID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] != DBNull.Value
                            && dr[1] != DBNull.Value)
                        {
                            if (int.TryParse(dr[0].ToString(), out int posid)
                                && int.TryParse(dr[1].ToString(), out int carid))
                            {
                                IDs.Add(new Tuple<int, int>(posid, carid));
                                maxPosID = Math.Max(posid, maxPosID);
                            }
                        }
                    }
                }
                Tools.DebugLog($"MigratePosOdometerNullValues IDs:{IDs.Count}");
                if (IDs.Count == 0)
                {
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(id)
FROM
    pos", con))
                        {
                            cmd.CommandTimeout = 6000;
                            maxPosID = (int)SQLTracer.TraceSc(cmd);
                        }
                    }
                }
                else if (IDs.Count > 0)
                {
                    foreach (Tuple<int, int> ID in IDs)
                    {
                        Tools.DebugLog($"MigratePosOdometerNullValues Tuple:{ID.Item1},{ID.Item2}");
                        double lowerOdo = 0;
                        double higherOdo = 0;
                        double odo = 0;
                        //find nearest pos with odometer NOT NULL lower than posid
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    odometer
FROM
    pos
WHERE
    odometer IS NOT NULL
    AND CarID = @CarID
    AND id < @posid
ORDER BY
    id DESC
LIMIT 1", con))
                            {
                                cmd.Parameters.AddWithValue("@CarID", ID.Item2);
                                cmd.Parameters.AddWithValue("@posid", ID.Item1);
                                cmd.CommandTimeout = 6000;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                if (dr.Read() && dr[0] != DBNull.Value)
                                {
                                    if (double.TryParse(dr[0].ToString(), out double lodo)
                                        && lodo > 0)
                                    {
                                        Tools.DebugLog($"MigratePosOdometerNullValues lowerOdo: {lodo}");
                                        lowerOdo = lodo;
                                    }
                                }
                            }
                        }
                        //find nearest pos with odometer NOT NULL higher than posid
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    odometer
FROM
    pos
WHERE
    odometer IS NOT NULL
    AND CarID = @CarID
    AND id > @posid
ORDER BY
    id ASC
LIMIT 1", con))
                            {
                                cmd.Parameters.AddWithValue("@CarID", ID.Item2);
                                cmd.Parameters.AddWithValue("@posid", ID.Item1);
                                cmd.CommandTimeout = 6000;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                if (dr.Read() && dr[0] != DBNull.Value)
                                {
                                    if (double.TryParse(dr[0].ToString(), out double hodo)
                                        && hodo > 0)
                                    {
                                        Tools.DebugLog($"MigratePosOdometerNullValues higherOdo:{hodo}");
                                        higherOdo = hodo;
                                    }
                                }
                            }
                        }
                        // compute average from lower and higher or just take lower?
                        if (lowerOdo > 0 && higherOdo > 0)
                        {
                            odo = (lowerOdo + higherOdo) / 2;
                        }
                        else
                        {
                            odo = lowerOdo;
                        }
                        if (odo > 0)
                        {
                            // update pos.odometer
                            Tools.DebugLog($"MigratePosOdometerNullValues id:{ID.Item1} new odo:{odo}");
                            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                            {
                                con.Open();
                                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    pos
SET
    odometer = @odometer
WHERE
    id = @id", con))
                                {
                                    cmd.Parameters.AddWithValue("@id", ID.Item1);
                                    cmd.Parameters.AddWithValue("@odometer", odo);
                                    int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                                    Tools.DebugLog($"MigratePosOdometerNullValues id:{ID.Item1} new odo:{odo} updated:{rowsUpdated}");
                                }
                            }
                        }
                    }
                }
                KVS.InsertOrUpdate("MigratePosOdometerNullValuesMaxPosID", maxPosID);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Updates the AP (Autopilot) column in pos records based on cruisestate data.
        /// Maps cruise control state transitions to time periods in position records.
        /// </summary>
        /// <param name="carid">Vehicle ID</param>
        /// <param name="start">Start time for update window</param>
        /// <param name="end">End time for update window</param>
        /// <remarks>
        /// Problem: pos.ap (autopilot) needs to be filled from separate cruisestate table
        /// which has state transitions (ON/OFF) rather than continuous state per position.
        /// 
        /// Solution:
        /// 1. Query cruisestate for state transitions within [start, end]
        /// 2. Find pairs of consecutive transitions (T1ridge -> T2ridge)
        /// 3. For each pair, the state between T1.date and T2.date is T1.state
        /// 4. Update all pos records in that timestamp range to ap=T1.state
        /// 
        /// Performance: Uses MySQL user variables for window functions (requires MySQL 5.7+)
        /// </remarks>
        internal static void UpdateAllPOS_AP_Column(int carid, DateTime start, DateTime end)
        {
            try
            {
                Tools.SetThreadEnUS();

                using (MySqlConnection con = new MySqlConnection($"{DBConnectionstring};Allow User Variables=True"))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(
@"SELECT 
    T1.date AS startdate,
    T1.state AS startstate,
    T2.date AS enddate,
    T2.state AS endstate
FROM
    (SELECT 
        (@rowid1:=@rowid1 + 1) T1rid, carid, date, state
    FROM
        cruisestate
    JOIN (SELECT @rowid1:=0) a) T1
        LEFT JOIN
    (SELECT 
        (@rowid2:=@rowid2 + 1) T2rid, date, state, carid
    FROM
        cruisestate
    JOIN (SELECT @rowid2:=0) b) T2 ON T1.T1rid + 1 = T2.T2rid
WHERE
    T1.carid = @carid
        AND T1.date BETWEEN @start AND @end
        AND T2.date BETWEEN @start AND @end
ORDER BY startdate", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", carid);
                        cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                        cmd.Parameters.AddWithValue("@end", end.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));

                        var dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            try
                            {
                                using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                                {
                                    int state = Convert.ToInt32(dr["startstate"]);
                                    DateTime startstate = (DateTime)dr["startdate"];
                                    DateTime endstate = (DateTime)dr["enddate"];

                                    con2.Open();
                                    using (MySqlCommand cmd2 = new MySqlCommand(@"update pos set ap=@ap where carid=@carid and datum between @start and @end", con2))
                                    {
                                        cmd2.Parameters.AddWithValue("@carid", carid);
                                        cmd2.Parameters.AddWithValue("@ap", state);
                                        cmd2.Parameters.AddWithValue("@start", startstate.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                                        cmd2.Parameters.AddWithValue("@end", endstate.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                                        cmd2.ExecuteNonQuery();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logfile.Log(ex.ToString());
                                ex.ToExceptionless().FirstCarUserID().Submit();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        /// <summary>
        /// Duplicates a position record (for internal GPS testing or debugging).
        /// Creates a new pos record with current timestamp from existing record data.
        /// </summary>
        /// <param name="id">Position ID to duplicate</param>
        /// <returns>LastInsertedId of the new duplicate record</returns>
        /// <remarks>
        /// Useful for:
        /// - Testing position update logic with synthetic data
        /// - Debugging geolocation or speed calculation issues
        /// - Creating test datasets with varied positions
        /// 
        /// Note: Sets Datum to current timestamp (now()), all other fields copied as-is.
        /// </remarks>
        internal static long DuplicatePos(int id)
        {
             string sql = $@"INSERT INTO `pos` (`Datum`,`lat`,`lng`,`speed`,`power`,`odometer`,`ideal_battery_range_km`,`address`,`outside_temp`,`altitude`,`battery_level`,`inside_temp`,`battery_heater`,`is_preconditioning`,`sentry_mode`,`battery_range_km`,`CarID`,`AP`) 
                select now() ,`lat`,`lng`,`speed`,`power`,`odometer`,`ideal_battery_range_km`,`address`,`outside_temp`,`altitude`,`battery_level`,`inside_temp`,`battery_heater`,`is_preconditioning`,`sentry_mode`,`battery_range_km`,`CarID`,`AP`
                from pos
                where id = {id};";

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(sql, con))
                {
                    cmd.ExecuteNonQuery();
                    return cmd.LastInsertedId;
                }
            }
        }
    }
}
