using MySql.Data.MySqlClient;
using Exceptionless;
using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Caching;

namespace TeslaLogger
{
    /// <summary>
    /// QueryMetadata: Database metadata queries and cached statistics (Batch 16)
    /// Provides schema introspection, version info, and cached statistics with memory caching
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Retrieves MySQL server version information
        /// Returns version string or "NULL" if unavailable
        /// </summary>
        /// <remarks>
        /// Query: SELECT @@version
        /// Used for logging server capabilities and debugging
        /// </remarks>
        /// <returns>MySQL version string (e.g., "8.0.35"), or "NULL" if error</returns>
        public static string? GetVersion()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT @@version", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return dr[0].ToString();
                    }
                }
            }

            return "NULL";
        }

        /// <summary>
        /// Checks if table exists in database
        /// Queries information_schema.tables for table existence
        /// </summary>
        /// <remarks>
        /// Security: Uses string interpolation for table name (internal system identifier)
        /// Suppressed CA2100 as table name is not user input
        /// </remarks>
        /// <param name="table">Table name to check for existence</param>
        /// <returns>True if table exists, false otherwise</returns>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static bool TableExists(string? table)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    *
FROM
    information_schema.tables
WHERE
    table_name = '{table}'", con))
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
        /// Retrieves data type of a column
        /// Returns column's DATA_TYPE from information_schema
        /// </summary>
        /// <remarks>
        /// Security: Uses string interpolation (internal system identifier, not user input)
        /// Returns empty string if column not found
        /// </remarks>
        /// <param name="table">Table name containing column</param>
        /// <param name="column">Column name</param>
        /// <returns>Data type string (e.g., "varchar", "int"), or empty string if not found</returns>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static string? GetColumnType(string? table, string? column)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    DATA_TYPE
FROM
    INFORMATION_SCHEMA.COLUMNS
