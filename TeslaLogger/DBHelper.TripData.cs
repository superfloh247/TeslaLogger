using MySql.Data.MySqlClient;
using Exceptionless;
using System;

namespace TeslaLogger
{
    /// <summary>
    /// TripData: Trip statistics and drive state data management (Batch 11)
    /// Handles trip completion analysis, drive statistics recalculation, and odometer-based queries
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Updates incomplete drivestate records by identifying trips with missing battery range data
        /// Identifies drive records where both start and end positions have null ideal_battery_range_km
        /// Validates odometer delta > 0.1 km before processing
        /// </summary>
        /// <remarks>
        /// Query joins drivestate with pos table for start/end positions
        /// Filters for incomplete trips: missing ideal_battery_range_km at start or end
        /// Currently contains TODO: UpdateDriveStatistics call is commented out
        /// Returns false indicating operation needs completion
        /// </remarks>
        /// <returns>bool - Always returns false (operation incomplete, requires UpdateDriveStatistics implementation)</returns>
        public static bool UpdateIncompleteTrips()
        {
            Tools.DebugLog("UpdateIncompleteTrips()");
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"SELECT pos_start.id as StartPos, pos_end.id as EndPos
                         FROM drivestate
                         JOIN pos pos_start ON drivestate . StartPos = pos_start. id
                         JOIN pos pos_end ON  drivestate . EndPos = pos_end. id
                         WHERE
                         (pos_end. odometer - pos_start. odometer ) > 0.1 and
                         (( pos_start. ideal_battery_range_km is null) or ( pos_end. ideal_battery_range_km is null))", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);

                        while (dr.Read())
                        {
                            try
                            {
                                int StartPos = Convert.ToInt32(dr[0], Tools.ciEnUS);
                                int EndPos = Convert.ToInt32(dr[1], Tools.ciEnUS);

                                // TODO UpdateDriveStatistics(StartPos, EndPos, false);
                            }
                            catch (Exception ex)
                            {
                                ex.ToExceptionless().FirstCarUserID().Submit();
                                Logfile.Log(ex.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "UpdateIncompleteTrips");
            }

            return false;
        }

        /// <summary>
        /// Recalculates drive statistics for all drivestate records with complete trip data
        /// Iterates all drive records and calls UpdateDriveStatistics for each valid trip
        /// Skips unfinished trips where EndPos is null
        /// Marks operation completion in KVS (Key-Value Store)
        /// </summary>
        /// <remarks>
        /// Process:
        /// 1. Selects all drivestate records (StartPos, EndPos, CarID)
        /// 2. For each record: validates EndPos is not null (skips incomplete trips)
        /// 3. Retrieves Car instance by CarID
        /// 4. Calls c.DbHelper.UpdateDriveStatistics(StartPos, EndPos, false)
        /// 5. Updates KVS["UpdateAllDrivestateData"] = 2 on completion
        /// 
        /// Error handling: Exceptionless integration catches and logs all exceptions per record
        /// Logging: Logs start/end with execution time markers
        /// </remarks>
        public static void UpdateAllDrivestateData()
        {
            Logfile.Log("UpdateAllDrivestateData start");
            try
            {

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("select StartPos,EndPos, carid from drivestate", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            try
                            {
                                int StartPos = Convert.ToInt32(dr[0], Tools.ciEnUS);
                                
                                if (dr[1] == DBNull.Value) // unfinished trips won't be updated
                                    continue;

                                int EndPos = Convert.ToInt32(dr[1], Tools.ciEnUS);
                                int CarId = Convert.ToInt32(dr[2], Tools.ciEnUS);

                                Car c = Car.GetCarByID(CarId);
                                if (c is not null)
                                {
                                    c.DbHelper.UpdateDriveStatistics(StartPos, EndPos, false);
                                }
                            }
                            catch (Exception ex)
                            {
                                ex.ToExceptionless().FirstCarUserID().Submit();
                                Logfile.Log(ex.ToString());
                            }
                        }
                    }

                    KVS.InsertOrUpdate("UpdateAllDrivestateData", 2);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            Logfile.Log("UpdateAllDrivestateData end");
        }
    }
}