WHERE
    table_name = '{table}'
    AND COLUMN_NAME = '{column}'", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return dr[0].ToString();
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Checks if column exists in table
        /// Uses SHOW COLUMNS with LIKE pattern matching
        /// </summary>
        /// <remarks>
        /// Handles MySQL error 1146 (table doesn't exist) by returning false
        /// Other exceptions are rethrown
        /// </remarks>
        /// <param name="table">Table name</param>
        /// <param name="column">Column name to check</param>
        /// <returns>True if column exists, false if table/column not found</returns>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static bool ColumnExists(string? table, string? column)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM `{table}` LIKE '{column}';", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return true;
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                if (ex.Number == 1146)  // Table doesn't exist
                    return false;

                throw;
            }

            return false;
        }

        /// <summary>
        /// Gets count of ScanMyTesla CAN signals received last week
        /// Result is cached for 4 hours per vehicle
        /// </summary>
        /// <remarks>
        /// Uses MemoryCache.Default for caching
        /// Cache key: GetScanMyTeslaSignalsLastWeek_{carID}
        /// TTL: 4 hours
        /// 
        /// Counts unique signal entries in 'can' table
        /// for current vehicle in last 7 days
        /// </remarks>
        /// <returns>Count of CAN signals last week, or 0 on error</returns>
        internal int GetScanMyTeslaSignalsLastWeek()
        {
            string cacheKey = $"GetScanMyTeslaSignalsLastWeek_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    COUNT(*)
FROM
    can
WHERE
    CarID = @CarID
    AND datum >= DATE(NOW()) - INTERVAL 7 DAY", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            int count = Convert.ToInt32(dr[0], Tools.ciEnUS);

                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(4));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetScanMyTeslaPacketsLastWeek");
                car.Log(ex.ToString());
            }
            return 0;
        }

        /// <summary>
        /// Gets count of ScanMyTesla CAN data packets received last week
        /// Aggregates signal count per second (UNIX_TIMESTAMP grouping)
        /// Result is cached for 4 hours per vehicle
        /// </summary>
        /// <remarks>
        /// Uses MemoryCache.Default for caching
        /// Cache key: GetScanMyTeslaPacketsLastWeek_{carID}
        /// TTL: 4 hours
        /// 
        /// Counts "packets" = grouped signals by UNIX_TIMESTAMP
        /// Detects message frequency independent of signal count
        /// </remarks>
        /// <returns>Count of CAN data packets last week, or 0 on error</returns>
        internal int GetScanMyTeslaPacketsLastWeek()
        {
            string cacheKey = $"GetScanMyTeslaPacketsLastWeek_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    COUNT(*)
FROM
    (
    SELECT
        COUNT(*) AS cnt
    FROM
        can
    WHERE
        CarID = @CarID
        AND datum >= DATE(NOW()) - INTERVAL 7 DAY
    GROUP BY
        UNIX_TIMESTAMP(datum)) AS T1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader r = SQLTracer.TraceDR(cmd);
                        if (r.Read())
                        {
                            int count = Convert.ToInt32(r[0], Tools.ciEnUS);

                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(4));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetScanMyTeslaPacketsLastWeek");
                car.Log(ex.ToString());
            }
            return 0;
        }

        /// <summary>
        /// Calculates average maximum Tesla range across recent charging sessions
        /// Based on end-of-charge battery level and range
        /// Result is cached (1 hour) per vehicle
        /// </summary>
        /// <remarks>
        /// Data source: charging sessions from last 60 days
        /// Formula: AVG(charging_End.ideal_battery_range_km / charging_End.battery_level * 100)
        /// Converged to max theoretical range at 100% SOC
        /// 
        /// Filters:
        /// - Charging sessions > 3 minutes duration
        /// - Odometer > 1 (excludes static tests)
        /// - Last 60 days
        /// 
        /// Cache TTL: 1 hour (0 range) or 1 hour (valid range)
        /// </remarks>
        /// <returns>Average max range in km, or 0 if no data</returns>
        public int GetAvgMaxRage()
        {
            string cacheKey = $"GetAvgMaxRage_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    AVG(charging_End.ideal_battery_range_km / charging_End.battery_level * 100) AS 'TRmax'
FROM
    charging
INNER JOIN
    chargingstate
ON
    charging.id = chargingstate.StartChargingID
INNER JOIN
    pos
ON
    chargingstate.pos = pos.id
LEFT OUTER JOIN
    charging AS charging_End
ON
    chargingstate.EndChargingID = charging_End.id
WHERE
    chargingstate.CarID = @CarID
    AND chargingstate.StartDate > SUBDATE(NOW(), INTERVAL 60 DAY)
    AND TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 3
    AND pos.odometer > 1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr[0] == DBNull.Value)
                            {
                                MemoryCache.Default.Add(cacheKey, 0, DateTime.Now.AddMinutes(5));
                                return 0;
                            }

                            int count = Convert.ToInt32(dr[0], Tools.ciEnUS);
                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(1));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetAvgMaxRage");
                car.Log(ex.ToString());
            }
            return 0;
        }

        /// <summary>
        /// Calculates average energy consumption metrics from completed trips
        /// Analyzes efficiency data across all metrics (distance, energy, SOC)
        /// </summary>
        /// <remarks>
        /// Metrics calculated (all rounded to 0.1):
        /// - sumkm: Total distance covered in analysis set
        /// - avgkm: Average distance per trip (only 100-800 km trips)
        /// - kwh100km: Average energy consumption per 100 km
        /// - avgsocdiff: Average SOC change per trip
        /// - maxkm: Theoretical max range at 100% SOC
        /// 
        /// Filters:
        /// - Trip distance 100-800 km (excludes short/incomplete trips)
        /// - Start and end battery_level must be NOT NULL
        /// - Uses trip, pos (start), pos (end) tables with joins
        /// 
        /// Security: SQL injection risk noted but accepted for single-vehicle scope
        /// </remarks>
        /// <param name="sumkm">Total km covered in dataset</param>
        /// <param name="avgkm">Average distance per trip</param>
        /// <param name="kwh100km">Average energy per 100 km</param>
        /// <param name="avgsocdiff">Average SOC percentage change per trip</param>
        /// <param name="maxkm">Theoretical maximum range atSOC 100%</param>
        [SuppressMessage("Security", "CA2100:SQL-Abfragen auf Sicherheitsrisiken überprüfen", Justification = "<Pending>")]
        public void GetAvgConsumption(out double sumkm, out double avgkm, out double kwh100km, out double avgsocdiff, out double maxkm)
        {
            sumkm = 0;
            avgkm = 0;
            kwh100km = 0;
            avgsocdiff = 0;
            maxkm = 0;

            try
            {
                using (DataTable dt = new DataTable())
                {
                    string sql = $@"
SELECT
    SUM(km_diff) AS sumkm,
    AVG(km_diff) AS avgkm,
    AVG(avg_consumption_kwh_100km) AS kwh100km,
    AVG(pos.battery_level - posend.battery_level) AS avgsocdiff,
    AVG(km_diff / (pos.battery_level - posend.battery_level) * 100) AS maxkm
FROM
    trip
JOIN
    pos
ON
    trip.startposid = pos.id
JOIN
    pos AS posend
ON
    trip.endposid = posend.id
WHERE
    km_diff BETWEEN 100 AND 800
    AND pos.battery_level IS NOT NULL
    AND trip.carid = {car.CarInDB};";

                    using (MySqlDataAdapter da = new MySqlDataAdapter(sql, DBConnectionstring))
                    {
                        _ = SQLTracer.TraceDA(dt, da);

                        if (dt.Rows.Count == 1)
                        {
                            var r = dt.Rows[0];

                            if (r["sumkm"] == DBNull.Value)
                            {
                                car.Log($"GetAvgConsumption: nothing found!!!");
                                return;
                            }

                            sumkm = Math.Round((double)r["sumkm"], 1);
                            avgkm = Math.Round((double)r["avgkm"], 1);
                            kwh100km = Math.Round((double)r["kwh100km"], 1);
                            avgsocdiff = Math.Round((double)r["avgsocdiff"], 1);
                            maxkm = Math.Round((double)r["maxkm"], 1);

                            car.Log($"GetAvgConsumption: sumkm: {sumkm}; avgkm: {avgkm}; kwh/100km: {kwh100km}; avgsocdiff: {avgsocdiff}; maxkm: {maxkm}");
                        }
                    }
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }
    }
}
